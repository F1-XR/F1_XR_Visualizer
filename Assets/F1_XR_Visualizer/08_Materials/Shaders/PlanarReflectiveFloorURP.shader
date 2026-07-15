Shader "F1XR/PlanarReflectiveFloorURP"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.0025, 0.0025, 0.0025, 1)
        _PlanarReflectionTex ("Planar Reflection", 2D) = "black" {}
        _ReflectionTint ("Reflection Tint", Color) = (0.82, 0.86, 0.9, 1)
        _ReflectionStrength ("Reflection Strength", Range(0, 1)) = 0.72
        _ReflectionBlur ("Reflection Blur", Range(0, 6)) = 1.15
        _WetSheen ("Wet Sheen", Range(0, 2)) = 0.18
        _ProbeIntensity ("Probe Reflection Intensity", Range(0, 2)) = 1.0
        _ProbeRoughness ("Probe Perceptual Roughness", Range(0, 1)) = 0.02
        _PlanarBlend ("Planar Blend (1 = planar, 0 = probe only)", Range(0, 1)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_PlanarReflectionTex);
            SAMPLER(sampler_PlanarReflectionTex);
            float4 _PlanarReflectionTex_TexelSize;
            float4x4 _ReflectionViewProjection;

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _ReflectionTint;
                half _ReflectionStrength;
                half _ReflectionBlur;
                half _WetSheen;
                half _ProbeIntensity;
                half _ProbeRoughness;
                half _PlanarBlend;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 reflectionCS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.reflectionCS = mul(_ReflectionViewProjection, float4(output.positionWS, 1.0));
                return output;
            }

            half3 SampleBlurredReflection(float2 uv)
            {
                float2 texel = _PlanarReflectionTex_TexelSize.xy * _ReflectionBlur;
                half3 color = SAMPLE_TEXTURE2D(_PlanarReflectionTex, sampler_PlanarReflectionTex, uv).rgb * 0.32h;
                color += SAMPLE_TEXTURE2D(_PlanarReflectionTex, sampler_PlanarReflectionTex, uv + texel * float2(1, 0)).rgb * 0.12h;
                color += SAMPLE_TEXTURE2D(_PlanarReflectionTex, sampler_PlanarReflectionTex, uv + texel * float2(-1, 0)).rgb * 0.12h;
                color += SAMPLE_TEXTURE2D(_PlanarReflectionTex, sampler_PlanarReflectionTex, uv + texel * float2(0, 1)).rgb * 0.12h;
                color += SAMPLE_TEXTURE2D(_PlanarReflectionTex, sampler_PlanarReflectionTex, uv + texel * float2(0, -1)).rgb * 0.12h;
                color += SAMPLE_TEXTURE2D(_PlanarReflectionTex, sampler_PlanarReflectionTex, uv + texel * float2(1, 1)).rgb * 0.05h;
                color += SAMPLE_TEXTURE2D(_PlanarReflectionTex, sampler_PlanarReflectionTex, uv + texel * float2(-1, 1)).rgb * 0.05h;
                color += SAMPLE_TEXTURE2D(_PlanarReflectionTex, sampler_PlanarReflectionTex, uv + texel * float2(1, -1)).rgb * 0.05h;
                color += SAMPLE_TEXTURE2D(_PlanarReflectionTex, sampler_PlanarReflectionTex, uv + texel * float2(-1, -1)).rgb * 0.05h;
                return color;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float4 reflectionScreen = ComputeScreenPos(input.reflectionCS);
                float2 uv = reflectionScreen.xy / max(reflectionScreen.w, 0.0001);

                half2 edge = saturate((0.5h - abs((half2)uv - 0.5h)) * 16.0h);
                half edgeFade = edge.x * edge.y;

                half3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight();
                half directLight = saturate(dot(normalWS, mainLight.direction)) * 0.003h;
                half3 baseFloor = _BaseColor.rgb + directLight * mainLight.color;

                half3 viewDirection = normalize(GetWorldSpaceViewDir(input.positionWS));

                // Nearby, on-screen reflection comes from the planar camera; anywhere that
                // data is invalid (screen edges, oblique angles) falls back to the reflection
                // probe instead of fading to black, and _PlanarBlend lets a platform (mobile)
                // force probe-only reflection without touching the planar render path.
                half3 reflectVector = reflect(-viewDirection, normalWS);
                half3 probeReflection = GlossyEnvironmentReflection(reflectVector, input.positionWS, _ProbeRoughness, 1.0h) * _ProbeIntensity;
                half3 planarColor = SampleBlurredReflection(uv);
                half3 reflected = lerp(probeReflection, planarColor, edgeFade * _PlanarBlend) * _ReflectionTint.rgb;

                half fresnel = pow(1.0h - saturate(dot(normalWS, viewDirection)), 3.0h);
                half sheen = fresnel * 0.1h * _WetSheen;

                half3 color = baseFloor + reflected * _ReflectionStrength + sheen;
                return half4(color, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
