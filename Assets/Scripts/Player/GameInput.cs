using System.Collections.Generic;
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

        /// <summary>Keys 1 to 5, one per gun in WeaponLibrary order.</summary>
        public const int WeaponKeys = 5;

        InputAction _move, _look, _fire, _aim, _reload, _sprint, _jump, _crouch, _pause, _grenade;
        readonly InputAction[] _weaponKeys = new InputAction[WeaponKeys];

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
        public bool FireRelease  => _fire.WasReleasedThisFrame();
        public bool GrenadePress => _grenade.WasPressedThisFrame();

        /// <summary>The weapon key pressed this frame - 0 for key 1 up to 4 for key 5 - or -1.</summary>
        public int WeaponKeyPress
        {
            get
            {
                for (int i = 0; i < _weaponKeys.Length; i++)
                    if (_weaponKeys[i].WasPressedThisFrame()) return i;
                return -1;
            }
        }

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
            _grenade = new InputAction("Grenade", InputActionType.Button, "<Keyboard>/g");

            for (int i = 0; i < _weaponKeys.Length; i++)
                _weaponKeys[i] = new InputAction($"Weapon{i + 1}", InputActionType.Button, $"<Keyboard>/{i + 1}");
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

        InputAction[] All()
        {
            var all = new List<InputAction> { _move, _look, _fire, _aim, _reload, _sprint, _jump, _crouch, _pause, _grenade };
            all.AddRange(_weaponKeys);
            return all.ToArray();
        }

        public static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
