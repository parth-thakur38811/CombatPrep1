// The storm sky. A skybox shader rather than a dome mesh, for two reasons: a skybox sits at
// infinity, so the heavy exponential fog the storm needs can't swallow it; and it is what
// Unity samples for reflections, so wet surfaces reflect these clouds for free.
//
// Everything is procedural - two layers of drifting fBm cloud over a horizon gradient - with
// two hooks driven from code (see FX/Storm.cs):
//   _Flash - lightning, brightest toward _FlashDir and strongest where the cloud is thick,
//            which is what makes it read as light inside the cloud rather than a fade.
//   _Glow  - a low orange bloom hugging the horizon: distant artillery and burning towns.
// An HDRI cubemap can be blended in underneath (_TexBlend) once one is imported.
Shader "CombatPrep/StormSky"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.07, 0.08, 0.095, 1)
        _HorizonColor ("Horizon (match fog)", Color) = (0.19, 0.21, 0.24, 1)
        _GroundColor ("Below Horizon", Color) = (0.10, 0.105, 0.115, 1)

        _CloudDark ("Cloud Base", Color) = (0.10, 0.11, 0.125, 1)
        _CloudLight ("Cloud Tops", Color) = (0.23, 0.245, 0.27, 1)
        _CloudScale ("Cloud Scale", Float) = 0.85
        _CloudCover ("Cloud Cover", Range(0, 1)) = 0.40
        _CloudSoftness ("Cloud Softness", Range(0.01, 0.5)) = 0.20
        _CloudOpacity ("Cloud Opacity", Range(0, 1)) = 0.92
        _Wind ("Wind (xy dir, z speed)", Vector) = (1, 0.35, 0.02, 0)

        _Flash ("Lightning", Float) = 0
        _FlashDir ("Lightning Direction", Vector) = (0, 0.3, 1, 0)
        _FlashColor ("Lightning Colour", Color) = (0.72, 0.80, 1.0, 1)

        _Glow ("Horizon Glow", Float) = 0
        _GlowDir ("Glow Direction", Vector) = (1, 0, 0, 0)
        _GlowColor ("Glow Colour", Color) = (1.0, 0.42, 0.14, 1)

        [NoScaleOffset] _Tex ("HDRI (optional)", Cube) = "grey" {}
        _TexBlend ("HDRI Blend", Range(0, 1)) = 0
        _TexExposure ("HDRI Exposure", Float) = 0.35
        _TexRotation ("HDRI Rotation", Range(0, 360)) = 0
        _TexSaturation ("HDRI Saturation", Range(0, 1)) = 0.5
        _TexTint ("HDRI Tint", Color) = (0.9, 0.95, 1.05, 1)

        _Exposure ("Exposure", Float) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _ZenithColor, _HorizonColor, _GroundColor;
            fixed4 _CloudDark, _CloudLight;
            float _CloudScale, _CloudCover, _CloudSoftness, _CloudOpacity;
            float4 _Wind;

            float _Flash;
            float4 _FlashDir;
            fixed4 _FlashColor;

            float _Glow;
            float4 _GlowDir;
            fixed4 _GlowColor;

            samplerCUBE _Tex;
            half4 _Tex_HDR;
            float _TexBlend, _TexExposure, _TexRotation, _TexSaturation;
            fixed4 _TexTint;

            float _Exposure;

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            // --- noise ------------------------------------------------------------------

            float Hash(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash(i);
                float b = Hash(i + float2(1, 0));
                float c = Hash(i + float2(0, 1));
                float d = Hash(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // Five octaves, each rotated, so no grid axis shows through.
            float Fbm(float2 p)
            {
                const float2x2 m = float2x2(1.6, 1.2, -1.2, 1.6);
                float v = 0.0;
                float a = 0.5;
                for (int k = 0; k < 5; k++)
                {
                    v += a * ValueNoise(p);
                    p = mul(m, p);
                    a *= 0.5;
                }
                return v;
            }

            float3 RotateY(float3 d, float degrees)
            {
                float r = radians(degrees);
                float s = sin(r), c = cos(r);
                return float3(c * d.x - s * d.z, d.y, s * d.x + c * d.z);
            }

            // --- sky --------------------------------------------------------------------

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y;

                // Base gradient: dark overhead, lifting to the fog colour at the horizon so the
                // sky and the fogged ground meet without a seam.
                float3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, pow(saturate(h), 0.42));
                float3 below = lerp(_HorizonColor.rgb, _GroundColor.rgb, saturate(-h * 5.0));
                float3 col = h >= 0.0 ? sky : below;

                // Optional photographic sky underneath.
                if (_TexBlend > 0.001)
                {
                    half4 tex = texCUBE(_Tex, RotateY(d, _TexRotation));
                    float3 hdri = DecodeHDR(tex, _Tex_HDR) * _TexExposure;
                    // Drain and cool the photo into the storm palette.
                    float lum = dot(hdri, float3(0.2126, 0.7152, 0.0722));
                    hdri = lerp(lum.xxx, hdri, _TexSaturation) * _TexTint.rgb;
                    // Fogged geometry is exactly the horizon colour, so the photo hands over to
                    // it just above the horizon - otherwise the skyline shows a hard seam.
                    hdri = lerp(_HorizonColor.rgb, hdri, smoothstep(-0.01, 0.16, h));
                    col = lerp(col, hdri, _TexBlend);
                }

                // Clouds: project the view ray onto a flat ceiling. The +0.12 stops the
                // projection racing to infinity at the horizon, where it would alias.
                float density = 0.0;
                float litAmt = 0.0;
                if (h > -0.02)
                {
                    float2 wind = normalize(_Wind.xy) * _Wind.z * _Time.y;
                    float2 uv = d.xz / (max(h, 0.0) + 0.12) * _CloudScale;

                    float n1 = Fbm(uv + wind);
                    float n2 = Fbm(uv * 2.3 + 17.7 + wind * 1.6);   // faster, finer scud underneath
                    float n = n1 * 0.72 + n2 * 0.38;

                    density = smoothstep(_CloudCover - _CloudSoftness, _CloudCover + _CloudSoftness, n);
                    litAmt = saturate(n2 * 1.35 - 0.25);

                    float3 cloud = lerp(_CloudDark.rgb, _CloudLight.rgb, litAmt);
                    // Toward the horizon the deck thins into haze and merges with the fog.
                    float fade = smoothstep(-0.02, 0.22, h);
                    col = lerp(col, cloud, density * fade * _CloudOpacity);
                }

                // Lightning: a soft wash over the whole sky, concentrated toward the strike and
                // strongest in thick cloud.
                if (_Flash > 0.0)
                {
                    float toward = saturate(dot(d, normalize(_FlashDir.xyz)));
                    float shape = pow(toward, 5.0) * 0.85 + 0.15;
                    col += _FlashColor.rgb * _Flash * shape * (0.35 + density * 0.9 + litAmt * 0.4);
                }

                // Horizon glow: fires and shelling beyond the ridge.
                if (_Glow > 0.0)
                {
                    float2 flatDir = normalize(d.xz + 1e-5);
                    float toward = saturate(dot(flatDir, normalize(_GlowDir.xz + 1e-5)));
                    float hug = exp(-max(h, 0.0) * 10.0) * step(-0.05, h);
                    col += _GlowColor.rgb * _Glow * pow(toward, 18.0) * hug * (0.55 + density);
                }

                return fixed4(col * _Exposure, 1.0);
            }
            ENDCG
        }
    }

    Fallback Off
}
