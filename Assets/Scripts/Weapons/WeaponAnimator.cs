using UnityEngine;
using CombatPrep.Core;
using CombatPrep.Player;

namespace CombatPrep.Weapons
{
    /// <summary>
    /// The visual recoil layer. Nothing here affects where bullets go - it exists purely
    /// to sell the shot. Kick, sway and bob are all spring-driven so they overshoot and
    /// settle instead of easing linearly, which is most of the difference between a gun
    /// that feels alive and one that feels like a sliding cube.
    ///
    /// The ADS pose is derived, not hand-tuned: we solve for the root position that puts
    /// the optic's SightPoint dead centre in front of the camera, so any weapon built by
    /// WeaponModelBuilder aims correctly with no magic offsets.
    /// </summary>
    public class WeaponAnimator : MonoBehaviour
    {
        [Header("Hip pose (local to camera)")]
        public Vector3 HipPosition = new Vector3(0.135f, -0.115f, 0.26f);
        public Vector3 HipEuler = new Vector3(0f, -2.5f, 0f);
        public float SightDistance = 0.22f;

        [Header("Sway / bob")]
        public float SwayStiffness = 120f;
        public float SwayDamping = 16f;
        public float SwayMax = 0.05f;
        public float BobFrequency = 11f;

        [Header("Sprint pose")]
        public Vector3 SprintPosition = new Vector3(0.18f, -0.16f, 0.16f);
        public Vector3 SprintEuler = new Vector3(12f, -28f, -14f);
        public float SprintBlendSpeed = 9f;

        WeaponDefinition _def;
        WeaponModel _model;
        PlayerMotor _motor;

        Vector3 _adsPosition;
        Spring3 _kickPos, _kickRot, _sway;
        float _adsT, _sprintT, _bobTime;
        float _reloadT = -1f, _reloadDuration;
        float _sprintSuppressUntil;

        public float AdsProgress => _adsT;

        /// <summary>Snaps out of aim/sprint poses. Used when the weapon is holstered for a grenade.</summary>
        public void ForceHip()
        {
            _adsT = 0f;
            _sprintT = 0f;
        }

        public void Init(WeaponDefinition def, WeaponModel model, PlayerMotor motor)
        {
            _def = def;
            _model = model;
            _motor = motor;

            // Solve the ADS pose: root + sightLocalOffset must land exactly on the camera
            // axis at the weapon's own eye-relief distance. Solving all three axes (not just
            // x/y) is what lets a long scope and a compact red dot both sit correctly.
            SightDistance = model.SightDistance;
            Vector3 sightLocal = model.SightPoint.localPosition;
            _adsPosition = new Vector3(0f, 0f, SightDistance) - sightLocal;
        }

        public void Tick(float dt, bool aiming, Vector2 lookDelta)
        {
            _adsT = Mathf.MoveTowards(_adsT, aiming ? 1f : 0f, dt / Mathf.Max(0.01f, _def.AdsTime));
            float ads = Mathf.SmoothStep(0f, 1f, _adsT);

            // Firing overrides the sprint pose for a moment, and the gun comes back up
            // faster than it goes down, so a shot taken at a run reads correctly.
            bool sprinting = _motor.IsSprinting && _adsT < 0.01f && Time.time > _sprintSuppressUntil;
            float blend = SprintBlendSpeed * (sprinting ? 1f : 2.4f);
            _sprintT = Mathf.MoveTowards(_sprintT, sprinting ? 1f : 0f, dt * blend);
            float sprint = Mathf.SmoothStep(0f, 1f, _sprintT);

            // --- sway: the gun lags behind the camera, more so from the hip ---
            Vector3 swayTarget = new Vector3(
                Mathf.Clamp(-lookDelta.x * _def.SwayAmount, -SwayMax, SwayMax),
                Mathf.Clamp(-lookDelta.y * _def.SwayAmount, -SwayMax, SwayMax),
                0f) * (1f - ads * 0.75f);
            _sway.Step(swayTarget, SwayStiffness, SwayDamping, dt);

            // --- bob: driven by actual movement speed, damped hard while aiming ---
            float speed = _motor.NormalizedSpeed;
            _bobTime += dt * BobFrequency * speed;
            float bobScale = _def.BobAmount * speed * (1f - ads * 0.85f);
            Vector3 bob = new Vector3(
                Mathf.Sin(_bobTime) * bobScale,
                -Mathf.Abs(Mathf.Sin(_bobTime * 2f)) * bobScale * 0.6f,
                0f);

            // --- springs settle back toward zero; shots inject velocity via Kick() ---
            _kickPos.Step(Vector3.zero, _def.KickStiffness, _def.KickDamping, dt);
            _kickRot.Step(Vector3.zero, _def.KickStiffness, _def.KickDamping, dt);

            // --- reload pose ---
            Vector3 reloadPos = Vector3.zero, reloadEuler = Vector3.zero;
            if (_reloadT >= 0f)
            {
                _reloadT += dt;
                float u = Mathf.Clamp01(_reloadT / _reloadDuration);
                // Dip down and tilt in, hold, then come back up.
                float shape = Mathf.Sin(Mathf.Clamp01(u * 1.15f) * Mathf.PI);
                reloadPos = new Vector3(0.02f, -0.11f, -0.05f) * shape;
                reloadEuler = new Vector3(-14f, 22f, 26f) * shape;
                if (u >= 1f) _reloadT = -1f;
            }

            // --- compose ---
            Vector3 basePos = Vector3.Lerp(HipPosition, _adsPosition, ads);
            Vector3 baseEuler = Vector3.Lerp(HipEuler, Vector3.zero, ads);
            basePos = Vector3.Lerp(basePos, SprintPosition, sprint);
            baseEuler = Vector3.Lerp(baseEuler, SprintEuler, sprint);

            transform.localPosition = basePos + _sway.Value + bob + _kickPos.Value + reloadPos;
            transform.localRotation = Quaternion.Euler(baseEuler + _kickRot.Value + reloadEuler
                                                       + new Vector3(-_sway.Value.y * 220f, _sway.Value.x * 220f, 0f));
        }

        /// <summary>Injects the per-shot visual punch. Scaled down while aiming.</summary>
        public void Kick()
        {
            float ads = Mathf.Lerp(1f, 0.55f, _adsT);
            _kickPos.Impulse(new Vector3(
                Random.Range(-0.2f, 0.2f) * _def.KickBack,
                _def.KickUp,
                -_def.KickBack) * 42f * ads);
            _kickRot.Impulse(new Vector3(
                -_def.KickPitch,
                Random.Range(-0.35f, 0.35f) * _def.KickRoll,
                Random.Range(-1f, 1f) * _def.KickRoll) * 26f * ads);
        }

        /// <summary>Holds the sprint pose off for a moment, so firing at a run looks right.</summary>
        public void SuppressSprint(float seconds)
            => _sprintSuppressUntil = Mathf.Max(_sprintSuppressUntil, Time.time + seconds);

        public void PlayReload(float duration)
        {
            _reloadT = 0f;
            _reloadDuration = duration;
        }
    }
}
