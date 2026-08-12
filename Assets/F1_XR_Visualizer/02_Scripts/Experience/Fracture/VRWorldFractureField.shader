Shader "F1XR/VRWorldFractureField"
{
    // VR -> MR, phase A: the VR world itself is what breaks.
    //
    // Every VR pixel is projected back to the world position of the surface it came from,
    // and that position is tested against a fracture field anchored in the VR world. Pixels
    // the field has claimed write framebuffer alpha 0, so the Meta passthrough compositor
    // shows the real room exactly there.
    //
    // The point of doing it in world space rather than screen space: the test depends only on
    // where a surface is, never on where the head is. Move, lean or turn and the hole stays
    // on the same part of the VR world, seen from the new angle. A screen-space wipe would
    // follow the eyes and read as a transition overlay pasted over the world.
    //
    // Nothing here is a snapshot. The VR scene keeps rendering live underneath for the whole
    // break; this only removes pixels from it.
    Properties
    {
        _FractureOrigin ("Fracture Origin (world)", Vector) = (0, 0, 0, 0)
        _Threshold ("Break Threshold (metres)", Float) = 0
        _EdgeWidth ("Crack Edge Width (metres)", Range(0.001, 1)) = 0.08
        _NoiseFrequency ("Noise Frequency", Range(0.05, 8)) = 1.1
        _NoiseStrength ("Noise Strength (metres)", Range(0, 4)) = 0.9
        _EdgeColor ("Crack Edge Colour", Color) = (0.02, 0.02, 0.03, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Overlay"
            // Just before the transparent queue, so world-space UI still draws on top of the
            // break instead of being punched out with it. Everything opaque is drawn by here,
            // so the depth texture is ready.
            "Queue" = "Transparent-1"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "VRWorldFractureField"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend Off
            ColorMask RGBA

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 _FractureOrigin;
            float _Threshold;
            float _EdgeWidth;
            float _NoiseFrequency;
            float _NoiseStrength;
            float4 _EdgeColor;

            float Hash31(float3 p)
            {
                p = frac(p * 0.3183099 + float3(0.71, 0.113, 0.419));
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            // Plain trilinear value noise. No texture to bind, no gradient table, and cheap
            // enough to run per pixel on Quest for the couple of seconds the break lasts.
            float ValueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);

                float n000 = Hash31(i + float3(0, 0, 0));
                float n100 = Hash31(i + float3(1, 0, 0));
                float n010 = Hash31(i + float3(0, 1, 0));
                float n110 = Hash31(i + float3(1, 1, 0));
                float n001 = Hash31(i + float3(0, 0, 1));
                float n101 = Hash31(i + float3(1, 0, 1));
                float n011 = Hash31(i + float3(0, 1, 1));
                float n111 = Hash31(i + float3(1, 1, 1));

                float x00 = lerp(n000, n100, f.x);
                float x10 = lerp(n010, n110, f.x);
                float x01 = lerp(n001, n101, f.x);
                float x11 = lerp(n011, n111, f.x);

                return lerp(lerp(x00, x10, f.y), lerp(x01, x11, f.y), f.z);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                // Clip-space triangle used untransformed. A camera-parented quad cannot work:
                // the two eye frustums are asymmetric, so one sized for one eye leaves a gap
                // in the other.
                OUT.positionHCS = float4(IN.positionOS.xy, UNITY_NEAR_CLIP_VALUE, 1.0);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                // _ScaledScreenParams, not _ScreenParams: under dynamic resolution only part
                // of the render target is in use and the wrong divisor slides the whole image.
                float2 uv = IN.positionHCS.xy / _ScaledScreenParams.xy;
                float rawDepth = SampleSceneDepth(uv);

            #if UNITY_REVERSED_Z
                bool isBackground = rawDepth <= 0.0;
            #else
                bool isBackground = rawDepth >= 1.0;
            #endif

                // No surface here, so there is nothing to break. Reconstructing it anyway
                // would invent a position out at the far plane and hand the field a pixel
                // that represents no part of the VR world.
                if (isBackground)
                    discard;

                // UNITY_MATRIX_I_VP is the current eye's inverse view-projection under
                // single-pass instanced stereo, so both eyes land on the same world point and
                // therefore agree on what is broken.
                float3 positionWS = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);

                float radial = distance(positionWS, _FractureOrigin.xyz);
                float wobble = (ValueNoise(positionWS * _NoiseFrequency) - 0.5) * 2.0;
                float fractureValue = radial + wobble * _NoiseStrength;

                // Gone: this piece of the VR world has been removed, so let the compositor
                // through. RGB is irrelevant at alpha 0 but is cleared rather than left as
                // whatever the VR scene happened to draw.
                if (fractureValue < _Threshold)
                    return half4(0.0, 0.0, 0.0, 0.0);

                // Cracked but still there: a thin dark line, no glow. The goal is a crack in
                // the world, not a dissolve effect.
                if (fractureValue < _Threshold + _EdgeWidth)
                    return half4(_EdgeColor.rgb, 1.0);

                // Intact. Leave the live VR pixel exactly as the scene rendered it.
                discard;
                return half4(0.0, 0.0, 0.0, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
