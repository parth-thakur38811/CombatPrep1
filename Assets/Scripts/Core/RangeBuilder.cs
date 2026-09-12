using UnityEngine;

namespace CombatPrep.Core
{
    /// <summary>
    /// Builds the shooting range: a sun-bleached desert facility. Everything is primitives
    /// plus generated textures, but the point of this pass is that flat grey boxes read as
    /// unfinished no matter how good the shooting feels. What fixes it is cheap and
    /// specific: tiled surface texture, a warm/cool colour split, clutter at three
    /// different scales, and something overhead to cast shadows across the firing line.
    /// </summary>
    public static class RangeBuilder
    {
        public const float Width = 52f;
        public const float Length = 86f;

        /// <summary>
        /// Invisible play-area walls live here. Excluded from the weapon hit mask, so they
        /// stop the player without stopping bullets - otherwise missed shots would burst
        /// against thin air above the berms.
        /// </summary>
        public const int BoundaryLayer = 10;

        // Play box. The side walls sit just inside the nearest possible berm base: berm
        // centres are at |x| = Width/2 + 3 and their X radius varies 6.5-9, so the closest
        // any berm reaches in is |x| = 20.
        const float BoundHalfX = 20.5f;
        const float BoundFrontZ = -12f;
        const float BoundBackZ = 78f;

        static Material _ground, _concrete, _concreteWall;

        public static void Build()
        {
            // Deterministic layout: the clutter looks scattered but is identical every run,
            // so tuning against it is repeatable.
            var prev = Random.state;
            Random.InitState(20260912);

            _ground = Tiled(Tex.Ground(256, 1), 34f, 34f, 0.02f);
            _concrete = Tiled(Tex.Concrete(256, 2), 10f, 10f, 0.06f);
            _concreteWall = Tiled(Tex.Concrete(256, 3), 8f, 2f, 0.06f);

            var root = Prim.Empty(null, "Range");

            Ground(root);
            Berms(root);
            Boundaries(root);
            DistantHills(root);
            FiringLine(root);
            Lanes(root);
            Perimeter(root);
            Clutter(root);

            Random.state = prev;
        }

        // -------------------------------------------------------------------------- base

        static void Ground(Transform root)
        {
            var g = Prim.Box(root, "Ground", new Vector3(0f, -0.5f, Length * 0.34f),
                             new Vector3(160f, 1f, 190f), Color.white, 0f, 0.04f, collider: true);
            Prim.SetMaterial(g, _ground);

            // Concrete apron under the shooter, so the firing line reads as built.
            var pad = Prim.Box(root, "Pad", new Vector3(0f, 0.012f, -1.5f),
                               new Vector3(26f, 0.06f, 12f), Color.white, 0f, 0.10f, collider: true);
            Prim.SetMaterial(pad, _concrete);
        }

        /// <summary>Earth berms down each side and a tall backstop - the classic range shape.</summary>
        static void Berms(Transform root)
        {
            var berms = Prim.Empty(root, "Berms");

            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < 9; i++)
                {
                    float z = -4f + i * 10.5f;
                    float h = 3.4f + Mathf.PerlinNoise(i * 0.6f, side * 3f) * 2.2f;
                    var m = Prim.Ball(berms, $"Berm{side}_{i}",
                                      new Vector3(side * (Width * 0.5f + 3f), h * 0.18f, z),
                                      1f, Color.white);
                    m.localScale = new Vector3(15f + Random.Range(-2f, 3f), h, 16f + Random.Range(-2f, 4f));
                    Prim.SetMaterial(m, _ground);
                }
            }

            // Backstop mound behind the far targets. Pushed out to Length so its inner face
            // clears the 68 m target, which the old 0.86 factor buried inside the mound.
            for (int i = 0; i < 7; i++)
            {
                float x = -24f + i * 8f;
                var m = Prim.Ball(berms, $"Backstop{i}", new Vector3(x, 1.2f, Length), 1f, Color.white);
                m.localScale = new Vector3(16f, 11f + Random.Range(-1.5f, 2.5f), 15f);
                Prim.SetMaterial(m, _ground);
            }
        }

        /// <summary>
        /// Invisible walls enclosing the play area. The berms and backstop are scaled
        /// spheres, and a SphereCollider takes its radius from the largest scale axis - on a
        /// 15 x 3.4 x 16 mound that would block a radius-8 ball, far more than you can see.
        /// A flat boundary is both cheaper and closer to the visible shape.
        /// </summary>
        static void Boundaries(Transform root)
        {
            var b = Prim.Empty(root, "Boundaries");

            float midZ = (BoundFrontZ + BoundBackZ) * 0.5f;
            float span = BoundBackZ - BoundFrontZ;
            const float h = 6f;    // well over jump height, no lip to vault

            Wall(b, "BoundL", new Vector3(-BoundHalfX, h * 0.5f, midZ), new Vector3(0.5f, h, span));
            Wall(b, "BoundR", new Vector3(BoundHalfX, h * 0.5f, midZ), new Vector3(0.5f, h, span));
            Wall(b, "BoundBack", new Vector3(0f, h * 0.5f, BoundBackZ), new Vector3(BoundHalfX * 2f, h, 0.5f));
            Wall(b, "BoundFront", new Vector3(0f, h * 0.5f, BoundFrontZ), new Vector3(BoundHalfX * 2f, h, 0.5f));
        }

        static void Wall(Transform parent, string name, Vector3 pos, Vector3 size)
        {
            var w = Prim.Box(parent, name, pos, size, Color.white, 0f, 0.3f, collider: true);
            w.GetComponent<MeshRenderer>().enabled = false;
            w.gameObject.layer = BoundaryLayer;
        }

        /// <summary>Flattened spheres far out, purely to give the horizon depth.</summary>
        static void DistantHills(Transform root)
        {
            var hills = Prim.Empty(root, "Hills");
            var tint = Mat.Get(new Color(0.62f, 0.56f, 0.46f), 0f, 0.05f);

            for (int i = 0; i < 16; i++)
            {
                float a = i / 16f * Mathf.PI * 2f;
                float dist = 200f + Random.Range(-40f, 70f);
                var h = Prim.Ball(hills, $"Hill{i}",
                                  new Vector3(Mathf.Sin(a) * dist, -6f, Mathf.Cos(a) * dist + 30f),
                                  1f, Color.white);
                h.localScale = new Vector3(Random.Range(90f, 170f), Random.Range(26f, 54f), Random.Range(90f, 150f));
                Prim.SetMaterial(h, tint);
            }
        }

        // ------------------------------------------------------------------- firing line

        /// <summary>Covered firing point. The canopy is what puts moving shadow on the pad.</summary>
        static void FiringLine(Transform root)
        {
            var fl = Prim.Empty(root, "FiringLine");

            float y = 3.5f;
            for (int i = -3; i <= 3; i++)
            {
                Prim.Box(fl, $"Post{i}", new Vector3(i * 4f, y * 0.5f, -5.5f),
                         new Vector3(0.22f, y, 0.22f), Mat.Steel, 0.55f, 0.35f, collider: true);
                Prim.Box(fl, $"PostF{i}", new Vector3(i * 4f, y * 0.5f, 2.5f),
                         new Vector3(0.22f, y, 0.22f), Mat.Steel, 0.55f, 0.35f, collider: true);
            }

            // Roof beams, deliberately slatted so the light breaks up across the ground.
            Prim.Box(fl, "BeamL", new Vector3(0f, y, -5.5f), new Vector3(26f, 0.18f, 0.30f), Mat.Steel, 0.5f, 0.3f);
            Prim.Box(fl, "BeamR", new Vector3(0f, y, 2.5f), new Vector3(26f, 0.18f, 0.30f), Mat.Steel, 0.5f, 0.3f);
            for (int i = 0; i < 17; i++)
            {
                float z = -5.5f + i * 0.5f;
                Prim.Box(fl, $"Slat{i}", new Vector3(0f, y + 0.12f, z),
                         new Vector3(26f, 0.06f, 0.22f), Mat.RustDark, 0.3f, 0.2f);
            }

            // Shooting benches.
            for (int i = -2; i <= 2; i++)
            {
                var bench = Prim.Empty(fl, $"Bench{i}", new Vector3(i * 4f, 0f, -0.6f));
                Prim.Box(bench, "Top", new Vector3(0f, 1.05f, 0f), new Vector3(1.5f, 0.09f, 0.75f),
                         Mat.Wood, 0.05f, 0.25f, collider: true);
                Prim.Box(bench, "LegL", new Vector3(-0.62f, 0.52f, 0f), new Vector3(0.09f, 1.05f, 0.09f), Mat.Steel, 0.5f, 0.3f);
                Prim.Box(bench, "LegR", new Vector3(0.62f, 0.52f, 0f), new Vector3(0.09f, 1.05f, 0.09f), Mat.Steel, 0.5f, 0.3f);
            }

            // Painted firing line.
            Prim.Box(fl, "Line", new Vector3(0f, 0.05f, 0f), new Vector3(24f, 0.02f, 0.20f), Mat.Accent, 0f, 0.25f);
        }

        /// <summary>Lane markers and distance posts, so range estimation is readable.</summary>
        static void Lanes(Transform root)
        {
            var lanes = Prim.Empty(root, "Lanes");

            for (int d = 10; d <= 70; d += 10)
            {
                Prim.Box(lanes, $"Mark{d}", new Vector3(0f, 0.02f, d),
                         new Vector3(Width - 14f, 0.03f, 0.12f), new Color(0.80f, 0.78f, 0.72f), 0f, 0.15f);

                for (int side = -1; side <= 1; side += 2)
                {
                    var post = Prim.Empty(lanes, $"Post{d}_{side}", new Vector3(side * (Width * 0.5f - 5f), 0f, d));
                    Prim.Box(post, "Pole", new Vector3(0f, 0.7f, 0f), new Vector3(0.10f, 1.4f, 0.10f),
                             Mat.Steel, 0.5f, 0.3f, collider: true);
                    // Alternating bands read as a surveyed distance marker.
                    for (int b = 0; b < 4; b++)
                        Prim.Box(post, $"Band{b}", new Vector3(0f, 0.25f + b * 0.32f, 0f),
                                 new Vector3(0.115f, 0.16f, 0.115f),
                                 b % 2 == 0 ? Mat.Accent : new Color(0.92f, 0.92f, 0.88f), 0f, 0.25f);
                    Prim.Box(post, "Plate", new Vector3(0f, 1.55f, 0f), new Vector3(0.52f, 0.30f, 0.04f),
                             Mat.Accent, 0f, 0.3f);
                }
            }
        }

        static void Perimeter(Transform root)
        {
            var p = Prim.Empty(root, "Perimeter");

            // Concrete blast walls behind the firing line.
            for (int i = -4; i <= 4; i++)
            {
                var w = Prim.Box(p, $"Wall{i}", new Vector3(i * 6f, 2.1f, -11f),
                                 new Vector3(5.8f, 4.2f, 0.55f), Color.white, 0f, 0.08f, collider: true);
                Prim.SetMaterial(w, _concreteWall);
                Prim.Box(p, $"WallCap{i}", new Vector3(i * 6f, 4.3f, -11f),
                         new Vector3(6.0f, 0.18f, 0.75f), Mat.ConcreteHi, 0f, 0.1f);
            }
        }

        // ----------------------------------------------------------------------- clutter

        static void Clutter(Transform root)
        {
            var c = Prim.Empty(root, "Clutter");

            Container(c, new Vector3(-19f, 0f, 22f), -14f, Mat.ContainerA, 1);
            Container(c, new Vector3(20f, 0f, 34f), 8f, Mat.ContainerB, 2);
            Container(c, new Vector3(-18f, 0f, 48f), 22f, Mat.ContainerC, 3);   // kept inside the boundary
            Container(c, new Vector3(19.5f, 2.62f, 34.4f), 6f, Mat.ContainerC, 4);   // stacked

            SandbagWall(c, new Vector3(-9f, 0f, 12f), 0f, 7);
            SandbagWall(c, new Vector3(10f, 0f, 18f), -12f, 6);
            SandbagWall(c, new Vector3(-2f, 0f, 41f), 6f, 9);

            for (int i = 0; i < 14; i++)
            {
                Vector3 p = new Vector3(Random.Range(-Width * 0.45f, Width * 0.45f), 0f, Random.Range(4f, Length * 0.78f));
                if (Mathf.Abs(p.x) < 6f && p.z < 14f) continue;   // keep the lanes clear

                float roll = Random.value;
                if (roll < 0.42f) Barrel(c, p, Random.Range(0f, 360f));
                else if (roll < 0.74f) Crate(c, p, Random.Range(0f, 360f));
                else Tyres(c, p);
            }

            // Barricades near the firing line, for cover-shooting practice.
            Barricade(c, new Vector3(-6.5f, 0f, 8f), 0f);
            Barricade(c, new Vector3(6.5f, 0f, 8f), 0f);
        }

        static void Container(Transform parent, Vector3 pos, float yaw, Color tint, int seed)
        {
            var t = Prim.Empty(parent, "Container", pos);
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);

            var body = Prim.Box(t, "Body", new Vector3(0f, 1.3f, 0f), new Vector3(6.1f, 2.6f, 2.44f),
                                Color.white, 0.35f, 0.25f, collider: true);
            Prim.SetMaterial(body, Tiled(Tex.Corrugated(tint, 22, seed), 3f, 1f, 0.25f, 0.3f));

            Prim.Box(t, "RimTop", new Vector3(0f, 2.62f, 0f), new Vector3(6.2f, 0.10f, 2.54f), tint * 0.7f, 0.4f, 0.3f);
            Prim.Box(t, "RimBot", new Vector3(0f, 0.05f, 0f), new Vector3(6.2f, 0.10f, 2.54f), tint * 0.7f, 0.4f, 0.3f);
            Prim.Box(t, "DoorL", new Vector3(3.06f, 1.3f, -0.6f), new Vector3(0.06f, 2.3f, 1.1f), tint * 0.8f, 0.4f, 0.35f);
            Prim.Box(t, "DoorR", new Vector3(3.06f, 1.3f, 0.6f), new Vector3(0.06f, 2.3f, 1.1f), tint * 0.8f, 0.4f, 0.35f);
        }

        static void SandbagWall(Transform parent, Vector3 pos, float yaw, int length)
        {
            var t = Prim.Empty(parent, "Sandbags", pos);
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);

            // Track the real extent of the bags as we lay them, rather than guessing a box
            // afterwards. Rows are offset and get shorter as they go up, so a guessed box
            // misses the ends - which is exactly where you could previously walk through
            // bags that were visibly there.
            float minX = float.MaxValue, maxX = float.MinValue, topY = 0f;
            const float bagHalfLength = 0.21f;   // capsule height 0.42, lying along X
            const float bagRadius = 0.15f;       // capsule diameter 0.30

            for (int row = 0; row < 3; row++)
            {
                float y = 0.12f + row * 0.22f;
                float offset = (row % 2) * 0.19f;
                int count = length - row;
                for (int i = 0; i < count; i++)
                {
                    float x = (i - count * 0.5f) * 0.38f + offset;
                    var bag = Prim.Capsule(t, $"Bag{row}_{i}", new Vector3(x, y, 0f), 0.30f, 0.42f,
                                           (row + i) % 2 == 0 ? Mat.Canvas : Mat.SandDark,
                                           false, new Vector3(0f, 0f, 90f));
                    bag.localRotation = Quaternion.Euler(0f, Random.Range(-7f, 7f), 90f);

                    minX = Mathf.Min(minX, x - bagHalfLength);
                    maxX = Mathf.Max(maxX, x + bagHalfLength);
                    topY = Mathf.Max(topY, y + bagRadius);
                }
            }

            var col = Prim.Box(t, "Collider",
                               new Vector3((minX + maxX) * 0.5f, topY * 0.5f, 0f),
                               new Vector3(maxX - minX, topY, 0.34f),
                               Color.white, 0f, 0.3f, collider: true);
            col.GetComponent<MeshRenderer>().enabled = false;
        }

        static void Barrel(Transform parent, Vector3 pos, float yaw)
        {
            var t = Prim.Empty(parent, "Barrel", pos);
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);

            Color c = Random.value < 0.5f ? Mat.Rust : new Color(0.30f, 0.40f, 0.28f);
            Prim.Pillar(t, "Body", new Vector3(0f, 0.44f, 0f), 0.58f, 0.88f, c, 0.45f, 0.25f, collider: true);
            Prim.Pillar(t, "RibA", new Vector3(0f, 0.28f, 0f), 0.62f, 0.06f, c * 0.75f, 0.45f, 0.3f);
            Prim.Pillar(t, "RibB", new Vector3(0f, 0.60f, 0f), 0.62f, 0.06f, c * 0.75f, 0.45f, 0.3f);
            Prim.Pillar(t, "Lid", new Vector3(0f, 0.89f, 0f), 0.60f, 0.04f, c * 0.85f, 0.5f, 0.35f);
        }

        static void Crate(Transform parent, Vector3 pos, float yaw)
        {
            var t = Prim.Empty(parent, "Crate", pos);
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);

            float s = Random.Range(0.7f, 1.05f);
            Prim.Box(t, "Body", new Vector3(0f, s * 0.5f, 0f), new Vector3(s, s, s * 0.95f),
                     Mat.Wood, 0.05f, 0.2f, collider: true);
            // Edge battens, so it does not read as a bare cube.
            Prim.Box(t, "BandA", new Vector3(0f, s * 0.5f, 0f), new Vector3(s * 1.02f, s * 0.12f, s * 0.97f),
                     Mat.Wood * 0.75f, 0.05f, 0.2f);
            Prim.Box(t, "BandB", new Vector3(0f, s * 0.86f, 0f), new Vector3(s * 1.02f, s * 0.10f, s * 0.97f),
                     Mat.Wood * 0.75f, 0.05f, 0.2f);
        }

        static void Tyres(Transform parent, Vector3 pos)
        {
            var t = Prim.Empty(parent, "Tyres", pos);
            int n = Random.Range(2, 5);
            for (int i = 0; i < n; i++)
                Prim.Pillar(t, $"Tyre{i}", new Vector3(0f, 0.10f + i * 0.19f, 0f), 0.92f, 0.18f,
                            new Color(0.10f, 0.10f, 0.11f), 0.1f, 0.25f, collider: i == 0)
                    .localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        }

        static void Barricade(Transform parent, Vector3 pos, float yaw)
        {
            var t = Prim.Empty(parent, "Barricade", pos);
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);

            var body = Prim.Box(t, "Body", new Vector3(0f, 0.55f, 0f), new Vector3(2.4f, 1.1f, 0.35f),
                                Color.white, 0f, 0.1f, collider: true);
            Prim.SetMaterial(body, _concrete);
            Prim.Box(t, "Cap", new Vector3(0f, 1.13f, 0f), new Vector3(2.5f, 0.08f, 0.45f), Mat.Accent, 0f, 0.25f);
            Prim.Box(t, "FootL", new Vector3(-1.0f, 0.08f, 0f), new Vector3(0.4f, 0.16f, 0.8f), Mat.ConcreteHi, 0f, 0.1f);
            Prim.Box(t, "FootR", new Vector3(1.0f, 0.08f, 0f), new Vector3(0.4f, 0.16f, 0.8f), Mat.ConcreteHi, 0f, 0.1f);
        }

        // ------------------------------------------------------------------------ helper

        static Material Tiled(Texture2D tex, float tx, float ty, float smoothness, float metallic = 0f)
        {
            var m = Mat.Textured(tex, metallic, smoothness);
            m.SetTextureScale("_BaseMap", new Vector2(tx, ty));
            return m;
        }
    }
}
