using UnityEngine;
using CombatPrep.Audio;
using CombatPrep.FX;
using CombatPrep.Player;
using CombatPrep.Targets;
using CombatPrep.UI;

namespace CombatPrep.Weapons
{
    /// <summary>
    /// Drives firing, reloading and ADS, and wires the recoil, spread, visual, audio and
    /// damage layers together.
    ///
    /// Shots are traced from the camera, then drawn from the muzzle to wherever that trace
    /// landed. Firing from the muzzle directly is the classic first-person bug: the barrel
    /// sits below and right of the eye, so shots taken while hugging cover hit the wall you
    /// are leaning past.
    /// </summary>
    public class Weapon : MonoBehaviour
    {
        public WeaponDefinition Def;

        [Header("Refs")]
        public Camera Cam;
        public PlayerLook Look;
        public PlayerMotor Motor;
        public CameraShake Shake;
        public LayerMask HitMask = ~0;

        WeaponModel _model;
        WeaponAnimator _anim;
        RecoilSystem _recoil;
        SpreadSystem _spread;

        int _mag, _reserve;
        float _nextShotTime;
        float _reloadDoneAt = -1f;
        int _burstRemaining;

        public bool IsReloading => _reloadDoneAt > 0f;
        public bool Active { get; set; } = true;

        public void Init(WeaponDefinition def, WeaponModel model, WeaponAnimator anim)
        {
            Def = def;
            _model = model;
            _anim = anim;

            _recoil = new RecoilSystem(def);
            _spread = new SpreadSystem(def);
            _spread.Reset();

            _mag = def.MagSize;
            _reserve = def.ReserveAmmo;

            // Aim sensitivity is a weapon property: a 22-degree scope is unusable at the
            // same turn rate as a red dot.
            Look.AdsSensScale = def.AdsSensScale;

            GameAudio.I.RebuildShot(def.ShotGain, def.ShotDecay, def.ShotBodyHz, def.ShotCrack, def.ShotTail);
            FxSystem.I.AttachMuzzle(model.Muzzle);

            Hud.I.SetWeaponName(def.DisplayName);
            Hud.I.SetAmmo(_mag, _reserve, false);
            Hud.I.SetCrosshairStyle(def.Crosshair, def.CrosshairColor);
        }

        void Update()
        {
            if (!Active) return;

            float dt = Time.deltaTime;
            var input = GameInput.I;

            // Aiming is not gated on sprinting: raising the sights cancels the sprint via
            // AdsLock instead. Gating it the other way round meant holding Shift+W+RMB
            // simply never aimed.
            bool aiming = input.Aiming && !IsReloading;
            Look.IsAiming = aiming;
            Motor.AdsLock = aiming;

            // --- reload ---
            if (input.Reload) TryReload();
            if (IsReloading && Time.time >= _reloadDoneAt) FinishReload();

            // --- fire ---
            if (Def.Mode == FireMode.Burst && input.FirePress && _burstRemaining <= 0)
                _burstRemaining = Def.BurstCount;

            bool wantsFire = Def.Mode switch
            {
                FireMode.Auto => input.Firing,
                FireMode.Semi => input.FirePress,
                FireMode.Burst => _burstRemaining > 0,
                _ => false
            };

            if (wantsFire && CanFire()) Fire(aiming);

            if (input.FirePress && _mag <= 0 && !IsReloading)
            {
                GameAudio.I.Play(GameAudio.I.DryFire, 0.5f);
                TryReload();
            }

            // --- spread + crosshair ---
            _spread.Tick(dt, Motor.NormalizedSpeed, aiming, Motor.IsCrouching, Motor.IsGrounded);

            float fov = Mathf.Lerp(Def.HipFov, Def.AdsFov, _anim.AdsProgress);
            Cam.fieldOfView = fov;

            Hud.I.SetSpread(_spread.Current, fov);
            Hud.I.SetCrosshairVisible(_anim.AdsProgress < 0.55f);

            // --- visual layer ---
            _anim.Tick(dt, aiming, input.Look);
        }

        bool CanFire()
        {
            // Sprinting and being airborne no longer block the trigger - both are paid for
            // in spread (MoveSpreadScale and AirSpreadScale) rather than by locking you out.
            if (IsReloading) return false;
            if (_mag <= 0) return false;
            if (Time.time < _nextShotTime) return false;
            return true;
        }

        void Fire(bool aiming)
        {
            _nextShotTime = Time.time + Def.ShotInterval;
            _mag--;
            if (Def.Mode == FireMode.Burst) _burstRemaining--;

            Vector3 origin = Cam.transform.position;
            Vector3 forward = Cam.transform.forward;
            Vector3 muzzle = _model.Muzzle.position;

            // Shotguns resolve as several independent rays through the same cone, then
            // report one combined result so the HUD and accuracy stats stay per-trigger-pull.
            int pellets = Mathf.Max(1, Def.PelletsPerShot);
            int hits = 0;
            float totalDamage = 0f;
            bool anyHead = false, anyKill = false;
            Vector3 hitCentroid = Vector3.zero;

            for (int i = 0; i < pellets; i++)
            {
                Vector3 dir = SpreadSystem.ApplyCone(forward, _spread.Current);
                Vector3 endPoint = origin + dir * Def.MaxRange;

                if (Physics.Raycast(origin, dir, out RaycastHit hit, Def.MaxRange,
                                    HitMask, QueryTriggerInteraction.Ignore))
                {
                    endPoint = hit.point;
                    if (ResolveHit(hit, dir, out float dmg, out bool head, out bool killed))
                    {
                        hits++;
                        totalDamage += dmg;
                        anyHead |= head;
                        anyKill |= killed;
                        hitCentroid += hit.point;
                    }
                }

                FxSystem.I.Tracer(muzzle, endPoint);
            }

            if (hits > 0)
                Hud.I.ReportHit(totalDamage, anyHead, anyKill, hitCentroid / hits);

            Hud.I.RegisterShot(hits > 0, anyHead, anyKill);

            if (hits > 0)
            {
                var marker = anyKill ? GameAudio.I.KillMarker
                           : anyHead ? GameAudio.I.HeadshotMarker
                           : GameAudio.I.HitMarker;
                GameAudio.I.Play(marker, 0.9f, 0.02f);
            }

            // --- feedback ---
            FxSystem.I.MuzzleFlash();
            GameAudio.I.Play(GameAudio.I.Shot, 1f, 0.045f);

            Look.AddRecoil(_recoil.NextImpulse(aiming));
            _anim.Kick();
            // Snap out of the lowered sprint pose so a sprint-fire looks like a shot rather
            // than the gun going off while pointed at the floor.
            _anim.SuppressSprint(0.35f);
            if (Shake != null)
                Shake.Add(Def.ShakeAmplitude * (aiming ? 0.6f : 1f), Def.ShakeDuration);

            _spread.OnShot();
            Hud.I.SetAmmo(_mag, _reserve, false);
        }

        /// <summary>Returns true if this ray hit a scoring target.</summary>
        bool ResolveHit(RaycastHit hit, Vector3 dir, out float damage, out bool headshot, out bool killed)
        {
            damage = 0f; headshot = false; killed = false;

            float distance = hit.distance;
            float falloff = Mathf.InverseLerp(Def.DamageFalloffStart, Def.DamageFalloffEnd, distance);
            float baseDamage = Def.Damage * Mathf.Lerp(1f, Def.MinDamageFraction, falloff);

            var zone = hit.collider.GetComponent<HitZone>();
            if (zone != null && zone.Owner != null)
            {
                var info = zone.Owner.ApplyDamage(baseDamage, zone.Zone, hit.point, dir,
                                                  Def.HeadshotMultiplier, Def.LimbMultiplier, distance);
                damage = info.Damage;
                headshot = info.Zone == Zone.Head;
                killed = info.Killed;

                GameAudio.I.PlayAt(GameAudio.I.ImpactSoft, hit.point, 0.45f);
                // Attach the hole to the board so it swings with the target.
                FxSystem.I.Impact(hit.point, hit.normal, new Color(0.55f, 0.48f, 0.38f),
                                  zone.Owner.Board, 2, spark: false);
                return true;
            }

            GameAudio.I.PlayAt(GameAudio.I.ImpactHard, hit.point, 0.4f);

            var r = hit.collider.GetComponent<Renderer>();
            Color tint = r != null && r.sharedMaterial != null && r.sharedMaterial.HasProperty("_BaseColor")
                ? r.sharedMaterial.GetColor("_BaseColor")
                : new Color(0.5f, 0.5f, 0.5f);
            FxSystem.I.Impact(hit.point, hit.normal, tint);
            return false;
        }

        void TryReload()
        {
            if (IsReloading || _mag >= Def.MagSize || _reserve <= 0) return;

            bool empty = _mag <= 0;
            float duration = empty ? Def.ReloadTimeEmpty : Def.ReloadTime;
            _reloadDoneAt = Time.time + duration;
            _burstRemaining = 0;

            _anim.PlayReload(duration);
            Hud.I.SetAmmo(_mag, _reserve, true);

            GameAudio.I.Play(GameAudio.I.MagOut, 0.7f);
            StartCoroutine(ReloadSfx(duration, empty));
        }

        System.Collections.IEnumerator ReloadSfx(float duration, bool empty)
        {
            yield return new WaitForSeconds(duration * 0.55f);
            GameAudio.I.Play(GameAudio.I.MagIn, 0.8f);
            if (empty)
            {
                yield return new WaitForSeconds(duration * 0.28f);
                GameAudio.I.Play(GameAudio.I.BoltRelease, 0.85f);
            }
        }

        void FinishReload()
        {
            _reloadDoneAt = -1f;
            int needed = Def.MagSize - _mag;
            int taken = Mathf.Min(needed, _reserve);
            _mag += taken;
            _reserve -= taken;

            _recoil.Reset();
            _spread.Reset();
            Hud.I.SetAmmo(_mag, _reserve, false);
        }
    }
}
