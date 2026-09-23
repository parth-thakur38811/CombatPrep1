using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using CombatPrep.Core;
using CombatPrep.Player;
using CombatPrep.UI;

namespace CombatPrep.Weapons
{
    /// <summary>
    /// Grenade loadout: G equips, holding fire draws the arc, releasing throws.
    ///
    /// The preview and the throw both read their launch velocity from <see cref="Velocity"/>
    /// and both integrate under plain gravity with no drag, which is the only reason the red
    /// line can be trusted. The grenade's Rigidbody is configured to match (damping 0), so
    /// the drawn parabola is the one it actually flies until first contact.
    /// </summary>
    public class GrenadeThrower : MonoBehaviour
    {
        [Header("Refs")]
        public Camera Cam;
        public PlayerMotor Motor;
        public GameObject WeaponHolder;
        /// <summary>The firearm, disabled while a grenade is out so LMB throws instead of shooting.</summary>
        public Weapon Weapon;

        [Header("Loadout")]
        public int StartingCount = 4;

        [Header("Throw")]
        public float ThrowSpeed = 17f;
        /// <summary>Slight upward lift, so a level throw arcs out rather than dropping underfoot.</summary>
        public float UpBias = 0.15f;
        public float ThrowCooldown = 0.55f;

        [Header("Grenade spec (copied onto each thrown grenade)")]
        public float FuseSeconds = 2.4f;
        public float CloseRadius = 3.5f;    // 100% damage
        public float MediumRadius = 6.5f;   //  50%
        public float OuterRadius = 10f;     //  25%, nothing beyond
        public float BaseDamage = 120f;

        [Header("Arc preview")]
        public Color ArcColor = new Color(1f, 0.16f, 0.12f);
        public int MaxArcSteps = 160;
        public float ArcProbeRadius = 0.055f;

        public LayerMask BlastMask = ~0;
        public LayerMask ArcMask = ~0;

        // Held-model poses.
        static readonly Vector3 ReadyPos = new Vector3(0.215f, -0.200f, 0.300f);
        static readonly Vector3 CockedPos = new Vector3(0.300f, -0.070f, 0.120f);
        static readonly Vector3 ReadyEuler = new Vector3(0f, 0f, 0f);
        static readonly Vector3 CockedEuler = new Vector3(-34f, -22f, 10f);

        Transform _held;
        LineRenderer _arc;
        Transform _marker;
        Transform _ring;

        readonly List<Vector3> _points = new();

        int _count;
        bool _equipped, _charging;
        float _poseT, _nextThrowAt;

        public int Count => _count;
        public bool Equipped => _equipped;

        // ------------------------------------------------------------------------ setup

        public void Init()
        {
            _count = StartingCount;

            _held = Grenade.BuildModel(Cam.transform, "HeldGrenade");
            _held.localPosition = ReadyPos;
            _held.gameObject.SetActive(false);

            var mat = Mat.Additive(ArcColor);

            var arcGo = new GameObject("ThrowArc");
            arcGo.transform.SetParent(transform, false);
            _arc = arcGo.AddComponent<LineRenderer>();
            _arc.material = mat;
            _arc.useWorldSpace = true;
            _arc.shadowCastingMode = ShadowCastingMode.Off;
            _arc.receiveShadows = false;
            _arc.numCapVertices = 2;
            _arc.startWidth = 0.075f;
            _arc.endWidth = 0.025f;
            _arc.positionCount = 0;
            arcGo.SetActive(false);

            _marker = Prim.Ball(transform, "ArcImpact", Vector3.zero, 0.26f, ArcColor, 0f, 1f);
            Prim.SetMaterial(_marker, mat);
            _marker.gameObject.SetActive(false);

            // Ring showing the 100%-damage radius, laid flat on the ground at the impact.
            _ring = Prim.Ring(transform, "BlastRing", Vector3.zero, CloseRadius * 2f, 0.16f, 0.16f, ArcColor, 28);
            foreach (var r in _ring.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = mat;
            _ring.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _ring.gameObject.SetActive(false);

            Hud.I.SetGrenades(_count);
        }

        // ----------------------------------------------------------------------- update

        void Update()
        {
            var input = GameInput.I;
            if (input == null || Cam == null) return;

            if (input.GrenadePress) Toggle();

            if (!_equipped)
            {
                ShowPreview(false);
                return;
            }

            // Charge while the trigger is held, then throw on release.
            if (input.Firing && Time.time >= _nextThrowAt) _charging = true;

            if (_charging && input.FireRelease)
            {
                _charging = false;
                Throw();
                return;
            }

            _poseT = Mathf.MoveTowards(_poseT, _charging ? 1f : 0f, Time.deltaTime * 7f);
            PoseHeld();

            ShowPreview(_charging);
            if (_charging) DrawArc();
        }

        void Toggle()
        {
            if (!_equipped && _count <= 0) return;

            _equipped = !_equipped;
            _charging = false;
            _poseT = 0f;

            if (_held != null) _held.gameObject.SetActive(_equipped);
            if (WeaponHolder != null) WeaponHolder.SetActive(!_equipped);

            // Hiding the gun's model is only cosmetic - the Weapon component lives on the
            // parent and would keep firing. Disable it so a held LMB charges the throw
            // instead of emptying the magazine.
            if (Weapon != null) Weapon.Active = !_equipped;

            // The weapon owns the crosshair while it is out; hide it for the grenade so the
            // arc is the only aiming aid on screen.
            Hud.I.SetCrosshairVisible(!_equipped);
            if (!_equipped) ShowPreview(false);

            PoseHeld();
        }

        void PoseHeld()
        {
            if (_held == null) return;
            float t = Mathf.SmoothStep(0f, 1f, _poseT);
            _held.localPosition = Vector3.Lerp(ReadyPos, CockedPos, t);
            _held.localRotation = Quaternion.Euler(Vector3.Lerp(ReadyEuler, CockedEuler, t));
        }

        // ------------------------------------------------------------------ trajectory

        /// <summary>Where the grenade leaves the hand, pulled back if that would be inside geometry.</summary>
        public Vector3 Origin()
        {
            Vector3 eye = Cam.transform.position;
            Vector3 wanted = eye + Cam.transform.forward * 0.45f
                                 + Cam.transform.right * 0.16f
                                 - Cam.transform.up * 0.08f;

            Vector3 delta = wanted - eye;
            float dist = delta.magnitude;
            if (dist > 0.01f && Physics.Raycast(eye, delta / dist, out var hit, dist,
                                                ArcMask, QueryTriggerInteraction.Ignore))
                return eye + delta / dist * Mathf.Max(0f, hit.distance - 0.08f);

            return wanted;
        }

        /// <summary>Single source of truth for launch velocity - preview and throw share it.</summary>
        public Vector3 Velocity()
            => (Cam.transform.forward + Cam.transform.up * UpBias).normalized * ThrowSpeed;

        /// <summary>
        /// Integrates the same semi-implicit Euler step PhysX uses (gravity first, then
        /// position), so the drawn line matches the Rigidbody's real path.
        /// </summary>
        bool Simulate(List<Vector3> points, out Vector3 impact, out Vector3 normal)
        {
            float dt = Time.fixedDeltaTime;
            Vector3 p = Origin();
            Vector3 v = Velocity();

            points.Clear();
            points.Add(p);
            impact = p;
            normal = Vector3.up;

            for (int i = 0; i < MaxArcSteps; i++)
            {
                v += Physics.gravity * dt;
                Vector3 next = p + v * dt;

                Vector3 step = next - p;
                float len = step.magnitude;
                if (len > 0.0001f &&
                    Physics.SphereCast(p, ArcProbeRadius, step / len, out var hit, len,
                                       ArcMask, QueryTriggerInteraction.Ignore))
                {
                    impact = hit.point;
                    normal = hit.normal;
                    points.Add(hit.point);
                    return true;
                }

                p = next;
                points.Add(p);
                impact = p;
            }
            return false;
        }

        void DrawArc()
        {
            bool hit = Simulate(_points, out Vector3 impact, out Vector3 normal);

            _arc.positionCount = _points.Count;
            _arc.SetPositions(_points.ToArray());

            _marker.gameObject.SetActive(hit);
            _ring.gameObject.SetActive(hit && Vector3.Dot(normal, Vector3.up) > 0.5f);

            if (hit)
            {
                _marker.position = impact + normal * 0.13f;
                _ring.position = impact + normal * 0.06f;
            }
        }

        void ShowPreview(bool on)
        {
            if (_arc != null) _arc.gameObject.SetActive(on);
            if (!on)
            {
                if (_marker != null) _marker.gameObject.SetActive(false);
                if (_ring != null) _ring.gameObject.SetActive(false);
            }
        }

        // ---------------------------------------------------------------------- throwing

        void Throw()
        {
            if (_count <= 0) return;

            var go = new GameObject("Grenade");
            go.transform.position = Origin();
            Grenade.BuildModel(go.transform);

            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.055f;

            var nade = go.AddComponent<Grenade>();
            nade.FuseSeconds = FuseSeconds;
            nade.CloseRadius = CloseRadius;
            nade.MediumRadius = MediumRadius;
            nade.OuterRadius = OuterRadius;
            nade.BaseDamage = BaseDamage;
            nade.Launch(Origin(), Velocity(), BlastMask);

            _count--;
            _nextThrowAt = Time.time + ThrowCooldown;
            Hud.I.SetGrenades(_count);

            ShowPreview(false);
            _poseT = 0f;

            // Back to the gun once the throw is away; G brings another out.
            _equipped = false;
            if (_held != null) _held.gameObject.SetActive(false);
            if (WeaponHolder != null) WeaponHolder.SetActive(true);
            if (Weapon != null) Weapon.Active = true;
            Hud.I.SetCrosshairVisible(true);
        }
    }
}
