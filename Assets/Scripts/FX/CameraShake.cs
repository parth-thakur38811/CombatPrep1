using UnityEngine;

namespace CombatPrep.FX
{
    /// <summary>
    /// Additive positional/rotational shake on a child of the camera, so it never fights
    /// the aim transform. Perlin-driven rather than random-per-frame: random noise reads as
    /// buzzing, smooth noise reads as impact.
    /// </summary>
    public class CameraShake : MonoBehaviour
    {
        public float PositionScale = 0.012f;
        public float RotationScale = 0.55f;
        public float Frequency = 26f;

        float _trauma;      // 0..1, decays every frame
        float _decay = 1f;
        float _seed;

        void Awake() => _seed = Random.value * 100f;

        public void Add(float amount, float duration)
        {
            _trauma = Mathf.Clamp01(_trauma + amount);
            _decay = 1f / Mathf.Max(0.01f, duration);
        }

        void LateUpdate()
        {
            if (_trauma <= 0f)
            {
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;
                return;
            }

            // Squaring the trauma makes small hits subtle and big ones dramatic.
            float s = _trauma * _trauma;
            float t = Time.time * Frequency;

            Vector3 n = new Vector3(
                Mathf.PerlinNoise(_seed, t) * 2f - 1f,
                Mathf.PerlinNoise(_seed + 11f, t) * 2f - 1f,
                Mathf.PerlinNoise(_seed + 23f, t) * 2f - 1f);

            transform.localPosition = n * (PositionScale * s);
            transform.localRotation = Quaternion.Euler(n * (RotationScale * s));

            _trauma = Mathf.Max(0f, _trauma - _decay * Time.deltaTime);
        }
    }
}
