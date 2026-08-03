Shader "F1XR/GridFloor"
{
    // A faint procedural floor grid that fades out with distance, to give the XR space a sense of
    // depth and ground. World-space cells so the grid stays uniform regardless of plane scale.
    Properties
    {
        [HDR] _LineColor ("Line Color", Color) = (0.35, 0.55, 0.75, 1)
        _CellSize ("Cell Size (m)", Float) = 0.25
        _LineWidth ("Line Width (px)", Range(0.5, 4)) = 1.2
        _FadeRadius ("Fade Radius (m)", Float) = 6
        _Intensity ("Intensity", Range(0,3)) = 0.6
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
            Name "Grid"
            Blend SrcAlpha One
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _LineColor;
                float _CellSize;
                float _LineWidth;
                float _FadeRadius;
                float _Intensity;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float3 ws = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionWS = ws;
                OUT.positionHCS = TransformWorldToHClip(ws);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float2 coord = IN.positionWS.xz / max(_CellSize, 1e-4);
                float2 grid = abs(frac(coord - 0.5) - 0.5) / max(fwidth(coord), 1e-5);
                float ln = min(grid.x, grid.y);
                float g = 1.0 - saturate(ln / _LineWidth);

                float fade = saturate(1.0 - length(IN.positionWS.xz) / max(_FadeRadius, 1e-3));
                fade *= fade;

                float a = g * fade * _Intensity;
                return half4(_LineColor.rgb * a, a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
