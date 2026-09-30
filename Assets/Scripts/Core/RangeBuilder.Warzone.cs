using UnityEngine;
using CombatPrep.FX;

namespace CombatPrep.Core
{
    /// <summary>
    /// War-zone dressing, layered over the range: shelled buildings inside the arena - two of
    /// them ablaze - with more on the skyline beyond, dead trees, concertina wire on the berms,
    /// shell craters, anti-tank hedgehogs, standing water, and work lamps under the firing-line
    /// canopy.
    ///
    /// Everything here is built after the gameplay props, still inside Build's seeded Random
    /// stream, so it comes out the same on every machine. Order matters: the stream is shared,
    /// so anything that drew from it before the clutter would move every container and sandbag
    /// wall. The ruins inside the arena claim their ground early instead (ClaimArenaRuins,
    /// before the clutter is scattered), so nothing lands inside one. They, the craters and the
    /// hedgehogs claim floor space; puddles avoid props but block nothing.
    /// </summary>
    public static partial class RangeBuilder
    {
        static readonly Vector3 ArenaCentre = new(0f, 0f, 42f);

        static Material[] _scorchMats, _puddleMats;
        static Material _ruinShell, _ruinScorched;
        static ArtLibrary.Surface _ruinShellArt, _ruinScorchedArt;

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

            RuinMaterials();
            Ruins(w);
            ArenaRuins(w);
            DeadTrees(w);
            Wire(w);
            Craters(w);
            Hedgehogs(w);
            Puddles(w);
            CanopyLamps(w);
        }

        // -------------------------------------------------------------------- horizon

        /// <summary>
        /// Concrete for the ruins, bare and burnt-out. With imported concrete the burnt variant
        /// is the same surface, darkened.
        /// </summary>
        static void RuinMaterials()
        {
            _ruinShell = Mat.Get(new Color(0.21f, 0.215f, 0.22f), 0f, 0.3f);
            _ruinScorched = Mat.Get(new Color(0.11f, 0.105f, 0.10f), 0f, 0.25f);

            _ruinShellArt = Art.Concrete;
            _ruinScorchedArt = null;
            if (!ArtLibrary.Has(_ruinShellArt)) return;

            var dark = new Material(_ruinShellArt.Material) { name = "ConcreteScorched" };
            var c = _ruinShellArt.Material.GetColor("_BaseColor");
            dark.SetColor("_BaseColor", new Color(c.r * 0.42f, c.g * 0.4f, c.b * 0.38f, 1f));
            _ruinScorchedArt = new ArtLibrary.Surface
            {
                Material = dark, TileMeters = _ruinShellArt.TileMeters, ImpactColor = _ruinShellArt.ImpactColor * 0.4f
            };
            SurfaceColors.Register(dark, _ruinScorchedArt.ImpactColor);
        }

        /// <summary>
        /// The skyline: shelled buildings all around, 100-160 m out - far enough to read as the
        /// town beyond the range, not something just over the berm. The fog does most of the
        /// art: the near ones are dark silhouettes, the far ones barely-there shapes. The ones
        /// burning are inside the arena (ArenaRuins).
        /// </summary>
        static void Ruins(Transform parent)
        {
            var t = Prim.Empty(parent, "Ruins");

            const int count = 26;
            for (int i = 0; i < count; i++)
            {
                float a = (i + Random.Range(-0.3f, 0.3f)) / count * Mathf.PI * 2f;
                float r = Random.Range(100f, 160f);
                var pos = ArenaCentre + new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);

                float yaw = Random.Range(0f, 360f), w = Random.Range(8f, 18f), d = Random.Range(7f, 13f),
                      h = Random.Range(9f, 28f);
                bool burnt = Random.value >= 0.5f;
                Ruin(t, pos, yaw, w, d, h, burnt ? _ruinScorched : _ruinShell, burnt ? _ruinScorchedArt : _ruinShellArt);
            }
        }

        /// <summary>
        /// A gutted building on the skyline: four walls broken into columns of different heights
        /// with a few missing outright, the stubs of floors showing through, rubble heaped at
        /// the base. Scenery only - nothing here is solid.
        /// </summary>
        static void Ruin(Transform parent, Vector3 pos, float yaw, float width, float depth,
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
        }

        // ----------------------------------------------------------------- arena ruins

        /// <summary>
        /// The buildings inside the arena, each the right-hand half of a mirrored pair: centre
        /// (x, z), size across (X) and along (Z), and whether it's ablaze or only smouldering.
        /// All clear of the target lanes (|x| under 16 out to z = 72), the spawn points and
        /// the distance posts at |x| = 29.
        /// </summary>
        static readonly (Vector2 centre, Vector2 size, bool ablaze)[] ArenaRuinSites =
        {
            (new Vector2(22.5f, 42f), new Vector2(8f, 11f), true),    // halfway down each flank
            (new Vector2(20.5f, 85f), new Vector2(9f, 8f), false),    // the far corners
        };

        const float RuinWall = 0.5f;
        const float RuinStorey = 3.2f;

        enum Bay { Solid, Window, Door, Blown }

        /// <summary>Claims the arena ruins' ground, and a margin for their rubble, so no clutter lands in one.</summary>
        static void ClaimArenaRuins()
        {
            foreach (var site in ArenaRuinSites)
                foreach (int side in new[] { -1, 1 })
                    Occupy(new Vector2(site.centre.x * side, site.centre.y), site.size.magnitude * 0.5f + 1f);
        }

        /// <summary>
        /// Shelled buildings to fight through, in mirrored pairs so neither side of an online
        /// match gets the better one. Each is a walk-in shell - breaches to run through, windows
        /// to shoot from, what's left of the upper floor overhead (it keeps the rain off), rubble
        /// - and every piece is solid, so it stops players, bullets and grenade blasts. The pair
        /// on the flanks is ablaze under a column of smoke; the pair at the far end smoulders.
        /// </summary>
        static void ArenaRuins(Transform parent)
        {
            var t = Prim.Empty(parent, "ArenaRuins");
            foreach (var site in ArenaRuinSites)
            {
                // Both halves are drawn from the same numbers, so each is the other's mirror
                // image - and the stream moves on exactly as far as it would for one.
                var start = Random.state;
                ArenaRuin(t, site.centre, site.size, site.ablaze, 1f);
                Random.state = start;
                ArenaRuin(t, site.centre, site.size, site.ablaze, -1f);
            }
        }

        /// <summary>
        /// One arena ruin, laid out for the right-hand side and flipped across the centre line
        /// when <paramref name="mirror"/> is -1. In its own frame -X faces the lanes, +X the side
        /// wall and -Z the firing line: the lane side gets the most ways in, the side-wall side
        /// stays mostly whole, and the fire burns at the front, open to the sky, with the
        /// surviving upper floor over the back.
        /// </summary>
        static void ArenaRuin(Transform parent, Vector2 centre, Vector2 size, bool ablaze, float mirror)
        {
            var b = Prim.Empty(parent, "Ruin", new Vector3(centre.x * mirror, 0f, centre.y));
            var mat = ablaze ? _ruinScorched : _ruinShell;
            var surface = ablaze ? _ruinScorchedArt : _ruinShellArt;

            Vector3 At(float x, float y, float z) => new(x * mirror, y, z);
            Vector3 Turn(float x, float y, float z) => new(x, y * mirror, z * mirror);
            void Piece(string name, Vector3 pos, Vector3 dims)
                => Prim.Surface(b, name, pos, dims, surface, mat, collider: true);
            void Rubble(Vector3 pos, float s)
                => Prim.Surface(b, "Rubble", pos, new Vector3(s, s * 0.45f, s * Random.Range(0.6f, 1f)),
                                Art.Rubble, mat, collider: true,
                                euler: Turn(Random.Range(-12f, 12f), Random.Range(0f, 360f), Random.Range(-12f, 12f)));

            float w = size.x, d = size.y, full = RuinStorey * 2f;
            float innerW = w - RuinWall * 2f, innerD = d - RuinWall * 2f;

            // --- walls, each side broken into bays. Front and back run the full width; the two
            // sides fit between them, so no two walls overlap at a corner. ---
            for (int side = 0; side < 4; side++)
            {
                bool alongX = side < 2;    // 0 front (-Z), 1 back (+Z), 2 lane side (-X), 3 side-wall side (+X)
                float span = alongX ? w : innerD;
                float across = alongX ? (side == 0 ? -1f : 1f) * (d - RuinWall) * 0.5f
                                      : (side == 2 ? -1f : 1f) * (w - RuinWall) * 0.5f;

                int n = Mathf.Max(3, Mathf.RoundToInt(span / 2.3f));
                float bw = span / n;
                var bays = new Bay[n];
                for (int i = 0; i < n; i++)
                {
                    float roll = Random.value;
                    bays[i] = side == 3
                        ? (roll < 0.25f ? Bay.Window : roll < 0.8f ? Bay.Solid : Bay.Blown)
                        : (roll < 0.5f ? Bay.Window : roll < 0.8f ? Bay.Solid : Bay.Blown);
                }

                // The corners are what's still holding it up: never blown, never a way in.
                bays[0] = bays[0] == Bay.Blown ? Bay.Solid : bays[0];
                bays[n - 1] = bays[n - 1] == Bay.Blown ? Bay.Solid : bays[n - 1];
                int doors = side == 3 ? 0 : side == 2 && n >= 5 ? 2 : 1;
                for (int k = 0; k < doors && k < n - 2; k++)
                {
                    int i = Random.Range(1, n - 1);
                    while (bays[i] == Bay.Door) i = 1 + i % (n - 2);
                    bays[i] = Bay.Door;
                }

                for (int i = 0; i < n; i++)
                {
                    float along = -span * 0.5f + bw * (i + 0.5f);
                    bool corner = i == 0 || i == n - 1;
                    float top = corner ? full * Random.Range(0.8f, 1f)
                              : Random.value < 0.35f ? Random.Range(RuinStorey - 0.3f, RuinStorey + 0.9f)
                              : full * Random.Range(0.55f, 1f);

                    Vector3 On(float y) => alongX ? At(along, y, across) : At(across, y, along);
                    void Wall(float from, float to)
                    {
                        if (to - from < 0.05f) return;
                        var dims = alongX ? new Vector3(bw, to - from, RuinWall) : new Vector3(RuinWall, to - from, bw);
                        Piece("Wall", On((from + to) * 0.5f), dims);
                    }

                    switch (bays[i])
                    {
                        case Bay.Solid:
                            Wall(0f, top);
                            break;

                        case Bay.Door:
                            // Half keep a lintel; the rest are blown open to the top.
                            if (Random.value < 0.5f && top > 2.9f) Wall(2.4f, top);
                            break;

                        case Bay.Window:
                            // A sill to crouch behind and shoot over, and upstairs a second window.
                            Wall(0f, 1.1f);
                            if (top > RuinStorey + 2.3f)
                            {
                                Wall(2.25f, RuinStorey + 1f);
                                Wall(RuinStorey + 2.1f, top);
                            }
                            else Wall(2.25f, top);
                            break;

                        case Bay.Blown:
                            // A stub low enough to jump, with the rest of the wall spilled either side.
                            Wall(0f, Random.Range(0.45f, 0.9f));
                            foreach (float face in new[] { -1f, 1f })
                            {
                                float s = Random.Range(0.6f, 1.05f);
                                float off = face * (RuinWall * 0.5f + s * 0.45f);
                                float a = Random.Range(-0.25f, 0.25f) * bw;
                                var pos = alongX ? At(along + a, s * 0.2f, across + off) : At(across + off, s * 0.2f, along + a);
                                Rubble(pos, s);
                            }
                            break;
                    }
                }
            }

            // --- what's left of the upper floor: strips of slab from the back wall, broken off at
            // different lengths. Overhead only - there's no way up. ---
            const int strips = 3;
            const float bed = 0.3f;    // how far the slab runs into the walls it rests on
            float stripW = innerW / strips;
            for (int k = 0; k < strips; k++)
            {
                float len = innerD * Random.Range(0.35f, 0.62f);
                float x0 = -innerW * 0.5f + stripW * k - (k == 0 ? bed : 0f);
                float x1 = -innerW * 0.5f + stripW * (k + 1) + (k == strips - 1 ? bed : 0f);
                float z0 = innerD * 0.5f - len, z1 = innerD * 0.5f + bed;
                Piece("Floor", At((x0 + x1) * 0.5f, RuinStorey + 0.14f, (z0 + z1) * 0.5f),
                      new Vector3(x1 - x0, 0.28f, z1 - z0));
            }

            // Debris heaped in the back corners, under the floor.
            foreach (float cx in new[] { -1f, 1f })
            {
                int pile = Random.Range(1, 3);
                for (int k = 0; k < pile; k++)
                {
                    float s = Random.Range(0.5f, 0.95f);
                    Rubble(At(cx * (innerW * 0.5f - 0.6f) + Random.Range(-0.3f, 0.3f), s * 0.2f,
                              innerD * 0.5f - 0.65f + Random.Range(-0.35f, 0.2f)), s);
                }
            }

            // --- fire, at the open front where its smoke has the sky: debris heaped in the corner
            // by the side wall, burning. A building ablaze burns in the other front corner too,
            // and sends up a column of smoke; the rest only smoulder. ---
            float frontZ = -innerD * 0.5f;
            BurningHeap(At(innerW * 0.5f - 0.95f, 0f, frontZ + 0.85f), 1.5f, ablaze ? 2f : 1.1f, ablaze ? 16f : 10f, ablaze);
            if (ablaze) BurningHeap(At(-innerW * 0.5f + 0.75f, 0f, frontZ + 0.75f), 1f, 1.1f, 9f, false);

            void BurningHeap(Vector3 at, float across, float fire, float light, bool column)
            {
                for (int k = 0; k < 3; k++)
                {
                    float s = across * Random.Range(0.45f, 0.65f);
                    Rubble(at + At(Random.Range(-0.3f, 0.3f) * across, s * 0.2f, Random.Range(-0.3f, 0.3f) * across), s);
                }
                // One box round the heap, so nobody walks through the flames.
                var box = Prim.Box(b, "HeapCollider", at + Vector3.up * 0.25f, new Vector3(across, 0.5f, across),
                                   Color.white, 0f, 0.3f, collider: true);
                box.GetComponent<MeshRenderer>().enabled = false;
                QueueFire(b, b.position + at + Vector3.up * 0.45f, fire, light, column);
            }
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

                // Drawn in the same order as always, so the seeded stream stays in step.
                float aspect = Random.Range(0.6f, 1f);
                float yaw = Random.Range(0f, 360f);
                var pos = new Vector3(p.x, 0.008f + placed * 0.0004f, p.y);

                var prefab = Art != null && Art.Fx != null ? Art.Fx.Puddle : null;
                var go = prefab != null ? Object.Instantiate(prefab) : FX.FxRecipes.Puddle();
                go.name = "Puddle";
                go.transform.SetParent(t, false);
                go.transform.localPosition = pos;
                go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                go.GetComponent<FX.PuddleFx>().Setup(placed, r * 2f, r * 2f * aspect, FX.ParticleKit.Seed(pos));
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
