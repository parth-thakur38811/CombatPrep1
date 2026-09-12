using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CombatPrep.Core
{
    /// <summary>
    /// Runtime material factory. No material assets exist in the project - every
    /// surface in the game is built here from a URP/Lit instance with parameters.
    /// Flat colours are cached by their parameters so we share instances and stay
    /// SRP-batcher friendly; textured and transparent materials are one-offs.
    /// </summary>
    public static class Mat
    {
        static readonly Dictionary<int, Material> Cache = new();
        static Shader _lit;

        static Shader Lit
        {
            get
            {
                if (_lit == null)
                    _lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                return _lit;
            }
        }

        public static Material Get(Color color, float metallic = 0f, float smoothness = 0.35f, float emission = 0f)
        {
            int key = color.GetHashCode();
            key = key * 397 ^ Mathf.RoundToInt(metallic * 100f);
            key = key * 397 ^ Mathf.RoundToInt(smoothness * 100f);
            key = key * 397 ^ Mathf.RoundToInt(emission * 100f);

            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var m = new Material(Lit) { name = $"Gen_{ColorUtility.ToHtmlStringRGB(color)}" };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);

            if (emission > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                m.SetColor("_EmissionColor", color * emission);
            }

            Cache[key] = m;
            return m;
        }

        /// <summary>Lit material carrying a generated texture. Used by targets and weapon skins.</summary>
        public static Material Textured(Texture2D tex, float metallic = 0f, float smoothness = 0.25f,
                                        Color? tint = null, float normalish = 0f)
        {
            var m = new Material(Lit) { name = "Gen_Tex" };
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", tint ?? Color.white);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            if (normalish > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                m.SetTexture("_EmissionMap", tex);
                m.SetColor("_EmissionColor", Color.white * normalish);
            }
            return m;
        }

        /// <summary>
        /// Alpha-blended Lit material. URP exposes surface type as shader properties rather
        /// than a separate shader, so transparency is configured entirely in code here.
        /// </summary>
        public static Material Glass(Color tint, float smoothness = 0.92f, float metallic = 0f)
        {
            var m = new Material(Lit) { name = "Gen_Glass" };
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Surface", 1f);                  // 0 opaque, 1 transparent
            m.SetFloat("_Blend", 0f);                    // alpha
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_AlphaClip", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        /// <summary>Unlit additive - reticle dots, tracers, glows. Always visible, never shaded.</summary>
        public static Material Additive(Color color)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var m = new Material(sh) { name = "Gen_Additive" };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 2f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent + 50;
            return m;
        }

        // Shared palette so the whole range reads as one art direction: sun-bleached desert
        // industrial, warm sand against cool concrete with rust and orange accents.
        public static readonly Color Gunmetal   = new(0.16f, 0.17f, 0.19f);
        public static readonly Color Polymer    = new(0.11f, 0.12f, 0.13f);
        public static readonly Color Steel      = new(0.42f, 0.44f, 0.47f);
        public static readonly Color Concrete   = new(0.60f, 0.58f, 0.54f);
        public static readonly Color ConcreteHi = new(0.70f, 0.68f, 0.63f);
        public static readonly Color Sand       = new(0.76f, 0.66f, 0.47f);
        public static readonly Color SandDark   = new(0.62f, 0.53f, 0.37f);
        public static readonly Color Dirt       = new(0.48f, 0.40f, 0.29f);
        public static readonly Color Rust       = new(0.51f, 0.29f, 0.17f);
        public static readonly Color RustDark   = new(0.36f, 0.21f, 0.13f);
        public static readonly Color Accent     = new(1.00f, 0.45f, 0.10f);
        public static readonly Color Wood       = new(0.55f, 0.40f, 0.24f);
        public static readonly Color Canvas     = new(0.46f, 0.44f, 0.35f);
        public static readonly Color ContainerA = new(0.22f, 0.40f, 0.44f);
        public static readonly Color ContainerB = new(0.60f, 0.32f, 0.22f);
        public static readonly Color ContainerC = new(0.40f, 0.44f, 0.32f);
        public static readonly Color TargetPaper= new(0.84f, 0.78f, 0.66f);
    }
}
