// The reticle of a red dot or a scope, drawn on a disc of glass inside the sight.
//
// The pattern is laid out in screen pixels around the exact centre of the screen - the line
// every shot is traced along - rather than being geometry on the gun. That makes it precise in
// three ways: it always marks where the bullets go, even mid-recoil while the gun kicks and
// sways; its lines stay one crisp pixel wide at any zoom or resolution; and because the disc it
// is drawn on sits inside the sight, you only ever see it *through* the sight - lower the gun
// and the centre of the screen is no longer behind the glass. A real red dot behaves the same
// way: its dot is projected to infinity, so it sits on the target, not on the housing.
//
// Sizes are authored in pixels at 1080p and scale with the screen height. The shader lives in
// a Resources folder so builds include it; nothing else references it by asset.
Shader "CombatPrep/Reticle"
{
    Properties
    {
        [HDR] _BaseColor ("Colour", Color) = (1, 0.06, 0.04, 1)
        _Style ("Style (0 dot, 1 crosshair)", Float) = 0
        _DotRadius ("Dot radius (px at 1080p)", Float) = 1.7
        _LineWidth ("Line width (px at 1080p)", Float) = 1
        _PostWidth ("Post width (px at 1080p)", Float) = 4
        _PostStart ("Post start (px at 1080p)", Float) = 130
        _Gap ("Gap around the dot (px at 1080p)", Float) = 5
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Style, _DotRadius, _LineWidth, _PostWidth, _PostStart, _Gap;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 disc : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.disc = input.uv - 0.5;
                return o;
            }

            // Coverage of a line `width` pixels wide, `dist` pixels from its centre line. The
            // one-pixel ramp keeps widths that aren't whole pixels even rather than jagged.
            float Band(float dist, float width)
            {
                return saturate(width * 0.5 + 0.5 - dist);
            }

            half4 frag(Varyings input) : SV_Target
            {
                if (dot(input.disc, input.disc) > 0.25) discard;    // round glass

                // Distance from the screen centre, snapped to a pixel centre so a one-pixel
                // line covers exactly one row of pixels instead of half of two.
                float2 centre = floor(_ScaledScreenParams.xy * 0.5) + 0.5;
                float2 d = abs(input.positionCS.xy - centre);
                float px = _ScaledScreenParams.y / 1080.0;

                float radius = max(_DotRadius * px, 0.75);
                float a = saturate(radius + 0.5 - length(d));

                if (_Style > 0.5)
                {
                    // Duplex crosshair: hairlines near the middle for precision, heavier posts
                    // toward the edge so the eye finds the centre fast.
                    float thin = max(_LineWidth * px, 1.0);
                    float thick = max(_PostWidth * px, thin);
                    float start = _PostStart * px;
                    float gap = _Gap * px;
                    float h = Band(d.y, d.x > start ? thick : thin) * step(gap, d.x);
                    float v = Band(d.x, d.y > start ? thick : thin) * step(gap, d.y);
                    a = max(a, max(h, v));
                }

                clip(a - 0.002);
                return half4(_BaseColor.rgb, _BaseColor.a * a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
