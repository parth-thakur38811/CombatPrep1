using UnityEngine;

namespace CombatPrep.Weapons
{
    public enum FireMode { Auto, Semi, Burst }

    /// <summary>Hip-fire reticle. Only shown while the sights are down.</summary>
    public enum CrosshairStyle
    {
        /// <summary>Four spreading arms plus a centre dot.</summary>
        Cross,
        /// <summary>A single point - less clutter for a high-rate, wide-cone weapon.</summary>
        Dot,
        /// <summary>Nothing. Forces the shooter onto the sights, as a marksman rifle should.</summary>
        None
    }

    /// <summary>
    /// All weapon tuning in one place. Kept as a ScriptableObject so you can make real
    /// .asset files and tune them live in the Inspector, but Bootstrap builds them in code
    /// via CreateInstance so the project still runs with zero assets on disk.
    /// </summary>
    [CreateAssetMenu(menuName = "CombatPrep/Weapon", fileName = "Weapon")]
    public class WeaponDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string DisplayName = "Rifle";

        [Header("Firing")]
        public FireMode Mode = FireMode.Auto;
        public float RoundsPerMinute = 640f;
        public int BurstCount = 3;
        public float Damage = 24f;
        public float HeadshotMultiplier = 2.0f;
        public float LimbMultiplier = 0.8f;
        /// <summary>Rounds released per trigger pull. Above 1 turns the weapon into a shotgun.</summary>
        public int PelletsPerShot = 1;

        [Header("Ammo")]
        public int MagSize = 30;
        public int ReserveAmmo = 210;
        public float ReloadTime = 2.1f;
        public float ReloadTimeEmpty = 2.7f;   // slower when the bolt has to be released

        [Header("Ballistics")]
        public bool Hitscan = true;            // Milestone 2 flips this to projectile sim
        public float MuzzleVelocity = 880f;    // m/s, used when Hitscan is false
        public float MaxRange = 300f;
        public float DamageFalloffStart = 40f;
        public float DamageFalloffEnd = 140f;
        public float MinDamageFraction = 0.55f;

        [Header("Recoil - aim layer (degrees)")]
        public float RecoilVertical = 0.42f;        // upward kick on the first shot
        public float RecoilVerticalSustain = 0.55f; // fraction of that once the pattern settles
        public float RecoilHorizontal = 0.30f;      // width of the horizontal walk
        public float RecoilRandom = 0.12f;          // jitter as a fraction, keeps it from feeling robotic
        public float PatternRampShots = 6f;         // shots taken to reach the sustained climb
        public float PatternResetTime = 0.35f;      // idle time before the pattern restarts
        public float PatternSeed = 3f;
        public float AdsRecoilScale = 0.78f;

        [Header("Spread - cone (degrees)")]
        public float BaseSpread = 0.22f;
        public float SpreadPerShot = 0.16f;
        public float MaxSpread = 3.2f;
        public float SpreadDecay = 5.0f;            // degrees recovered per second
        public float SpreadDecayDelay = 0.09f;
        public float MoveSpreadScale = 2.4f;        // multiplied by normalized speed
        public float AdsSpreadScale = 0.35f;
        public float CrouchSpreadScale = 0.7f;
        /// <summary>Airborne accuracy penalty. Kept punishing but survivable, so jump-firing
        /// is a real option with a cost rather than a guaranteed miss.</summary>
        public float AirSpreadScale = 2.2f;

        [Header("Visual recoil - does not affect aim")]
        public float KickBack = 0.045f;    // metres the model punches toward the player
        public float KickUp = 0.018f;
        public float KickRoll = 3.2f;      // degrees
        public float KickPitch = 4.5f;
        public float KickStiffness = 220f;
        public float KickDamping = 18f;

        [Header("Reticle")]
        public CrosshairStyle Crosshair = CrosshairStyle.Cross;
        public Color CrosshairColor = new Color(1f, 0.22f, 0.16f, 0.95f);

        [Header("Handling")]
        public float AdsTime = 0.16f;
        /// <summary>Look-sensitivity multiplier while aiming. Magnified optics need this well below 1.</summary>
        public float AdsSensScale = 0.72f;
        public float HipFov = 78f;
        public float AdsFov = 58f;
        public float SwayAmount = 0.012f;
        public float BobAmount = 0.022f;

        [Header("Audio synth")]
        public float ShotGain = 0.85f;
        public float ShotDecay = 26f;      // higher = tighter, snappier report
        public float ShotBodyHz = 150f;    // low-end thump
        public float ShotCrack = 0.7f;     // high-frequency crack amount
        public float ShotTail = 0.22f;     // room decay

        [Header("Camera shake")]
        public float ShakeAmplitude = 0.35f;
        public float ShakeDuration = 0.12f;

        public float ShotInterval => 60f / Mathf.Max(1f, RoundsPerMinute);
    }
}
