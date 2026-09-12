using UnityEngine;

namespace CombatPrep.Core
{
    /// <summary>
    /// Damped spring integrator. Substepped so it stays stable at low framerates.
    /// Springs (not lerps) are what make weapon kick and camera punch feel physical:
    /// they overshoot slightly and settle, instead of easing in a dead straight line.
    /// </summary>
    [System.Serializable]
    public struct Spring3
    {
        public Vector3 Value;
        public Vector3 Velocity;

        public void Step(Vector3 target, float stiffness, float damping, float dt)
        {
            int steps = Mathf.Clamp(Mathf.CeilToInt(dt / 0.005f), 1, 16);
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                Vector3 accel = (target - Value) * stiffness - Velocity * damping;
                Velocity += accel * h;
                Value += Velocity * h;
            }
        }

        public void Impulse(Vector3 v) => Velocity += v;
        public void Reset() { Value = Vector3.zero; Velocity = Vector3.zero; }
    }
}
