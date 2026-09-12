using UnityEngine;

namespace CombatPrep.Player
{
    /// <summary>CharacterController movement: walk/sprint/crouch, air control, jump.</summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMotor : MonoBehaviour
    {
        [Header("Speeds (m/s)")]
        public float WalkSpeed = 4.2f;
        public float SprintSpeed = 7.0f;
        public float CrouchSpeed = 2.0f;
        public float AdsSpeed = 2.8f;

        [Header("Feel")]
        public float GroundAccel = 14f;
        public float AirAccel = 3f;
        public float Gravity = -22f;
        public float JumpHeight = 1.05f;

        [Header("Stance")]
        public float StandHeight = 1.8f;
        public float CrouchHeight = 1.15f;
        public float StanceSpeed = 10f;
        public Transform CameraPivot;

        CharacterController _cc;
        Vector3 _velocity;

        public bool IsGrounded => _cc.isGrounded;
        /// <summary>Horizontal speed normalized against sprint speed. Drives bob and spread.</summary>
        public float NormalizedSpeed { get; private set; }
        public bool IsCrouching { get; private set; }
        public bool IsSprinting { get; private set; }
        /// <summary>Set by the weapon while aiming down sights, to slow the player and block sprint.</summary>
        public bool AdsLock { get; set; }

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _cc.height = StandHeight;
            _cc.center = new Vector3(0f, StandHeight * 0.5f, 0f);
        }

        void Update()
        {
            var input = GameInput.I;
            float dt = Time.deltaTime;

            Vector2 move = input.Move;
            Vector3 wish = transform.right * move.x + transform.forward * move.y;
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            IsCrouching = input.Crouching;
            IsSprinting = input.Sprinting && !IsCrouching && !AdsLock && move.y > 0.1f;

            float speed = IsCrouching ? CrouchSpeed
                        : AdsLock ? AdsSpeed
                        : IsSprinting ? SprintSpeed
                        : WalkSpeed;

            Vector3 targetVel = wish * speed;
            Vector3 horizontal = new Vector3(_velocity.x, 0f, _velocity.z);
            float accel = IsGrounded ? GroundAccel : AirAccel;
            horizontal = Vector3.Lerp(horizontal, targetVel, 1f - Mathf.Exp(-accel * dt));
            _velocity.x = horizontal.x;
            _velocity.z = horizontal.z;

            if (IsGrounded)
            {
                if (_velocity.y < 0f) _velocity.y = -2f;
                if (input.JumpPress && !IsCrouching)
                    _velocity.y = Mathf.Sqrt(JumpHeight * -2f * Gravity);
            }
            else _velocity.y += Gravity * dt;

            _cc.Move(_velocity * dt);

            NormalizedSpeed = new Vector2(_velocity.x, _velocity.z).magnitude / SprintSpeed;

            // Stance: shrink the controller and drop the camera pivot together.
            float targetHeight = IsCrouching ? CrouchHeight : StandHeight;
            float h = Mathf.Lerp(_cc.height, targetHeight, 1f - Mathf.Exp(-StanceSpeed * dt));
            _cc.height = h;
            _cc.center = new Vector3(0f, h * 0.5f, 0f);
            if (CameraPivot != null)
                CameraPivot.localPosition = new Vector3(0f, h - 0.18f, 0f);
        }
    }
}
