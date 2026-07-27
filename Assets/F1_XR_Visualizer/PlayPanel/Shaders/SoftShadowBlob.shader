Shader "F1XR/SoftShadowBlob"
{
    // A soft, dark, elliptical blob drawn on a quad. Used as an art-directed contact shadow that the
    // protruding 3D play triangle casts onto the panel face (long, downward). Alpha-blended (darkens
    // the glass beneath it) rather than additive. UV.y bias lets the falloff trail further downward.
    Properties
    {
        _Color ("Shadow Color", Color) = (0, 0, 0, 1)
        _Strength ("Strength", Range(0,1)) = 0.55
        _Softness ("Softness", Range(0.01, 1.0)) = 0.75
        _VerticalBias ("Vertical Bias (tail down)", Range(-0.5, 0.5)) = 0.18
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ShadowBlob"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Strength;
                float _Softness;
                float _VerticalBias;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                // Centred coords; shift the centre up so the soft tail lingers toward the bottom.
                float2 p = (IN.uv - 0.5) * 2.0;
                p.y += _VerticalBias;

                float r = length(p);
                float a = (1.0 - smoothstep(1.0 - _Softness, 1.0, r)) * _Strength * _Color.a;
                return half4(_Color.rgb, saturate(a));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
