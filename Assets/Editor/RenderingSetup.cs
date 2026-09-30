using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CombatPrep.EditorTools
{
    /// <summary>
    /// Generates the "keep-alive" materials that let a *build* render at all.
    ///
    /// Unity strips every shader that no asset references, and this project deliberately has
    /// no material assets - all materials are made in code - so a build shipped with no URP
    /// shaders and crashed on its first `new Material`. The editor never showed it, because
    /// the editor can always find every shader.
    ///
    /// A bare shader reference isn't enough either: URP compiles features such as emission and
    /// transparency as optional variants, included only if some material uses them. So there is
    /// one material per keyword combination the code actually creates. Each also sets the
    /// properties that justify its keyword, because URP re-validates materials on import and
    /// would, for instance, strip _EMISSION from a material whose emission colour is black.
    /// </summary>
    public static class RenderingSetup
    {
        const string Dir = "Assets/Rendering/Generated";

        const string Lit = "Universal Render Pipeline/Lit";
        const string Unlit = "Universal Render Pipeline/Unlit";
        const string Particles = "Universal Render Pipeline/Particles/Unlit";
        const string Sky = "Skybox/Procedural";
        const string StormSky = "CombatPrep/StormSky";
        const string Glow = "CombatPrep/GlowNoFog";

        public static Material[] BuildKeepAliveMaterials()
        {
            Directory.CreateDirectory(Dir);

            var mats = new[]
            {
                // Mat.Get / Mat.Textured: plain opaque Lit.
                Save("Lit_Opaque", Lit, _ => { }),

                // Mat.Get(emission > 0): reticles, red dot, skin accents.
                Save("Lit_Emission", Lit, m =>
                {
                    m.SetColor("_EmissionColor", Color.white);
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    m.EnableKeyword("_EMISSION");
                }),

                // Mat.Glass: the optic lens.
                Save("Lit_Transparent", Lit, m =>
                {
                    MakeTransparent(m, blend: 0f, dst: BlendMode.OneMinusSrcAlpha);
                }),

                // Mat.Additive / FxSystem: tracers, muzzle flash, reticles, grenade arc.
                Save("Unlit_Transparent", Unlit, m =>
                {
                    MakeTransparent(m, blend: 2f, dst: BlendMode.One);
                }),

                // Bootstrap.Lighting: the procedural sky, now only a fallback.
                Save("Skybox_Procedural", Sky, _ => { }),

                // Storm: the animated storm sky, and the fog-proof glow the lightning uses.
                Save("Sky_Storm", StormSky, _ => { }),
                Save("Glow_NoFog", Glow, _ => { }),

                // Mat.Particle: rain, splashes and ripples (plain) and smoke and fire (soft).
                // Alpha and additive blending share a keyword set, so two cover all four.
                Save("Particles_Plain", Particles, m => MakeParticle(m, soft: false)),
                Save("Particles_Soft", Particles, m => MakeParticle(m, soft: true)),
            };

            AssetDatabase.SaveAssets();
            return mats;
        }

        static void MakeTransparent(Material m, float blend, BlendMode dst)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", blend);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)dst);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        static void MakeParticle(Material m, bool soft)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;

            // URP re-derives this keyword from the toggle on import, so set both.
            m.SetFloat("_SoftParticlesEnabled", soft ? 1f : 0f);
            if (soft)
            {
                m.SetFloat("_SoftParticlesFarFadeDistance", 0.8f);
                m.SetVector("_SoftParticleFadeParams", new Vector4(0f, 1f / 0.8f, 0f, 0f));
                m.EnableKeyword("_SOFTPARTICLES_ON");
            }
        }

        // ------------------------------------------------------------- volumetric light

        const string RendererPath = "Assets/Settings/PC_Renderer.asset";
        const string VolumetricFeature = "VolumetricLight";

        /// <summary>
        /// Puts the light-in-the-air pass (Shaders/VolumetricLight, driven by FX/VolumetricLight)
        /// on the PC renderer as a URP full-screen pass, if it isn't there yet: after the opaque
        /// scene, before transparents, reading depth. Runs by itself when scripts load.
        /// </summary>
        public static void EnsureVolumetricLight()
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            var shader = Shader.Find("CombatPrep/VolumetricLight");
            if (data == null || shader == null) return;

            Directory.CreateDirectory(Dir);
            string matPath = $"{Dir}/{VolumetricFeature}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader) { name = VolumetricFeature };
                AssetDatabase.CreateAsset(mat, matPath);
            }

            foreach (var f in data.rendererFeatures)
                if (f is FullScreenPassRendererFeature existing && existing.name == VolumetricFeature)
                {
                    if (existing.passMaterial != mat)
                    {
                        existing.passMaterial = mat;
                        EditorUtility.SetDirty(data);
                        AssetDatabase.SaveAssets();
                    }
                    return;
                }

            var feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
            feature.name = VolumetricFeature;
            feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingTransparents;
            feature.fetchColorBuffer = false;     // the shader adds its light over the scene by blending
            feature.requirements = ScriptableRenderPassInput.Depth;
            feature.passMaterial = mat;
            AssetDatabase.AddObjectToAsset(feature, data);

            // Registered the way the renderer's inspector does it: the list, plus the map of
            // local file ids URP uses to find the sub-asset again.
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);
            var so = new SerializedObject(data);
            var list = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log("<b>CombatPrep</b>: added the volumetric light pass to " + RendererPath);
        }

        [InitializeOnLoad]
        static class AutoSetup
        {
            static AutoSetup() => EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode) EnsureVolumetricLight();
            };
        }

        static Material Save(string name, string shaderName, Action<Material> configure)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
                throw new InvalidOperationException($"Shader '{shaderName}' not found - is URP installed?");

            string path = $"{Dir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = shader;
            }

            // Start clean each time, so regenerating can't accumulate stale keywords.
            mat.shaderKeywords = Array.Empty<string>();
            configure(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
