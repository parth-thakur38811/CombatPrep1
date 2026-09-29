using UnityEngine;
using CombatPrep.Core;

namespace CombatPrep.FX
{
    /// <summary>
    /// A fire: flame tongues, rising embers, smoke, and a flickering orange light. In a cold,
    /// blue, rain-dark scene these warm pools are what the eye goes to - they make the
    /// battlefield feel inhabited and give the gloom something to contrast against.
    ///
    /// With <c>column</c> set, the smoke becomes a towering plume that leans with the wind -
    /// the "town burning on the horizon" silhouette.
    /// </summary>
    public class FireFx : MonoBehaviour
    {
        Light _light;
        float _baseIntensity;
        float _phase;

        public static FireFx Create(Transform parent, Vector3 worldPos, float scale, float lightRange,
                                    bool column = false)
        {
            var go = new GameObject(column ? "BurningWreck" : "Fire");
            go.transform.SetParent(parent, true);
            go.transform.position = worldPos;

            var fire = go.AddComponent<FireFx>();
            uint seed = ParticleKit.Seed(worldPos);
            fire._phase = ParticleKit.Hash01(worldPos) * 100f;

            Flames(go.transform, scale, seed);
            Embers(go.transform, scale, seed + 1);
            Smoke(go.transform, scale, seed + 2, column);

            var lightGo = new GameObject("FireLight");
            lightGo.transform.SetParent(go.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.45f * scale + 0.3f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.55f, 0.22f);
            light.range = lightRange;
            light.intensity = 1.6f + scale * 1.4f;
            light.shadows = LightShadows.None;
            fire._light = light;
            fire._baseIntensity = light.intensity;

            return fire;
        }

        static void Flames(Transform root, float scale, uint seed)
        {
            var ps = ParticleKit.New(root, "Flames", Vector3.zero, seed, ParticleKit.Flame);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f * scale, 1.3f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f * scale, 0.75f * scale);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            main.gravityModifier = -0.08f;
            main.maxParticles = 90;

            var em = ps.emission;
            em.rateOverTime = 18f + 16f * scale;

            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 10f;
            sh.radius = 0.18f * scale;
            sh.rotation = new Vector3(-90f, 0f, 0f);   // cone axis +Z turned to point up

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = ParticleKit.Gradient(
                new[] { (0f, new Color(1f, 0.86f, 0.58f)), (0.35f, new Color(1f, 0.52f, 0.16f)),
                        (0.75f, new Color(0.62f, 0.16f, 0.04f)), (1f, new Color(0.2f, 0.05f, 0.02f)) },
                new[] { (0f, 0f), (0.12f, 0.95f), (0.6f, 0.55f), (1f, 0f) });

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = ParticleKit.Curve((0f, 0.7f), (0.3f, 1f), (1f, 0.25f));

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.35f * scale;
            noise.frequency = 1.4f;
            noise.scrollSpeed = 0.8f;
            noise.quality = ParticleSystemNoiseQuality.Low;

            ps.Play();
        }

        static void Embers(Transform root, float scale, uint seed)
        {
            var ps = ParticleKit.New(root, "Embers", new Vector3(0f, 0.2f * scale, 0f), seed, ParticleKit.Ember);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f * scale, 2.8f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
            main.startColor = new Color(1f, 0.62f, 0.25f);
            main.gravityModifier = -0.12f;
            main.maxParticles = 60;

            var em = ps.emission;
            em.rateOverTime = 6f * scale;

            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 25f;
            sh.radius = 0.2f * scale;
            sh.rotation = new Vector3(-90f, 0f, 0f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = ParticleKit.Gradient(
                new[] { (0f, new Color(1f, 0.8f, 0.4f)), (1f, new Color(0.8f, 0.18f, 0.05f)) },
                new[] { (0f, 1f), (0.7f, 0.8f), (1f, 0f) });

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.8f;
            noise.frequency = 0.6f;
            noise.quality = ParticleSystemNoiseQuality.Low;

            ps.Play();
        }

        static void Smoke(Transform root, float scale, uint seed, bool column)
        {
            var ps = ParticleKit.New(root, column ? "SmokeColumn" : "Smoke",
                                     new Vector3(0f, 0.6f * scale + 0.3f, 0f), seed, ParticleKit.Smoke);
            var main = ps.main;
            main.startLifetime = column ? new ParticleSystem.MinMaxCurve(16f, 22f) : new ParticleSystem.MinMaxCurve(4f, 7f);
            main.startSpeed = column ? new ParticleSystem.MinMaxCurve(2.6f, 3.6f) : new ParticleSystem.MinMaxCurve(0.8f, 1.5f);
            main.startSize = column ? new ParticleSystem.MinMaxCurve(5f, 8f)
                                    : new ParticleSystem.MinMaxCurve(0.8f * scale, 1.4f * scale);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = column ? new Color(0.07f, 0.07f, 0.075f, 0.62f) : new Color(0.10f, 0.10f, 0.10f, 0.45f);
            main.maxParticles = column ? 240 : 60;

            var em = ps.emission;
            em.rateOverTime = column ? 4.5f : 4f * scale;

            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = column ? 6f : 12f;
            sh.radius = column ? 2.2f : 0.25f * scale;
            sh.rotation = new Vector3(-90f, 0f, 0f);

            // Drift downwind: the plume leans, which is what sells it as big and far away.
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            float drift = column ? 1.4f : 0.6f;
            vel.x = new ParticleSystem.MinMaxCurve(Weather.Wind.x * drift * 0.7f, Weather.Wind.x * drift * 1.3f);
            vel.y = new ParticleSystem.MinMaxCurve(0f, 0.2f);
            vel.z = new ParticleSystem.MinMaxCurve(Weather.Wind.z * drift * 0.7f, Weather.Wind.z * drift * 1.3f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = ParticleKit.Gradient(
                new[] { (0f, Color.white), (1f, new Color(0.75f, 0.75f, 0.78f)) },
                new[] { (0f, 0f), (0.1f, 1f), (0.65f, 0.7f), (1f, 0f) });

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = column ? ParticleKit.Curve((0f, 0.6f), (1f, 4f)) : ParticleKit.Curve((0f, 0.5f), (1f, 3f));

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);

            ps.Play();
        }

        void Update()
        {
            if (_light == null) return;
            // Two noise rates: a slow breathing plus a fast flicker, like a real flame.
            float t = Time.time;
            float slow = Mathf.PerlinNoise(t * 1.3f + _phase, _phase * 0.37f);
            float fast = Mathf.PerlinNoise(t * 9f + _phase * 2f, 3.1f);
            _light.intensity = _baseIntensity * (0.62f + slow * 0.38f + fast * 0.28f);
        }
    }
}
