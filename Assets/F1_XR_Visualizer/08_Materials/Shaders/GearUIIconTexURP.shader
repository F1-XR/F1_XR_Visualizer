Shader "F1XR/GearUI_IconTex"
{
    // 기어 UI 카드 정중앙에 아이콘 이미지를 올리는 셰이더. 카드마다 다른 텍스처를 쓰려면
    // 카드별 머티리얼을 하나씩 두고 _IconTex 만 바꾸면 된다.
    Properties
    {
        _IconTex ("Icon", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        [Toggle] _KeepAspect ("Keep Aspect (fit square)", Float) = 1
        _Scale ("Icon Scale", Range(0.2, 1.5)) = 1
        _KeyWhite ("White Key (0 = off)", Range(0, 1)) = 0
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

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_IconTex);
            SAMPLER(sampler_IconTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _IconTex_ST;
                half4 _BaseColor;
                half _KeepAspect;
                half _Scale;
                half _KeyWhite;
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

                // 정중앙 기준으로 축소/확대. _Scale 1 = 쿼드를 꽉 채움.
                float2 uv = (input.uv - 0.5) / max(_Scale, 1e-3) + 0.5;
                if (any(uv < 0.0) || any(uv > 1.0))
                    discard;

                half4 tex = SAMPLE_TEXTURE2D(_IconTex, sampler_IconTex, TRANSFORM_TEX(uv, _IconTex));

                // 흰 배경 PNG 대응: 흰색에 가까운 픽셀을 투명 처리(0 이면 끔).
                if (_KeyWhite > 0.001)
                {
                    half white = min(min(tex.r, tex.g), tex.b);
                    tex.a *= 1.0 - smoothstep(_KeyWhite - 0.08, _KeyWhite, white);
                }

                return half4(tex.rgb * _BaseColor.rgb, tex.a * _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
