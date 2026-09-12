using UnityEngine;

namespace CombatPrep.Weapons
{
    /// <summary>
    /// Bullet spread cone, tracked separately from recoil. Recoil moves your aim
    /// predictably; spread is the unpredictable part. Keeping them apart is what lets the
    /// crosshair honestly show one of them (spread) while the player learns the other.
    /// </summary>
    public class SpreadSystem
    {
        readonly WeaponDefinition _def;
        float _bloom;          // accumulated spread from firing, in degrees
        float _lastShotTime = -99f;

        /// <summary>Total cone half-angle in degrees, including all modifiers.</summary>
        public float Current { get; private set; }

        public SpreadSystem(WeaponDefinition def) { _def = def; }

        public void Tick(float dt, float normalizedSpeed, bool aiming, bool crouching, bool grounded)
        {
            if (Time.time - _lastShotTime > _def.SpreadDecayDelay)
                _bloom = Mathf.Max(0f, _bloom - _def.SpreadDecay * dt);

            float spread = _def.BaseSpread + _bloom;
            spread += normalizedSpeed * _def.MoveSpreadScale;

            if (aiming) spread *= _def.AdsSpreadScale;
            if (crouching) spread *= _def.CrouchSpreadScale;
            if (!grounded) spread *= _def.AirSpreadScale;

            Current = Mathf.Min(spread, _def.MaxSpread);
        }

        public void OnShot()
        {
            _bloom = Mathf.Min(_bloom + _def.SpreadPerShot, _def.MaxSpread);
            _lastShotTime = Time.time;
        }

        public void Reset() { _bloom = 0f; Current = _def.BaseSpread; }

        /// <summary>Scatters a direction inside a cone of the given half-angle.</summary>
        public static Vector3 ApplyCone(Vector3 forward, float degrees)
        {
            if (degrees <= 0.0001f) return forward;
            Vector2 disc = Random.insideUnitCircle * degrees;
            return Quaternion.LookRotation(forward) * Quaternion.Euler(disc.y, disc.x, 0f) * Vector3.forward;
        }
    }
}
