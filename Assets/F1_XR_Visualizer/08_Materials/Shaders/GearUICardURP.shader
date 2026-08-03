Shader "F1XR/GearUI_Card"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.043, 0.043, 0.051, 0.62)
        _AccentColor ("Accent Bar", Color) = (1, 0.165, 0.133, 1)
        _Chamfer ("Corner Cut", Range(0, 0.4)) = 0.18
        _WeaveScale ("Weave Scale", Range(4, 90)) = 44
        _WeaveStrength ("Weave Strength", Range(0, 0.3)) = 0.018
        _BarHeight ("Accent Bar Height", Range(0, 0.4)) = 0.11
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

            // XR / Single Pass Instanced 대응
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _AccentColor;
                half _Chamfer;
                half _WeaveScale;
                half _WeaveStrength;
                half _BarHeight;
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

                float2 uv = input.uv;
                float2 q = uv - 0.5;

                // F1 리버리풍 각진 실루엣: 마주보는 두 모서리를 45도로 잘라낸다.
                float box = max(abs(q.x), abs(q.y));
                float diag = abs(q.x + q.y);
                float limit = 1.0 - _Chamfer;
                float mask = (1.0 - smoothstep(0.5 - fwidth(box), 0.5, box))
                           * (1.0 - smoothstep(limit - fwidth(diag), limit, diag));

                // 카본 트윌 결 + 위로 갈수록 밝아지는 그라데이션.
                // 카드 바탕이 거의 검정이라 곱셈으로는 아무것도 안 보인다 -> 절대값으로 더한다.
                // 카본 능직: 한 방향 대각 리브 + 성긴 직조 격자. 무늬로 보이면 안 되고 표면 결로만 읽혀야 한다.
                float2 w = uv * _WeaveScale;
                float rib = sin((w.x + w.y) * PI);
                float tow = sin(w.x * PI * 0.5) * sin(w.y * PI * 0.5);
                float weave = rib * 0.7 + tow * 0.3;
                float lift = weave * _WeaveStrength + lerp(-0.012, 0.038, uv.y);

                half3 col = _BaseColor.rgb + lift;

                // 아래쪽 레드 액센트 바 + 그 위 얇은 검은 분리선(선택 시 붉은 카드에서도 바가 읽히도록).
                float bar = 1.0 - step(_BarHeight, uv.y);
                float sep = step(_BarHeight, uv.y) - step(_BarHeight + 0.014, uv.y);
                col = lerp(col, _AccentColor.rgb, bar);
                col = lerp(col, half3(0, 0, 0), sep * 0.85);

                return half4(col, mask * _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
