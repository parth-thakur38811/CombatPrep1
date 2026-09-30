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
        static Shader _lit, _unlit, _particles;

        static Shader Lit => _lit != null ? _lit : _lit = Require("Universal Render Pipeline/Lit");
        public static Shader UnlitShader => _unlit != null ? _unlit : _unlit = Require("Universal Render Pipeline/Unlit");
        static Shader ParticleShader => _particles != null ? _particles : _particles = Require("Universal Render Pipeline/Particles/Unlit");

        /// <summary>
        /// Shader.Find only sees shaders that made it into the build, and Unity strips any
        /// shader no asset references. This project creates every material in code, so the
        /// scene carries generated keep-alive materials (see Bootstrap.ShaderKeepAlive) purely
        /// to stop that stripping. If one ever goes missing, say so plainly - the alternative
        /// is a bare ArgumentNullException from deep inside Material's constructor.
        /// </summary>
        public static Shader Require(string name)
        {
            var shader = Shader.Find(name);
            if (shader == null)
                Debug.LogError($"[Mat] Shader '{name}' is not in this build. Run CombatPrep > Build Range " +
                               "Scene so the scene references it, then rebuild.");
            return shader;
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
            var m = new Material(UnlitShader) { name = "Gen_Additive" };
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

        /// <summary>
        /// Transparent Lit carrying a texture whose alpha is the shape - scorch marks and
        /// puddles lying on the ground. Lit, so they darken and glint with the rest of the
        /// world instead of glowing like a sticker.
        /// </summary>
        public static Material Decal(Texture2D tex, Color tint, float smoothness, float metallic = 0f)
        {
            var m = Glass(tint, smoothness, metallic);
            m.name = "Gen_Decal";
            m.SetTexture("_BaseMap", tex);
            // Glass keeps its reflection where it's clear; a decal must not, or a puddle shows the
            // sky's reflection across its whole square. (URP re-derives the blend from this flag
            // when the material is saved as an asset, as the FX prefabs' are.)
            m.SetFloat("_BlendModePreserveSpecular", 0f);
            m.renderQueue = (int)RenderQueue.Transparent - 50;   // under particles and glass
            return m;
        }

        /// <summary>
        /// URP particle material, set up in code exactly as its inspector would. Soft
        /// particles fade where a sprite meets geometry - right for smoke and fire, wrong for a
        /// ripple lying flat on the ground, which would fade away entirely - hence the switch.
        /// Additive and alpha share one keyword set, so builds need just two keep-alive
        /// variants (see RenderingSetup).
        /// </summary>
        public static Material Particle(Texture2D tex, bool additive, bool soft, Color? tint = null)
        {
            var m = new Material(ParticleShader) { name = additive ? "Gen_ParticleAdd" : "Gen_ParticleAlpha" };
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", tint ?? Color.white);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            if (soft)
            {
                const float far = 0.8f;
                m.SetFloat("_SoftParticlesEnabled", 1f);
                m.SetFloat("_SoftParticlesNearFadeDistance", 0f);
                m.SetFloat("_SoftParticlesFarFadeDistance", far);
                m.SetVector("_SoftParticleFadeParams", new Vector4(0f, 1f / far, 0f, 0f));
                m.EnableKeyword("_SOFTPARTICLES_ON");
            }

            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        /// <summary>
        /// Additive glow that ignores fog (CombatPrep/GlowNoFog). Falls back to the fogged
        /// additive if the shader is somehow missing, so a stripped build degrades rather than
        /// throwing.
        /// </summary>
        public static Material GlowNoFog(Color hdrColor)
        {
            var shader = Require("CombatPrep/GlowNoFog");
            if (shader == null) return Additive(hdrColor);
            var m = new Material(shader) { name = "Gen_Glow" };
            m.SetColor("_BaseColor", hdrColor);
            m.renderQueue = (int)RenderQueue.Transparent + 60;
            return m;
        }

        // Shared palette so the whole range reads as one art direction: a rain-soaked
        // battlefield at dusk. Everything is darker and less saturated than it would be dry,
        // which is most of what "wet" looks like; the smoothness values in RangeBuilder do the
        // rest. Weapon colours are untouched - skins own those.
        public static readonly Color Gunmetal   = new(0.16f, 0.17f, 0.19f);
        public static readonly Color Polymer    = new(0.11f, 0.12f, 0.13f);
        public static readonly Color Steel      = new(0.42f, 0.44f, 0.47f);
        public static readonly Color Concrete   = new(0.40f, 0.41f, 0.42f);
        public static readonly Color ConcreteHi = new(0.47f, 0.48f, 0.49f);
        public static readonly Color Sand       = new(0.34f, 0.29f, 0.23f);   // wet mud
        public static readonly Color SandDark   = new(0.25f, 0.21f, 0.16f);
        public static readonly Color Dirt       = new(0.19f, 0.15f, 0.11f);
        public static readonly Color Rust       = new(0.36f, 0.19f, 0.11f);
        public static readonly Color RustDark   = new(0.23f, 0.13f, 0.08f);
        public static readonly Color Accent     = new(0.80f, 0.46f, 0.10f);   // faded hazard paint
        public static readonly Color Wood       = new(0.30f, 0.22f, 0.14f);
        public static readonly Color Canvas     = new(0.35f, 0.32f, 0.23f);   // soaked burlap
        public static readonly Color Charred    = new(0.07f, 0.065f, 0.06f);
        public static readonly Color ContainerA = new(0.19f, 0.25f, 0.19f);   // olive drab
        public static readonly Color ContainerB = new(0.36f, 0.17f, 0.12f);   // oxide red
        public static readonly Color ContainerC = new(0.17f, 0.21f, 0.26f);   // navy grey
        public static readonly Color TargetPaper= new(0.84f, 0.78f, 0.66f);
    }
}
