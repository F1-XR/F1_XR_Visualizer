Shader "F1XR/GearUI_Glow"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 0.118, 0.078, 1)
        _Falloff ("Falloff", Range(0.5, 6)) = 2.2
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Transparent"
            "Queue"="Transparent"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // XR / Single Pass Instanced 대응
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Falloff;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;

                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 q = input.uv - 0.5;

                // 둥근 사각형(4-norm) 감쇠. max() 를 쓰면 별처럼 뾰족해져서 후광으로 안 읽힌다.
                float2 a = abs(q) * 2.0;
                float2 a4 = (a * a) * (a * a);
                float d = saturate(sqrt(sqrt(a4.x + a4.y)));
                float glow = pow(1.0 - d, _Falloff);

                return half4(_BaseColor.rgb, glow * _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
