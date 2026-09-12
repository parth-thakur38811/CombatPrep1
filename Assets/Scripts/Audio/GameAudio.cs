using UnityEngine;

namespace CombatPrep.Audio
{
    /// <summary>
    /// Builds the whole sound bank at startup and plays it through a round-robin source
    /// pool, so rapid fire overlaps properly instead of cutting itself off. Slight pitch
    /// jitter per shot stops full-auto from turning into a machine-gun buzzsaw.
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        public static GameAudio I { get; private set; }

        [Header("Mix")]
        public float MasterVolume = 0.75f;
        public int VoiceCount = 12;

        AudioSource[] _voices;
        int _next;

        public AudioClip Shot, DryFire, MagOut, MagIn, BoltRelease;
        public AudioClip HitMarker, HeadshotMarker, KillMarker;
        public AudioClip ImpactHard, ImpactSoft;

        void Awake()
        {
            I = this;

            _voices = new AudioSource[VoiceCount];
            for (int i = 0; i < VoiceCount; i++)
            {
                var go = new GameObject($"Voice{i}");
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;     // 2D by default; world sounds override per-call
                src.rolloffMode = AudioRolloffMode.Linear;
                src.maxDistance = 60f;
                _voices[i] = src;
            }

            BuildBank();
        }

        void BuildBank()
        {
            // Weapon report is rebuilt per-weapon in Rebuild(); this is the default rifle.
            Shot = Synth.GunShot("Shot", 0.85f, 26f, 150f, 0.7f, 0.22f);
            DryFire = Synth.Click("DryFire", 1.4f, 0.30f);
            MagOut = Synth.Click("MagOut", 0.8f, 0.38f);
            MagIn = Synth.Click("MagIn", 1.0f, 0.45f);
            BoltRelease = Synth.Click("BoltRelease", 1.6f, 0.50f);

            HitMarker = Synth.Blip("HitMarker", 1350f, 55f, 0.30f);
            HeadshotMarker = Synth.Blip("Headshot", 1750f, 45f, 0.34f, 2600f);
            KillMarker = Synth.Blip("Kill", 880f, 22f, 0.32f, 1320f);

            ImpactHard = Synth.Impact("ImpactHard", 1.6f, 0.45f);
            ImpactSoft = Synth.Impact("ImpactSoft", 0.7f, 0.40f);
        }

        /// <summary>Regenerates the gunshot for a specific weapon's synth parameters.</summary>
        public void RebuildShot(float gain, float decay, float bodyHz, float crack, float tail)
            => Shot = Synth.GunShot("Shot", gain, decay, bodyHz, crack, tail);

        public void Play(AudioClip clip, float volume = 1f, float pitchJitter = 0.04f)
        {
            if (clip == null) return;
            var src = Take();
            src.spatialBlend = 0f;
            src.clip = clip;
            src.volume = volume * MasterVolume;
            src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            src.Play();
        }

        public void PlayAt(AudioClip clip, Vector3 position, float volume = 1f, float pitchJitter = 0.08f)
        {
            if (clip == null) return;
            var src = Take();
            src.transform.position = position;
            src.spatialBlend = 1f;
            src.clip = clip;
            src.volume = volume * MasterVolume;
            src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            src.Play();
        }

        AudioSource Take()
        {
            var src = _voices[_next];
            _next = (_next + 1) % _voices.Length;
            src.transform.localPosition = Vector3.zero;
            return src;
        }
    }
}
