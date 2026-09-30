using UnityEngine;
using CombatPrep.Audio;
using CombatPrep.FX;
using CombatPrep.Player;
using CombatPrep.Skins;
using CombatPrep.UI;
using CombatPrep.Weapons;

namespace CombatPrep.Core
{
    /// <summary>Everything a caller needs back after the first-person rig is built.</summary>
    public struct PlayerRig
    {
        public GameObject Root;
        public Camera Cam;
        public PlayerLook Look;
        public PlayerMotor Motor;
        public CameraShake Shake;
        /// <summary>All five guns; keys 1-5 switch between them.</summary>
        public WeaponLoadout Loadout;
        public GrenadeThrower Thrower;
    }

    /// <summary>
    /// Builds the local, first-person player: controller, camera, all five guns and grenades.
    ///
    /// Shared by Practice and Online so the two can never drift apart. It builds onto a root
    /// the caller supplies rather than creating one, because online that root already
    /// exists - it is the network player object, spawned by Netcode.
    /// </summary>
    public static class PlayerRigBuilder
    {
        /// <summary>The local player lives here, and its own shots are masked off it.</summary>
        public const int PlayerLayer = 8;

        /// <summary>Other players' avatars live here, so local shots can hit them.</summary>
        public const int RemotePlayerLayer = 11;

        /// <summary>
        /// What a shot can hit: everything except yourself, spent debris, and the invisible
        /// arena walls (which would otherwise make misses spark in mid-air).
        /// </summary>
        public static int HitMask => ~((1 << PlayerLayer)
                                      | (1 << FxSystem.DebrisLayer)
                                      | (1 << RangeBuilder.BoundaryLayer));

        /// <summary>
        /// What a grenade physically bounces off - includes the boundary walls, so the arc
        /// preview stops exactly where the real grenade will.
        /// </summary>
        public static int ArcMask => ~((1 << PlayerLayer) | (1 << FxSystem.DebrisLayer));

        public static PlayerRig Build(GameObject root, WeaponEntry entry, SkinDefinition skin)
        {
            root.layer = PlayerLayer;

            var cc = root.AddComponent<CharacterController>();
            cc.radius = 0.34f;
            cc.height = 1.8f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.slopeLimit = 50f;
            cc.stepOffset = 0.35f;
            cc.skinWidth = 0.02f;

            var motor = root.AddComponent<PlayerMotor>();

            var pivot = Prim.Empty(root.transform, "CameraPivot", new Vector3(0f, 1.62f, 0f));
            motor.CameraPivot = pivot;

            var shakeGo = Prim.Empty(pivot, "ShakeRoot");
            var shake = shakeGo.gameObject.AddComponent<CameraShake>();

            var camGo = new GameObject("MainCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(shakeGo, false);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 78f;
            cam.nearClipPlane = 0.012f;
            cam.farClipPlane = 600f;
            Bootstrap.ConfigureCamera(cam);

            if (ListenerRig.I != null) ListenerRig.I.Follow = camGo.transform;

            var look = root.AddComponent<PlayerLook>();
            look.Body = root.transform;
            look.Cam = cam;

            var rig = new PlayerRig
            {
                Root = root, Cam = cam, Look = look, Motor = motor, Shake = shake
            };
            BuildWeapons(ref rig, entry, skin);
            return rig;
        }

        /// <summary>
        /// Every gun in the roster, all in the chosen finish, each on its own holder with its own
        /// animator and magazine; <paramref name="start"/> is the one in hand. The loadout that
        /// switches them and the grenades share a holder of their own above them.
        /// </summary>
        static void BuildWeapons(ref PlayerRig rig, WeaponEntry start, SkinDefinition skin)
        {
            var rack = Prim.Empty(rig.Cam.transform, "Weapons");

            var all = WeaponLibrary.All;
            var weapons = new Weapon[all.Length];
            for (int i = 0; i < all.Length; i++)
                weapons[i] = BuildWeapon(rack, rig, all[i], skin);

            var loadout = rack.gameObject.AddComponent<WeaponLoadout>();

            // The grenade puts the gun in hand away while it's out. Blast casts share the
            // guns' hit mask; the arc preview uses the physical mask so it bounces off exactly
            // what the real grenade bounces off.
            var thrower = rack.gameObject.AddComponent<GrenadeThrower>();
            thrower.Cam = rig.Cam;
            thrower.Motor = rig.Motor;
            thrower.Loadout = loadout;
            thrower.BlastMask = HitMask;
            thrower.ArcMask = ArcMask;
            thrower.Init();

            loadout.Thrower = thrower;
            loadout.Init(weapons, System.Array.IndexOf(all, start));

            rig.Loadout = loadout;
            rig.Thrower = thrower;
        }

        static Weapon BuildWeapon(Transform rack, PlayerRig rig, WeaponEntry entry, SkinDefinition skin)
        {
            var holder = Prim.Empty(rack, entry.Id);
            var anim = holder.gameObject.AddComponent<WeaponAnimator>();

            var model = WeaponModelBuilder.Build(holder, entry.Shape);
            SkinApplier.Apply(model, skin);
            anim.Init(entry.Def, model, rig.Motor);

            var weapon = holder.gameObject.AddComponent<Weapon>();
            weapon.Cam = rig.Cam;
            weapon.Look = rig.Look;
            weapon.Motor = rig.Motor;
            weapon.Shake = rig.Shake;
            weapon.HitMask = HitMask;
            weapon.Init(entry.Def, model, anim);

            holder.gameObject.SetActive(false);    // until the loadout takes it out
            return weapon;
        }
    }
}
