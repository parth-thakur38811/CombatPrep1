using UnityEngine;
using UnityEngine.Rendering;
using CombatPrep.Core;

namespace CombatPrep.FX
{
    /// <summary>
    /// Shared plumbing for the weather and fire particle systems: a clean-slate system
    /// factory, the handful of shared materials, and gradient/curve shorthands. Every system is
    /// built in code, so this keeps each effect's own file down to the numbers that make it
    /// look like itself.
    /// </summary>
    public static class ParticleKit
    {
        static Material _smoke, _flame, _ember;

        public static Material Smoke => _smoke ??= Mat.Particle(Tex.SmokePuff(128, 4), additive: false, soft: true);
        public static Material Flame => _flame ??= Mat.Particle(Tex.Flame(), additive: true, soft: true);
        public static Material Ember => _ember ??= Mat.Particle(Tex.SoftDot(32, 3f), additive: true, soft: false);

        /// <summary>
        /// A stopped, cleared system with a fixed random seed. Fixed seeds matter: RangeBuilder
        /// builds the arena inside a seeded Random stream shared by every machine, and nothing
        /// here may draw from it or later props would land in different places online.
        /// </summary>
        public static ParticleSystem New(Transform parent, string name, Vector3 localPos, uint seed, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false;
            ps.randomSeed = seed;

            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = true;

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        /// <summary>Colour and alpha keys given separately, as Unity's Gradient wants them.</summary>
        public static Gradient Gradient((float t, Color c)[] colours, (float t, float a)[] alphas)
        {
            var g = new Gradient();
            var ck = new GradientColorKey[colours.Length];
            for (int i = 0; i < colours.Length; i++) ck[i] = new GradientColorKey(colours[i].c, colours[i].t);
            var ak = new GradientAlphaKey[alphas.Length];
            for (int i = 0; i < alphas.Length; i++) ak[i] = new GradientAlphaKey(alphas[i].a, alphas[i].t);
            g.SetKeys(ck, ak);
            return g;
        }

        /// <summary>A multiplier curve through the given (time, value) points.</summary>
        public static ParticleSystem.MinMaxCurve Curve(params (float t, float v)[] points)
        {
            var c = new AnimationCurve();
            foreach (var p in points) c.AddKey(p.t, p.v);
            return new ParticleSystem.MinMaxCurve(1f, c);
        }

        /// <summary>Cheap stable hash of a position, for per-prop variety that draws no randomness.</summary>
        public static float Hash01(Vector3 p)
        {
            float h = Mathf.Sin(p.x * 12.9898f + p.y * 4.1414f + p.z * 78.233f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        public static uint Seed(Vector3 p) => (uint)(Hash01(p) * 1_000_000f) + 1u;
    }
}
