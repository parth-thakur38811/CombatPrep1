using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CombatPrep.Core
{
    /// <summary>
    /// Render quality that has to be decided at runtime. The fixed part lives in the URP asset
    /// (Settings/PC_RPAsset): FSR 1 upscaling, a 64-bit HDR buffer, HDR colour grading, shadows
    /// out to 120 m on a 4096 map. Cameras use temporal anti-aliasing (Bootstrap.ConfigureCamera).
    ///
    /// Above 1660 lines the frame is drawn smaller and FSR sharpens it back up to the screen,
    /// which is how a 4K screen stays crisp without shading 8 million pixels. (URP's STP
    /// upscaler was tried first: it posterised the dark sky and fog into bands.) How much smaller
    /// depends on the screen, so it's set here - in a build only: in the editor, changing the
    /// asset would rewrite it on disk.
    /// </summary>
    public class GraphicsSetup : MonoBehaviour
    {
        int _height = -1;

        public static void Apply(GameObject host)
        {
            // Sharp ground at a glancing angle: the floor of an FPS is almost always seen that way.
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            Texture.SetGlobalAnisotropicFilteringLimits(8, 16);
            QualitySettings.globalTextureMipmapLimit = 0;

            host.AddComponent<GraphicsSetup>();
        }

        /// <summary>
        /// Fraction of the screen actually rendered: all of it up to 1660 lines, then about
        /// 1660 lines' worth - FSR's "quality" ratio at 4K.
        /// </summary>
        public static float RenderScaleFor(int screenHeight)
            => Mathf.Clamp(1660f / Mathf.Max(1, screenHeight), 0.67f, 1f);

        void Update()
        {
            if (Application.isEditor || Screen.height == _height) return;
            _height = Screen.height;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
                urp.renderScale = RenderScaleFor(_height);
        }
    }
}
