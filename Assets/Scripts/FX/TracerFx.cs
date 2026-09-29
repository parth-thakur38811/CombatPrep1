using UnityEngine;

namespace CombatPrep.FX
{
    /// <summary>
    /// A tracer: a short, glowing streak that flies from the muzzle to wherever the shot
    /// landed, instead of a line that blinks into existence along the whole path. Shots are
    /// hitscan, so the damage is already done - this is just the round you see going.
    ///
    /// The line's look (width, colour, glow) is on its LineRenderer in the prefab; speed and
    /// length are here.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class TracerFx : MonoBehaviour
    {
        [Tooltip("How fast the streak travels, m/s. Slower than a real bullet, so the eye can follow it.")]
        public float Speed = 520f;
        [Tooltip("Length of the streak, metres.")]
        public float Length = 7f;

        LineRenderer _line;
        Vector3 _from, _dir;
        float _distance, _head;

        public void Fire(Vector3 from, Vector3 to)
        {
            if (_line == null) _line = GetComponent<LineRenderer>();
            _line.positionCount = 2;
            _line.useWorldSpace = true;

            _from = from;
            _dir = to - from;
            _distance = _dir.magnitude;
            _dir = _distance > 1e-4f ? _dir / _distance : Vector3.forward;
            // Start a little way out of the barrel, so a point-blank shot still shows a streak.
            _head = Mathf.Min(_distance, Length * 0.5f);

            gameObject.SetActive(true);
            Apply();
        }

        void Update()
        {
            _head += Speed * Time.deltaTime;
            if (_head - Length >= _distance)
            {
                gameObject.SetActive(false);
                return;
            }
            Apply();
        }

        void Apply()
        {
            float head = Mathf.Min(_head, _distance);
            float tail = Mathf.Clamp(_head - Length, 0f, head);
            _line.SetPosition(0, _from + _dir * tail);
            _line.SetPosition(1, _from + _dir * head);
        }
    }
}
