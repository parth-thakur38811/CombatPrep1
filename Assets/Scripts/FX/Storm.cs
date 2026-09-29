using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using CombatPrep.Audio;
using CombatPrep.Core;
using CombatPrep.Weapons;

namespace CombatPrep.FX
{
    /// <summary>
    /// The storm overhead and the war beyond the ridge.
    ///
    ///   Sky       - the procedural StormSky skybox, clouds drifting with the wind.
    ///   Lightning - flickering strikes that light the clouds, throw hard shadows across the
    ///               arena for a heartbeat and, if close, draw a bolt; thunder follows after
    ///               the delay sound would really take.
    ///   Shelling  - an orange glow pulsing on the horizon, then a muffled boom.
    ///   Skirmish  - distant, muffled bursts of gunfire from somewhere else in the war.
    ///
    /// All of it is local ambience: each player's storm runs on its own clock, nothing is
    /// networked, and none of it touches the seeded arena layout.
    /// </summary>
    public class Storm : MonoBehaviour
    {
        public static Storm I { get; private set; }

        /// <summary>Horizon colour. Bootstrap's fog uses exactly this so sky and fog meet seamlessly.</summary>
        public static readonly Color Horizon = new(0.19f, 0.21f, 0.24f);
        static readonly Color FlashTint = new(0.78f, 0.84f, 1f);
        static readonly Vector3 ArenaCentre = new(0f, 0f, 42f);

        [Header("Timing (seconds between events)")]
        public Vector2 StrikeInterval = new(8f, 22f);
        public Vector2 ShellInterval = new(16f, 40f);
        public Vector2 SkirmishInterval = new(12f, 32f);

        enum Strike { Far, Mid, Close }

        Material _sky;
        Light _key;
        Quaternion _keyRotation;
        Color _keyColor;
        float _keyIntensity;
        Color _ambientSky, _ambientEquator;

        readonly List<LineRenderer> _bolts = new();
        readonly List<Vector3> _points = new();
        Material _boltMat;

        AudioSource _thunder, _boom, _gunfire;
        AudioLowPassFilter _thunderFilter;
        AudioClip[] _thunderClose, _thunderFar;

        static readonly int FlashId = Shader.PropertyToID("_Flash");
        static readonly int FlashDirId = Shader.PropertyToID("_FlashDir");
        static readonly int GlowId = Shader.PropertyToID("_Glow");
        static readonly int GlowDirId = Shader.PropertyToID("_GlowDir");

        void Awake()
        {
            I = this;
            _thunder = Source("Thunder", out _thunderFilter, 22000f);
            _boom = Source("Shelling", out _, 650f);
            _gunfire = Source("Skirmish", out _, 1100f);
        }

        /// <summary>
        /// Called by Bootstrap once the key light exists: installs the sky, records the
        /// resting light and ambient so strikes can flare and restore them, and starts the clock.
        /// </summary>
        public void Setup(Light key)
        {
            _key = key;
            _keyRotation = key.transform.rotation;
            _keyColor = key.color;
            _keyIntensity = key.intensity;
            _ambientSky = RenderSettings.ambientSkyColor;
            _ambientEquator = RenderSettings.ambientEquatorColor;

            var shader = Mat.Require("CombatPrep/StormSky");
            if (shader != null)
            {
                _sky = new Material(shader) { name = "StormSky" };
                _sky.SetColor("_HorizonColor", Horizon);
                _sky.SetVector("_Wind", new Vector4(Weather.Wind.x, Weather.Wind.z, 0.018f, 0f));

                // With a photographed sky imported, it becomes the cloud deck and our own
                // clouds thin out to dark scud drifting underneath it.
                var art = ArtLibrary.I;
                if (art != null && art.Sky != null)
                {
                    _sky.SetTexture("_Tex", art.Sky);
                    _sky.SetFloat("_TexBlend", 1f);
                    _sky.SetFloat("_TexExposure", art.SkyExposure);
                    _sky.SetFloat("_CloudOpacity", 0.55f);
                    _sky.SetColor("_CloudDark", new Color(0.05f, 0.055f, 0.065f));
                    _sky.SetColor("_CloudLight", new Color(0.12f, 0.13f, 0.15f));
                }
                RenderSettings.skybox = _sky;
            }
            RenderSettings.sun = key;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 0.8f;
            DynamicGI.UpdateEnvironment();

            // HDR, so the bolt blooms; alpha stays 1 because the shader blends by alpha too,
            // and the per-pulse fade rides on the line's vertex colour.
            var boltColor = FlashTint * 4f;
            boltColor.a = 1f;
            _boltMat = Mat.GlowNoFog(boltColor);

            StartCoroutine(BuildThunder());
            StartCoroutine(LightningLoop());
            StartCoroutine(ShellingLoop());
            StartCoroutine(SkirmishLoop());
        }

        // ------------------------------------------------------------------- lightning

        /// <summary>
        /// Thunder takes a while to synthesise, so it's built a clip per frame after startup
        /// rather than stalling the first frame. The first strike is well after this finishes.
        /// </summary>
        IEnumerator BuildThunder()
        {
            var close = new AudioClip[2];
            var far = new AudioClip[3];
            for (int i = 0; i < close.Length; i++)
            {
                close[i] = Synth.Thunder($"ThunderClose{i}", 700 + i, close: true, seconds: 6f);
                yield return null;
            }
            for (int i = 0; i < far.Length; i++)
            {
                far[i] = Synth.Thunder($"ThunderFar{i}", 800 + i, close: false, seconds: 7.5f, gain: 0.8f);
                yield return null;
            }
            _thunderClose = close;
            _thunderFar = far;
        }

        IEnumerator LightningLoop()
        {
            yield return new WaitForSeconds(Random.Range(4f, 9f));
            while (true)
            {
                float roll = Random.value;
                var kind = roll < 0.12f ? Strike.Close : roll < 0.45f ? Strike.Mid : Strike.Far;
                yield return StrikeRoutine(kind);
                yield return new WaitForSeconds(Random.Range(StrikeInterval.x, StrikeInterval.y));
            }
        }

        IEnumerator StrikeRoutine(Strike kind)
        {
            float azimuth = Random.Range(0f, Mathf.PI * 2f);
            var dir = new Vector3(Mathf.Sin(azimuth), 0f, Mathf.Cos(azimuth));
            float peak = kind switch { Strike.Close => 2.6f, Strike.Mid => 1.5f, _ => 0.75f };
            bool bolt = kind != Strike.Far;

            if (_sky != null) _sky.SetVector(FlashDirId, (dir + Vector3.up * 0.35f).normalized);
            if (bolt)
            {
                float dist = kind == Strike.Close ? Random.Range(170f, 230f) : Random.Range(280f, 400f);
                BuildBolt(ArenaCentre + dir * dist, kind == Strike.Close ? 1.6f : 1.1f);
            }

            // A real strike is several return strokes down the same channel: 2-4 pulses.
            int pulses = Random.Range(2, 5);
            for (int p = 0; p < pulses; p++)
            {
                float amount = peak * (p == 0 ? 1f : Random.Range(0.35f, 0.9f));
                ApplyFlash(amount, dir, bolt);
                yield return new WaitForSeconds(Random.Range(0.04f, 0.09f));
                ApplyFlash(amount * 0.25f, dir, bolt);
                yield return new WaitForSeconds(Random.Range(0.04f, 0.12f));
            }

            // Afterglow fading out.
            for (float t = 0f; t < 0.25f; t += Time.deltaTime)
            {
                ApplyFlash(peak * 0.25f * (1f - t / 0.25f), dir, false);
                yield return null;
            }
            ApplyFlash(0f, dir, false);

            if (kind == Strike.Close) ShakeCamera(0.35f, 0.5f);

            // Sound travels ~340 m/s; the delays are stretched for drama, not physics.
            float delay = kind switch
            {
                Strike.Close => Random.Range(0.35f, 0.8f),
                Strike.Mid => Random.Range(1.4f, 3.2f),
                _ => Random.Range(3.5f, 7.5f)
            };
            float volume = kind switch { Strike.Close => 1f, Strike.Mid => 0.7f, _ => 0.45f };
            float cutoff = kind switch { Strike.Close => 22000f, Strike.Mid => 3500f, _ => 1200f };
            StartCoroutine(PlayThunder(delay, kind == Strike.Close, volume, cutoff));
        }

        IEnumerator PlayThunder(float delay, bool close, float volume, float cutoff)
        {
            yield return new WaitForSeconds(delay);
            var bank = close ? _thunderClose : _thunderFar;
            if (bank == null) yield break;

            _thunderFilter.cutoffFrequency = cutoff;
            _thunder.pitch = Random.Range(0.9f, 1.08f);
            _thunder.PlayOneShot(bank[Random.Range(0, bank.Length)], volume * Master);
            if (close) ShakeCamera(0.25f, 1.2f);
        }

        /// <summary>
        /// One frame of lightning. The key light swings to the strike and flares, so for an
        /// instant every sandbag and container throws a hard shadow away from the flash.
        /// </summary>
        void ApplyFlash(float amount, Vector3 fromDir, bool boltVisible)
        {
            if (_sky != null) _sky.SetFloat(FlashId, amount * 0.6f);

            if (_key != null)
            {
                if (amount > 0.02f)
                {
                    const float elevation = 38f * Mathf.Deg2Rad;
                    var toArena = -(fromDir * Mathf.Cos(elevation) + Vector3.up * Mathf.Sin(elevation));
                    _key.transform.rotation = Quaternion.LookRotation(toArena);
                    _key.color = Color.Lerp(_keyColor, FlashTint, Mathf.Clamp01(amount));
                    _key.intensity = _keyIntensity + amount * 1.8f;
                }
                else
                {
                    _key.transform.rotation = _keyRotation;
                    _key.color = _keyColor;
                    _key.intensity = _keyIntensity;
                }
            }

            RenderSettings.ambientSkyColor = _ambientSky + FlashTint * (amount * 0.22f);
            RenderSettings.ambientEquatorColor = _ambientEquator + FlashTint * (amount * 0.12f);

            foreach (var lr in _bolts)
            {
                if (!lr.gameObject.activeSelf) continue;
                bool show = boltVisible && amount > 0.05f;
                lr.enabled = show;
                if (!show) continue;
                var c = new Color(1f, 1f, 1f, Mathf.Clamp01(amount / 1.5f));
                lr.startColor = c;
                lr.endColor = c;
            }
            if (!boltVisible && amount <= 0f)
                foreach (var lr in _bolts) lr.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------------ bolt

        /// <summary>
        /// A main channel from the cloud base to the ground, jagged by midpoint displacement,
        /// plus a few forks. Drawn with the fog-proof glow so it still burns through the murk.
        /// </summary>
        void BuildBolt(Vector3 ground, float width)
        {
            foreach (var lr in _bolts) lr.gameObject.SetActive(false);

            var top = ground + new Vector3(Random.Range(-25f, 25f), Random.Range(150f, 190f), Random.Range(-25f, 25f));
            Jagged(top, ground, 6, 0.16f);
            var main = TakeBolt();
            main.widthMultiplier = width;
            main.positionCount = _points.Count;
            main.SetPositions(_points.ToArray());
            var channel = _points.ToArray();

            int forks = Random.Range(1, 4);
            for (int f = 0; f < forks; f++)
            {
                int at = Random.Range(2, Mathf.Max(3, channel.Length * 2 / 3));
                var start = channel[at];
                var down = (channel[Mathf.Min(at + 1, channel.Length - 1)] - start).normalized;
                var outward = Vector3.Cross(down, Vector3.up).normalized * Random.Range(-1f, 1f);
                var end = start + (down + outward * 0.9f).normalized * Random.Range(25f, 60f);

                Jagged(start, end, 4, 0.2f);
                var fork = TakeBolt();
                fork.widthMultiplier = width * 0.45f;
                fork.positionCount = _points.Count;
                fork.SetPositions(_points.ToArray());
            }
        }

        void Jagged(Vector3 a, Vector3 b, int depth, float roughness)
        {
            _points.Clear();
            _points.Add(a);
            Subdivide(a, b, depth, (b - a).magnitude * roughness);
        }

        void Subdivide(Vector3 a, Vector3 b, int depth, float amount)
        {
            if (depth == 0) { _points.Add(b); return; }
            var axis = (b - a).normalized;
            var offset = Vector3.ProjectOnPlane(Random.insideUnitSphere, axis) * amount;
            var mid = (a + b) * 0.5f + offset;
            Subdivide(a, mid, depth - 1, amount * 0.5f);
            Subdivide(mid, b, depth - 1, amount * 0.5f);
        }

        LineRenderer TakeBolt()
        {
            foreach (var lr in _bolts)
                if (!lr.gameObject.activeSelf) { lr.gameObject.SetActive(true); lr.enabled = false; return lr; }

            var go = new GameObject("Bolt");
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.sharedMaterial = _boltMat;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.numCornerVertices = 0;
            line.numCapVertices = 0;
            line.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0.45f);
            line.enabled = false;
            _bolts.Add(line);
            return line;
        }

        // -------------------------------------------------------------------- shelling

        IEnumerator ShellingLoop()
        {
            yield return new WaitForSeconds(Random.Range(10f, 20f));
            while (true)
            {
                float azimuth = Random.Range(0f, Mathf.PI * 2f);
                var dir = new Vector3(Mathf.Sin(azimuth), 0f, Mathf.Cos(azimuth));
                int salvo = Random.value < 0.35f ? Random.Range(2, 5) : 1;

                for (int s = 0; s < salvo; s++)
                {
                    // Neighbouring impacts land close together, not all over the horizon.
                    var d = Quaternion.Euler(0f, Random.Range(-12f, 12f), 0f) * dir;
                    StartCoroutine(Shell(d, Random.Range(0.6f, 1.3f)));
                    yield return new WaitForSeconds(Random.Range(0.4f, 1.2f));
                }
                yield return new WaitForSeconds(Random.Range(ShellInterval.x, ShellInterval.y));
            }
        }

        IEnumerator Shell(Vector3 dir, float strength)
        {
            if (_sky != null) _sky.SetVector(GlowDirId, dir);

            const float rise = 0.08f, fall = 0.9f;
            for (float t = 0f; t < rise + fall; t += Time.deltaTime)
            {
                float g = t < rise ? t / rise : 1f - (t - rise) / fall;
                if (_sky != null) _sky.SetFloat(GlowId, strength * g * g);
                yield return null;
            }
            if (_sky != null) _sky.SetFloat(GlowId, 0f);

            yield return new WaitForSeconds(Random.Range(1.0f, 3.0f));
            if (GameAudio.I != null && GameAudio.I.Explosion != null)
            {
                _boom.pitch = Random.Range(0.42f, 0.6f);
                _boom.PlayOneShot(GameAudio.I.Explosion, Random.Range(0.22f, 0.4f) * strength * Master);
            }
        }

        // -------------------------------------------------------------------- skirmish

        IEnumerator SkirmishLoop()
        {
            yield return new WaitForSeconds(Random.Range(6f, 14f));
            while (true)
            {
                var weapons = WeaponLibrary.All;
                var def = weapons[Random.Range(0, weapons.Length)].Def;
                var clip = GameAudio.I != null ? GameAudio.I.ShotFor(def) : null;
                if (clip != null)
                {
                    int shots = def.Mode == FireMode.Auto ? Random.Range(4, 11) : Random.Range(2, 5);
                    float interval = 60f / def.RoundsPerMinute;
                    float volume = Random.Range(0.05f, 0.1f) * Master;
                    for (int i = 0; i < shots; i++)
                    {
                        _gunfire.pitch = Random.Range(0.86f, 0.98f);
                        _gunfire.PlayOneShot(clip, volume * Random.Range(0.8f, 1f));
                        yield return new WaitForSeconds(interval * Random.Range(1f, 1.35f));
                    }
                }
                yield return new WaitForSeconds(Random.Range(SkirmishInterval.x, SkirmishInterval.y));
            }
        }

        // ---------------------------------------------------------------------- helpers

        static float Master => GameAudio.I != null ? GameAudio.I.MasterVolume : 0.75f;

        AudioSource Source(string name, out AudioLowPassFilter filter, float cutoff)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            filter = go.AddComponent<AudioLowPassFilter>();
            filter.cutoffFrequency = cutoff;
            return src;
        }

        static void ShakeCamera(float amount, float duration)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var shake = cam.GetComponentInParent<CameraShake>();
            if (shake != null) shake.Add(amount, duration);
        }
    }
}
