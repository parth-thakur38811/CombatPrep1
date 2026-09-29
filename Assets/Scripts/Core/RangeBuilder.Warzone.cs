using UnityEngine;
using CombatPrep.FX;

namespace CombatPrep.Core
{
    /// <summary>
    /// War-zone dressing, layered over the range: a ring of shelled buildings smouldering in
    /// the fog, dead trees, concertina wire on the berms, shell craters, anti-tank hedgehogs,
    /// standing water, and work lamps under the firing-line canopy.
    ///
    /// Everything here is built after every existing prop, still inside Build's seeded Random
    /// stream. Order matters: the stream is shared, so anything that drew from it before the
    /// existing clutter would move every container and sandbag wall. Appended here, the old
    /// layout is untouched and the new pieces are just as identical on every machine.
    /// Only the craters and hedgehogs claim floor space; puddles avoid props but block nothing.
    /// </summary>
    public static partial class RangeBuilder
    {
        static readonly Vector3 ArenaCentre = new(0f, 0f, 42f);

        static Material[] _scorchMats, _puddleMats;

        static void Warzone(Transform root)
        {
            var w = Prim.Empty(root, "Warzone");

            _scorchMats = new Material[3];
            for (int i = 0; i < _scorchMats.Length; i++)
                _scorchMats[i] = Mat.Decal(Tex.Scorch(256, i + 1), Color.white, 0.3f);

            // Near-black and mirror-smooth: water reads by what it reflects, not its colour.
            _puddleMats = new Material[4];
            for (int i = 0; i < _puddleMats.Length; i++)
                _puddleMats[i] = Mat.Decal(Tex.Puddle(256, i + 7), new Color(0.045f, 0.05f, 0.055f), 0.96f);

            Ruins(w);
            DeadTrees(w);
            Wire(w);
            Craters(w);
            Hedgehogs(w);
            Puddles(w);
            CanopyLamps(w);
        }

        // -------------------------------------------------------------------- horizon

        /// <summary>
        /// Shelled buildings all around, 80-150 m out. The fog does most of the art: the near
        /// ones are dark silhouettes, the far ones barely-there shapes. A few are still
        /// burning, which puts orange light on their walls and a plume of smoke over the ridge.
        /// </summary>
        static void Ruins(Transform parent)
        {
            var t = Prim.Empty(parent, "Ruins");
            var shell = Mat.Get(new Color(0.21f, 0.215f, 0.22f), 0f, 0.3f);
            var scorched = Mat.Get(new Color(0.11f, 0.105f, 0.10f), 0f, 0.25f);

            // With imported concrete, the burnt-out variant is the same surface, darkened.
            var shellArt = Art.Concrete;
            ArtLibrary.Surface scorchedArt = null;
            if (ArtLibrary.Has(shellArt))
            {
                var dark = new Material(shellArt.Material) { name = "ConcreteScorched" };
                var c = shellArt.Material.GetColor("_BaseColor");
                dark.SetColor("_BaseColor", new Color(c.r * 0.42f, c.g * 0.4f, c.b * 0.38f, 1f));
                scorchedArt = new ArtLibrary.Surface
                {
                    Material = dark, TileMeters = shellArt.TileMeters, ImpactColor = shellArt.ImpactColor * 0.4f
                };
                SurfaceColors.Register(dark, scorchedArt.ImpactColor);
            }

            const int count = 26;
            int burning = 0;
            for (int i = 0; i < count; i++)
            {
                float a = (i + Random.Range(-0.3f, 0.3f)) / count * Mathf.PI * 2f;
                float r = Random.Range(82f, 150f);
                var pos = ArenaCentre + new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);

                float yaw = Random.Range(0f, 360f), w = Random.Range(8f, 18f), d = Random.Range(7f, 13f),
                      h = Random.Range(9f, 28f);
                bool burnt = Random.value >= 0.5f;
                var ruin = Ruin(t, pos, yaw, w, d, h, burnt ? scorched : shell, burnt ? scorchedArt : shellArt);

                if (burning < 5 && Random.value < 0.3f)
                {
                    burning++;
                    QueueFire(ruin, pos + Vector3.up * 0.5f, 2.4f, 24f, true);
                }
            }
        }

        /// <summary>
        /// A gutted building: four walls broken into columns of different heights with a few
        /// missing outright, the stubs of floors showing through, rubble heaped at the base.
        /// </summary>
        static Transform Ruin(Transform parent, Vector3 pos, float yaw, float width, float depth,
                              float height, Material mat, ArtLibrary.Surface surface)
        {
            var b = Prim.Empty(parent, "Ruin", pos);
            b.localRotation = Quaternion.Euler(0f, yaw, 0f);

            const float wall = 0.5f;
            for (int side = 0; side < 4; side++)
            {
                bool alongX = side % 2 == 0;
                float span = alongX ? width : depth;
                float offset = (alongX ? depth : width) * 0.5f * (side < 2 ? 1f : -1f);

                int cols = Random.Range(3, 6);
                float colW = span / cols;
                for (int c = 0; c < cols; c++)
                {
                    if (Random.value < 0.18f) continue;            // blown out
                    float h = height * Random.Range(0.35f, 1f);
                    float along = -span * 0.5f + colW * (c + 0.5f);
                    var local = alongX ? new Vector3(along, h * 0.5f, offset) : new Vector3(offset, h * 0.5f, along);
                    var size = alongX ? new Vector3(colW * 1.02f, h, wall) : new Vector3(wall, h, colW * 1.02f);
                    Prim.Surface(b, "Wall", local, size, surface, mat);
                }
            }

            // Floor slabs that survived, jutting from the shell.
            for (float y = 3.4f; y < height - 1f; y += 3.4f)
            {
                if (Random.value > 0.6f) continue;
                float len = width * Random.Range(0.4f, 0.95f);
                Prim.Surface(b, "Floor", new Vector3(Random.Range(-1f, 1f), y, Random.Range(-1f, 1f)),
                             new Vector3(len, 0.3f, depth * Random.Range(0.5f, 0.95f)), surface, mat,
                             euler: new Vector3(Random.Range(-6f, 6f), 0f, Random.Range(-8f, 8f)));
            }

            // Rubble.
            int chunks = Random.Range(5, 9);
            for (int i = 0; i < chunks; i++)
            {
                float s = Random.Range(0.8f, 2.6f);
                Prim.Surface(b, "Rubble",
                             new Vector3(Random.Range(-width, width) * 0.6f, s * 0.3f, Random.Range(-depth, depth) * 0.6f),
                             new Vector3(s, s * 0.6f, s * Random.Range(0.6f, 1.2f)), Art.Rubble, mat,
                             euler: new Vector3(Random.Range(-20f, 20f), Random.Range(0f, 360f), Random.Range(-20f, 20f)));
            }
            return b;
        }

        /// <summary>Shell-stripped trees beyond the berms - the classic no-man's-land silhouette.</summary>
        static void DeadTrees(Transform parent)
        {
            var t = Prim.Empty(parent, "DeadTrees");
            for (int i = 0; i < 26; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f);
                float r = Random.Range(52f, 95f);
                var pos = ArenaCentre + new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
                // Keep them out of the play box and off the berms.
                if (Mathf.Abs(pos.x) < 46f && pos.z > -20f && pos.z < 114f) continue;
                DeadTree(t, pos);
            }
        }

        static void DeadTree(Transform parent, Vector3 pos)
        {
            var tree = Prim.Empty(parent, "DeadTree", pos);
            tree.localRotation = Quaternion.Euler(Random.Range(-6f, 6f), Random.Range(0f, 360f), Random.Range(-6f, 6f));

            float h = Random.Range(4f, 9f);
            float dia = Random.Range(0.25f, 0.5f);
            Prim.Pillar(tree, "Trunk", new Vector3(0f, h * 0.5f, 0f), dia, h, Mat.Charred, 0f, 0.2f);

            int branches = Random.Range(2, 5);
            for (int i = 0; i < branches; i++)
            {
                float y = Random.Range(h * 0.45f, h * 0.9f);
                float len = Random.Range(1f, 2.8f);
                var q = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Quaternion.Euler(0f, 0f, -Random.Range(30f, 65f));
                var dir = q * Vector3.up;
                var branch = Prim.Pillar(tree, "Branch", new Vector3(0f, y, 0f) + dir * (len * 0.5f),
                                         dia * 0.35f, len, Mat.Charred, 0f, 0.2f);
                branch.localRotation = q;
            }
        }

        // ---------------------------------------------------------------------- berms

        /// <summary>
        /// Concertina wire along the berms, just outside the invisible walls: it frames the
        /// arena as a defended position without ever snagging a player.
        /// </summary>
        static void Wire(Transform parent)
        {
            var t = Prim.Empty(parent, "Wire");
            var steel = Mat.Get(new Color(0.20f, 0.19f, 0.18f), 0.75f, 0.5f);

            float x = BoundHalfX + 0.55f;
            float back = BoundBackZ + 0.45f;
            Coil(t, new Vector3(-x, 0f, BoundFrontZ + 1f), new Vector3(-x, 0f, back), steel, 1);
            Coil(t, new Vector3(x, 0f, BoundFrontZ + 1f), new Vector3(x, 0f, back), steel, 2);
            Coil(t, new Vector3(-x, 0f, back), new Vector3(x, 0f, back), steel, 3);
        }

        static void Coil(Transform parent, Vector3 from, Vector3 to, Material mat, int seed)
        {
            var go = new GameObject("Concertina");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = MeshGen.HelixTube(from, to, 0.42f, 0.22f, 0.012f, 9, 0.12f, seed);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Steel pickets holding it down.
            int n = Mathf.FloorToInt(Vector3.Distance(from, to) / 5f);
            for (int i = 0; i <= n; i++)
            {
                var p = Vector3.Lerp(from, to, i / (float)Mathf.Max(1, n));
                Prim.Box(parent, "Picket", p + Vector3.up * 0.55f, new Vector3(0.05f, 1.1f, 0.05f),
                         Mat.RustDark, 0.5f, 0.45f);
            }
        }

        // ---------------------------------------------------------------------- floor

        /// <summary>Old shell holes: a scorch, a lip of thrown earth, rainwater in the bottom.</summary>
        static void Craters(Transform parent)
        {
            var t = Prim.Empty(parent, "Craters");
            for (int i = 0; i < 5; i++)
            {
                if (!FindSpot(3f, 27f, 4f, 90f, 2.2f, out var p)) continue;
                var pos = new Vector3(p.x, 0f, p.y);
                float radius = Random.Range(1.8f, 2.8f);

                Prim.Quad(t, "Crater", pos + Vector3.up * 0.012f, new Vector2(radius * 2.4f, radius * 2.4f),
                          _scorchMats[i % _scorchMats.Length], new Vector3(90f, Random.Range(0f, 360f), 0f));

                int clods = Random.Range(10, 16);
                for (int c = 0; c < clods; c++)
                {
                    float a = c / (float)clods * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
                    float rr = radius * Random.Range(0.85f, 1.15f);
                    Prim.Surface(t, "Clod", pos + new Vector3(Mathf.Sin(a) * rr, 0.04f, Mathf.Cos(a) * rr),
                                 new Vector3(Random.Range(0.2f, 0.55f), Random.Range(0.08f, 0.22f), Random.Range(0.2f, 0.5f)),
                                 Art.Berm, Mat.Dirt, 0f, 0.35f,
                                 euler: new Vector3(Random.Range(-15f, 15f), Random.Range(0f, 360f), Random.Range(-15f, 15f)));
                }

                Prim.Quad(t, "CraterWater", pos + Vector3.up * 0.016f, new Vector2(radius * 1.1f, radius * 1.1f),
                          _puddleMats[i % _puddleMats.Length], new Vector3(90f, Random.Range(0f, 360f), 0f));
            }
        }

        /// <summary>
        /// Czech hedgehogs: three steel beams crossed so that whichever way it lies, one points
        /// up. Solid cover you can shoot through the gaps of.
        /// </summary>
        static void Hedgehogs(Transform parent)
        {
            var t = Prim.Empty(parent, "Hedgehogs");
            for (int i = 0; i < 5; i++)
            {
                if (!FindSpot(5f, 27f, 3f, 88f, 1.5f, out var p)) continue;
                Hedgehog(t, new Vector3(p.x, 0f, p.y), Random.Range(0f, 360f));
            }
        }

        static void Hedgehog(Transform parent, Vector3 pos, float yaw)
        {
            var h = Prim.Empty(parent, "Hedgehog", pos);
            h.localRotation = Quaternion.Euler(0f, yaw, 0f);

            // Each beam passes through the centre; the tilt is chosen so its low end just
            // touches the ground.
            const float centre = 0.78f, half = 1.3f;
            float rise = centre / half;
            float spread = Mathf.Sqrt(1f - rise * rise);
            for (int k = 0; k < 3; k++)
            {
                float a = k * 120f * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(a) * spread, rise, Mathf.Cos(a) * spread);
                var beam = Prim.Surface(h, $"Beam{k}", new Vector3(0f, centre, 0f), new Vector3(0.16f, 0.16f, half * 2f),
                                        Art.Metal, Mat.RustDark, 0.55f, 0.5f, collider: true);
                beam.localRotation = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 0f, 45f);
            }
        }

        /// <summary>
        /// Standing water. Placed clear of every claimed footprint so none lands inside a
        /// container, but claims nothing itself - you can walk straight through a puddle.
        /// Each sits a hair higher than the last so overlapping ones can't flicker.
        /// </summary>
        static void Puddles(Transform parent)
        {
            var t = Prim.Empty(parent, "Puddles");
            int placed = 0;
            for (int attempt = 0; attempt < 90 && placed < 16; attempt++)
            {
                var p = new Vector2(Random.Range(-28f, 28f), Random.Range(-8f, 92f));
                float r = Random.Range(0.9f, 2.4f);
                if (OverlapsProp(p, r)) continue;

                Prim.Quad(t, "Puddle", new Vector3(p.x, 0.008f + placed * 0.0004f, p.y),
                          new Vector2(r * 2f, r * 2f * Random.Range(0.6f, 1f)),
                          _puddleMats[placed % _puddleMats.Length], new Vector3(90f, Random.Range(0f, 360f), 0f));
                placed++;
            }
        }

        // --- fires, lit after the seeded pass (see Build) ---

        static readonly System.Collections.Generic.List<(Transform parent, Vector3 pos, float scale, float range, bool column)>
            PendingFires = new();

        static void QueueFire(Transform parent, Vector3 pos, float scale, float range, bool column)
            => PendingFires.Add((parent, pos, scale, range, column));

        static void LightFires()
        {
            foreach (var f in PendingFires)
                if (f.parent != null) FireFx.Create(f.parent, f.pos, f.scale, f.range, f.column);
            PendingFires.Clear();
        }

        static bool OverlapsProp(Vector2 p, float radius)
        {
            foreach (var o in Occupied)
                if (Vector2.Distance(p, o.pos) < radius + o.radius) return true;
            return false;
        }

        // ---------------------------------------------------------------------- lamps

        /// <summary>
        /// Bare bulbs under the firing-line canopy - warm, human light in the cold, and one of
        /// them on a failing circuit.
        /// </summary>
        static void CanopyLamps(Transform parent)
        {
            var t = Prim.Empty(parent, "Lamps");
            var bulb = Mat.Get(new Color(1f, 0.78f, 0.5f), 0f, 0.5f, emission: 3f);

            float[] xs = { -8f, 0f, 8f };
            for (int i = 0; i < xs.Length; i++)
            {
                var lamp = Prim.Empty(t, "Lamp", new Vector3(xs[i], 3.25f, -1.5f));
                Prim.Box(lamp, "Cord", new Vector3(0f, 0.12f, 0f), new Vector3(0.012f, 0.24f, 0.012f), Mat.Polymer);
                Prim.Pillar(lamp, "Shade", new Vector3(0f, -0.02f, 0f), 0.34f, 0.08f, Mat.Gunmetal, 0.5f, 0.4f);
                Prim.SetMaterial(Prim.Ball(lamp, "Bulb", new Vector3(0f, -0.1f, 0f), 0.1f, Color.white), bulb);

                var lightGo = new GameObject("LampLight");
                lightGo.transform.SetParent(lamp, false);
                lightGo.transform.localPosition = new Vector3(0f, -0.2f, 0f);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.74f, 0.46f);
                light.range = 9f;
                light.intensity = 2.4f;
                light.shadows = LightShadows.None;

                if (i == 1) lightGo.AddComponent<LightFlicker>();
            }
        }
    }
}
