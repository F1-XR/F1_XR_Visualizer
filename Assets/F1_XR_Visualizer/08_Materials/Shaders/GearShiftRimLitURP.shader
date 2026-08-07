Shader "F1XR/GearShiftRimLitURP"
{
    // Black glossy lathe-turned lever with red glass, matching the reference turnaround sheet.
    //
    // The reference has two different kinds of red, and they need two different mechanisms:
    //
    //   PAINTED RINGS - the base torus, the neck ring and the foot hairlines. These are real
    //     360-degree bands: a horizontal stripe from the front, a complete circle from the top.
    //     Fixed on the surface, so they come from the mask texture's R channel.
    //
    //   GLASS SHELL - the bulb and the column. The turnaround proves this one is view-dependent:
    //     from the top the bulb shows an unbroken red RING around the black dome, while front,
    //     back, left and right all look identical with red at the two silhouette edges. A painted
    //     panel cannot do both - it would read as a couple of patches from above, and it would
    //     swing out of view when the object turns. What does do both is a red translucent shell
    //     over a black core: you see red wherever you look through the shell at a grazing angle.
    //     So the texture only says WHERE the shell exists (G channel) and how wide it reads, and
    //     the grazing-angle term is evaluated here.
    //
    // Mask texture (GearShift_Mask) is painted in cylindrical space: U = azimuth, V = rest height.
    //   R = painted red coverage (full 360 rings)
    //   G = glass shell weight: 1 over the bulb, ~0.45 over the column, 0 on the cone and collar
    // V comes from UV2.x, baked from rest-pose height, so skinning cannot drag the layout around
    // when the lever tilts. U is derived per-pixel from the object-space position.
    Properties
    {
        [MainColor] _BaseColor ("Body Color", Color) = (0.022, 0.022, 0.025, 1)
        _Metallic ("Body Metallic", Range(0,1)) = 0.05
        _Smoothness ("Body Smoothness", Range(0,1)) = 0.94

        _MaskMap ("Mask (R = painted, G = glass shell)", 2D) = "black" {}
        _MaskRotation ("Mask Rotation (deg)", Range(0,360)) = 0

        [HDR] _EdgeColor ("Red Glass Color", Color) = (0.42, 0.010, 0.004, 1)
        _EdgeEmission ("Red Emission", Range(0, 2)) = 0.05
        _EdgeSmoothness ("Red Smoothness", Range(0,1)) = 0.96

        _ShellStart ("Shell Grazing Start", Range(0,1)) = 0.30
        _ShellSoft ("Shell Edge Softness", Range(0.01,0.6)) = 0.16
        _ThicknessGlow ("Glass Thickness Glow", Range(0, 3)) = 0.9
        _SeamEmission ("Boundary Seam", Range(0, 2)) = 0.35
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MaskMap); SAMPLER(sampler_MaskMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _EdgeColor;
                float4 _MaskMap_ST;
                half _Metallic;
                half _Smoothness;
                half _MaskRotation;
                half _EdgeEmission;
                half _EdgeSmoothness;
                half _ShellStart;
                half _ShellSoft;
                half _ThicknessGlow;
                half _SeamEmission;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv2 : TEXCOORD1;   // x = rest height 0..1
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float2 profile : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionOS = input.positionOS.xyz;
                output.profile = input.uv2;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float u = atan2(input.positionOS.z, input.positionOS.x) * (1.0 / TWO_PI) + 0.5
                        + _MaskRotation * (1.0 / 360.0);
                float v = input.profile.x;
                float2 uv = float2(u, v);

                // atan2 jumps a full turn along one meridian; left alone that derivative spike
                // picks the smallest mip and draws a blurred stripe there.
                float2 duvdx = float2(ddx(u), ddx(v));
                float2 duvdy = float2(ddy(u), ddy(v));
                if (abs(duvdx.x) > 0.5) duvdx.x -= sign(duvdx.x);
                if (abs(duvdy.x) > 0.5) duvdy.x -= sign(duvdy.x);
                half4 m = SAMPLE_TEXTURE2D_GRAD(_MaskMap, sampler_MaskMap, uv, duvdx, duvdy);

                half3 normalWS = normalize(input.normalWS);
                half3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

                half painted = saturate(m.r);
                half shellW = saturate(m.g);

                // 0 looking straight through the shell, 1 looking through it edge-on.
                half graze = 1.0h - saturate(dot(normalWS, viewWS));
                // A low shell weight only lights up at the extreme silhouette, so the column reads
                // as thin edge lines while the bulb reads as broad panels, from one texture.
                half start = lerp(0.90h, _ShellStart, shellW);
                half shell = step(0.01h, shellW) * smoothstep(start, start + _ShellSoft, graze);

                // Looking through the most glass keeps the colour luminous instead of going black
                // where the surface faces away from every light.
                half thickness = shell * smoothstep(start, 1.0h, graze);
                // Bright line where the black core's edge shows through the shell.
                half seam = step(0.01h, shellW)
                          * (1.0h - smoothstep(0.0h, _ShellSoft * 0.6h, abs(graze - start)));

                half mask = saturate(painted + shell);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = lerp(_BaseColor.rgb, _EdgeColor.rgb, mask);
                surface.metallic = lerp(_Metallic, 0.0h, mask);   // red reads as glass, not metal
                surface.smoothness = lerp(_Smoothness, _EdgeSmoothness, mask);
                surface.occlusion = 1.0h;
                surface.alpha = 1.0h;
                surface.emission = _EdgeColor.rgb * (mask * _EdgeEmission
                                                   + thickness * _ThicknessGlow
                                                   + seam * _SeamEmission);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = viewWS;
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, ComputeFogFactor(input.positionCS.z));
                return color;
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }

    FallBack "Universal Render Pipeline/Lit"
}
