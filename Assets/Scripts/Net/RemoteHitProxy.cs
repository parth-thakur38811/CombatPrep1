using UnityEngine;
using CombatPrep.Targets;

namespace CombatPrep.Net
{
    /// <summary>
    /// Stands in for a remote player wherever the weapon code expects something damageable.
    /// The avatar's hitzone colliders point here.
    ///
    /// It never changes health itself - only the server may. When your bullet hits it, it
    /// queues the hit on your own NetPlayer, which sends every hit from that trigger pull to
    /// the server in one message, and returns a *provisional* result so your hitmarker and
    /// damage number appear instantly instead of a round-trip later. The server recomputes
    /// the damage from your weapon, the zone and the distance; the client's number is never
    /// trusted.
    ///
    /// Hits only count while <see cref="Collector"/> is set, which the local shooter does for
    /// the duration of a single trigger pull. Anything else that deals damage (a grenade) gets
    /// a zero result here - online, blast damage is the server's job.
    /// </summary>
    public class RemoteHitProxy : MonoBehaviour, IDamageable
    {
        /// <summary>The local shooter collecting hits during one trigger pull, or null.</summary>
        public static NetPlayer Collector;

        public NetPlayer Player;

        public bool IsAlive => Player != null && Player.IsAlive;

        /// <summary>No holes on players: they would hang in mid-air once the player moves.</summary>
        public Transform HoleAnchor => null;

        public HitInfo ApplyDamage(float baseDamage, Zone zone, Vector3 point, Vector3 direction,
                                   float headMult, float limbMult, float distance)
        {
            var info = new HitInfo { Target = this, Zone = zone, Point = point, Distance = distance };
            if (Collector == null || !IsAlive) return info;

            float mult = zone == Zone.Head ? headMult : zone == Zone.Limb ? limbMult : 1f;
            info.Damage = baseDamage * mult;
            info.Killed = Player.Health.Value - info.Damage <= 0f;   // provisional - the server decides

            Collector.QueueHit(Player, zone, distance);
            return info;
        }
    }
}
