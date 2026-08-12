Shader "F1XR/ShellFragmentShard"
{
    // VR -> MR: a detached piece that still carries the VR image it broke off with.
    //
    // The piece samples the camera opaque texture, but not at where it is now: it samples at
    // where it *was* when it let go. _OriginalObjectToWorld is the piece's pose at detach,
    // set once per fragment, and the vertex stage projects the vertex through that pose to
    // get the sampling position. So the shard shows the VR pixels it was cut from and keeps
    // showing them as it tumbles away.
    //
    // Why not a snapshot RenderTexture: capturing the XR camera gives one mono image for two
    // eyes, so the debris sits at the wrong depth. Projecting through the eye's own matrices
    // is per-eye correct and costs no capture. It does require Opaque Texture to be on in
    // the URP asset.
    //
    // Alpha 1 always. The piece is solid VR, so passthrough must not show through it; only
    // the fixed hole it left behind is allowed to open onto the real room.
    Properties
    {
        _FallbackColor ("Fallback Colour", Color) = (0.10, 0.11, 0.13, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            // After the fixed reveal holes (Overlay+100), so a shard covering its own hole
            // puts the VR image and alpha 1 back over it.
            "Queue" = "Overlay+101"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ShellShard"
            ZWrite Off
            ZTest Always
            // Pieces are thin and tumble, so both faces have to draw.
            Cull Off
            Blend Off
            ColorMask RGBA

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 sourceSS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Deliberately outside UnityPerMaterial: these are per-fragment values pushed
            // through a MaterialPropertyBlock, which the SRP batcher's constant buffer cannot
            // carry. One shared material, thirty different poses.
            float4x4 _OriginalObjectToWorld;
            float4 _FallbackColor;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);

                // Where this vertex sat on screen at the moment the piece detached.
                float3 sourceWS = mul(_OriginalObjectToWorld, float4(IN.positionOS.xyz, 1.0)).xyz;
                OUT.sourceSS = ComputeScreenPos(TransformWorldToHClip(sourceWS));
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float2 uv = IN.sourceSS.xy / max(IN.sourceSS.w, 1e-5);

                // Behind the head, or off screen: there is no VR pixel to carry, so fall back
                // to a dark chip rather than smearing the edge of the frame.
                bool offScreen = IN.sourceSS.w <= 0.0 ||
                    uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0;

                half3 vr = offScreen
                    ? _FallbackColor.rgb
                    : SampleSceneColor(saturate(uv));

                return half4(vr, 1.0h);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
