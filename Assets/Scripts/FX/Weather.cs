using UnityEngine;
using CombatPrep.Audio;
using CombatPrep.Core;

namespace CombatPrep.FX
{
    /// <summary>
    /// Rain. A box of streaks hangs above whichever camera is live (the player, or the lobby
    /// orbit), leading it slightly so you never run out from under the downpour. Drops die on
    /// whatever they hit, and each landing may throw a splash and leave a ripple - so rain
    /// stops at a container roof, pools on sandbags, and the inside of a container stays dry.
    ///
    /// The sound follows the same logic: a ray straight up decides whether you're under
    /// cover, and if so the open-air hiss is muffled and the drum of rain on the roof above
    /// you fades in.
    /// </summary>
    public class Weather : MonoBehaviour
    {
        public static Weather I { get; private set; }

        /// <summary>Horizontal wind, m/s. Rain slants with it; smoke and cloud drift along it.</summary>
        public static readonly Vector3 Wind = new(1.8f, 0f, 0.7f);

        [Header("Rain")]
        public float DropsPerSecond = 3800f;
        public float Area = 46f;          // side of the square rained on around the camera
        public float Height = 14f;       // emitter height above the camera

        [Header("Sound")]
        public float OutsideVolume = 0.55f;
        public float RoofVolume = 0.6f;

        ParticleSystem _rain;
        AudioSource _outside, _roof;
        AudioLowPassFilter _outsideFilter;

        Vector3 _lastPos;
        Vector3 _velocity;
        bool _emitting;
        bool _covered;
        float _cover;            // 0 open sky .. 1 under a roof, eased
        float _nextCoverCheck;

        /// <summary>
        /// The rain's own layer (Unity's TransparentFX). Cameras draw it; reflection probes leave
        /// it out, since the downpour moves with the camera and would freeze into every reflection.
        /// </summary>
        public const int CameraOnlyLayer = 1;

        /// <summary>What rain lands on: world geometry, never players, debris or the invisible walls.</summary>
        public static int WorldMask => ~((1 << PlayerRigBuilder.PlayerLayer) | (1 << PlayerRigBuilder.RemotePlayerLayer)
                                  | (1 << FxSystem.DebrisLayer) | (1 << RangeBuilder.BoundaryLayer)
                                  | (1 << 2));   // Ignore Raycast

        void Awake()
        {
            I = this;
            BuildRain();
            BuildAudio();
        }

        // ------------------------------------------------------------------------ rain

        /// <summary>The rain prefab (Prefabs/FX/Rain), or the same effect built from its recipe.</summary>
        void BuildRain()
        {
            var prefab = ArtLibrary.I != null && ArtLibrary.I.Fx != null ? ArtLibrary.I.Fx.Rain : null;
            var go = prefab != null ? Instantiate(prefab) : FxRecipes.Rain(DropsPerSecond, Area);
            go.name = "Rain";
            go.transform.SetParent(transform, false);
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = CameraOnlyLayer;
            _rain = go.GetComponent<ParticleSystem>();

            // Layers are the code's to define, so the drops always land on the current world.
            var col = _rain.collision;
            col.collidesWith = WorldMask;

            // Held until LateUpdate has found the camera and moved the emitter over it.
            _rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        // ----------------------------------------------------------------------- sound

        void BuildAudio()
        {
            _outside = Loop("RainOutside", Synth.RainLoop("RainOutside", 101));
            _outsideFilter = _outside.gameObject.AddComponent<AudioLowPassFilter>();
            _outsideFilter.cutoffFrequency = 22000f;

            // Rain on a roof: fewer, heavier, lower drops and almost no hiss.
            _roof = Loop("RainRoof", Synth.RainLoop("RainRoof", 202, 4f, patterRate: 300f, patterHz: 1400f,
                                                    hiss: 0.12f, patter: 1.0f, wash: 0.25f, gain: 0.55f));
            _roof.volume = 0f;
        }

        AudioSource Loop(string name, AudioClip clip)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.spatialBlend = 0f;
            src.playOnAwake = false;
            src.volume = 0f;
            src.Play();
            return src;
        }

        // --------------------------------------------------------------------- update

        void LateUpdate()
        {
            // The listener rig always tracks the live camera, so rain follows the same thing
            // you hear from. The menu's showroom stage is parked far below the world - no rain
            // falls in there, you just hear it outside.
            var target = ListenerRig.I != null ? ListenerRig.I.Follow : null;
            bool outdoors = target != null && target.position.y > -100f;

            if (outdoors) FollowCamera(target);
            SetEmitting(outdoors);

            if (outdoors && Time.time >= _nextCoverCheck)
            {
                _nextCoverCheck = Time.time + 0.2f;
                _covered = Physics.Raycast(target.position + Vector3.up * 0.2f, Vector3.up, 30f,
                                           WorldMask, QueryTriggerInteraction.Ignore);
            }

            // In the menu the rain sits outside the "tent": muffled, a little quieter.
            float coverTarget = outdoors ? (_covered ? 1f : 0f) : 0.7f;
            _cover = Mathf.MoveTowards(_cover, coverTarget, Time.deltaTime * 2.5f);

            float master = GameAudio.I != null ? GameAudio.I.MasterVolume : 0.75f;
            _outside.volume = Mathf.Lerp(OutsideVolume, OutsideVolume * 0.55f, _cover) * master;
            // Exponential sweep, so the muffling sounds even rather than all happening at the end.
            _outsideFilter.cutoffFrequency = 22000f * Mathf.Pow(1400f / 22000f, _cover);
            _roof.volume = (outdoors ? _cover : 0f) * RoofVolume * master;
        }

        void FollowCamera(Transform target)
        {
            Vector3 pos = target.position;
            if (Time.deltaTime > 0f)
            {
                var v = (pos - _lastPos) / Time.deltaTime;
                // Teleports (spawn, respawn) would fling the emitter across the map for a frame.
                if (v.sqrMagnitude > 30f * 30f) v = Vector3.zero;
                _velocity = Vector3.Lerp(_velocity, v, 1f - Mathf.Exp(-4f * Time.deltaTime));
            }
            _lastPos = pos;

            // Drops take most of a second to fall: emit where the camera will be, not where it is.
            var lead = new Vector3(_velocity.x, 0f, _velocity.z) * 0.8f;
            _rain.transform.position = new Vector3(pos.x, pos.y + Height, pos.z) + lead - Wind * 0.8f;
        }

        void SetEmitting(bool on)
        {
            if (on == _emitting) return;
            _emitting = on;
            if (on) _rain.Play(true);
            else _rain.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }
}
