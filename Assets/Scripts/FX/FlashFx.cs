using UnityEngine;

namespace CombatPrep.FX
{
    /// <summary>
    /// A one-shot burst made of particle systems and an optional light: a muzzle flash, a
    /// bullet impact, a grenade blast. The look lives entirely in the prefab (Prefabs/FX) -
    /// open it and change anything.
    ///
    /// How it fires: each child system keeps playing with its emission switched off, and
    /// Play() throws that system's first burst by hand. So the burst counts you see in the
    /// Inspector are the settings, and nothing ever goes off on its own - including when a
    /// pooled copy is switched back on.
    /// </summary>
    public class FlashFx : MonoBehaviour
    {
        [Tooltip("Peak brightness of the light, if the prefab has one.")]
        public float LightIntensity = 10f;
        [Tooltip("Seconds for the light to fade back out.")]
        public float LightFade = 0.06f;
        [Tooltip("Systems that take the colour of the surface that was hit - impact dust and chips.")]
        public ParticleSystem[] Tinted = new ParticleSystem[0];
        [Tooltip("Systems left out when a hit shouldn't spark, such as a bullet into a person.")]
        public ParticleSystem[] Sparks = new ParticleSystem[0];
        [Tooltip("Remove the whole object this many seconds after it plays (0 = keep it, for pooling).")]
        public float DestroyAfter;

        ParticleSystem[] _systems;
        Light _light;
        float _lightT = -1f, _peak;

        void Awake() => Cache();

        void Cache()
        {
            if (_systems != null) return;
            _systems = GetComponentsInChildren<ParticleSystem>(true);
            _light = GetComponentInChildren<Light>(true);
            if (_light != null)
            {
                _light.intensity = 0f;
                _light.enabled = false;
            }
        }

        public void Play() => Play(1f, null, true);

        /// <param name="amount">Scales every burst - fewer chips off a paper target, say.</param>
        /// <param name="tint">The surface colour, for the Tinted systems.</param>
        /// <param name="sparks">Whether the Sparks systems fire.</param>
        public void Play(float amount, Color? tint, bool sparks)
        {
            Cache();
            foreach (var ps in _systems)
            {
                if (!sparks && System.Array.IndexOf(Sparks, ps) >= 0) continue;
                var emission = ps.emission;
                if (emission.burstCount == 0) continue;

                var burst = emission.GetBurst(0);
                int count = Mathf.RoundToInt(Random.Range(burst.minCount, burst.maxCount + 1) * amount);
                if (count <= 0) continue;
                if (!ps.isPlaying) ps.Play(false);

                if (tint.HasValue && System.Array.IndexOf(Tinted, ps) >= 0)
                {
                    var p = new ParticleSystem.EmitParams { startColor = ps.main.startColor.color * tint.Value };
                    ps.Emit(p, count);
                }
                else
                {
                    ps.Emit(count);
                }
            }

            if (_light != null && LightIntensity > 0f)
            {
                _peak = LightIntensity * Random.Range(0.85f, 1.15f) * Mathf.Max(0.3f, amount);
                _light.intensity = _peak;
                _light.enabled = true;
                _lightT = 0f;
            }

            if (DestroyAfter > 0f) Destroy(gameObject, DestroyAfter);
        }

        void Update()
        {
            if (_lightT < 0f || _light == null) return;
            _lightT += Time.deltaTime;
            float u = Mathf.Clamp01(_lightT / Mathf.Max(0.001f, LightFade));
            _light.intensity = _peak * (1f - u) * (1f - u);
            if (u >= 1f)
            {
                _light.enabled = false;
                _lightT = -1f;
            }
        }
    }
}
