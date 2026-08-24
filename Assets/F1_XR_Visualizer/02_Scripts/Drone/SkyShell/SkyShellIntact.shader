Shader "F1XR/SkyShellIntact"
{
    Properties
    {
        _BaseColor ("Fallback Color", Color) = (0.29, 0.37, 0.53, 1)
        _SkyTex ("Baked Sky", CUBE) = "" {}
        [Toggle] _UseSkyTex ("Use Baked Sky", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Background"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "SkyShellIntact"

            ZWrite Off
            ZTest LEqual
            Cull Back
            Blend One Zero
            ColorMask RGBA

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _USESKYTEX_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
#if defined(_USESKYTEX_ON)
                float3 viewDirWS  : TEXCOORD0;
#endif
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END

#if defined(_USESKYTEX_ON)
            TEXTURECUBE(_SkyTex);
            SAMPLER(sampler_SkyTex);
#endif

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
#if defined(_USESKYTEX_ON)
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.viewDirWS = posWS - _WorldSpaceCameraPos.xyz;
#endif
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

#if defined(_USESKYTEX_ON)
                float3 dir = normalize(IN.viewDirWS);
                half4 sky = SAMPLE_TEXTURECUBE_LOD(_SkyTex, sampler_SkyTex, dir, 0);
                return half4(sky.rgb, 1);
#else
                return half4(_BaseColor.rgb, 1);
#endif
            }
            ENDHLSL
        }
    }

    Fallback Off
}
