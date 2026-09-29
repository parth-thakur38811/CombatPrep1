using System.Collections.Generic;
using UnityEngine;
using CombatPrep.Core;
using CombatPrep.Weapons;

namespace CombatPrep.Audio
{
    /// <summary>
    /// Builds the whole sound bank at startup and plays it through a round-robin source
    /// pool, so rapid fire overlaps properly instead of cutting itself off. Slight pitch
    /// jitter per shot stops full-auto from turning into a machine-gun buzzsaw.
    ///
    /// Gunshots are real recordings where the art library has them (Art/Audio/Weapons),
    /// several takes per gun played in turn, each layered over a synthesised tail for the
    /// space around it; the synthesised report stands in for any gun without recordings.
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        public static GameAudio I { get; private set; }

        [Header("Mix")]
        public float MasterVolume = 0.75f;
        public int VoiceCount = 24;

        AudioSource[] _voices;
        int _next;

        public AudioClip DryFire, MagOut, MagIn, BoltRelease;
        public AudioClip HitMarker, HeadshotMarker, KillMarker;
        public AudioClip ImpactHard, ImpactSoft;
        public AudioClip Explosion;

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
            DryFire = Synth.Click("DryFire", 1.4f, 0.30f);
            MagOut = Synth.Click("MagOut", 0.8f, 0.38f);
            MagIn = Synth.Click("MagIn", 1.0f, 0.45f);
            BoltRelease = Synth.Click("BoltRelease", 1.6f, 0.50f);

            HitMarker = Synth.Blip("HitMarker", 1350f, 55f, 0.30f);
            HeadshotMarker = Synth.Blip("Headshot", 1750f, 45f, 0.34f, 2600f);
            KillMarker = Synth.Blip("Kill", 880f, 22f, 0.32f, 1320f);

            ImpactHard = Synth.Impact("ImpactHard", 1.6f, 0.45f);
            ImpactSoft = Synth.Impact("ImpactSoft", 0.7f, 0.40f);
            Explosion = Synth.Explosion("Explosion", 0.95f);
        }


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

        /// <param name="maxDistance">Audible range. Impacts are local (60 m); gunshots should
        /// carry across the whole arena, so remote shots pass a much larger value.</param>
        public void PlayAt(AudioClip clip, Vector3 position, float volume = 1f, float pitchJitter = 0.08f,
                           float maxDistance = 60f)
        {
            if (clip == null) return;
            var src = Take();
            src.transform.position = position;
            src.spatialBlend = 1f;
            src.maxDistance = maxDistance;
            src.clip = clip;
            src.volume = volume * MasterVolume;
            src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            src.Play();
        }

        // ------------------------------------------------------------------ gunshots

        /// <summary>One gun's sounds: its takes, played in turn, and the tail layered under them.</summary>
        sealed class ShotSet
        {
            public AudioClip[] Takes;
            public AudioClip Tail;
            public float TailGain;
            public int Next;
        }

        readonly Dictionary<WeaponDefinition, ShotSet> _shots = new();
        WeaponDefinition _local;

        /// <summary>
        /// Every gun has its own set, built once. A shared clip would make a friend's shotgun
        /// boom like your SMG.
        /// </summary>
        ShotSet SetFor(WeaponDefinition def)
        {
            if (_shots.TryGetValue(def, out var set)) return set;

            set = new ShotSet();
            var recorded = ArtLibrary.I != null ? ArtLibrary.I.ShotsFor(def.Id) : null;
            if (recorded != null)
            {
                set.Takes = recorded;
                // Longer, louder space behind the heavier guns (ShotTail runs 0.22 to 0.40).
                set.Tail = Synth.ShotTail("Tail_" + def.Id, 0.7f + def.ShotTail * 2.4f, def.Id.GetHashCode());
                set.TailGain = Mathf.Clamp01(0.12f + def.ShotTail * 0.9f);
            }
            else
            {
                set.Takes = new[] { Synth.GunShot("Shot_" + def.DisplayName, def.ShotGain, def.ShotDecay,
                                                  def.ShotBodyHz, def.ShotCrack, def.ShotTail) };
            }
            // Start each gun on a different take.
            set.Next = Random.Range(0, set.Takes.Length);
            _shots[def] = set;
            return set;
        }

        /// <summary>The next take, never the same one twice running when there are several.</summary>
        static AudioClip NextTake(ShotSet set)
        {
            var clip = set.Takes[set.Next];
            if (set.Takes.Length > 1)
                set.Next = (set.Next + Random.Range(1, set.Takes.Length)) % set.Takes.Length;
            return clip;
        }

        /// <summary>A report for this gun - distant skirmishes use it too.</summary>
        public AudioClip ShotFor(WeaponDefinition def) => def == null ? null : NextTake(SetFor(def));

        /// <summary>The gun in your hands, for PlayLocalShot.</summary>
        public void SetLocalWeapon(WeaponDefinition def)
        {
            _local = def;
            if (def != null) SetFor(def);    // build now rather than on the first trigger pull
        }

        /// <summary>Your own gunshot: flat in both ears, not placed in the world.</summary>
        public void PlayLocalShot()
        {
            if (_local == null) return;
            var set = SetFor(_local);
            Play(NextTake(set), _local.ShotGain, 0.035f);
            if (set.Tail != null) Play(set.Tail, _local.ShotGain * set.TailGain, 0.06f);
        }

        /// <summary>Someone else's gunshot, from where their gun is.</summary>
        public void PlayShotAt(WeaponDefinition def, Vector3 position, float maxDistance = 220f)
        {
            if (def == null) return;
            var set = SetFor(def);
            PlayAt(NextTake(set), position, def.ShotGain, 0.05f, maxDistance);
            if (set.Tail != null) PlayAt(set.Tail, position, def.ShotGain * set.TailGain, 0.06f, maxDistance);
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
