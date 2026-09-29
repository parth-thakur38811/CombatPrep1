using UnityEngine;

namespace CombatPrep.Targets
{
    /// <summary>
    /// Anything a bullet or blast can hurt. Hitzone colliders point at one of these, so the
    /// weapon code never needs to know whether it hit a paper target (Practice) or another
    /// player (Online) - each resolves the damage its own way.
    /// </summary>
    public interface IDamageable
    {
        bool IsAlive { get; }

        /// <summary>
        /// Where bullet holes should be parented so they move with the thing that was hit,
        /// or null for "leave no holes" - holes floating off a moving player look broken.
        /// </summary>
        Transform HoleAnchor { get; }

        HitInfo ApplyDamage(float baseDamage, Zone zone, Vector3 point, Vector3 direction,
                            float headMult, float limbMult, float distance);
    }
}
