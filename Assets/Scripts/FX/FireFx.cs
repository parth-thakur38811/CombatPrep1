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
        [Tooltip("The fire's own light; it flickers around the intensity it's placed with.")]
        public Light Light;

        float _baseIntensity;
        float _phase;

        /// <summary>
        /// A fire from its prefab (Prefabs/FX/Fire or BurningWreck), scaled and seeded for this
        /// spot. Seeds come from the position, not from Random: fires are lit during the arena
        /// build, which every machine must replay identically.
        /// </summary>
        /// <param name="shadows">Its light casts shadows - flames throwing the walls round them
        /// across the ground. Kept for the big fires: each costs six shadow-map renders a frame.</param>
        public static FireFx Create(Transform parent, Vector3 worldPos, float scale, float lightRange,
                                    bool column = false, bool shadows = false)
        {
            var lib = ArtLibrary.I != null ? ArtLibrary.I.Fx : null;
            var prefab = lib == null ? null : column ? lib.BurningWreck : lib.Fire;
            var go = prefab != null ? Instantiate(prefab)
                   : column ? FxRecipes.BurningWreck() : FxRecipes.Fire();
            go.name = column ? "BurningWreck" : "Fire";
            go.transform.SetParent(parent, true);
            go.transform.position = worldPos;
            go.transform.localScale = Vector3.one * scale;

            var fire = go.GetComponent<FireFx>();
            if (fire == null) fire = go.AddComponent<FireFx>();

            uint seed = ParticleKit.Seed(worldPos);
            var systems = go.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                systems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                systems[i].useAutoRandomSeed = false;
                systems[i].randomSeed = seed + (uint)i;
                systems[i].Play(false);
            }

            fire._phase = ParticleKit.Hash01(worldPos) * 100f;
            if (fire.Light != null)
            {
                fire.Light.range = lightRange;
                fire.Light.intensity = 1.6f + scale * 1.4f;
                // Keep the light just above the flames whatever the scale.
                fire.Light.transform.localPosition = new Vector3(0f, 0.45f + 0.3f / Mathf.Max(0.1f, scale), 0f);
                fire._baseIntensity = fire.Light.intensity;
                VolumetricLight.Register(fire.Light);     // glows in the haze around it

                if (shadows)
                {
                    fire.Light.shadows = LightShadows.Soft;
                    fire.Light.shadowStrength = 0.9f;
                    fire.Light.shadowNearPlane = 0.1f;   // resolution: the URP asset's "high" tier, 512
                }
            }
            return fire;
        }

        void OnDestroy()
        {
            if (Light != null) VolumetricLight.Unregister(Light);
        }

        void Start()
        {
            if (Light != null && _baseIntensity <= 0f) _baseIntensity = Light.intensity;
        }

        void Update()
        {
            if (Light == null) return;
            // Two noise rates: a slow breathing plus a fast flicker, like a real flame.
            float t = Time.time;
            float slow = Mathf.PerlinNoise(t * 1.3f + _phase, _phase * 0.37f);
            float fast = Mathf.PerlinNoise(t * 9f + _phase * 2f, 3.1f);
            Light.intensity = _baseIntensity * (0.62f + slow * 0.38f + fast * 0.28f);
        }
    }
}
