using System.Collections.Generic;
using UnityEngine;
using CombatPrep.Core;

namespace CombatPrep.FX
{
    /// <summary>
    /// Muzzle flashes, tracers, impacts, bullet holes and explosions - each one a prefab in
    /// Prefabs/FX (see FxRecipes for how they were first made), so they can be opened and
    /// tuned in the editor. If a prefab is missing, the same effect is built from its recipe.
    ///
    /// Everything that fires constantly is pooled; nothing allocates after warm-up, which
    /// matters once you are dumping 600 RPM downrange.
    /// </summary>
    public class FxSystem : MonoBehaviour
    {
        public static FxSystem I { get; private set; }

        /// <summary>Spent debris lives here so bullets pass straight through it.</summary>
        public const int DebrisLayer = 9;

        [Header("Pools")]
        public int MaxHoles = 120;
        public int ImpactPool = 24;
        public int RemoteFlashPool = 8;

        readonly List<TracerFx> _tracers = new();
        readonly List<FlashFx> _impacts = new();
        readonly List<FlashFx> _remoteFlashes = new();
        readonly Queue<Transform> _holes = new();
        int _nextImpact, _nextRemote;

        static ArtLibrary.Effects Lib => ArtLibrary.I != null ? ArtLibrary.I.Fx : null;

        void Awake() => I = this;

        /// <summary>A copy of an effect: its prefab if there is one, otherwise built from its recipe.</summary>
        static GameObject Spawn(GameObject prefab, System.Func<GameObject> recipe, Transform parent)
        {
            var go = prefab != null ? Instantiate(prefab) : recipe();
            go.transform.SetParent(parent, false);
            return go;
        }

        // ------------------------------------------------------------------ muzzle flash

        /// <summary>
        /// A flash riding a gun's muzzle, for that gun to play when it fires. Every gun gets its
        /// own - each of yours, and each one another player is seen holding.
        /// </summary>
        public FlashFx AttachMuzzle(Transform muzzle)
        {
            var go = Spawn(Lib?.MuzzleFlash, FxRecipes.MuzzleFlash, muzzle);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            return go.GetComponent<FlashFx>();
        }

        /// <summary>A flash at an arbitrary point, for a gun with none attached.</summary>
        public void RemoteMuzzleFlash(Vector3 position)
        {
            FlashFx flash;
            if (_remoteFlashes.Count < RemoteFlashPool)
            {
                flash = Spawn(Lib?.MuzzleFlash, FxRecipes.MuzzleFlash, transform).GetComponent<FlashFx>();
                _remoteFlashes.Add(flash);
            }
            else
            {
                flash = _remoteFlashes[_nextRemote];
                _nextRemote = (_nextRemote + 1) % _remoteFlashes.Count;
            }
            flash.transform.position = position;
            flash.Play();
        }

        // ------------------------------------------------------------------------ tracer

        public void Tracer(Vector3 from, Vector3 to)
        {
            TracerFx tracer = null;
            foreach (var t in _tracers)
                if (!t.gameObject.activeSelf) { tracer = t; break; }

            if (tracer == null)
            {
                tracer = Spawn(Lib?.Tracer, FxRecipes.Tracer, transform).GetComponent<TracerFx>();
                _tracers.Add(tracer);
            }
            tracer.Fire(from, to);
        }

        // ------------------------------------------------------------------------ impact

        /// <summary>
        /// Impact burst: dust and chips in the surface's colour, sparks if it's hard, and a hole.
        /// Pass <paramref name="attachTo"/> for surfaces that move (a swinging target board) so
        /// the hole travels with them instead of hanging in world space.
        /// </summary>
        /// <param name="debrisCount">How much flies off: -1 for the effect's own amount, or a
        /// count where 4 is a full burst.</param>
        public void Impact(Vector3 point, Vector3 normal, Color surfaceTint,
                           Transform attachTo = null, int debrisCount = -1, bool spark = true,
                           bool hole = true)
        {
            FlashFx fx;
            if (_impacts.Count < ImpactPool)
            {
                fx = Spawn(Lib?.Impact, FxRecipes.Impact, transform).GetComponent<FlashFx>();
                _impacts.Add(fx);
            }
            else
            {
                fx = _impacts[_nextImpact];
                _nextImpact = (_nextImpact + 1) % _impacts.Count;
            }

            fx.transform.SetPositionAndRotation(point + normal * 0.01f, Quaternion.LookRotation(normal));
            fx.Play(debrisCount < 0 ? 1f : debrisCount / 4f, surfaceTint, spark);

            if (hole) BulletHole(point, normal, attachTo);
        }

        void BulletHole(Vector3 point, Vector3 normal, Transform attachTo)
        {
            var q = Spawn(Lib?.BulletHole, FxRecipes.BulletHole, attachTo != null ? attachTo : transform).transform;
            // Lift off the surface to avoid z-fighting, and face outward.
            q.position = point + normal * 0.004f;
            q.rotation = Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            q.localScale = Vector3.one * Random.Range(0.03f, 0.045f);
            if (attachTo != null)
            {
                // Undo the target's scale so every hole is the same size.
                var s = attachTo.lossyScale;
                q.localScale = new Vector3(q.localScale.x / Mathf.Max(1e-4f, s.x),
                                           q.localScale.y / Mathf.Max(1e-4f, s.y),
                                           q.localScale.z / Mathf.Max(1e-4f, s.z));
                return;   // holes on a target die with that target's board on respawn
            }

            _holes.Enqueue(q);
            while (_holes.Count > MaxHoles)
            {
                var old = _holes.Dequeue();
                if (old != null) Destroy(old.gameObject);
            }
        }

        // ------------------------------------------------------------------- explosion

        /// <summary>
        /// Grenade blast: the Explosion prefab scaled to the blast radius - fireball, smoke,
        /// sparks, debris, a flash of light and a scorch mark. It removes itself afterwards.
        /// </summary>
        public void Explosion(Vector3 centre, float radius)
        {
            var go = Spawn(Lib?.Explosion, FxRecipes.Explosion, transform);
            go.transform.SetPositionAndRotation(centre, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            go.transform.localScale = Vector3.one * (radius / 4f);
            var light = go.GetComponentInChildren<Light>(true);
            if (light != null) light.range = radius * 3.2f;
            go.GetComponent<FlashFx>().Play();
        }
    }
}
