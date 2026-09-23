using System.Collections.Generic;
using UnityEngine;
using CombatPrep.Audio;
using CombatPrep.Core;
using CombatPrep.FX;
using CombatPrep.Targets;
using CombatPrep.UI;

namespace CombatPrep.Weapons
{
    /// <summary>
    /// A thrown fragmentation grenade: rigidbody flight, fuse, then a radial blast with
    /// four damage bands.
    ///
    /// Flight uses a plain Rigidbody under standard gravity with no drag, which matters
    /// because the aiming arc is drawn by integrating that same parabola. Adding drag here
    /// without adding it to the preview would make the red line quietly lie.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Grenade : MonoBehaviour
    {
        [Header("Fuse")]
        public float FuseSeconds = 2.4f;

        [Header("Blast bands (metres)")]
        public float CloseRadius = 3.5f;    // 100% damage
        public float MediumRadius = 6.5f;   //  50%
        public float OuterRadius = 10f;     //  25%, and nothing past it

        [Header("Damage")]
        public float BaseDamage = 120f;

        public LayerMask BlastMask = ~0;

        Rigidbody _rb;
        float _explodeAt;
        bool _spent;

        /// <summary>Fraction of BaseDamage at a given distance from the blast centre.</summary>
        public float FalloffAt(float distance)
        {
            if (distance <= CloseRadius) return 1f;
            if (distance <= MediumRadius) return 0.5f;
            if (distance <= OuterRadius) return 0.25f;
            return 0f;
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
            if (_spent || Time.time < _explodeAt) return;
            Explode();
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
            var nearest = new Dictionary<Target, (float dist, Vector3 point, Zone zone)>();

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

                Hud.I.ReportHit(info.Damage, false, info.Killed, kv.Value.point);
            }
        }

        /// <summary>Solid geometry between the blast and the target shields it.</summary>
        bool HasLineOfSight(Vector3 centre, Vector3 point, Target owner)
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
