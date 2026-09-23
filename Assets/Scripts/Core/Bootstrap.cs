using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using CombatPrep.Audio;
using CombatPrep.FX;
using CombatPrep.Player;
using CombatPrep.Skins;
using CombatPrep.Targets;
using CombatPrep.UI;
using CombatPrep.Weapons;

namespace CombatPrep.Core
{
    /// <summary>
    /// Builds the entire game at runtime and owns the menu/gameplay flow. Drop this one
    /// component on one empty GameObject in an otherwise empty scene and press Play.
    /// Nothing is authored in the scene file, so there is nothing to import and nothing to
    /// merge-conflict.
    /// </summary>
    public class Bootstrap : MonoBehaviour
    {
        public const int PlayerLayer = 8;

        GameObject _systems;
        ListenerRig _listenerRig;
        MainMenu _menu;
        GameObject _playerRoot;
        GameObject _targetsRoot;
        bool _inGame;

        void Awake()
        {
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 1;
            QualitySettings.shadowDistance = 130f;

            BuildSystems();
            Lighting();
            RangeBuilder.Build();
            PostFx();
            ShowMenu();
        }

        void Update()
        {
            if (GameInput.I == null) return;
            if (_inGame && GameInput.I.PausePress) ReturnToMenu();
        }

        // ---------------------------------------------------------------------- systems

        void BuildSystems()
        {
            _systems = new GameObject("Systems");
            _systems.AddComponent<GameInput>();
            _systems.AddComponent<GameAudio>();
            _systems.AddComponent<FxSystem>();
            _systems.AddComponent<Hud>();

            // One listener for the whole session. It stays parented here forever and
            // follows the live camera by transform - see ListenerRig for why parenting it
            // to the camera is unsafe.
            var l = new GameObject("Listener");
            l.transform.SetParent(_systems.transform, false);
            l.AddComponent<AudioListener>();
            _listenerRig = l.AddComponent<ListenerRig>();
        }

        // --------------------------------------------------------------------- lighting

        void Lighting()
        {
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.94f, 0.83f);
            sun.intensity = 1.75f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.78f;
            // Low-ish sun: long shadows across the range read as far more finished than a
            // noon sun that flattens everything.
            sunGo.transform.rotation = Quaternion.Euler(34f, 152f, 0f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.50f, 0.58f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.46f, 0.42f, 0.36f);
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.23f, 0.17f);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.74f, 0.72f, 0.66f);
            RenderSettings.fogStartDistance = 70f;
            RenderSettings.fogEndDistance = 340f;

            var skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                var sky = new Material(skyShader);
                sky.SetFloat("_SunSize", 0.045f);
                sky.SetFloat("_SunSizeConvergence", 4f);
                sky.SetFloat("_AtmosphereThickness", 1.15f);
                sky.SetColor("_SkyTint", new Color(0.52f, 0.60f, 0.74f));
                sky.SetColor("_GroundColor", new Color(0.42f, 0.37f, 0.30f));
                sky.SetFloat("_Exposure", 1.25f);
                RenderSettings.skybox = sky;
                RenderSettings.sun = sun;
            }
        }

        void PostFx()
        {
            var go = new GameObject("PostFx");
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 1f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            vol.sharedProfile = profile;

            // ACES tonemapping is doing most of the heavy lifting here: without it, URP
            // renders linear and everything looks washed out and plasticky.
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);

            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.55f);
            bloom.threshold.Override(1.05f);
            bloom.scatter.Override(0.62f);

            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.20f);
            color.contrast.Override(14f);
            color.saturation.Override(6f);
            color.colorFilter.Override(new Color(1f, 0.98f, 0.93f));

            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.28f);
            vig.smoothness.Override(0.45f);
        }

        /// <summary>URP renders post-processing per camera, so every camera must opt in.</summary>
        public static void ConfigureCamera(Camera cam)
        {
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null) return;
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.Medium;
        }

        // -------------------------------------------------------------------- menu flow

        void ShowMenu()
        {
            _inGame = false;

            var go = new GameObject("MainMenu");
            go.transform.SetParent(transform, false);
            _menu = go.AddComponent<MainMenu>();
            _menu.OnStart += StartGame;

            // Park the listener on the preview camera while the menu is up.
            if (_menu.PreviewCamera != null)
            {
                _listenerRig.Follow = _menu.PreviewCamera.transform;
            }

            Hud.I.SetGameplayVisible(false);
        }

        void StartGame(WeaponEntry entry, SkinDefinition skin)
        {
            if (_menu != null) { Destroy(_menu.gameObject); _menu = null; }

            var rig = BuildPlayer();
            BuildWeapon(rig, entry, skin);
            BuildTargets();

            Hud.I.SetGameplayVisible(true);
            Hud.I.ResetStats();
            GameInput.LockCursor(true);
            _inGame = true;
        }

        void ReturnToMenu()
        {
            _inGame = false;

            // Drop the follow target before tearing the player down, so the rig is not
            // chasing a destroyed transform for the rest of the frame.
            _listenerRig.Follow = null;

            if (_playerRoot != null) { Destroy(_playerRoot); _playerRoot = null; }
            if (_targetsRoot != null) { Destroy(_targetsRoot); _targetsRoot = null; }

            ShowMenu();
        }

        // ----------------------------------------------------------------------- player

        struct PlayerRig
        {
            public GameObject Root;
            public Camera Cam;
            public PlayerLook Look;
            public PlayerMotor Motor;
            public CameraShake Shake;
        }

        PlayerRig BuildPlayer()
        {
            var playerGo = new GameObject("Player");
            playerGo.layer = PlayerLayer;
            playerGo.transform.position = new Vector3(0f, 0.2f, -3f);
            _playerRoot = playerGo;

            var cc = playerGo.AddComponent<CharacterController>();
            cc.radius = 0.34f;
            cc.height = 1.8f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.slopeLimit = 50f;
            cc.stepOffset = 0.35f;
            cc.skinWidth = 0.02f;

            var motor = playerGo.AddComponent<PlayerMotor>();

            var pivot = Prim.Empty(playerGo.transform, "CameraPivot", new Vector3(0f, 1.62f, 0f));
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
            ConfigureCamera(cam);

            _listenerRig.Follow = camGo.transform;

            var look = playerGo.AddComponent<PlayerLook>();
            look.Body = playerGo.transform;
            look.Cam = cam;

            Hud.I.SetCamera(cam);

            return new PlayerRig { Root = playerGo, Cam = cam, Look = look, Motor = motor, Shake = shake };
        }

        void BuildWeapon(PlayerRig rig, WeaponEntry entry, SkinDefinition skin)
        {
            var holder = Prim.Empty(rig.Cam.transform, "WeaponHolder");
            var anim = holder.gameObject.AddComponent<WeaponAnimator>();

            var model = WeaponModelBuilder.Build(holder, entry.Shape);
            SkinApplier.Apply(model, skin);
            anim.Init(entry.Def, model, rig.Motor);

            var weapon = holder.gameObject.AddComponent<Weapon>();
            weapon.Cam = rig.Cam;
            weapon.Look = rig.Look;
            weapon.Motor = rig.Motor;
            weapon.Shake = rig.Shake;
            // Never trace against ourselves, and let rounds pass through spent debris and
            // the invisible play-area walls - otherwise misses would spark in mid-air.
            int hitMask = ~((1 << PlayerLayer)
                          | (1 << FxSystem.DebrisLayer)
                          | (1 << RangeBuilder.BoundaryLayer));
            weapon.HitMask = hitMask;
            weapon.Init(entry.Def, model, anim);

            // Grenade loadout lives on the same holder. It disables the weapon's model while
            // equipped, and blast/arc casts share the weapon's hit mask so they respect the
            // same walls and debris rules.
            var thrower = holder.gameObject.AddComponent<GrenadeThrower>();
            thrower.Cam = rig.Cam;
            thrower.Motor = rig.Motor;
            thrower.WeaponHolder = model.Root.gameObject;
            thrower.Weapon = weapon;
            // Blast damage and line-of-sight ignore the boundary walls (they only ring the
            // arena's edge). The arc preview must instead include them, so it bounces off
            // exactly what the thrown grenade physically bounces off - everything but the
            // player and spent debris.
            thrower.BlastMask = hitMask;
            thrower.ArcMask = ~((1 << PlayerLayer) | (1 << FxSystem.DebrisLayer));
            thrower.Init();
        }

        // ---------------------------------------------------------------------- targets

        void BuildTargets()
        {
            var root = new GameObject("Targets");
            _targetsRoot = root;

            // Static row: zeroing and pure accuracy.
            for (int i = -2; i <= 2; i++)
                Spawn(root.transform, new Vector3(i * 3.2f, 0f, 18f), MoveStyle.Static, 0f, 0f);

            // Strafers: lead and tracking.
            Spawn(root.transform, new Vector3(-9f, 0f, 28f), MoveStyle.Strafe, 2.6f, 4.5f);
            Spawn(root.transform, new Vector3(0f, 0f, 29f), MoveStyle.Strafe, 3.5f, 6.0f);
            Spawn(root.transform, new Vector3(9f, 0f, 28f), MoveStyle.Strafe, 2.2f, 3.5f);

            // Jitter: reactive micro-adjustment, the hardest one.
            Spawn(root.transform, new Vector3(-3.5f, 0f, 23f), MoveStyle.Jitter, 3.2f, 3.0f);

            // Bobbers: vertical correction.
            Spawn(root.transform, new Vector3(-6f, 0f, 40f), MoveStyle.Bob, 3.0f, 5.0f);
            Spawn(root.transform, new Vector3(6f, 0f, 40f), MoveStyle.Bob, 3.6f, 5.5f);

            // Long range: damage falloff and the DMR's natural home.
            Spawn(root.transform, new Vector3(-4f, 0f, 56f), MoveStyle.Static, 0f, 0f);
            Spawn(root.transform, new Vector3(4f, 0f, 56f), MoveStyle.Strafe, 2.0f, 5.0f);
            Spawn(root.transform, new Vector3(0f, 0f, 68f), MoveStyle.Static, 0f, 0f);
        }

        void Spawn(Transform parent, Vector3 pos, MoveStyle style, float speed, float range)
        {
            var go = new GameObject($"Target_{style}");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;

            var mover = go.AddComponent<TargetMover>();
            mover.Style = style;
            mover.Speed = speed;
            mover.Range = range;

            go.AddComponent<Target>();
        }
    }
}
