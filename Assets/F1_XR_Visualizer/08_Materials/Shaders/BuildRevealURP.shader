Shader "F1XR/BuildRevealURP"
{
    Properties
    {
        // 원본 머티리얼에서 복사되는 기본 텍스처/색입니다. 트랙 자체의 색감이 바뀝니다.
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _AlphaCutoff ("Alpha Cutoff", Range(0,1)) = 0.5

        // _BuildHeight를 올리면 이 높이보다 아래에 있는 부분만 보입니다. 코드가 매 프레임 올려서 아래->위 생성 효과를 만듭니다.
        _BuildHeight ("Build Height", Float) = 0
        // 생성 경계선의 두께입니다. 값을 키우면 빛나는 라인이 두꺼워지고, 줄이면 얇아집니다.
        _EdgeWidth ("Edge Width", Float) = 0.18
        // 생성 경계선의 색입니다. HDR 색이라 값을 1보다 크게 주면 더 강하게 빛납니다.
        [HDR]_EdgeColor ("Edge Color", Color) = (4,1.8,0.1,1)

        // 생성 중 전체 모델에 살짝 섞이는 색입니다. 완성 후 원본 머티리얼을 복구하면 최종 색에는 남지 않습니다.
        _BuildTintColor ("Build Tint Color", Color) = (1,0.62,0.15,1)
        // 위 색을 얼마나 섞을지 정합니다. 0이면 원본색, 1이면 거의 Build Tint Color가 됩니다.
        _BuildTintStrength ("Build Tint Strength", Range(0,1)) = 0.1

        // 전체 투명도입니다. 현재 빌드 리빌은 불투명 렌더링 기준이라 보통 1로 둡니다.
        _Alpha ("Alpha", Range(0,1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Opaque"
            "Queue"="Geometry"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            // XR / Single Pass Instanced 대응
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _AlphaCutoff;

                float _BuildHeight;
                float _EdgeWidth;
                half4 _EdgeColor;

                half4 _BuildTintColor;
                float _BuildTintStrength;

                float _Alpha;
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
                float3 positionWS : TEXCOORD1;

                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(input.positionOS.xyz);

                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // XR / VR에서 현재 눈 정보를 세팅하는 코드.
                // 이게 없으면 한쪽 눈에만 보일 수 있음.
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // ------------------------------------------------------------
                // 1. 아래에서 위로 나타나는 핵심 부분
                // ------------------------------------------------------------
                // input.positionWS.y = 현재 픽셀의 월드 Y 높이
                // _BuildHeight = 현재 건설 진행 높이
                //
                // clip(x)는 x가 0보다 작으면 그 픽셀을 버림.
                //
                // 예:
                // _BuildHeight = 1.0
                // 현재 픽셀 y = 1.2
                // 1.0 - 1.2 = -0.2 → 버림 → 안 보임
                //
                // 현재 픽셀 y = 0.8
                // 1.0 - 0.8 = 0.2 → 살아남음 → 보임
                clip(_BuildHeight - input.positionWS.y);

                // ------------------------------------------------------------
                // 2. 원래 material 색/텍스처 가져오기
                // ------------------------------------------------------------
                // _BaseMap = 원래 텍스처
                // _BaseColor = 원래 색
                half4 baseCol =
                    SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                clip(baseCol.a - _AlphaCutoff);

                // ------------------------------------------------------------
                // 3. 생성 중에 살짝 주황빛을 섞기
                // ------------------------------------------------------------
                // _BuildTintStrength가 0이면 원래 색 그대로.
                // _BuildTintStrength가 1이면 거의 _BuildTintColor 색으로 덮임.
                //
                // 지금은 0.08~0.15 정도가 적당.
                baseCol.rgb = lerp(
                    baseCol.rgb,
                    _BuildTintColor.rgb,
                    saturate(_BuildTintStrength)
                );

                // ------------------------------------------------------------
                // 4. 잘리는 경계선 근처 계산
                // ------------------------------------------------------------
                // edgeWidth = 노란 생성 라인의 두께
                // 값이 크면 두꺼운 노란 띠.
                // 값이 작으면 얇은 스캔 라인.
                float edgeWidth = max(_EdgeWidth, 0.0001);

                // 현재 픽셀이 BuildHeight 선보다 얼마나 아래에 있는지 계산.
                //
                // distBelowLine이 0에 가까움 = 자르는 선 바로 근처
                // distBelowLine이 큼 = 이미 한참 아래라서 생성 라인과 멀리 떨어짐
                float distBelowLine = _BuildHeight - input.positionWS.y;

                // edge 값 만들기.
                //
                // 자르는 선 바로 근처면 edge = 1에 가까움
                // 자르는 선에서 멀면 edge = 0에 가까움
                //
                // 즉 edge는 "여기에 노란 발광을 얼마나 줄까?" 값임.
                float edge = 1.0 - saturate(distBelowLine / edgeWidth);

                // ------------------------------------------------------------
                // 5. 살짝 깜빡이는 느낌
                // ------------------------------------------------------------
                // _Time.y는 시간.
                // sin을 써서 0.5~1.0 사이로 왔다 갔다 하는 느낌을 줌.
                float pulse = 0.75 + 0.25 * sin(_Time.y * 18.0);

                // ------------------------------------------------------------
                // 6. 노란 라인 색 섞기
                // ------------------------------------------------------------
                // 첫 줄: 원래 색과 노란색을 섞음.
                // edge가 클수록 노란색에 가까워짐.
                baseCol.rgb = lerp(baseCol.rgb, _EdgeColor.rgb, edge * 0.35);

                // 둘째 줄: 노란 발광 느낌 추가.
                // 숫자가 클수록 더 번쩍이고 노랗게 보임.
                baseCol.rgb += _EdgeColor.rgb * edge * 0.9 * pulse;

                // ------------------------------------------------------------
                // 7. 최종 알파
                // ------------------------------------------------------------
                baseCol.a *= _Alpha;

                return baseCol;
            }
            ENDHLSL
        }
    }
}
