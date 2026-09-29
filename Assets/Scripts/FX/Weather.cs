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

        /// <summary>What rain lands on: world geometry, never players, debris or the invisible walls.</summary>
        static int WorldMask => ~((1 << PlayerRigBuilder.PlayerLayer) | (1 << PlayerRigBuilder.RemotePlayerLayer)
                                  | (1 << FxSystem.DebrisLayer) | (1 << RangeBuilder.BoundaryLayer)
                                  | (1 << 2));   // Ignore Raycast

        void Awake()
        {
            I = this;
            BuildRain();
            BuildAudio();
        }

        // ------------------------------------------------------------------------ rain

        void BuildRain()
        {
            _rain = ParticleKit.New(transform, "Rain", Vector3.zero, 11u,
                                    Mat.Particle(Tex.RainStreak(), additive: false, soft: false));

            var main = _rain.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.25f, 1.5f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.03f);
            main.startColor = new Color(0.70f, 0.76f, 0.84f, 0.26f);
            main.maxParticles = 7000;

            var em = _rain.emission;
            em.rateOverTime = DropsPerSecond;

            var sh = _rain.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(Area, 0.2f, Area);

            // Fall speed and slant come from velocity, not start speed, so the wind is in world
            // space no matter how the emitter is oriented.
            var vel = _rain.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(Wind.x * 0.85f, Wind.x * 1.15f);
            vel.y = new ParticleSystem.MinMaxCurve(-19f, -15.5f);
            vel.z = new ParticleSystem.MinMaxCurve(Wind.z * 0.85f, Wind.z * 1.15f);

            var col = _rain.collision;
            col.enabled = true;
            col.type = ParticleSystemCollisionType.World;
            col.mode = ParticleSystemCollisionMode.Collision3D;
            col.quality = ParticleSystemCollisionQuality.Medium;   // Low misses thin roofs
            col.collidesWith = WorldMask;
            col.lifetimeLoss = 1f;
            col.bounce = 0f;
            col.dampen = 1f;
            col.radiusScale = 0.5f;
            col.enableDynamicColliders = false;
            col.maxCollisionShapes = 128;

            var r = _rain.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.045f;
            r.lengthScale = 2.2f;
            r.cameraVelocityScale = 0f;

            var subs = _rain.subEmitters;
            subs.enabled = true;
            subs.AddSubEmitter(BuildSplash(), ParticleSystemSubEmitterType.Collision,
                               ParticleSystemSubEmitterProperties.InheritNothing, 0.35f);
            subs.AddSubEmitter(BuildRipple(), ParticleSystemSubEmitterType.Collision,
                               ParticleSystemSubEmitterProperties.InheritNothing, 0.22f);
        }

        /// <summary>A couple of droplets kicked up where a drop lands.</summary>
        ParticleSystem BuildSplash()
        {
            var ps = ParticleKit.New(_rain.transform, "Splash", Vector3.zero, 12u,
                                     Mat.Particle(Tex.SoftDot(32, 3f), additive: false, soft: false));
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.16f, 0.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.9f, 2.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.045f);
            main.startColor = new Color(0.75f, 0.80f, 0.86f, 0.45f);
            main.gravityModifier = 1.6f;
            main.maxParticles = 1500;

            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 2, 3) });

            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 32f;
            sh.radius = 0.01f;
            sh.rotation = new Vector3(-90f, 0f, 0f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = ParticleKit.Gradient(new[] { (0f, Color.white), (1f, Color.white) },
                                             new[] { (0f, 1f), (1f, 0f) });
            return ps;
        }

        /// <summary>
        /// A ring spreading on the surface. Lifted a couple of centimetres so it can't
        /// z-fight the ground, and deliberately not a soft particle: soft particles fade where
        /// they meet geometry, and a ripple is nothing but meeting geometry.
        /// </summary>
        ParticleSystem BuildRipple()
        {
            var ps = ParticleKit.New(_rain.transform, "Ripple", Vector3.zero, 13u,
                                     Mat.Particle(Tex.Ring(), additive: false, soft: false));
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.5f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.24f, 0.36f);
            main.startColor = new Color(0.80f, 0.85f, 0.90f, 0.30f);
            main.maxParticles = 900;

            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });

            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.0001f;
            sh.position = new Vector3(0f, 0.02f, 0f);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = ParticleKit.Curve((0f, 0.15f), (1f, 1f));

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = ParticleKit.Gradient(new[] { (0f, Color.white), (1f, Color.white) },
                                             new[] { (0f, 1f), (1f, 0f) });

            ps.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            return ps;
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
