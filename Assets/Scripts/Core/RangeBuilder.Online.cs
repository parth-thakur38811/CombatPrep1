using UnityEngine;

namespace CombatPrep.Core
{
    /// <summary>
    /// Extra cover for online matches, in the middle lanes that practice keeps clear for its
    /// paper targets. Without it the centre of the arena is an open field, and a deathmatch
    /// there is decided by whoever spawned with the longest sightline.
    ///
    /// It is built with the rest of the arena - so it is identical on every machine - and
    /// simply hidden in practice. The layout mirrors left-right and front-back about the
    /// arena's centre (0, 42): spawn points come in opposite pairs, so whichever one you get,
    /// the cover ahead of you has the same shape.
    ///
    /// Heights are mixed on purpose: a container and concrete blast walls that hide you
    /// completely, sandbags and jersey barriers you crouch behind (Left Ctrl), and
    /// see-through hedgehogs and low crates that only half-help.
    /// </summary>
    public static partial class RangeBuilder
    {
        static GameObject _onlineCover;

        /// <summary>Show the online-only cover (true) or hide it for practice (false).</summary>
        public static void SetOnlineCover(bool on)
        {
            if (_onlineCover != null) _onlineCover.SetActive(on);
        }

        static void OnlineCover(Transform root)
        {
            var t = Prim.Empty(root, "OnlineCover");
            _onlineCover = t.gameObject;

            // Centre: a container across the middle, doors open at both ends to run through,
            // with a burn barrel either side lighting it.
            Container(t, AtCentre(0f, 0f), 8f, Mat.ContainerC, Art.ContainerC, 7);
            BuildBarrel(t, AtCentre(5f, -2.5f), 0f, Mat.Rust, burning: true);
            BuildBarrel(t, AtCentre(-5f, 2.5f), 0f, Mat.Rust, burning: true);

            foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
            {
                float turn = sx * sz;   // a mirrored piece turns the other way

                // Blast-wall pairs 9 m either side of the centre: full-height cover in the lanes.
                TWallPair(t, AtCentre(5.5f * sx, 9f * sz), 4f * turn);

                // Jersey barriers along the sides, lying lengthways to cover a run up the lane.
                Barricade(t, AtCentre(11f * sx, 3f * sz), 90f + 5f * turn);

                // Crouch-height sandbags toward each end, angled in toward the middle.
                SandbagWall(t, AtCentre(10f * sx, 18f * sz), 25f * turn, 6, 5);

                // Hedgehogs out wide, crates near the centre line.
                Hedgehog(t, AtCentre(13.5f * sx, 12f * sz), 30f * turn);
                Crate(t, AtCentre(3f * sx, 22f * sz), 90f + 10f * turn);
            }

            _onlineCover.SetActive(false);   // practice first; Bootstrap turns it on for online
        }

        static Vector3 AtCentre(float x, float dz) => new(x, 0f, ArenaCentre.z + dz);

        /// <summary>Two T-walls side by side, with a slit between them just wide enough to shoot through.</summary>
        static void TWallPair(Transform parent, Vector3 pos, float yaw)
        {
            var t = Prim.Empty(parent, "TWalls", pos);
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);
            TWall(t, new Vector3(-0.84f, 0f, 0f));
            TWall(t, new Vector3(0.84f, 0f, 0f));
        }

        /// <summary>
        /// A T-wall: a tall precast slab on a wide foot - the blast wall of every forward base.
        /// At 3.3 m it hides a standing player completely.
        /// </summary>
        static void TWall(Transform parent, Vector3 local)
        {
            var t = Prim.Empty(parent, "TWall", local);
            Prim.Surface(t, "Slab", new Vector3(0f, 1.75f, 0f), new Vector3(1.5f, 3.1f, 0.26f),
                         Art.Concrete, _concreteWall, collider: true);
            Prim.Surface(t, "Foot", new Vector3(0f, 0.2f, 0f), new Vector3(1.5f, 0.4f, 1.1f),
                         Art.Concrete, _concreteWall, collider: true);
            Prim.Box(t, "LiftingEye", new Vector3(0f, 3.34f, 0f), new Vector3(0.12f, 0.08f, 0.05f),
                     Mat.RustDark, 0.6f, 0.45f);
        }
    }
}
