using UnityEngine;

namespace CombatPrep.Weapons
{
    /// <summary>
    /// Produces the per-shot aim impulse from a deterministic, learnable spray pattern
    /// (the CS/Valorant model) rather than pure randomness.
    ///
    /// The pattern is generated from seeded value noise instead of a hand-authored array:
    /// same shot index always gives the same offset, so a player can memorise and
    /// counter-pull it, but authoring a new weapon is one seed rather than 30 keyframes.
    /// A small jitter on top keeps it from feeling mechanical.
    /// </summary>
    public class RecoilSystem
    {
        readonly WeaponDefinition _def;
        int _shotIndex;
        float _lastShotTime = -99f;

        public int ShotIndex => _shotIndex;

        public RecoilSystem(WeaponDefinition def) { _def = def; }

        public Vector2 NextImpulse(bool aiming)
        {
            if (Time.time - _lastShotTime > _def.PatternResetTime) _shotIndex = 0;
            _lastShotTime = Time.time;

            float t = _shotIndex;

            // Vertical: hard first kick that tapers into a steady climb.
            float ramp = Mathf.Clamp01(t / Mathf.Max(1f, _def.PatternRampShots));
            float vertical = _def.RecoilVertical * Mathf.Lerp(1f, _def.RecoilVerticalSustain, ramp);

            // Horizontal: a seeded walk. Held near zero for the opening shots so the gun
            // climbs straight up first, then widens - the classic AK/M4 shape.
            float noise = Mathf.PerlinNoise(_def.PatternSeed * 13.1f + t * 0.34f, _def.PatternSeed * 4.7f) * 2f - 1f;
            float horizontal = noise * _def.RecoilHorizontal * Mathf.Clamp01(t / 2.5f);

            // Jitter, expressed as a fraction of each axis so it scales with the weapon.
            vertical   += Random.Range(-1f, 1f) * _def.RecoilRandom * _def.RecoilVertical;
            horizontal += Random.Range(-1f, 1f) * _def.RecoilRandom * _def.RecoilHorizontal;

            _shotIndex++;

            float scale = aiming ? _def.AdsRecoilScale : 1f;
            return new Vector2(vertical, horizontal) * scale;
        }

        public void Reset() => _shotIndex = 0;
    }
}
