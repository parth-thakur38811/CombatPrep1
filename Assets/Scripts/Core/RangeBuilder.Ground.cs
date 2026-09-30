using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CombatPrep.Core
{
    /// <summary>
    /// The ground. With the imported surfaces it's a flat terrain painted from three of them -
    /// wet mud, burnt earth and broken-concrete grit - instead of one texture repeating to the
    /// horizon: burnt patches drift across it, the earth is scorched round every crater and
    /// fire, and grit spills out round the ruins. It stays dead flat at the old ground height,
    /// so nothing placed on it moves. Without the imported surfaces it's the old textured box.
    ///
    /// The paint is worked out from positions and fixed noise, never Random, so it can run
    /// after the seeded build.
    /// </summary>
    public static partial class RangeBuilder
    {
        static Terrain _terrain;
        static readonly List<(Vector2 pos, float radius)> CraterSpots = new();
        static readonly List<Vector2> FireSpots = new();

        /// <summary>Colour a bullet throws up off the ground (the terrain has no renderer to ask).</summary>
        public static Color GroundImpactColor => ArtLibrary.Has(Art.Ground) ? Art.Ground.ImpactColor : Mat.Sand;

        static bool GroundTerrain(Transform root, Vector3 centre, Vector3 size)
        {
            _terrain = null;
            CraterSpots.Clear();
            FireSpots.Clear();

            var layers = new[] { Art.Ground, Art.Berm, Art.Rubble };
            foreach (var l in layers)
                if (!ArtLibrary.Has(l)) return false;
            if (Art.TerrainMaterial == null) return false;

            var data = new TerrainData
            {
                heightmapResolution = 33,       // flat: the fewest samples there are
                alphamapResolution = 512,
                baseMapResolution = 512,
            };
            data.size = new Vector3(size.x, 1f, size.z);
            var terrainLayers = new TerrainLayer[layers.Length];
            for (int i = 0; i < layers.Length; i++) terrainLayers[i] = Layer(layers[i]);
            data.terrainLayers = terrainLayers;

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Ground";
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(centre.x - size.x * 0.5f, 0f, centre.z - size.z * 0.5f);

            var terrain = go.GetComponent<Terrain>();
            terrain.materialTemplate = Art.TerrainMaterial;
            // Never instanced: a build keeps a shader's instancing variants only for terrains in a
            // scene or materials marked for it, and this one is made at runtime - so in a build an
            // instanced terrain draws nothing at all (the editor compiles every variant, so it looks
            // fine there). Flat and small, it costs nothing drawn the plain way.
            terrain.drawInstanced = false;
            terrain.heightmapPixelError = 8f;
            terrain.basemapDistance = 200f;
            terrain.shadowCastingMode = ShadowCastingMode.Off;      // flat: nothing to cast on
            terrain.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
            _terrain = terrain;
            return true;
        }

        /// <summary>A terrain layer from an imported surface: its textures, tiling and tint.</summary>
        static TerrainLayer Layer(ArtLibrary.Surface s)
        {
            var m = s.Material;
            var tint = m.GetColor("_BaseColor");
            return new TerrainLayer
            {
                diffuseTexture = m.GetTexture("_BaseMap") as Texture2D,
                normalMapTexture = m.GetTexture("_BumpMap") as Texture2D,
                maskMapTexture = m.GetTexture("_MetallicGlossMap") as Texture2D,
                tileSize = Vector2.one * s.TileMeters,
                normalScale = 1f,
                diffuseRemapMin = Vector4.zero,
                diffuseRemapMax = new Vector4(tint.r, tint.g, tint.b, 1f),
                maskMapRemapMin = Vector4.zero,
                maskMapRemapMax = Vector4.one,
            };
        }

        /// <summary>Paints the terrain once everything that marks the ground is in place.</summary>
        static void PaintGround()
        {
            if (_terrain == null) return;
            var data = _terrain.terrainData;
            int res = data.alphamapResolution;
            var origin = _terrain.transform.position;
            var size = data.size;
            var map = new float[res, res, 3];

            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                var p = new Vector2(origin.x + (x + 0.5f) / res * size.x, origin.z + (y + 0.5f) / res * size.z);

                // Drifting patches of burnt earth, broken up at a finer scale.
                float n = Mathf.PerlinNoise(p.x * 0.035f + 41.3f, p.y * 0.035f + 7.9f) * 0.75f
                        + Mathf.PerlinNoise(p.x * 0.16f + 3.1f, p.y * 0.16f + 19.4f) * 0.25f;
                float burnt = Smooth(0.52f, 0.7f, n) * 0.85f;

                // Scorched round every crater and fire.
                foreach (var c in CraterSpots)
                    burnt = Mathf.Max(burnt, 1f - Smooth(c.radius * 0.8f, c.radius * 1.9f, Vector2.Distance(p, c.pos)));
                foreach (var f in FireSpots)
                    burnt = Mathf.Max(burnt, (1f - Smooth(1.2f, 4.5f, Vector2.Distance(p, f))) * 0.9f);

                // Grit: rubble spilled round the ruins, and a scatter of it everywhere.
                float grit = Smooth(0.62f, 0.8f, Mathf.PerlinNoise(p.x * 0.09f + 11.7f, p.y * 0.09f + 3.3f)) * 0.6f;
                foreach (var site in ArenaRuinSites)
                    foreach (int side in new[] { -1, 1 })
                    {
                        var c = new Vector2(site.centre.x * side, site.centre.y);
                        var d = new Vector2(Mathf.Max(0f, Mathf.Abs(p.x - c.x) - site.size.x * 0.5f),
                                            Mathf.Max(0f, Mathf.Abs(p.y - c.y) - site.size.y * 0.5f)).magnitude;
                        grit = Mathf.Max(grit, 1f - Smooth(0.5f, 3.5f, d + (n - 0.5f) * 2f));
                    }

                float mud = 1f;
                float sum = mud + burnt + grit;
                map[y, x, 0] = mud / sum;
                map[y, x, 1] = burnt / sum;
                map[y, x, 2] = grit / sum;
            }
            data.SetAlphamaps(0, 0, map);
        }

        /// <summary>0 at <paramref name="from"/>, 1 at <paramref name="to"/>, eased between.</summary>
        static float Smooth(float from, float to, float x)
        {
            float t = Mathf.Clamp01((x - from) / (to - from));
            return t * t * (3f - 2f * t);
        }
    }
}
