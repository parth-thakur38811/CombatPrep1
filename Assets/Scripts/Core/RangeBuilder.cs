using System.Collections.Generic;
using UnityEngine;

namespace CombatPrep.Core
{
    /// <summary>
    /// Builds the shooting range: a rain-soaked firing position at the edge of a war zone.
    /// Everything is primitives plus generated textures, but the point of this pass is that
    /// flat grey boxes read as unfinished no matter how good the shooting feels. What fixes it
    /// is cheap and specific: tiled surface texture, a warm/cool colour split, clutter at
    /// three different scales, and something overhead to cast shadows across the firing line.
    /// Surfaces are tuned wet - darker and glossier than they would be dry - and the war-zone
    /// dressing (RangeBuilder.Warzone.cs) is layered on last.
    /// </summary>
    public static partial class RangeBuilder
    {
        public const float Width = 72f;
        public const float Length = 104f;

        /// <summary>
        /// Invisible play-area walls live here. Excluded from the weapon hit mask, so they
        /// stop the player without stopping bullets - otherwise missed shots would burst
        /// against thin air above the berms.
        /// </summary>
        public const int BoundaryLayer = 10;

        // Play box. The side walls sit just inside the nearest possible berm base: berm
        // centres are at |x| = Width/2 + 3 = 39 and their X radius varies 6.5-9, so the
        // closest any berm reaches in is |x| = 30. The backstop sits at z = Length with a
        // Z radius of ~7.5, so its inner face lands at 96.5.
        const float BoundHalfX = 30.5f;
        const float BoundFrontZ = -12f;
        const float BoundBackZ = 96f;

        static Material _ground, _concrete, _concreteWall;

        /// <summary>Imported PBR art. Every use falls back to the procedural look if an entry is missing.</summary>
        static ArtLibrary Art => ArtLibrary.I;

        public static void Build()
        {
            // Deterministic layout: the clutter looks scattered but is identical every run,
            // so tuning against it is repeatable.
            var prev = Random.state;
            Random.InitState(20260912);

            // Wet: mud and concrete both glint under the storm light.
            _ground = Tiled(Tex.Ground(256, 1), 34f, 34f, 0.32f);
            _concrete = Tiled(Tex.Concrete(256, 2), 10f, 10f, 0.36f);
            _concreteWall = Tiled(Tex.Concrete(256, 3), 8f, 2f, 0.30f);

            var root = Prim.Empty(null, "Range");

            Ground(root);
            Berms(root);
            Boundaries(root);
            DistantHills(root);
            FiringLine(root);
            Lanes(root);
            Perimeter(root);
            Clutter(root);
            Warzone(root);      // strictly last - see RangeBuilder.Warzone.cs

            Random.state = prev;

            // Fires are lit only once the layout is final and the seeded stream is released:
            // particle systems are engine internals, and nothing that might touch Random may
            // run while the shared stream is still placing cover.
            LightFires();
        }

        // -------------------------------------------------------------------------- base

        static void Ground(Transform root)
        {
            // Sized off the arena so it always runs well past the berms and the backstop.
            Prim.Surface(root, "Ground", new Vector3(0f, -0.5f, Length * 0.36f),
                         new Vector3(Width + 140f, 1f, Length + 130f), Art.Ground, _ground, collider: true);

            // Concrete apron under the shooter, so the firing line reads as built.
            Prim.Surface(root, "Pad", new Vector3(0f, 0.012f, -1.5f), new Vector3(34f, 0.06f, 12f),
                         Art.Floor, _concrete, collider: true);
        }

        /// <summary>Earth berms down each side and a tall backstop - the classic range shape.</summary>
        static void Berms(Transform root)
        {
            var berms = Prim.Empty(root, "Berms");

            // Flank berms, counted off the arena length so they always run the full play
            // box rather than stopping short when the arena grows.
            const float flankSpacing = 10.5f;
            float flankStart = BoundFrontZ - 4f;
            int flankCount = Mathf.CeilToInt((Length + 8f - flankStart) / flankSpacing) + 1;

            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < flankCount; i++)
                {
                    float z = flankStart + i * flankSpacing;
                    float h = 3.4f + Mathf.PerlinNoise(i * 0.6f, side * 3f) * 2.2f;
                    // Same draws in the same order either way, so the seeded layout can't shift.
                    float sx = 15f + Random.Range(-2f, 3f);
                    float sz = 16f + Random.Range(-2f, 4f);
                    Berm(berms, $"Berm{side}_{i}", new Vector3(side * (Width * 0.5f + 3f), 0f, z), sx, h, sz, h * 0.18f);
                }
            }

            // Backstop mound behind the far targets, wide enough to close off the arena.
            const float backSpacing = 8f;
            float backHalf = BoundHalfX + 6f;
            int backCount = Mathf.CeilToInt(backHalf * 2f / backSpacing) + 1;

            for (int i = 0; i < backCount; i++)
            {
                float x = -backHalf + i * backSpacing;
                float h = 11f + Random.Range(-1.5f, 2.5f);
                Berm(berms, $"Backstop{i}", new Vector3(x, 0f, Length), 16f, h, 15f, 1.2f);
            }
        }

        /// <summary>
        /// One mound. With imported art it is a real earth-mound mesh, textured in world space
        /// and matched to the footprint and height the old sphere showed above ground. Without,
        /// it is the original half-buried scaled sphere.
        /// </summary>
        static void Berm(Transform parent, string name, Vector3 foot, float sx, float h, float sz, float sink)
        {
            if (ArtLibrary.Has(Art.Berm))
            {
                // Where a sphere of this scale, centred `sink` above ground, meets the ground.
                float cut = sink / (h * 0.5f);
                float spread = Mathf.Sqrt(Mathf.Max(0f, 1f - cut * cut));
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = foot;
                go.AddComponent<MeshFilter>().sharedMesh = MeshGen.Mound(foot, sx * spread, sink + h * 0.5f, sz * spread,
                    Art.Berm.TileMeters, Mathf.RoundToInt(foot.x * 7f + foot.z * 13f));
                go.AddComponent<MeshRenderer>().sharedMaterial = Art.Berm.Material;
                return;
            }

            var m = Prim.Ball(parent, name, foot + Vector3.up * sink, 1f, Color.white);
            m.localScale = new Vector3(sx, h, sz);
            Prim.SetMaterial(m, _ground);
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
            var tint = Mat.Get(new Color(0.16f, 0.155f, 0.15f), 0f, 0.2f);

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
                Prim.Surface(fl, $"Post{i}", new Vector3(i * 4f, y * 0.5f, -5.5f),
                             new Vector3(0.22f, y, 0.22f), Art.Metal, Mat.Steel, 0.55f, 0.55f, collider: true);
                Prim.Surface(fl, $"PostF{i}", new Vector3(i * 4f, y * 0.5f, 2.5f),
                             new Vector3(0.22f, y, 0.22f), Art.Metal, Mat.Steel, 0.55f, 0.55f, collider: true);
            }

            // Roof beams, deliberately slatted so the light breaks up across the ground.
            Prim.Surface(fl, "BeamL", new Vector3(0f, y, -5.5f), new Vector3(26f, 0.18f, 0.30f), Art.Metal, Mat.Steel, 0.5f, 0.52f);
            Prim.Surface(fl, "BeamR", new Vector3(0f, y, 2.5f), new Vector3(26f, 0.18f, 0.30f), Art.Metal, Mat.Steel, 0.5f, 0.52f);
            for (int i = 0; i < 17; i++)
            {
                float z = -5.5f + i * 0.5f;
                Prim.Surface(fl, $"Slat{i}", new Vector3(0f, y + 0.12f, z),
                             new Vector3(26f, 0.06f, 0.22f), Art.Metal, Mat.RustDark, 0.3f, 0.45f);
            }

            // Shooting benches.
            for (int i = -2; i <= 2; i++)
            {
                var bench = Prim.Empty(fl, $"Bench{i}", new Vector3(i * 4f, 0f, -0.6f));
                Prim.Surface(bench, "Top", new Vector3(0f, 1.05f, 0f), new Vector3(1.5f, 0.09f, 0.75f),
                             Art.Wood, Mat.Wood, 0.05f, 0.42f, collider: true);
                Prim.Surface(bench, "LegL", new Vector3(-0.62f, 0.52f, 0f), new Vector3(0.09f, 1.05f, 0.09f),
                             Art.Metal, Mat.Steel, 0.5f, 0.3f);
                Prim.Surface(bench, "LegR", new Vector3(0.62f, 0.52f, 0f), new Vector3(0.09f, 1.05f, 0.09f),
                             Art.Metal, Mat.Steel, 0.5f, 0.3f);
            }

            // Painted firing line.
            Prim.Box(fl, "Line", new Vector3(0f, 0.05f, 0f), new Vector3(24f, 0.02f, 0.20f), Mat.Accent, 0f, 0.25f);
        }

        /// <summary>Lane markers and distance posts, so range estimation is readable.</summary>
        static void Lanes(Transform root)
        {
            var lanes = Prim.Empty(root, "Lanes");

            for (int d = 10; d <= 90; d += 10)
            {
                Prim.Box(lanes, $"Mark{d}", new Vector3(0f, 0.02f, d),
                         new Vector3(BoundHalfX * 2f - 8f, 0.03f, 0.12f),
                         new Color(0.56f, 0.55f, 0.51f), 0f, 0.4f);

                // Posts hug the boundary from the inside, so they stay reachable rather
                // than stranded behind the invisible wall.
                for (int side = -1; side <= 1; side += 2)
                {
                    var post = Prim.Empty(lanes, $"Post{d}_{side}", new Vector3(side * (BoundHalfX - 1.5f), 0f, d));
                    Prim.Box(post, "Pole", new Vector3(0f, 0.7f, 0f), new Vector3(0.10f, 1.4f, 0.10f),
                             Mat.Steel, 0.5f, 0.3f, collider: true);
                    // Alternating bands read as a surveyed distance marker.
                    for (int b = 0; b < 4; b++)
                        Prim.Box(post, $"Band{b}", new Vector3(0f, 0.25f + b * 0.32f, 0f),
                                 new Vector3(0.115f, 0.16f, 0.115f),
                                 b % 2 == 0 ? Mat.Accent : new Color(0.70f, 0.70f, 0.67f), 0f, 0.45f);
                    Prim.Box(post, "Plate", new Vector3(0f, 1.55f, 0f), new Vector3(0.52f, 0.30f, 0.04f),
                             Mat.Accent, 0f, 0.3f);
                }
            }
        }

        static void Perimeter(Transform root)
        {
            var p = Prim.Empty(root, "Perimeter");

            // Concrete blast walls behind the firing line, spanning the full arena width.
            int half = Mathf.CeilToInt(BoundHalfX / 6f);
            for (int i = -half; i <= half; i++)
            {
                Prim.Surface(p, $"Wall{i}", new Vector3(i * 6f, 2.1f, -11f), new Vector3(5.8f, 4.2f, 0.55f),
                             Art.Concrete, _concreteWall, collider: true);
                Prim.Surface(p, $"WallCap{i}", new Vector3(i * 6f, 4.3f, -11f), new Vector3(6.0f, 0.18f, 0.75f),
                             Art.Concrete, Mat.ConcreteHi, 0f, 0.35f);
            }
        }

        // ----------------------------------------------------------------------- clutter

        // --- placement bookkeeping -------------------------------------------------
        //
        // Props are scattered by rejection sampling against a list of claimed footprints,
        // so nothing ever spawns inside anything else. Targets are handled as one broad
        // corridor rather than by mirroring Bootstrap's individual spawn coordinates -
        // that would be a second copy of the layout, quietly drifting out of sync the
        // first time a target moves.

        static readonly List<(Vector2 pos, float radius)> Occupied = new();

        const float LaneHalfX = 16f;    // every target Bootstrap spawns sits inside this
        const float LaneMinZ = 13f;
        const float LaneMaxZ = 72f;

        // Online spawn points (x, z). Spread around the edge of the play box so players
        // start apart, each clear of the target corridor and the firing point. Identical on
        // every machine because they are constants, so they never need syncing.
        static readonly Vector2[] SpawnSpots =
        {
            new(-24f, -4f), new(24f, -4f),
            new(-26f, 26f), new(26f, 26f),
            new(-26f, 58f), new(26f, 58f),
            new(-11f, 88f), new(11f, 88f),
        };

        public static int SpawnCount => SpawnSpots.Length;

        /// <summary>A spawn position, facing into the middle of the arena.</summary>
        public static (Vector3 pos, Quaternion rot) SpawnPoint(int index)
        {
            var s = SpawnSpots[((index % SpawnSpots.Length) + SpawnSpots.Length) % SpawnSpots.Length];
            var pos = new Vector3(s.x, 0.1f, s.y);
            var toCentre = new Vector3(0f, 0f, 42f) - pos;
            toCentre.y = 0f;
            return (pos, Quaternion.LookRotation(toCentre));
        }

        static bool InTargetLane(Vector2 p, float radius)
            => Mathf.Abs(p.x) < LaneHalfX + radius
               && p.y > LaneMinZ - radius && p.y < LaneMaxZ + radius;

        static bool IsClear(Vector2 p, float radius)
        {
            if (InTargetLane(p, radius)) return false;
            foreach (var o in Occupied)
                if (Vector2.Distance(p, o.pos) < radius + o.radius) return false;
            return true;
        }

        static void Occupy(Vector2 p, float radius) => Occupied.Add((p, radius));

        static bool FindSpot(float minAbsX, float maxAbsX, float minZ, float maxZ,
                             float radius, out Vector2 spot)
        {
            for (int attempt = 0; attempt < 90; attempt++)
            {
                float x = Random.Range(minAbsX, maxAbsX) * (Random.value < 0.5f ? -1f : 1f);
                var p = new Vector2(x, Random.Range(minZ, maxZ));
                if (!IsClear(p, radius)) continue;
                Occupy(p, radius);
                spot = p;
                return true;
            }
            spot = default;
            return false;
        }

        /// <summary>Places a sandbag wall at a chosen spot, skipping it if something is already there.</summary>
        static void CoverWall(Transform c, float x, float z, float yaw, int length, int rows)
        {
            var p = new Vector2(x, z);
            if (!IsClear(p, 2.4f)) return;
            Occupy(p, 2.4f);
            SandbagWall(c, new Vector3(x, 0f, z), yaw, length, rows);
        }

        static void Clutter(Transform root)
        {
            var c = Prim.Empty(root, "Clutter");

            Occupied.Clear();
            Occupy(new Vector2(0f, -1.5f), 8f);    // firing point, benches and canopy posts

            // Spawn points are claimed first, so no container or sandbag can land on one.
            foreach (var s in SpawnSpots) Occupy(s, 2.5f);

            // Barricades near the firing line, for cover-shooting practice.
            Barricade(c, new Vector3(-6.5f, 0f, 8f), 0f);
            Barricade(c, new Vector3(6.5f, 0f, 8f), 0f);
            Occupy(new Vector2(-6.5f, 8f), 2.2f);
            Occupy(new Vector2(6.5f, 8f), 2.2f);

            // --- three open containers, scattered down the flanks ---
            // Capped at |x| = 24 so that even end-on they stay clear of the distance posts
            // at |x| = 29, and clear of the target corridor at |x| = 16.
            var tints = new[] { Mat.ContainerA, Mat.ContainerB, Mat.ContainerC };
            var paints = new[] { Art.ContainerA, Art.ContainerB, Art.ContainerC };
            for (int i = 0; i < 3; i++)
                if (FindSpot(20f, 24f, 6f, 82f, ContRadius, out var p))
                    Container(c, new Vector3(p.x, 0f, p.y), Random.Range(0f, 360f), tints[i], paints[i], i + 1);

            // --- cover to break line of sight behind ---
            // Near the firing point, spaced so there is room to move between them.
            CoverWall(c, -9.5f, 7f, 14f, 7, 5);
            CoverWall(c, 9f, 9f, -22f, 6, 5);
            CoverWall(c, -1.5f, 11.5f, 82f, 5, 4);
            CoverWall(c, 14f, 4f, 48f, 5, 4);
            CoverWall(c, -15f, 3f, -40f, 5, 4);

            // Out on the flanks, alongside the containers.
            for (int i = 0; i < 5; i++)
                if (FindSpot(18f, 25.5f, 10f, 80f, 2.4f, out var p))
                    SandbagWall(c, new Vector3(p.x, 0f, p.y), Random.Range(0f, 360f),
                                Random.Range(5, 8), Random.Range(3, 6));

            // --- small scatter ---
            for (int i = 0; i < 22; i++)
            {
                if (!FindSpot(2f, 26.5f, 4f, Length * 0.8f, 1.2f, out var p)) continue;
                var pos = new Vector3(p.x, 0f, p.y);

                float roll = Random.value;
                if (roll < 0.42f) Barrel(c, pos, Random.Range(0f, 360f));
                else if (roll < 0.74f) Crate(c, pos, Random.Range(0f, 360f));
                else Tyres(c, pos);
            }
        }

        // Container shell. Length runs along local X, width along Z, and both ends are open
        // so the player can run straight through.
        const float ContLen = 6.1f;
        const float ContWid = 2.44f;
        const float ContHgt = 2.6f;
        const float ContWall = 0.10f;
        /// <summary>Footprint radius, for keeping other props clear of a container.</summary>
        const float ContRadius = 3.6f;

        /// <summary>
        /// An open cargo container you can run through end to end. Built as a shell - floor,
        /// roof and two long side walls - rather than a solid block, with both ends open and
        /// the door leaves swung back out of the way.
        ///
        /// Every panel is a solid box with a collider, so the walls block movement, sight
        /// and bullets, while the interior stays a clear 2.24 m wide by 2.36 m tall. The
        /// floor is only 0.10 m proud of the ground, well under the CharacterController's
        /// 0.35 m step offset, so you walk in without having to jump.
        /// </summary>
        static void Container(Transform parent, Vector3 pos, float yaw, Color tint, ArtLibrary.Surface paint, int seed)
        {
            var t = Prim.Empty(parent, "Container", pos);
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);

            // The procedural skin is only drawn if there's no photographed one to use.
            var skin = ArtLibrary.Has(paint) ? null : Tiled(Tex.Corrugated(tint, 22, seed), 3f, 1f, 0.5f, 0.3f);
            float halfLen = ContLen * 0.5f;
            float sideZ = (ContWid - ContWall) * 0.5f;
            float innerH = ContHgt - ContWall * 2f;

            // Floor and roof cap the shell top and bottom.
            Prim.Surface(t, "Floor", new Vector3(0f, ContWall * 0.5f, 0f),
                         new Vector3(ContLen, ContWall, ContWid), Art.Wood, tint * 0.55f, 0.4f, 0.3f, collider: true);
            Prim.Surface(t, "Roof", new Vector3(0f, ContHgt - ContWall * 0.5f, 0f),
                         new Vector3(ContLen, ContWall, ContWid), paint, skin, collider: true);

            // The two long walls. These are what actually give cover.
            foreach (int s in new[] { -1, 1 })
            {
                Prim.Surface(t, s > 0 ? "SideA" : "SideB", new Vector3(0f, ContHgt * 0.5f, s * sideZ),
                             new Vector3(ContLen, innerH, ContWall), paint, skin, collider: true);
            }

            // Corner posts and end rails frame the openings without narrowing them.
            foreach (int ex in new[] { -1, 1 })
            {
                float x = ex * (halfLen + ContWall * 0.5f);
                Prim.Surface(t, $"Rail{ex}Top", new Vector3(x, ContHgt - ContWall * 0.5f, 0f),
                             new Vector3(ContWall, ContWall * 1.3f, ContWid + ContWall), paint, tint * 0.7f, 0.45f, 0.3f);
                Prim.Surface(t, $"Rail{ex}Bot", new Vector3(x, ContWall * 0.5f, 0f),
                             new Vector3(ContWall, ContWall * 1.3f, ContWid + ContWall), paint, tint * 0.7f, 0.45f, 0.3f);

                foreach (int ez in new[] { -1, 1 })
                    Prim.Surface(t, $"Post{ex}{ez}", new Vector3(x, ContHgt * 0.5f, ez * (ContWid + ContWall) * 0.5f),
                                 new Vector3(ContWall, ContHgt, ContWall), paint, tint * 0.7f, 0.45f, 0.3f, collider: true);
            }

            // Door leaves, hinged at the +X corners and swung back 115 degrees so they sit
            // clear of the opening. A closed leaf points from its hinge toward z = 0;
            // swinging it by `openDeg` rotates that direction out past the end of the
            // container. The box's long axis is local X, so its yaw is whatever aligns +X
            // with that swung direction.
            const float leaf = 1.15f, openDeg = 115f;
            float a = openDeg * Mathf.Deg2Rad;

            foreach (int ez in new[] { -1, 1 })
            {
                var dir = new Vector2(Mathf.Sin(a), -ez * Mathf.Cos(a));
                var hinge = new Vector2(halfLen, ez * ContWid * 0.5f);
                Vector2 mid = hinge + dir * (leaf * 0.5f);
                float yawDeg = Mathf.Atan2(ez * Mathf.Cos(a), Mathf.Sin(a)) * Mathf.Rad2Deg;

                Prim.Surface(t, ez > 0 ? "DoorL" : "DoorR",
                             new Vector3(mid.x, ContHgt * 0.5f, mid.y),
                             new Vector3(leaf, ContHgt - 0.3f, 0.06f),
                             paint, tint * 0.8f, 0.4f, 0.35f, collider: true,
                             euler: new Vector3(0f, yawDeg, 0f));
            }
        }

        /// <param name="rows">Courses of bags. 3 is knee-high; 5 gives cover you can crouch
        /// fully behind and still stand up to shoot over.</param>
        static void SandbagWall(Transform parent, Vector3 pos, float yaw, int length, int rows = 3)
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

            for (int row = 0; row < rows; row++)
            {
                float y = 0.12f + row * 0.22f;
                float offset = (row % 2) * 0.19f;
                int count = Mathf.Max(1, length - row);
                for (int i = 0; i < count; i++)
                {
                    float x = (i - count * 0.5f) * 0.38f + offset;
                    var bag = Prim.Capsule(t, $"Bag{row}_{i}", new Vector3(x, y, 0f), 0.30f, 0.42f,
                                           (row + i) % 2 == 0 ? Mat.Canvas : Mat.SandDark,
                                           false, new Vector3(0f, 0f, 90f));
                    var burlap = SandbagMaterial(row + i);
                    if (burlap != null) Prim.SetMaterial(bag, burlap);
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

        /// <summary>
        /// An oil drum. About a third are burn barrels - lid off, fire inside - chosen by a
        /// hash of the position rather than by Random, so the choice can't shift the seeded
        /// layout of everything placed after it.
        /// </summary>
        static void Barrel(Transform parent, Vector3 pos, float yaw)
        {
            var t = Prim.Empty(parent, "Barrel", pos);
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);

            // Drawn whichever barrel gets built, to keep the seeded stream in step.
            Color c = Random.value < 0.5f ? Mat.Rust : new Color(0.20f, 0.26f, 0.19f);
            bool burning = FX.ParticleKit.Hash01(pos) < 0.3f;

            var model = burning ? Art.BurnBarrel : Art.Barrel;
            if (ArtLibrary.Has(model))
            {
                Prim.Prop(t, burning ? "BurnBarrel" : "Drum", model, Vector3.zero, Quaternion.identity);
                if (burning) QueueFire(t, pos + Vector3.up * (model.Size.y - 0.08f), 0.55f, 6.5f, false);
                return;
            }

            Prim.Pillar(t, "Body", new Vector3(0f, 0.44f, 0f), 0.58f, 0.88f, c, 0.45f, 0.5f, collider: true);
            Prim.Pillar(t, "RibA", new Vector3(0f, 0.28f, 0f), 0.62f, 0.06f, c * 0.75f, 0.45f, 0.5f);
            Prim.Pillar(t, "RibB", new Vector3(0f, 0.60f, 0f), 0.62f, 0.06f, c * 0.75f, 0.45f, 0.5f);

            if (burning)
            {
                // Charred rim and a bed of embers where the lid was.
                Prim.Pillar(t, "Char", new Vector3(0f, 0.87f, 0f), 0.56f, 0.03f, Mat.Charred, 0f, 0.1f);
                QueueFire(t, pos + Vector3.up * 0.88f, 0.55f, 6.5f, false);
            }
            else
            {
                Prim.Pillar(t, "Lid", new Vector3(0f, 0.89f, 0f), 0.60f, 0.04f, c * 0.85f, 0.5f, 0.5f);
            }
        }

        static void Crate(Transform parent, Vector3 pos, float yaw)
        {
            var t = Prim.Empty(parent, "Crate", pos);
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);

            float s = Random.Range(0.7f, 1.05f);
            if (ArtLibrary.Has(Art.Crate))
            {
                Prim.Prop(t, "Crate", Art.Crate, Vector3.zero, Quaternion.identity,
                          scale: Mathf.Lerp(0.85f, 1.1f, (s - 0.7f) / 0.35f));
                return;
            }

            Prim.Box(t, "Body", new Vector3(0f, s * 0.5f, 0f), new Vector3(s, s, s * 0.95f),
                     Mat.Wood, 0.05f, 0.4f, collider: true);
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
            var tyre = Art.Tyre;
            bool art = ArtLibrary.Has(tyre);
            float step = art ? tyre.Size.z + 0.004f : 0.19f;

            for (int i = 0; i < n; i++)
            {
                float yaw = Random.Range(0f, 360f);
                if (art)
                    // Laid flat: the model stands on its tread, so tip it over.
                    Prim.Prop(t, $"Tyre{i}", tyre, new Vector3(0f, step * (i + 0.5f), 0f),
                              Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(90f, 0f, 0f),
                              collider: false, pivot: Prim.Pivot.Centre);
                else
                    Prim.Pillar(t, $"Tyre{i}", new Vector3(0f, 0.10f + i * 0.19f, 0f), 0.92f, 0.18f,
                                new Color(0.08f, 0.08f, 0.09f), 0.1f, 0.5f, collider: i == 0)
                        .localRotation = Quaternion.Euler(0f, yaw, 0f);
            }

            // One collider for the whole stack, so a shot at the top tyre can't pass through.
            if (art)
            {
                var col = t.gameObject.AddComponent<BoxCollider>();
                col.center = new Vector3(0f, step * n * 0.5f, 0f);
                col.size = new Vector3(tyre.Size.x, step * n, tyre.Size.x);
            }
        }

        static void Barricade(Transform parent, Vector3 pos, float yaw)
        {
            var t = Prim.Empty(parent, "Barricade", pos);
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);

            if (ArtLibrary.Has(Art.Barrier))
            {
                // Two jersey barriers dropped end to end, slightly askew, as at a checkpoint.
                Prim.Prop(t, "BarrierL", Art.Barrier, new Vector3(-0.8f, 0f, 0f), Quaternion.Euler(0f, 3f, 0f));
                Prim.Prop(t, "BarrierR", Art.Barrier, new Vector3(0.8f, 0f, 0.06f), Quaternion.Euler(0f, -4f, 0f));
                return;
            }

            var body = Prim.Box(t, "Body", new Vector3(0f, 0.55f, 0f), new Vector3(2.4f, 1.1f, 0.35f),
                                Color.white, 0f, 0.1f, collider: true);
            Prim.SetMaterial(body, _concrete);
            Prim.Box(t, "Cap", new Vector3(0f, 1.13f, 0f), new Vector3(2.5f, 0.08f, 0.45f), Mat.Accent, 0f, 0.45f);
            Prim.Box(t, "FootL", new Vector3(-1.0f, 0.08f, 0f), new Vector3(0.4f, 0.16f, 0.8f), Mat.ConcreteHi, 0f, 0.1f);
            Prim.Box(t, "FootR", new Vector3(1.0f, 0.08f, 0f), new Vector3(0.4f, 0.16f, 0.8f), Mat.ConcreteHi, 0f, 0.1f);
        }

        // ------------------------------------------------------------------------ helper

        /// <summary>Burlap for a bag, alternating two dye lots; null without imported art.</summary>
        public static Material SandbagMaterial(int index)
        {
            var bag = index % 2 == 0 ? Art.SandbagA : Art.SandbagB;
            return ArtLibrary.Has(bag) ? bag.Material : null;
        }

        static Material Tiled(Texture2D tex, float tx, float ty, float smoothness, float metallic = 0f)
        {
            var m = Mat.Textured(tex, metallic, smoothness);
            m.SetTextureScale("_BaseMap", new Vector2(tx, ty));
            return m;
        }
    }
}
