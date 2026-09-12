using UnityEngine;

namespace CombatPrep.Player
{
    /// <summary>
    /// Mouse aim plus the aim-recoil layer.
    ///
    /// The important part is recoil *compensation*. Recoil lives in its own accumulator,
    /// separate from the player's aim. When the player drags the mouse against the recoil,
    /// we spend that input on shrinking the accumulator before it ever reaches their base
    /// aim. Pulling down therefore cancels the climb instead of fighting the recovery, and
    /// on release the gun only springs back by the amount the player did NOT compensate.
    /// Skipping this step is why naive recoil implementations feel drunk.
    /// </summary>
    public class PlayerLook : MonoBehaviour
    {
        [Header("Refs")]
        public Transform Body;
        public Camera Cam;

        [Header("Sensitivity")]
        public float Sensitivity = 0.14f;     // degrees per mouse count
        public float AdsSensScale = 0.72f;    // lower sens while aiming down sights
        public float PitchMin = -89f, PitchMax = 89f;

        [Header("Recoil feel")]
        public float RecoilStiffness = 180f;
        public float RecoilDamping = 22f;
        public float RecoveryDelay = 0.08f;   // grace after the last shot before the gun walks back
        public float RecoverySpeed = 9f;

        float _pitch, _yaw;
        Vector2 _recoilTarget;    // where recoil wants the camera, in degrees (x = up, y = right)
        Vector2 _recoilCurrent;   // spring-smoothed actual offset
        Vector2 _recoilVel;
        float _lastShotTime = -99f;

        public bool IsAiming { get; set; }
        public Vector3 AimOrigin => Cam.transform.position;
        public Vector3 AimForward => Cam.transform.forward;

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            Vector2 raw = GameInput.I.Look;
            float sens = Sensitivity * (IsAiming ? AdsSensScale : 1f);
            Vector2 delta = raw * sens;   // already a per-frame delta; never scale by dt

            // --- compensation: the player's input eats the recoil accumulator first ---
            if (_recoilTarget.x > 0f && delta.y < 0f)
            {
                float consume = Mathf.Min(_recoilTarget.x, -delta.y);
                _recoilTarget.x -= consume;
                delta.y += consume;
            }
            if (delta.x != 0f && _recoilTarget.y != 0f && Mathf.Sign(delta.x) != Mathf.Sign(_recoilTarget.y))
            {
                float consume = Mathf.Min(Mathf.Abs(_recoilTarget.y), Mathf.Abs(delta.x));
                float dir = Mathf.Sign(_recoilTarget.y);
                _recoilTarget.y -= dir * consume;
                delta.x += dir * consume;
            }

            _yaw += delta.x;
            _pitch = Mathf.Clamp(_pitch - delta.y, PitchMin, PitchMax);

            // --- recovery: only the uncompensated remainder walks back ---
            if (Time.time - _lastShotTime > RecoveryDelay)
                _recoilTarget = Vector2.Lerp(_recoilTarget, Vector2.zero, 1f - Mathf.Exp(-RecoverySpeed * dt));

            // --- spring the visible offset toward the target, substepped for stability ---
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / 0.005f), 1, 16);
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                Vector2 accel = (_recoilTarget - _recoilCurrent) * RecoilStiffness - _recoilVel * RecoilDamping;
                _recoilVel += accel * h;
                _recoilCurrent += _recoilVel * h;
            }

            Body.localRotation = Quaternion.Euler(0f, _yaw, 0f);
            Cam.transform.localRotation = Quaternion.Euler(_pitch - _recoilCurrent.x, _recoilCurrent.y, 0f);
        }

        /// <summary>Called by the weapon on each shot. x = upward kick, y = horizontal, in degrees.</summary>
        public void AddRecoil(Vector2 impulse)
        {
            _recoilTarget += impulse;
            _lastShotTime = Time.time;
        }

        public void ResetRecoil()
        {
            _recoilTarget = Vector2.zero;
            _recoilCurrent = Vector2.zero;
            _recoilVel = Vector2.zero;
        }
    }
}
