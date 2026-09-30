using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CombatPrep.Core
{
    /// <summary>
    /// Renders the world as seen from a point into a cubemap, for reflections.
    ///
    /// Unity's own route to a reflection - the sky baked by DynamicGI.UpdateEnvironment, and
    /// realtime probes rendered as part of drawing the frame - gave this game a clear blue sky in
    /// every puddle and on every wet surface, nothing like the storm overhead: the storm sky
    /// shader doesn't survive that bake. Capturing the finished scene directly shows what's
    /// really around: the dark sky, the ruins, the fires.
    /// </summary>
    public static class EnvironmentCapture
    {
        static readonly (CubemapFace face, Vector3 look, Vector3 up)[] Faces =
        {
            (CubemapFace.PositiveX, Vector3.right, Vector3.down),
            (CubemapFace.NegativeX, Vector3.left, Vector3.down),
            (CubemapFace.PositiveY, Vector3.up, Vector3.forward),
            (CubemapFace.NegativeY, Vector3.down, Vector3.back),
            (CubemapFace.PositiveZ, Vector3.forward, Vector3.down),
            (CubemapFace.NegativeZ, Vector3.back, Vector3.down),
        };

        /// <param name="target">A cubemap from an earlier capture to draw into again, or null for a new one.</param>
        public static RenderTexture Capture(Vector3 position, int size, int cullingMask, RenderTexture target = null)
        {
            if (target == null)
            {
                target = new RenderTexture(size, size, 24, RenderTextureFormat.ARGBHalf)
                {
                    name = "Reflection",
                    dimension = TextureDimension.Cube,
                    useMipMap = true,
                    autoGenerateMips = false,
                    filterMode = FilterMode.Trilinear,
                };
                target.Create();
            }

            var go = new GameObject("ReflectionCamera");
            go.transform.position = position;
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.cullingMask = cullingMask;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 600f;
            cam.fieldOfView = 90f;
            cam.aspect = 1f;
            cam.clearFlags = CameraClearFlags.Skybox;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.renderShadows = true;

            // Each face is drawn flat and copied into the cube. Handed the cube itself, URP draws
            // every face into a texture of its own with no depth buffer, and logs a warning for
            // each one. The descriptor is the bare one, as URP's is: a cube face mustn't be flipped.
            bool copy = (SystemInfo.copyTextureSupport & CopyTextureSupport.DifferentTypes) != 0;
            var flat = copy ? RenderTexture.GetTemporary(new RenderTextureDescriptor
            {
                width = size,
                height = size,
                volumeDepth = 1,
                msaaSamples = 1,
                dimension = TextureDimension.Tex2D,
                graphicsFormat = target.graphicsFormat,
                depthStencilFormat = SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil),
                shadowSamplingMode = ShadowSamplingMode.None,
            }) : null;

            foreach (var (face, look, up) in Faces)
            {
                go.transform.rotation = Quaternion.LookRotation(look, up);
                var request = copy
                    ? new RenderPipeline.StandardRequest { destination = flat }
                    : new RenderPipeline.StandardRequest { destination = target, face = face };
                if (!RenderPipeline.SupportsRenderRequest(cam, request)) continue;
                RenderPipeline.SubmitRenderRequest(cam, request);
                if (copy) Graphics.CopyTexture(flat, 0, 0, target, (int)face, 0);
            }

            if (flat != null) RenderTexture.ReleaseTemporary(flat);
            target.GenerateMips();
            Object.Destroy(go);
            return target;
        }
    }
}
