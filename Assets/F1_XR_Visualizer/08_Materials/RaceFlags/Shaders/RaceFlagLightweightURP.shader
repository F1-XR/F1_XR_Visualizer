Shader "F1XR/RaceFlagLightweightURP"
{
    Properties
    {
        _FlagMode ("Flag Mode", Float) = 0
        _FlagColor ("Flag Color", Color) = (1, 0.75, 0.03, 1)
        _Metallic ("Metallic", Range(0, 1)) = 0.65
        _Smoothness ("Smoothness", Range(0, 1)) = 0.7
        _WaveAmplitude ("Wave Amplitude", Float) = 0.04
        _WaveFrequency ("Wave Frequency", Float) = 10
        _WaveSpeed ("Wave Speed", Float) = 6
        _SecondaryWave ("Secondary Wave", Float) = 0.35
        _MotionPhase ("Motion Phase", Float) = 0
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

            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _FlagMode;
                half4 _FlagColor;
                float _Metallic;
                float _Smoothness;
                float _WaveAmplitude;
                float _WaveFrequency;
                float _WaveSpeed;
                float _SecondaryWave;
                float _MotionPhase;
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
                float3 normalWS : TEXCOORD2;
                float3 viewDirWS : TEXCOORD3;

                UNITY_VERTEX_OUTPUT_STEREO
            };

            float SmoothAttachment(float u)
            {
                return smoothstep(0.08, 1.0, u);
            }

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float2 uv = input.uv;
                float time = _Time.y;
                float attachment = SmoothAttachment(uv.x);

                float primaryArg = time * _WaveSpeed + uv.x * _WaveFrequency + _MotionPhase;
                float secondaryArg =
                    time * (_WaveSpeed * 0.73) +
                    uv.x * (_WaveFrequency * 1.7) +
                    uv.y * 5.0 +
                    _MotionPhase * 1.37;

                float primaryWave = sin(primaryArg);
                float secondaryWave = sin(secondaryArg);
                float combinedWave = primaryWave + secondaryWave * _SecondaryWave;
                float zDisplacement = combinedWave * _WaveAmplitude * attachment;
                float edgeCurl =
                    smoothstep(0.72, 1.0, uv.x) *
                    sin(primaryArg * 0.62 + uv.y * 3.0) *
                    _WaveAmplitude *
                    0.14;

                float3 positionOS = input.positionOS.xyz;
                positionOS.z += zDisplacement + edgeCurl * attachment;
                positionOS.y += zDisplacement * 0.18;

                float slopeX =
                    (cos(primaryArg) * _WaveFrequency +
                    cos(secondaryArg) * (_WaveFrequency * 1.7) * _SecondaryWave) *
                    _WaveAmplitude *
                    attachment;
                float slopeY =
                    cos(secondaryArg) *
                    5.0 *
                    _SecondaryWave *
                    _WaveAmplitude *
                    attachment;

                float3 normalOS = normalize(float3(-slopeX, -slopeY, 1.0));

                VertexPositionInputs positionInputs = GetVertexPositionInputs(positionOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(normalOS);
                output.viewDirWS = GetWorldSpaceNormalizeViewDir(positionInputs.positionWS);
                output.uv = uv;

                return output;
            }

            half3 GetFlagBaseColor(float2 uv)
            {
                if (_FlagMode < 0.5)
                    return _FlagColor.rgb;

                float2 cells = floor(uv * float2(8.0, 6.0));
                float checker = fmod(cells.x + cells.y, 2.0);
                return lerp(half3(0.90, 0.90, 0.90), half3(0.03, 0.03, 0.03), checker);
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(input.viewDirWS);
                normalWS = faceforward(normalWS, -viewDirWS, normalWS);

                float3 lightDirWS = normalize(float3(0.35, 0.75, 0.45));
                float diffuse = saturate(dot(normalWS, lightDirWS));
                float rim = pow(1.0 - saturate(dot(normalWS, viewDirWS)), 2.0);
                float3 halfDirWS = normalize(lightDirWS + viewDirWS);
                float metallic = saturate(_Metallic);
                float specularPower = lerp(16.0, 128.0, saturate(_Smoothness));
                float specular = pow(saturate(dot(normalWS, halfDirWS)), specularPower);

                half3 baseColor = GetFlagBaseColor(input.uv);
                half diffuseLighting = (half)(0.42 + diffuse * lerp(0.42, 0.18, metallic));
                half3 specularColor = lerp(half3(0.04, 0.04, 0.04), baseColor, metallic);
                half3 color =
                    baseColor * diffuseLighting +
                    specularColor * (half)(specular * lerp(0.25, 1.0, metallic)) +
                    baseColor * (half)(rim * 0.10);

                return half4(saturate(color), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
