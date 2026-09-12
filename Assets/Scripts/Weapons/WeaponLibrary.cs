using UnityEngine;

namespace CombatPrep.Weapons
{
    public class WeaponEntry
    {
        public string Id;
        public string Role;
        public WeaponDefinition Def;
        public WeaponShape Shape;
    }

    /// <summary>
    /// The weapon roster, defined entirely in code. Each entry pairs tuning
    /// (WeaponDefinition) with proportions (WeaponShape), so adding a weapon is one block
    /// here rather than a new model and a new asset.
    ///
    /// The five are deliberately spread across the handling space: a balanced rifle, a
    /// fast-and-loose SMG, a slow precise DMR, a snappy sidearm, and a shotgun that trades
    /// everything for close-range burst damage.
    /// </summary>
    public static class WeaponLibrary
    {
        static WeaponEntry[] _all;

        public static WeaponEntry[] All => _all ??= Build();

        static WeaponEntry[] Build() => new[]
        {
            Carbine(), Smg(), Dmr(), Pistol(), Shotgun()
        };

        // ------------------------------------------------------------------- assault rifle

        static WeaponEntry Carbine()
        {
            var d = ScriptableObject.CreateInstance<WeaponDefinition>();
            d.DisplayName = "MK-4 CARBINE";
            d.Mode = FireMode.Auto;
            d.RoundsPerMinute = 640f;
            d.Damage = 26f;
            d.MagSize = 30; d.ReserveAmmo = 240;
            d.ReloadTime = 2.1f; d.ReloadTimeEmpty = 2.7f;

            d.RecoilVertical = 0.40f; d.RecoilVerticalSustain = 0.55f;
            d.RecoilHorizontal = 0.30f; d.PatternRampShots = 6f; d.PatternSeed = 3f;

            d.BaseSpread = 0.20f; d.SpreadPerShot = 0.15f; d.MaxSpread = 3.0f;
            d.HipFov = 78f; d.AdsFov = 52f; d.AdsTime = 0.17f;

            d.ShotGain = 0.85f; d.ShotDecay = 26f; d.ShotBodyHz = 150f; d.ShotCrack = 0.70f;
            d.MuzzleVelocity = 880f;

            return new WeaponEntry
            {
                Id = "carbine", Role = "Assault Rifle", Def = d,
                Shape = new WeaponShape
                {
                    ReceiverLen = 0.30f, ReceiverH = 0.086f, ReceiverW = 0.052f,
                    BarrelLen = 0.20f, BarrelDia = 0.020f,
                    HandguardLen = 0.24f,
                    Stock = true, StockLen = 0.22f,
                    Magazine = true, MagLen = 0.170f, MagWidth = 0.028f,
                    Optic = OpticType.RedDot, OpticBore = 0.072f, OpticLen = 0.068f,
                    SightDistance = 0.195f
                }
            };
        }

        // ----------------------------------------------------------------------------- smg

        static WeaponEntry Smg()
        {
            var d = ScriptableObject.CreateInstance<WeaponDefinition>();
            d.DisplayName = "WASP-9";
            d.Mode = FireMode.Auto;
            d.RoundsPerMinute = 950f;
            d.Damage = 17f;
            d.MagSize = 35; d.ReserveAmmo = 280;
            d.ReloadTime = 1.8f; d.ReloadTimeEmpty = 2.3f;

            // Less kick per round, but far more of them - climbs fast if you hold it down.
            d.RecoilVertical = 0.26f; d.RecoilVerticalSustain = 0.62f;
            d.RecoilHorizontal = 0.34f; d.PatternRampShots = 5f; d.PatternSeed = 11f;

            d.BaseSpread = 0.34f; d.SpreadPerShot = 0.13f; d.MaxSpread = 4.2f;
            d.MoveSpreadScale = 1.7f;              // forgiving on the move, by design
            d.Crosshair = CrosshairStyle.Dot;      // a wide bloom reads better as one point
            d.HipFov = 80f; d.AdsFov = 60f; d.AdsTime = 0.12f;

            d.ShotGain = 0.72f; d.ShotDecay = 34f; d.ShotBodyHz = 190f; d.ShotCrack = 0.62f;
            d.KickBack = 0.030f; d.KickPitch = 3.2f;
            d.MuzzleVelocity = 400f;

            return new WeaponEntry
            {
                Id = "smg", Role = "Submachine Gun", Def = d,
                Shape = new WeaponShape
                {
                    ReceiverLen = 0.235f, ReceiverH = 0.080f, ReceiverW = 0.050f,
                    BarrelLen = 0.11f, BarrelDia = 0.017f,
                    HandguardLen = 0.13f, Foregrip = true,
                    Stock = true, StockLen = 0.14f,
                    Magazine = true, MagLen = 0.200f, MagWidth = 0.026f,
                    Optic = OpticType.RedDot, OpticBore = 0.068f, OpticLen = 0.060f,
                    SightDistance = 0.185f
                }
            };
        }

        // ----------------------------------------------------------------------------- dmr

        static WeaponEntry Dmr()
        {
            var d = ScriptableObject.CreateInstance<WeaponDefinition>();
            d.DisplayName = "LONGBOW DMR";
            d.Mode = FireMode.Semi;
            d.RoundsPerMinute = 240f;
            d.Damage = 62f;
            d.HeadshotMultiplier = 2.5f;
            d.MagSize = 20; d.ReserveAmmo = 120;
            d.ReloadTime = 2.5f; d.ReloadTimeEmpty = 3.1f;

            d.RecoilVertical = 1.10f; d.RecoilVerticalSustain = 0.85f;
            d.RecoilHorizontal = 0.22f; d.PatternRampShots = 3f; d.PatternSeed = 23f;
            d.PatternResetTime = 0.5f;

            d.BaseSpread = 0.05f; d.SpreadPerShot = 0.50f; d.MaxSpread = 3.5f;
            d.SpreadDecay = 3.2f; d.AdsSpreadScale = 0.10f;
            d.HipFov = 78f; d.AdsFov = 22f; d.AdsTime = 0.26f;
            d.AdsSensScale = 0.45f;               // magnified optics need a much slower turn
            d.Crosshair = CrosshairStyle.None;    // no hip reticle: use the scope

            d.DamageFalloffStart = 90f; d.DamageFalloffEnd = 250f; d.MinDamageFraction = 0.8f;
            d.ShotGain = 1.0f; d.ShotDecay = 17f; d.ShotBodyHz = 105f; d.ShotCrack = 0.85f; d.ShotTail = 0.34f;
            d.KickBack = 0.075f; d.KickPitch = 7.5f; d.ShakeAmplitude = 0.55f;
            d.MuzzleVelocity = 1010f;

            return new WeaponEntry
            {
                Id = "dmr", Role = "Marksman Rifle", Def = d,
                Shape = new WeaponShape
                {
                    ReceiverLen = 0.34f, ReceiverH = 0.090f, ReceiverW = 0.054f,
                    BarrelLen = 0.38f, BarrelDia = 0.022f,
                    HandguardLen = 0.26f,
                    Stock = true, StockLen = 0.26f,
                    Magazine = true, MagLen = 0.140f, MagWidth = 0.030f,
                    Optic = OpticType.Scope, OpticBore = 0.076f, OpticLen = 0.200f,
                    SightDistance = 0.090f      // eye relief: the rear ring sits near the eye
                }
            };
        }

        // -------------------------------------------------------------------------- pistol

        static WeaponEntry Pistol()
        {
            var d = ScriptableObject.CreateInstance<WeaponDefinition>();
            d.DisplayName = "P-11 SIDEARM";
            d.Mode = FireMode.Semi;
            d.RoundsPerMinute = 420f;
            d.Damage = 30f;
            d.MagSize = 17; d.ReserveAmmo = 102;
            d.ReloadTime = 1.6f; d.ReloadTimeEmpty = 2.1f;

            d.RecoilVertical = 0.52f; d.RecoilVerticalSustain = 0.70f;
            d.RecoilHorizontal = 0.38f; d.PatternRampShots = 4f; d.PatternSeed = 37f;

            d.BaseSpread = 0.28f; d.SpreadPerShot = 0.38f; d.MaxSpread = 4.0f;
            d.SpreadDecay = 6.5f;
            d.HipFov = 80f; d.AdsFov = 62f; d.AdsTime = 0.12f;

            d.DamageFalloffStart = 22f; d.DamageFalloffEnd = 70f; d.MinDamageFraction = 0.45f;
            d.ShotGain = 0.70f; d.ShotDecay = 32f; d.ShotBodyHz = 175f; d.ShotCrack = 0.60f;
            d.KickBack = 0.038f; d.KickPitch = 6.0f;
            d.MuzzleVelocity = 360f;

            return new WeaponEntry
            {
                Id = "pistol", Role = "Sidearm", Def = d,
                Shape = new WeaponShape
                {
                    ReceiverLen = 0.175f, ReceiverH = 0.072f, ReceiverW = 0.036f,
                    BarrelLen = 0.045f, BarrelDia = 0.016f,
                    HandguardLen = 0f,
                    Stock = false,
                    Magazine = true, MagLen = 0.105f, MagWidth = 0.024f,
                    Optic = OpticType.Iron,
                    SightDistance = 0.235f
                }
            };
        }

        // ------------------------------------------------------------------------ shotgun

        static WeaponEntry Shotgun()
        {
            var d = ScriptableObject.CreateInstance<WeaponDefinition>();
            d.DisplayName = "BREACH-12";
            d.Mode = FireMode.Semi;
            d.RoundsPerMinute = 78f;
            d.Damage = 13f;
            d.PelletsPerShot = 9;
            d.HeadshotMultiplier = 1.6f;
            d.MagSize = 7; d.ReserveAmmo = 48;
            d.ReloadTime = 2.9f; d.ReloadTimeEmpty = 3.4f;

            d.RecoilVertical = 1.85f; d.RecoilVerticalSustain = 0.95f;
            d.RecoilHorizontal = 0.60f; d.PatternRampShots = 2f; d.PatternSeed = 47f;

            // BaseSpread is the pellet cone here, so it stays wide no matter what.
            d.BaseSpread = 2.30f; d.SpreadPerShot = 0.60f; d.MaxSpread = 6.0f;
            d.AdsSpreadScale = 0.62f; d.MoveSpreadScale = 1.2f;
            d.HipFov = 80f; d.AdsFov = 66f; d.AdsTime = 0.18f;

            d.DamageFalloffStart = 9f; d.DamageFalloffEnd = 34f; d.MinDamageFraction = 0.2f;
            d.ShotGain = 1.0f; d.ShotDecay = 14f; d.ShotBodyHz = 85f; d.ShotCrack = 0.9f; d.ShotTail = 0.40f;
            d.KickBack = 0.095f; d.KickPitch = 10f; d.ShakeAmplitude = 0.7f; d.ShakeDuration = 0.18f;
            d.MuzzleVelocity = 400f;

            return new WeaponEntry
            {
                Id = "shotgun", Role = "Shotgun", Def = d,
                Shape = new WeaponShape
                {
                    ReceiverLen = 0.26f, ReceiverH = 0.084f, ReceiverW = 0.050f,
                    BarrelLen = 0.34f, BarrelDia = 0.030f,
                    HandguardLen = 0.20f, PumpAction = true,
                    Stock = true, StockLen = 0.24f,
                    Magazine = false,
                    Optic = OpticType.Iron,
                    SightDistance = 0.215f
                }
            };
        }
    }
}
