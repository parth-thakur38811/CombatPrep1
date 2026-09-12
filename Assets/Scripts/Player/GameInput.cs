using UnityEngine;
using UnityEngine.InputSystem;

namespace CombatPrep.Player
{
    /// <summary>
    /// Input System actions declared in code rather than an .inputactions asset, so the
    /// project stays asset-free. Rebinding and gamepad support drop in here later without
    /// touching any consumer.
    /// </summary>
    public class GameInput : MonoBehaviour
    {
        public static GameInput I { get; private set; }

        InputAction _move, _look, _fire, _aim, _reload, _sprint, _jump, _crouch, _pause;

        public Vector2 Move    => _move.ReadValue<Vector2>();
        public Vector2 Look    => _look.ReadValue<Vector2>();
        public bool Firing     => _fire.IsPressed();
        public bool FirePress  => _fire.WasPressedThisFrame();
        public bool Aiming     => _aim.IsPressed();
        public bool Reload     => _reload.WasPressedThisFrame();
        public bool Sprinting  => _sprint.IsPressed();
        public bool JumpPress  => _jump.WasPressedThisFrame();
        public bool Crouching  => _crouch.IsPressed();
        public bool PausePress => _pause.WasPressedThisFrame();

        void Awake()
        {
            I = this;

            _move = new InputAction("Move", InputActionType.Value);
            _move.AddCompositeBinding("2DVector")
                 .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                 .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");

            _look   = new InputAction("Look",   InputActionType.Value,  "<Mouse>/delta");
            _fire   = new InputAction("Fire",   InputActionType.Button, "<Mouse>/leftButton");
            _aim    = new InputAction("Aim",    InputActionType.Button, "<Mouse>/rightButton");
            _reload = new InputAction("Reload", InputActionType.Button, "<Keyboard>/r");
            _sprint = new InputAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift");
            _jump   = new InputAction("Jump",   InputActionType.Button, "<Keyboard>/space");
            _crouch = new InputAction("Crouch", InputActionType.Button, "<Keyboard>/leftCtrl");
            _pause  = new InputAction("Pause",  InputActionType.Button, "<Keyboard>/escape");
        }

        void OnEnable()
        {
            foreach (var a in All()) a.Enable();
            LockCursor(true);
        }

        void OnDisable()
        {
            foreach (var a in All()) a.Disable();
        }

        InputAction[] All() => new[] { _move, _look, _fire, _aim, _reload, _sprint, _jump, _crouch, _pause };

        public static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
