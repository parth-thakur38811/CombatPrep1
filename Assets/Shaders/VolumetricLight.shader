// Light in the air: the storm light falling through the rain haze - blocked, in shafts, by
// whatever casts a shadow - and the glow the fires throw into the smoke and drizzle around them.
//
// Drawn once per frame over the finished opaque scene (before transparents), by URP's full
// screen pass (see Editor/RenderingSetup). Each pixel marches from the camera to the surface it
// sees, gathering light scattered toward the eye; the result is added over the scene, which is
// dimmed a little by the air it looked through. The march starts at a different offset every
// pixel and frame, and the temporal anti-aliasing blends that noise away.
//
// URP's own distance fog still paints the haze itself; this adds only the light in it. The
// parameters are globals, set every frame by FX/VolumetricLight.
Shader "CombatPrep/VolumetricLight"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        // result = scattered light + scene * transmittance
        Blend One SrcAlpha

        Pass
        {
            Name "VolumetricLight"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #define MAX_LOCAL_LIGHTS 8

            float4 _VolAir;        // x density at ground (1/m), y 1/height of the haze (1/m), z reach (m), w steps
            float4 _VolLight;      // x storm-light scatter, y forward scatter (g), z fire scatter, w extinction scale
            float4 _VolNoise;      // x frame index
            float4 _VolLocalPos[MAX_LOCAL_LIGHTS];     // xyz position, w 1/range^2
            float4 _VolLocalColor[MAX_LOCAL_LIGHTS];   // rgb colour * intensity
            int _VolLocalCount;

            // Henyey-Greenstein: how much light carries on toward the eye rather than scattering sideways.
            float Phase(float cosTheta, float g)
            {
                float g2 = g * g;
                return (1.0 - g2) / (12.5663706 * pow(max(1e-4, 1.0 + g2 - 2.0 * g * cosTheta), 1.5));
            }

            // Jorge Jimenez's interleaved gradient noise, shifted each frame.
            float Dither(float2 pixel, float frame)
            {
                pixel += 5.588238 * fmod(frame, 64.0);
                return frac(52.9829189 * frac(0.06711056 * pixel.x + 0.00583715 * pixel.y));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float rawDepth = SampleSceneDepth(uv);
                float3 surface = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float3 origin = _WorldSpaceCameraPos;

                float3 ray = surface - origin;
                float length_ = length(ray);
                float3 dir = ray / max(length_, 1e-4);
                float reach = min(length_, _VolAir.z);        // the sky counts as far away

                int steps = (int)_VolAir.w;
                float stepLen = reach / steps;
                float t = stepLen * Dither(input.positionCS.xy, _VolNoise.x);

                Light sun = GetMainLight();
                float sunPhase = Phase(dot(dir, sun.direction), _VolLight.y) * _VolLight.x;
                float3 sunColor = sun.color * sunPhase;
                const float isotropic = 0.0795775;              // 1 / 4 pi

                float3 scattered = 0;
                float transmittance = 1;

                [loop]
                for (int i = 0; i < steps; i++)
                {
                    float3 p = origin + dir * t;

                    // Haze thickest at the ground, thinning with height; none underground, where
                    // the menu's weapon stage sits.
                    float density = _VolAir.x * exp(-max(p.y, 0.0) * _VolAir.y) * step(-2.0, p.y);

                    float3 light = sunColor * MainLightRealtimeShadow(TransformWorldToShadowCoord(p));

                    for (int l = 0; l < _VolLocalCount; l++)
                    {
                        float3 d = _VolLocalPos[l].xyz - p;
                        float d2 = dot(d, d);
                        float fade = saturate(1.0 - d2 * _VolLocalPos[l].w);
                        light += _VolLocalColor[l].rgb * (fade * fade / (1.0 + d2)) * (isotropic * _VolLight.z);
                    }

                    float extinction = density * stepLen;
                    scattered += transmittance * light * extinction;
                    transmittance *= exp(-extinction * _VolLight.w);
                    t += stepLen;
                }

                return half4(scattered, transmittance);
            }
            ENDHLSL
        }
    }
}
