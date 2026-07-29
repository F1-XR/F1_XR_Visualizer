Shader "F1XR/GearUI_Icon"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _Rotation ("Rotation (deg)", Range(0, 360)) = 0
        _Thickness ("Stroke", Range(0.01, 0.2)) = 0.075
        _ArmLength ("Arm Length", Range(0.05, 0.5)) = 0.24
        _Gap ("Chevron Gap", Range(0, 0.5)) = 0.24
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
                half _Rotation;
                half _Thickness;
                half _ArmLength;
                half _Gap;
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

            // 꼭짓점이 원점이고 위를 향하는 갈매기(^) 까지의 거리.
            float ChevronDistance(float2 p, float armLength)
            {
                p.x = abs(p.x);
                float2 dir = float2(0.70710678, -0.70710678);
                float t = clamp(dot(p, dir), 0.0, armLength);
                return length(p - dir * t);
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

                float s, c;
                sincos(radians(_Rotation), s, c);
                q = float2(q.x * c - q.y * s, q.x * s + q.y * c);

                // 시프트 인디케이터풍 이중 갈매기.
                float d = min(ChevronDistance(q - float2(0, _Gap * 0.5), _ArmLength),
                              ChevronDistance(q + float2(0, _Gap * 0.5), _ArmLength));

                float aa = fwidth(d);
                float mask = 1.0 - smoothstep(_Thickness - aa, _Thickness, d);

                return half4(_BaseColor.rgb, mask * _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
