using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using CombatPrep.Audio;
using CombatPrep.FX;
using CombatPrep.Net;
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
        public const int PlayerLayer = PlayerRigBuilder.PlayerLayer;

        /// <summary>
        /// Never read at runtime - it exists only to be referenced by the scene. Unity strips
        /// from a build every shader no asset references, and this project creates all of its
        /// materials in code, so without these the build would ship with no URP shaders and
        /// crash on its first `new Material`. Each generated material enables exactly one
        /// keyword combination the code uses (plain, emission, transparent, additive, sky),
        /// because URP only compiles an optional variant if some material asks for it.
        /// Filled in by CombatPrep > Build Range Scene.
        /// </summary>
        public Material[] ShaderKeepAlive;

        GameObject _systems;
        ListenerRig _listenerRig;
        MainMenu _menu;
        LobbyMenu _lobby;
        GameObject _playerRoot;
        GameObject _targetsRoot;
        bool _inGame;       // practice
        bool _online;       // in an online match
        bool _leaving;

        void Awake()
        {
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 1;
            GraphicsSetup.Apply(gameObject);

            BuildSystems();
            Lighting();
            RangeBuilder.Build();
            PostFx();

            NetPlayer.LocalRigBuilt += OnLocalRigBuilt;
            SessionService.SessionEnded += OnSessionEnded;

            ShowMenu();
        }

        /// <summary>
        /// Netcode callbacks are hooked in Start, not Awake: the scene's NetworkManager sets
        /// NetworkManager.Singleton in its own OnEnable, which may run after this Awake.
        /// </summary>
        void Start()
        {
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;

            StartCoroutine(CaptureReflections());
        }

        /// <summary>The reflection probes, once the fires have had a moment to take hold.</summary>
        System.Collections.IEnumerator CaptureReflections()
        {
            yield return new WaitForSeconds(1.5f);
            RangeBuilder.RenderReflections();
        }

        void OnDestroy()
        {
            NetPlayer.LocalRigBuilt -= OnLocalRigBuilt;
            SessionService.SessionEnded -= OnSessionEnded;
            if (NetworkManager.Singleton != null)
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
        }

        void Update()
        {
            if (GameInput.I == null || !GameInput.I.PausePress) return;

            if (_inGame) ReturnToMenu();
            else if (_online) LeaveOnline(null);
        }

        // ---------------------------------------------------------------------- systems

        void BuildSystems()
        {
            _systems = new GameObject("Systems");
            _systems.AddComponent<GameInput>();
            _systems.AddComponent<GameAudio>();
            _systems.AddComponent<FxSystem>();
            _systems.AddComponent<Storm>();
            _systems.AddComponent<Weather>();
            _systems.AddComponent<VolumetricLight>();
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

        /// <summary>
        /// Storm light. One cold key light standing in for a sun buried in cloud: soft shadows,
        /// because overcast light comes from everywhere. It comes in low from the south-west,
        /// behind the firing line, so the faces you look at from there and from the spawns are
        /// lit and the shadows run long down the lanes - lit from the far end, everything was a
        /// black silhouette. Ambient does most of the work, and dense exponential fog - the
        /// same colour as the sky's horizon - eats the distance so ruins fade out instead of
        /// ending. Storm owns the sky and borrows this light for lightning.
        /// </summary>
        void Lighting()
        {
            var keyGo = new GameObject("StormLight");
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(0.66f, 0.74f, 0.86f);
            key.intensity = 1.3f;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.7f;
            keyGo.transform.rotation = Quaternion.Euler(40f, 32f, 0f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.41f, 0.46f, 0.53f);
            RenderSettings.ambientEquatorColor = new Color(0.29f, 0.305f, 0.335f);
            RenderSettings.ambientGroundColor = new Color(0.14f, 0.14f, 0.145f);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Storm.Horizon;
            RenderSettings.fogDensity = 0.0085f;

            // Fallback sky, only if the storm shader didn't make it into a build.
            var skyShader = Mat.Require("Skybox/Procedural");
            if (skyShader != null)
            {
                var sky = new Material(skyShader);
                sky.SetFloat("_SunSize", 0f);
                sky.SetFloat("_AtmosphereThickness", 0.6f);
                sky.SetColor("_SkyTint", new Color(0.30f, 0.32f, 0.36f));
                sky.SetColor("_GroundColor", new Color(0.12f, 0.12f, 0.13f));
                sky.SetFloat("_Exposure", 0.35f);
                RenderSettings.skybox = sky;
            }

            Storm.I.Setup(key);
        }

        /// <summary>
        /// The grade that makes it a war film rather than a wet afternoon: cooled, drained of
        /// colour, pushed contrast, grain, a heavy vignette, and bloom with a dirty lens so
        /// muzzle flashes, fires and lightning flare across the grime on the glass.
        /// </summary>
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
            bloom.intensity.Override(0.85f);
            bloom.threshold.Override(0.95f);
            bloom.scatter.Override(0.7f);
            bloom.dirtTexture.Override(Tex.LensDirt());
            bloom.dirtIntensity.Override(2.2f);

            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.6f);
            color.contrast.Override(15f);
            color.saturation.Override(-30f);
            color.colorFilter.Override(new Color(0.92f, 0.96f, 1f));

            var balance = profile.Add<WhiteBalance>(true);
            balance.temperature.Override(-12f);
            balance.tint.Override(3f);

            // Cold shadows, a faint warmth kept in the highlights so fire still reads as fire.
            var smh = profile.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(0.93f, 0.98f, 1.08f, -0.03f));
            smh.highlights.Override(new Vector4(1.04f, 1.0f, 0.96f, 0f));

            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.26f);
            vig.smoothness.Override(0.5f);

            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Medium3);
            grain.intensity.Override(0.22f);
            grain.response.Override(0.75f);

            var aberration = profile.Add<ChromaticAberration>(true);
            aberration.intensity.Override(0.06f);

            // Camera glare off the brightest things - fires, muzzle flashes, lightning: a faint
            // horizontal streak and a ghost or two, as a real lens throws.
            var flare = profile.Add<ScreenSpaceLensFlare>(true);
            flare.intensity.Override(0.35f);
            flare.firstFlareIntensity.Override(0.4f);
            flare.secondaryFlareIntensity.Override(0.25f);
            flare.warpedFlareIntensity.Override(0.15f);
            flare.streaksIntensity.Override(0.55f);
            flare.streaksLength.Override(0.45f);
            flare.streaksThreshold.Override(0.4f);
            flare.tintColor.Override(new Color(1f, 0.86f, 0.72f));
        }

        /// <summary>
        /// URP renders post-processing per camera, so every camera must opt in. Anti-aliasing is
        /// temporal, so thin things - wire, rain, distant edges - stop crawling; on big screens
        /// FSR then upscales the result (see GraphicsSetup).
        /// </summary>
        public static void ConfigureCamera(Camera cam)
        {
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null) return;
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.TemporalAntiAliasing;
            data.taaSettings.quality = TemporalAAQuality.High;
            data.taaSettings.contrastAdaptiveSharpening = 0.3f;
        }

        // -------------------------------------------------------------------- menu flow

        void ShowMenu()
        {
            _inGame = false;
            _online = false;

            var go = new GameObject("MainMenu");
            go.transform.SetParent(transform, false);
            _menu = go.AddComponent<MainMenu>();
            _menu.OnStart += StartGame;
            _menu.OnPlayOnline += PlayOnline;

            // Park the listener on the preview camera while the menu is up.
            if (_menu.PreviewCamera != null)
            {
                _listenerRig.Follow = _menu.PreviewCamera.transform;
            }

            Hud.I.SetGameplayVisible(false);
        }

        void CloseMenu()
        {
            if (_menu != null) { Destroy(_menu.gameObject); _menu = null; }
        }

        void EnterGameplay()
        {
            Hud.I.SetGameplayVisible(true);
            Hud.I.ResetStats();
            Hud.I.ResetMatchUi();
            GameInput.LockCursor(true);
        }

        /// <summary>Practice: the offline range with targets.</summary>
        void StartGame(WeaponEntry entry, SkinDefinition skin)
        {
            CloseMenu();
            RangeBuilder.SetOnlineCover(false);   // the middle lanes belong to the targets

            var root = new GameObject("Player");
            root.transform.position = new Vector3(0f, 0.2f, -3f);
            _playerRoot = root;
            PlayerRigBuilder.Build(root, entry, skin);

            BuildTargets();
            EnterGameplay();
            Hud.I.SetHealthVisible(false);   // targets can't shoot back
            _inGame = true;
        }

        // ------------------------------------------------------------------ online flow

        /// <summary>Carry the chosen loadout into the online lobby.</summary>
        void PlayOnline(WeaponEntry entry, SkinDefinition skin)
        {
            CloseMenu();
            RangeBuilder.SetOnlineCover(true);    // up before the lobby, so its orbit shot shows it
            NetPlayer.PendingWeapon = Mathf.Max(0, Array.IndexOf(WeaponLibrary.All, entry));
            NetPlayer.PendingSkin = Mathf.Max(0, Array.IndexOf(SkinLibrary.All, skin));
            _leaving = false;

            var go = new GameObject("LobbyMenu");
            go.transform.SetParent(transform, false);
            _lobby = go.AddComponent<LobbyMenu>();
            _lobby.OnBack += () => { CloseLobby(); ShowMenu(); };
            _lobby.OnLeave += () => LeaveOnline(null);

            Hud.I.SetGameplayVisible(false);
        }

        void CloseLobby()
        {
            if (_lobby != null) { Destroy(_lobby.gameObject); _lobby = null; }
        }

        /// <summary>The host started the match and our own player's rig now exists.</summary>
        void OnLocalRigBuilt(PlayerRig rig)
        {
            CloseLobby();
            EnterGameplay();
            _online = true;
        }

        /// <summary>
        /// Leaves the session and shuts Netcode down, which despawns every networked object -
        /// including our own rig - then returns to the main menu, optionally explaining why.
        /// Guarded, because a host quitting can raise both a session and a netcode event.
        /// </summary>
        async void LeaveOnline(string notice)
        {
            if (_leaving) return;
            _leaving = true;
            _online = false;
            _listenerRig.Follow = null;

            await SessionService.LeaveAsync();

            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsListening) nm.Shutdown();

            CloseLobby();
            MainMenu.PendingNotice = notice;
            ShowMenu();
            _leaving = false;
        }

        void OnSessionEnded() => LeaveOnline("The session ended - the host may have left.");

        void OnClientDisconnect(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            // Only react when *we* are the one cut off. The host sees every client leave here
            // too, but their player objects simply despawn and the match carries on.
            if (nm == null || nm.IsServer || clientId != nm.LocalClientId) return;

            string reason = string.IsNullOrEmpty(nm.DisconnectReason)
                ? "Lost connection to the host."
                : nm.DisconnectReason;
            LeaveOnline(reason);
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
