using System.Collections.Generic;
using UnityEngine;

namespace CombatPrep.FX
{
    /// <summary>
    /// Feeds Shaders/VolumetricLight: how thick the air is, how much of the storm light it
    /// scatters, and the nearest fires to glow in it. The shader itself runs as a full-screen
    /// pass on the renderer (added by Editor/RenderingSetup); with no pass, this does nothing.
    /// </summary>
    public class VolumetricLight : MonoBehaviour
    {
        const int MaxLocal = 8;

        [Header("Air")]
        [Tooltip("Haze at ground level: how much of the light passing through each metre it catches.")]
        public float Density = 0.012f;
        [Tooltip("Height over which the haze thins to a third.")]
        public float HazeHeight = 10f;
        [Tooltip("How far out the light in the air is gathered; the fog hides what's beyond.")]
        public float Reach = 70f;
        public int Steps = 20;

        [Header("Light")]
        [Tooltip("Strength of the storm light's shafts.")]
        public float SunScatter = 2.5f;
        [Tooltip("How strongly light scatters on toward the eye: 0 evenly, near 1 only looking into the light.")]
        public float Forward = 0.35f;
        [Tooltip("Strength of the glow around fires.")]
        public float FireScatter = 24f;
        [Tooltip("How much the air dims what's behind it (URP's fog does most of that already).")]
        public float Extinction = 0.5f;

        static readonly List<Light> Locals = new();
        readonly Vector4[] _pos = new Vector4[MaxLocal];
        readonly Vector4[] _color = new Vector4[MaxLocal];
        readonly List<(float weight, Light light)> _ranked = new();

        static readonly int AirId = Shader.PropertyToID("_VolAir");
        static readonly int LightId = Shader.PropertyToID("_VolLight");
        static readonly int NoiseId = Shader.PropertyToID("_VolNoise");
        static readonly int PosId = Shader.PropertyToID("_VolLocalPos");
        static readonly int ColorId = Shader.PropertyToID("_VolLocalColor");
        static readonly int CountId = Shader.PropertyToID("_VolLocalCount");

        /// <summary>A light that should glow in the air - fires register theirs.</summary>
        public static void Register(Light light)
        {
            if (light != null && !Locals.Contains(light)) Locals.Add(light);
        }

        public static void Unregister(Light light) => Locals.Remove(light);

        void LateUpdate()
        {
            Shader.SetGlobalVector(AirId, new Vector4(Density, 1f / Mathf.Max(0.1f, HazeHeight), Reach, Mathf.Clamp(Steps, 4, 64)));
            Shader.SetGlobalVector(LightId, new Vector4(SunScatter, Forward, FireScatter, Extinction));
            Shader.SetGlobalVector(NoiseId, new Vector4(Time.frameCount % 64, 0f, 0f, 0f));

            // The fires that matter most to this view: bright and near.
            var cam = Camera.main;
            Vector3 eye = cam != null ? cam.transform.position : Vector3.zero;
            _ranked.Clear();
            for (int i = Locals.Count - 1; i >= 0; i--)
            {
                var l = Locals[i];
                if (l == null) { Locals.RemoveAt(i); continue; }
                if (!l.isActiveAndEnabled || l.intensity <= 0f) continue;
                float d = Vector3.Distance(eye, l.transform.position);
                _ranked.Add((l.intensity * l.range / (1f + d * d), l));
            }
            _ranked.Sort((a, b) => b.weight.CompareTo(a.weight));

            int n = Mathf.Min(MaxLocal, _ranked.Count);
            for (int i = 0; i < n; i++)
            {
                var l = _ranked[i].light;
                Vector3 p = l.transform.position;
                _pos[i] = new Vector4(p.x, p.y, p.z, 1f / Mathf.Max(0.01f, l.range * l.range));
                Color c = l.color.linear * l.intensity;
                _color[i] = new Vector4(c.r, c.g, c.b, 0f);
            }
            Shader.SetGlobalVectorArray(PosId, _pos);
            Shader.SetGlobalVectorArray(ColorId, _color);
            Shader.SetGlobalInt(CountId, n);
        }
    }
}
