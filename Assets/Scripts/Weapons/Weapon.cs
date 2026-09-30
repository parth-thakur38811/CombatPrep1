using UnityEngine;
using CombatPrep.Audio;
using CombatPrep.Core;
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
    ///
    /// The player carries all five guns, one Weapon each (see WeaponLoadout); only the one in
    /// hand is active. Each keeps its own magazine while it's put away.
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
        FlashFx _flash;

        int _mag, _reserve;
        float _nextShotTime;
        float _reloadDoneAt = -1f;
        int _burstRemaining;
        float _readyAt;

        public bool IsReloading => _reloadDoneAt > 0f;

        /// <summary>False while the gun is still being brought up after being taken out.</summary>
        public bool IsReady => Time.time >= _readyAt;

        /// <summary>Raised once per trigger pull, before any pellet is traced.</summary>
        public event System.Action ShotStarting;

        /// <summary>Raised once per trigger pull: muzzle position and where each pellet ended.</summary>
        public event System.Action<Vector3, Vector3[]> ShotFired;

        /// <summary>Full magazine and reserve - used when a player respawns.</summary>
        public void ResetAmmo()
        {
            StopAllCoroutines();          // cancels a pending reload sound
            _reloadDoneAt = -1f;
            _burstRemaining = 0;
            _mag = Def.MagSize;
            _reserve = Def.ReserveAmmo;
            _recoil.Reset();
            _spread.Reset();
            if (isActiveAndEnabled) Hud.I.SetAmmo(_mag, _reserve, false);
        }

        bool _active = true;
        /// <summary>
        /// Set false to holster the weapon (e.g. while a grenade is out). Update stops, so
        /// the gun cannot fire - and disabling cleanly cancels any in-progress aim so the
        /// zoom, movement lock and FOV don't strand at their aimed values.
        /// </summary>
        public bool Active
        {
            get => _active;
            set
            {
                if (_active == value) return;
                _active = value;
                if (!value) LetGoOfAim();
            }
        }

        void LetGoOfAim()
        {
            if (Look != null) Look.IsAiming = false;
            if (Motor != null) Motor.AdsLock = false;
            if (_anim != null) _anim.ForceHip();
            if (Cam != null && Def != null) Cam.fieldOfView = Def.HipFov;
        }

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

            GameAudio.I.Prepare(def);
            _flash = FxSystem.I.AttachMuzzle(model.Muzzle);
        }

        /// <summary>
        /// Takes this gun out: it comes up from below the screen and can't fire or aim until it
        /// is up (DrawTime). The HUD, zoom and aim sensitivity switch to it.
        /// </summary>
        public void Draw()
        {
            _readyAt = Time.time + Def.DrawTime;
            _anim.PlayDraw(Def.DrawTime);
            _recoil.Reset();
            _spread.Reset();

            // Aim sensitivity is a weapon property: a 22-degree scope is unusable at the
            // same turn rate as a red dot.
            Look.AdsSensScale = Def.AdsSensScale;
            Cam.fieldOfView = Def.HipFov;

            Hud.I.SetWeaponName(Def.DisplayName);
            Hud.I.SetAmmo(_mag, _reserve, false);
            Hud.I.SetCrosshairStyle(Def.Crosshair, Def.CrosshairColor);
            GameAudio.I.Play(GameAudio.I.WeaponDraw, 0.55f);
        }

        /// <summary>
        /// Put away - for another gun, or for a grenade. The aim lets go, and a reload that
        /// hadn't finished is abandoned: the magazine stays as it was.
        /// </summary>
        void OnDisable()
        {
            LetGoOfAim();
            _reloadDoneAt = -1f;
            _burstRemaining = 0;
        }

        void Update()
        {
            if (!Active) return;

            float dt = Time.deltaTime;
            var input = GameInput.I;

            // Aiming is not gated on sprinting: raising the sights cancels the sprint via
            // AdsLock instead. Gating it the other way round meant holding Shift+W+RMB
            // simply never aimed. A gun still coming up can't aim, fire or reload yet.
            bool ready = IsReady;
            bool aiming = input.Aiming && !IsReloading && ready;
            Look.IsAiming = aiming;
            Motor.AdsLock = aiming;

            // --- reload ---
            if (input.Reload && ready) TryReload();
            if (IsReloading && Time.time >= _reloadDoneAt) FinishReload();

            // --- fire ---
            if (Def.Mode == FireMode.Burst && input.FirePress && ready && _burstRemaining <= 0)
                _burstRemaining = Def.BurstCount;

            bool wantsFire = Def.Mode switch
            {
                FireMode.Auto => input.Firing,
                FireMode.Semi => input.FirePress,
                FireMode.Burst => _burstRemaining > 0,
                _ => false
            };

            if (wantsFire && ready && CanFire()) Fire(aiming);

            if (input.FirePress && ready && _mag <= 0 && !IsReloading)
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

            // Opens the window in which hits on other players are collected for sending.
            ShotStarting?.Invoke();

            // Shotguns resolve as several independent rays through the same cone, then
            // report one combined result so the HUD and accuracy stats stay per-trigger-pull.
            int pellets = Mathf.Max(1, Def.PelletsPerShot);
            int hits = 0;
            float totalDamage = 0f;
            bool anyHead = false, anyKill = false;
            Vector3 hitCentroid = Vector3.zero;
            var ends = new Vector3[pellets];

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

                ends[i] = endPoint;
                FxSystem.I.Tracer(muzzle, endPoint);
            }

            // Online, this is what carries the shot to everyone else - after the pellet loop,
            // so any player hits queued during it travel in the same single message.
            ShotFired?.Invoke(muzzle, ends);

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
            if (_flash != null) _flash.Play();
            GameAudio.I.PlayLocalShot(Def);

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

                // Paper targets keep their holes (parented, so they swing with the board).
                // Players leave none - a hole hanging in the air after they move looks broken.
                var anchor = zone.Owner.HoleAnchor;
                if (anchor != null)
                    FxSystem.I.Impact(hit.point, hit.normal, new Color(0.55f, 0.48f, 0.38f), anchor, 2, spark: false);
                else
                    FxSystem.I.Impact(hit.point, hit.normal, new Color(0.45f, 0.08f, 0.07f), null, 4, spark: false, hole: false);
                return true;
            }

            GameAudio.I.PlayAt(GameAudio.I.ImpactHard, hit.point, 0.4f);

            // Props carry their collider on a parent of the renderer.
            var r = hit.collider.GetComponent<Renderer>();
            if (r == null) r = hit.collider.GetComponentInChildren<Renderer>();
            Color tint = SurfaceColors.For(r != null ? r.sharedMaterial : null, new Color(0.5f, 0.5f, 0.5f));
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
