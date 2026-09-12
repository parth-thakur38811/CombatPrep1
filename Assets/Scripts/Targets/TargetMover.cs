using UnityEngine;

namespace CombatPrep.Targets
{
    public enum MoveStyle { Static, Strafe, Bob, Orbit, Jitter }

    /// <summary>
    /// Movement patterns for dummies. Each one trains a different skill: Strafe for
    /// tracking and lead, Bob for vertical correction, Orbit for sustained tracking,
    /// Jitter for reactive micro-adjustment.
    /// </summary>
    public class TargetMover : MonoBehaviour
    {
        public MoveStyle Style = MoveStyle.Strafe;
        public float Speed = 2.4f;
        public float Range = 4.0f;
        public float JitterInterval = 0.55f;

        Vector3 _origin;
        float _phase;
        float _nextJitter;
        Vector3 _jitterTarget;

        void Awake()
        {
            _origin = transform.position;
            _phase = Random.value * Mathf.PI * 2f;
            _jitterTarget = _origin;
        }

        void OnEnable()
        {
            if (_origin != Vector3.zero) transform.position = _origin;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            _phase += dt * Speed / Mathf.Max(0.1f, Range);

            switch (Style)
            {
                case MoveStyle.Static:
                    break;

                case MoveStyle.Strafe:
                    transform.position = _origin + transform.right * (Mathf.Sin(_phase) * Range);
                    break;

                case MoveStyle.Bob:
                    transform.position = _origin
                        + transform.right * (Mathf.Sin(_phase) * Range)
                        + Vector3.up * (Mathf.Sin(_phase * 2.3f) * 0.55f);
                    break;

                case MoveStyle.Orbit:
                    transform.position = _origin + new Vector3(
                        Mathf.Cos(_phase) * Range, 0f, Mathf.Sin(_phase) * Range * 0.45f);
                    break;

                case MoveStyle.Jitter:
                    if (Time.time >= _nextJitter)
                    {
                        _nextJitter = Time.time + JitterInterval * Random.Range(0.6f, 1.5f);
                        _jitterTarget = _origin + new Vector3(
                            Random.Range(-Range, Range), 0f, Random.Range(-Range, Range) * 0.4f);
                    }
                    transform.position = Vector3.MoveTowards(transform.position, _jitterTarget, Speed * 2.2f * dt);
                    break;
            }
        }
    }
}
