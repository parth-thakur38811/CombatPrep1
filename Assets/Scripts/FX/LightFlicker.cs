using UnityEngine;

namespace CombatPrep.FX
{
    /// <summary>
    /// A bulb on a bad circuit: steady most of the time, then a burst of stutters and
    /// dropouts. The irregular rhythm is the point - a regular blink reads as a signal, an
    /// irregular one reads as something broken.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class LightFlicker : MonoBehaviour
    {
        Light _light;
        float _base;
        float _nextBurst;
        float _burstEnd;
        float _nextToggle;
        bool _on = true;

        void Awake()
        {
            _light = GetComponent<Light>();
            _base = _light.intensity;
            _nextBurst = Time.time + Random.Range(2f, 6f);
        }

        void Update()
        {
            float now = Time.time;

            if (now >= _nextBurst)
            {
                _burstEnd = now + Random.Range(0.3f, 1.4f);
                _nextBurst = _burstEnd + Random.Range(3f, 9f);
            }

            if (now < _burstEnd)
            {
                if (now >= _nextToggle)
                {
                    _on = !_on;
                    _nextToggle = now + Random.Range(0.02f, 0.12f);
                }
            }
            else
            {
                _on = true;
            }

            float hum = 1f + (Mathf.PerlinNoise(now * 12f, 0.5f) - 0.5f) * 0.08f;
            _light.intensity = _on ? _base * hum : _base * 0.06f;
        }
    }
}
