using System;
using UnityEngine;
using Random = UnityEngine.Random;   // disambiguate against System.Random

namespace CombatPrep.Targets
{
    public enum Zone { Body, Head, Limb }

    /// <summary>Per-part collider that routes damage back to its Target with a zone multiplier.</summary>
    public class HitZone : MonoBehaviour
    {
        public Target Owner;
        public Zone Zone = Zone.Body;
    }

    public struct HitInfo
    {
        public Target Target;
        public float Damage;
        public Zone Zone;
        public bool Killed;
        public Vector3 Point;
        public float Distance;
    }

    /// <summary>
    /// A hanging paper target. Rounds rock the board on its top pivot; enough damage swings
    /// it flat downrange, where it stays for a beat before popping back up. The swing is the
    /// feedback - it is legible from any distance, which a colour flash is not.
    /// </summary>
    public class Target : MonoBehaviour
    {
        public static event Action<HitInfo> OnAnyHit;

        [Header("Health")]
        public float MaxHealth = 100f;
        public float RespawnDelay = 1.8f;

        [Header("Swing")]
        public float SwingStiffness = 55f;
        public float SwingDamping = 5.5f;
        public float HitImpulsePerDamage = 2.2f;
        public float DownAngle = -96f;

        float _health;
        TargetVisual _visual;
        TargetMover _mover;
        float _respawnAt = -1f;

        float _angle, _angleVel, _angleTarget;

        public bool IsAlive { get; private set; }
        public Transform Board => _visual?.Board;

        void Awake()
        {
            _mover = GetComponent<TargetMover>();
            Spawn();
        }

        public void Spawn()
        {
            if (_visual != null && _visual.Root != null) Destroy(_visual.Root.gameObject);

            _visual = TargetBuilder.Build(transform, this);
            _health = MaxHealth;
            IsAlive = true;
            _respawnAt = -1f;
            _angle = _angleVel = _angleTarget = 0f;

            if (_mover != null) _mover.enabled = true;
        }

        /// <summary>Called by the weapon on a confirmed hit. Returns the resolved hit info.</summary>
        public HitInfo ApplyDamage(float baseDamage, Zone zone, Vector3 point, Vector3 direction,
                                   float headMult, float limbMult, float distance)
        {
            float mult = zone switch
            {
                Zone.Head => headMult,
                Zone.Limb => limbMult,
                _ => 1f
            };

            float damage = baseDamage * mult;
            bool killed = false;

            if (IsAlive)
            {
                _health -= damage;

                // Rock away downrange, scaled by how hard it was hit.
                _angleVel -= damage * HitImpulsePerDamage;

                if (_health <= 0f)
                {
                    killed = true;
                    IsAlive = false;
                    _angleTarget = DownAngle;
                    _angleVel -= 140f;
                    _respawnAt = Time.time + RespawnDelay;
                    if (_mover != null) _mover.enabled = false;
                }
            }

            var info = new HitInfo
            {
                Target = this,
                Damage = damage,
                Zone = zone,
                Killed = killed,
                Point = point,
                Distance = distance
            };

            OnAnyHit?.Invoke(info);
            return info;
        }

        void Update()
        {
            float dt = Time.deltaTime;

            if (!IsAlive && _respawnAt > 0f && Time.time >= _respawnAt)
            {
                // Pop back up and clear the old holes with a fresh board.
                Spawn();
                return;
            }

            if (_visual == null || _visual.Swing == null) return;

            // Damped spring on the swing angle, substepped so a heavy hit cannot explode it.
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / 0.005f), 1, 16);
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                float accel = (_angleTarget - _angle) * SwingStiffness - _angleVel * SwingDamping;
                _angleVel += accel * h;
                _angle += _angleVel * h;
            }

            // A live board must not swing past flat in either direction.
            if (IsAlive) _angle = Mathf.Clamp(_angle, -82f, 30f);
            else _angle = Mathf.Clamp(_angle, DownAngle, 30f);

            _visual.Swing.localRotation = Quaternion.Euler(_angle, 0f, 0f);
        }
    }
}
