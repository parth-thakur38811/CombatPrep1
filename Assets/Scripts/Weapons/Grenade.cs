using System.Collections.Generic;
using UnityEngine;
using CombatPrep.Audio;
using CombatPrep.Core;
using CombatPrep.FX;
using CombatPrep.Targets;
using CombatPrep.UI;

namespace CombatPrep.Weapons
{
    /// <summary>What one grenade object is for.</summary>
    public enum GrenadeRole
    {
        /// <summary>Practice: flies, explodes and damages targets itself.</summary>
        Practice,
        /// <summary>Online, on every screen: flies and bounces, but the server decides the blast.</summary>
        Visual,
        /// <summary>Online, on the server only and unseen: the real one, whose fuse sets off the blast.</summary>
        Authority
    }

    /// <summary>
    /// A thrown fragmentation grenade: rigidbody flight, fuse, then a radial blast with
    /// four damage bands.
    ///
    /// Flight uses a plain Rigidbody under standard gravity with no drag, which matters
    /// because the aiming arc is drawn by integrating that same parabola. Adding drag here
    /// without adding it to the preview would make the red line quietly lie.
    ///
    /// Online, one throw becomes several grenades: an unseen Authority copy on the server,
    /// whose fuse sets off the blast and the damage (NetPlayer), and a Visual copy on every
    /// screen that just flies until the server says where it went off.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Grenade : MonoBehaviour
    {
        // The standard frag, shared by the thrower's settings and the server's blast.
        public const float DefaultFuse = 2.4f;
        public const float DefaultClose = 3.5f, DefaultMedium = 6.5f, DefaultOuter = 10f;
        public const float DefaultDamage = 120f;

        /// <summary>Grenades fly on the debris layer: bullets pass through them, and they don't hit each other.</summary>
        public static int Layer => FxSystem.DebrisLayer;

        [Header("Fuse")]
        public float FuseSeconds = DefaultFuse;

        [Header("Blast bands (metres)")]
        public float CloseRadius = DefaultClose;    // 100% damage
        public float MediumRadius = DefaultMedium;  //  50%
        public float OuterRadius = DefaultOuter;    //  25%, and nothing past it

        [Header("Damage")]
        public float BaseDamage = DefaultDamage;

        public LayerMask BlastMask = ~0;
        public GrenadeRole Role = GrenadeRole.Practice;

        /// <summary>Authority grenades: called at the end of the fuse with where it went off.</summary>
        public System.Action<Vector3> Detonated;

        Rigidbody _rb;
        float _explodeAt;
        bool _spent;

        /// <summary>Fraction of BaseDamage at a given distance from the blast centre.</summary>
        public float FalloffAt(float distance) => Falloff(distance, CloseRadius, MediumRadius, OuterRadius);

        /// <summary>The standard frag's falloff: 100 / 50 / 25 % bands, nothing beyond.</summary>
        public static float DefaultFalloff(float distance) => Falloff(distance, DefaultClose, DefaultMedium, DefaultOuter);

        static float Falloff(float distance, float close, float medium, float outer)
        {
            if (distance <= close) return 1f;
            if (distance <= medium) return 0.5f;
            if (distance <= outer) return 0.25f;
            return 0f;
        }

        // ------------------------------------------------------------------ spawning

        /// <summary>A grenade in flight. Authority grenades have no model - nobody sees them.</summary>
        public static Grenade Spawn(Vector3 origin, Vector3 velocity, GrenadeRole role, LayerMask blastMask)
        {
            var go = new GameObject(role == GrenadeRole.Authority ? "GrenadeAuthority" : "Grenade");
            go.transform.position = origin;
            go.layer = Layer;
            Physics.IgnoreLayerCollision(Layer, Layer, true);   // the server's two copies overlap
            if (role != GrenadeRole.Authority) BuildModel(go.transform);

            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.055f;

            var nade = go.AddComponent<Grenade>();
            nade.Role = role;
            nade.Launch(origin, velocity, blastMask);
            return nade;
        }

        // Visual grenades by (thrower, throw number), so the server's word can find them.
        static readonly Dictionary<(ulong, int), Grenade> Visuals = new();

        public static void SpawnVisual(ulong thrower, int id, Vector3 origin, Vector3 velocity)
        {
            RemoveVisual(thrower, id);
            Visuals[(thrower, id)] = Spawn(origin, velocity, GrenadeRole.Visual, 0);
        }

        public static void RemoveVisual(ulong thrower, int id)
        {
            if (!Visuals.TryGetValue((thrower, id), out var g)) return;
            Visuals.Remove((thrower, id));
            if (g != null) Destroy(g.gameObject);
        }

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.useGravity = true;
            _rb.linearDamping = 0f;         // must match the preview integrator
            _rb.angularDamping = 0.25f;
            _rb.mass = 0.4f;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        public void Launch(Vector3 position, Vector3 velocity, LayerMask blastMask)
        {
            transform.position = position;
            BlastMask = blastMask;
            _rb.position = position;
            _rb.linearVelocity = velocity;
            _rb.angularVelocity = Random.insideUnitSphere * 12f;
            _explodeAt = Time.time + FuseSeconds;
        }

        void OnCollisionEnter(Collision c)
        {
            // Metallic clatter on every bounce, quieter the softer the impact.
            float force = Mathf.Clamp01(c.relativeVelocity.magnitude / 14f);
            if (force > 0.08f)
                GameAudio.I.PlayAt(GameAudio.I.BoltRelease, transform.position, 0.25f + force * 0.35f, 0.18f);
        }

        void Update()
        {
            if (_spent) return;
            switch (Role)
            {
                case GrenadeRole.Practice:
                    if (Time.time >= _explodeAt) Explode();
                    break;
                case GrenadeRole.Authority:
                    if (Time.time < _explodeAt) break;
                    _spent = true;
                    Detonated?.Invoke(transform.position);
                    Destroy(gameObject);
                    break;
                case GrenadeRole.Visual:
                    // The server's explosion normally removes it first; this only catches a
                    // grenade whose thrower left mid-flight.
                    if (Time.time >= _explodeAt + 3f) Destroy(gameObject);
                    break;
            }
        }

        void Explode()
        {
            _spent = true;
            Vector3 centre = transform.position;

            FxSystem.I.Explosion(centre, OuterRadius);
            GameAudio.I.PlayAt(GameAudio.I.Explosion, centre, 1f, 0.05f);

            ApplyBlast(centre);
            Destroy(gameObject);
        }

        void ApplyBlast(Vector3 centre)
        {
            // A target has several hitzone colliders, so collect the nearest hit per target
            // and resolve once - otherwise a grenade would deal its damage three times over.
            var nearest = new Dictionary<IDamageable, (float dist, Vector3 point, Zone zone)>();

            foreach (var col in Physics.OverlapSphere(centre, OuterRadius, BlastMask,
                                                      QueryTriggerInteraction.Ignore))
            {
                var zone = col.GetComponent<HitZone>();
                if (zone == null || zone.Owner == null || !zone.Owner.IsAlive) continue;

                Vector3 point = col.ClosestPoint(centre);
                float dist = Vector3.Distance(centre, point);
                if (dist > OuterRadius) continue;
                if (!HasLineOfSight(centre, point, zone.Owner)) continue;

                if (!nearest.TryGetValue(zone.Owner, out var best) || dist < best.dist)
                    nearest[zone.Owner] = (dist, point, zone.Zone);
            }

            foreach (var kv in nearest)
            {
                float fraction = FalloffAt(kv.Value.dist);
                if (fraction <= 0f) continue;

                Vector3 dir = (kv.Value.point - centre).normalized;

                // Blast damage ignores the hitzone multiplier - a grenade does not care
                // whether the fragment that reached you hit a head or a leg.
                var info = kv.Key.ApplyDamage(BaseDamage * fraction, Zone.Body,
                                              kv.Value.point, dir, 1f, 1f, kv.Value.dist);

                // Something that declined the damage (a remote player, whose blast damage the
                // server resolves) mustn't flash a hitmarker for a hit that never landed.
                if (info.Damage > 0f)
                    Hud.I.ReportHit(info.Damage, false, info.Killed, kv.Value.point);
            }
        }

        /// <summary>Solid geometry between the blast and the target shields it.</summary>
        bool HasLineOfSight(Vector3 centre, Vector3 point, IDamageable owner)
        {
            Vector3 delta = point - centre;
            float dist = delta.magnitude;
            if (dist < 0.05f) return true;

            if (!Physics.Raycast(centre, delta / dist, out var hit, dist - 0.02f,
                                 BlastMask, QueryTriggerInteraction.Ignore))
                return true;

            // Hitting another part of the same target still counts as reaching it.
            var zone = hit.collider.GetComponent<HitZone>();
            return zone != null && zone.Owner == owner;
        }

        // ------------------------------------------------------------------ construction

        /// <summary>Builds the grenade body from primitives - a body, a fuse cap and a lever.</summary>
        public static Transform BuildModel(Transform parent, string name = "Grenade")
        {
            var root = Prim.Empty(parent, name);

            var body = Prim.Ball(root, "Body", Vector3.zero, 0.105f, new Color(0.20f, 0.26f, 0.19f), 0.35f, 0.30f);
            body.localScale = new Vector3(0.095f, 0.115f, 0.095f);

            Prim.Pillar(root, "Cap", new Vector3(0f, 0.062f, 0f), 0.042f, 0.030f, Mat.Steel, 0.75f, 0.45f);
            Prim.Box(root, "Lever", new Vector3(0.028f, 0.052f, 0f), new Vector3(0.012f, 0.070f, 0.022f),
                     Mat.Steel, 0.75f, 0.5f, false, new Vector3(0f, 0f, 8f));
            Prim.Pillar(root, "Ring", new Vector3(-0.030f, 0.066f, 0f), 0.030f, 0.006f,
                        new Color(0.72f, 0.66f, 0.30f), 0.8f, 0.6f, false, new Vector3(90f, 0f, 0f));

            return root;
        }
    }
}
