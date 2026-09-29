// Unlit additive glow that ignores fog. URP's own shaders blend every pixel toward the fog
// colour, so a lightning bolt 300 m out in storm fog would draw as a faint grey smear. Light
// is what you see through haze, so the bolt skips the fog and stays hot enough to bloom.
// Multiplies by vertex colour, so LineRenderer start/end colours and alpha work as usual.
Shader "CombatPrep/GlowNoFog"
{
    Properties
    {
        [HDR] _BaseColor ("Colour", Color) = (1, 1, 1, 1)
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

        Blend SrcAlpha One
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.color = input.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                return _BaseColor * i.color;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
