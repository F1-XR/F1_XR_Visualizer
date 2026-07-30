Shader "F1XR/GearUI_Frame"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 0.165, 0.133, 0.9)
        _Chamfer ("Corner Cut", Range(0, 0.4)) = 0.155
        _Thickness ("Line Thickness", Range(0.005, 0.2)) = 0.028
        _CornerBoost ("Corner Bracket Boost", Range(1, 5)) = 2.6
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
                half _Chamfer;
                half _Thickness;
                half _CornerBoost;
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

            // 잘린 모서리 사각형의 안쪽 덮임 정도. half_ 를 줄이면 같은 모양이 안쪽으로 수축한다.
            float Coverage(float2 q, float half_, float limit)
            {
                float box = max(abs(q.x), abs(q.y));
                float diag = abs(q.x + q.y);
                return (1.0 - smoothstep(half_ - fwidth(box), half_, box))
                     * (1.0 - smoothstep(limit - fwidth(diag), limit, diag));
            }

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
                float limit = 1.0 - _Chamfer;

                // 잘린 모서리 근처에서는 선을 두껍게 -> HUD 코너 브라켓처럼 보인다.
                float onChamfer = smoothstep(limit - 0.16, limit - 0.02, abs(q.x + q.y));
                float t = _Thickness * lerp(1.0, _CornerBoost, onChamfer);

                float ring = Coverage(q, 0.5, limit) - Coverage(q, 0.5 - t, limit - t * 2.0);

                return half4(_BaseColor.rgb, saturate(ring) * _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
