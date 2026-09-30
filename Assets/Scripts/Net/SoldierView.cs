using UnityEngine;
using CombatPrep.Core;
using CombatPrep.Weapons;

namespace CombatPrep.Net
{
    /// <summary>
    /// A remote player as the rigged soldier: Mixamo animations (Art/Characters/Mixamo, built
    /// into a controller by Editor/SoldierBuilder) driven by what the network already carries -
    /// movement from the synced position, the crouch flag, look pitch, shots and deaths.
    ///
    /// Two things are done by hand after the Animator has posed the body each frame. The spine
    /// bends with the owner's look pitch, so you can see where they aim. And the gun - whichever
    /// of the five they have out, built from their weapon and skin as everywhere else - is
    /// placed in their hands rather than parented to one: grip in the right hand, barrel
    /// pointing at the left. Any rifle animation then holds any of the guns, whatever its length.
    /// </summary>
    public class SoldierView : AvatarView
    {
        /// <summary>How long the firing pose is held after a shot - bridges an automatic's gaps.</summary>
        const float FireHold = 0.35f;
        /// <summary>How long a body lies after the death animation starts, before it's hidden.</summary>
        const float DeathLinger = 2.4f;
        /// <summary>
        /// A throw is shown from this long before the grenade leaves the hand: the grenade
        /// itself appears the moment the throw arrives, so the arm has to be nearly there.
        /// </summary>
        const float ThrowLead = 0.12f;
        /// <summary>Moving faster than this, a throw is the running one.</summary>
        const float RunningThrowSpeed = 2.5f;

        static readonly int MoveX = Animator.StringToHash("MoveX");
        static readonly int MoveZ = Animator.StringToHash("MoveZ");
        static readonly int Crouch = Animator.StringToHash("Crouch");
        static readonly int Firing = Animator.StringToHash("Firing");
        static readonly int Dead = Animator.StringToHash("Dead");

        Animator _animator;
        bool _hasDeath;
        int _throwLayer = -1;
        ArtLibrary.AnimMove _throwStand, _throwCrouch, _throwRun;
        float _throwUntil = -1f;
        Transform _spine, _chest, _upperChest;
        Transform _handR, _handL, _fingersR, _fingersL;
        Vector3 _gripLocal;
        Quaternion _gunAlign;

        Vector3 _lastPos;
        bool _hasLastPos;
        Vector2 _move;
        float _lastShot = -99f;
        bool _dead;
        float _hideAt = -1f;

        public void Init(Animator animator, ArtLibrary.Character art)
        {
            _animator = animator;
            _hasDeath = art.HasDeath;
            _throwLayer = art.ThrowLayer;
            _throwStand = art.ThrowStand;
            _throwCrouch = art.ThrowCrouch;
            _throwRun = art.ThrowRun;

            _spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            _chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            _upperChest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            _handR = animator.GetBoneTransform(HumanBodyBones.RightHand);
            _handL = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            _fingersR = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            _fingersL = animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
        }

        protected override void OnWeaponShown()
        {
            _gripLocal = Weapon.Grip.localPosition;
            // Turns the gun so its grip-to-support line runs down +Z; PlaceGun then points that
            // line from one hand to the other.
            _gunAlign = Quaternion.Inverse(Quaternion.LookRotation(
                Weapon.Support.localPosition - Weapon.Grip.localPosition, Vector3.up));

            // Switched mid-throw: this gun stays away until the arm comes down too.
            if (_throwUntil > 0f) ShowGun(false);
        }

        public override void OnShot()
        {
            base.OnShot();
            _lastShot = Time.time;
        }

        /// <summary>
        /// A grenade throw: standing, crouched or on the run to match what the player is doing,
        /// with the gun put away until the arm comes back down.
        /// </summary>
        public override void OnThrow()
        {
            if (_animator == null || _dead || _throwLayer < 0 || _throwLayer >= _animator.layerCount) return;
            var move = PickThrow();
            if (move == null) return;

            float start = Mathf.Clamp(move.Release - ThrowLead, 0f, move.Length);
            _animator.CrossFadeInFixedTime(move.State, 0.08f, _throwLayer, start);
            _throwUntil = Time.time + Mathf.Max(0.3f, (move.Length - start) * 0.95f);
            ShowGun(false);
        }

        ArtLibrary.AnimMove PickThrow()
        {
            static bool Ok(ArtLibrary.AnimMove m) => m != null && m.Valid;
            if (Stance > 0.5f && Ok(_throwCrouch)) return _throwCrouch;
            if (_move.magnitude > RunningThrowSpeed && Ok(_throwRun)) return _throwRun;
            if (Ok(_throwStand)) return _throwStand;
            if (Ok(_throwRun)) return _throwRun;
            return Ok(_throwCrouch) ? _throwCrouch : null;
        }

        void ShowGun(bool shown)
        {
            if (Weapon != null && Weapon.Root != null) Weapon.Root.gameObject.SetActive(shown);
        }

        public override void OnDied()
        {
            _dead = true;
            _throwUntil = -1f;
            ShowGun(true);
            SetColliders(false);    // a falling body can't be shot or walked into
            if (_hasDeath) _hideAt = Time.time + DeathLinger;
            else SetVisible(false);
        }

        public override void OnRespawned()
        {
            _dead = false;
            _throwUntil = -1f;
            ShowGun(true);
            _hideAt = -1f;
            _hasLastPos = false;    // they teleported; that isn't a sprint
            _move = Vector2.zero;
            if (_animator != null)
            {
                _animator.SetBool(Dead, false);
                _animator.SetBool(Firing, false);
            }
            SetVisible(true);
        }

        void Update()
        {
            if (_animator == null) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);

            // Velocity from the synced position. NetworkTransform already interpolates it, so
            // differencing frames is smooth enough; a jump of metres in one frame is a
            // teleport, not a sprint.
            Vector3 pos = transform.position;
            Vector3 velocity = _hasLastPos ? (pos - _lastPos) / dt : Vector3.zero;
            _lastPos = pos;
            _hasLastPos = true;
            if (velocity.sqrMagnitude > 15f * 15f) velocity = Vector3.zero;

            Vector3 local = transform.InverseTransformDirection(velocity);
            _move = Vector2.Lerp(_move, new Vector2(local.x, local.z), 1f - Mathf.Exp(-10f * dt));

            _animator.SetFloat(MoveX, _move.x);
            _animator.SetFloat(MoveZ, _move.y);
            _animator.SetFloat(Crouch, Stance);
            _animator.SetBool(Firing, !_dead && Time.time - _lastShot < FireHold);
            _animator.SetBool(Dead, _dead);

            if (_throwUntil > 0f && Time.time >= _throwUntil)
            {
                _throwUntil = -1f;
                ShowGun(true);
            }

            if (_hideAt > 0f && Time.time >= _hideAt)
            {
                _hideAt = -1f;
                SetVisible(false);
            }
        }

        protected override void Pose()
        {
            if (_animator == null) return;

            // Aim: bend the spine by the look pitch, spread down three joints so it reads as a
            // lean from the waist rather than a broken neck. Positive pitch looks down.
            if (!_dead)
            {
                float p = Mathf.Clamp(Pitch, -70f, 70f);
                Vector3 axis = transform.right;
                Bend(_spine, axis, p * 0.3f);
                Bend(_chest, axis, p * 0.3f);
                Bend(_upperChest, axis, p * 0.4f);
            }

            PlaceGun();
        }

        static void Bend(Transform bone, Vector3 axis, float degrees)
        {
            if (bone != null) bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
        }

        /// <summary>Grip in the right hand, barrel toward the left hand, kept upright.</summary>
        void PlaceGun()
        {
            if (GunMount == null || Weapon == null || _handR == null || _handL == null) return;

            Vector3 right = Palm(_handR, _fingersR);
            Vector3 along = Palm(_handL, _fingersL) - right;
            if (along.sqrMagnitude < 0.01f) along = transform.forward;   // hands together: point ahead

            var rotation = Quaternion.LookRotation(along, transform.up) * _gunAlign;
            GunMount.SetPositionAndRotation(right - rotation * _gripLocal, rotation);
        }

        /// <summary>Mixamo hand bones sit at the wrist; the grip is held further in, at the fingers' base.</summary>
        static Vector3 Palm(Transform hand, Transform fingers)
            => fingers != null ? Vector3.Lerp(hand.position, fingers.position, 0.7f) : hand.position;
    }
}
