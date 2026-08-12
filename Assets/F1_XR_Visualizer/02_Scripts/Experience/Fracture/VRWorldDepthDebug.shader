Shader "F1XR/VRWorldDepthDebug"
{
    // Step 1 of the VR -> MR world-space fracture: prove that world position can be
    // recovered per pixel, per eye, on the device. Nothing is fractured here and no alpha is
    // touched; this only paints the reconstructed world position so it can be judged by eye.
    //
    // Read it like this: the colour of a surface is a function of where that surface is in
    // the VR world, on a repeating grid. Move your head and the pattern must stay welded to
    // the geometry. If it slides, swims, or differs between eyes, the reconstruction is
    // wrong and the fracture field built on top of it would be wrong the same way.
    //
    // Background pixels (no geometry: sky, far plane) are painted flat black rather than
    // reconstructed. Their depth carries no surface, so projecting them into the world would
    // invent a position somewhere out at the far plane and hand the fracture a pixel that
    // has nothing to break.
    Properties
    {
        _GridMetres ("Grid Size (metres)", Range(0.05, 5)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Overlay"
            // Just before the transparent queue, which is where world-space UI lives. At
            // Overlay this covered the debug panel itself, including the button that turns
            // it off. Everything opaque is already drawn by here, so the depth texture is
            // ready and the only thing given up is drawing over transparent VR objects.
            "Queue" = "Transparent-1"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "VRWorldDepthDebug"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend Off
            // RGB only. Framebuffer alpha is what the Meta passthrough compositor reads, and
            // a debug pass must not disturb which world the user is looking at.
            ColorMask RGB

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

            float _GridMetres;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                // The mesh is a clip-space triangle, used directly and never transformed.
                // A quad parented to the camera would not do: the two eye frustums are
                // asymmetric, so a quad sized for one eye leaves a gap in the other.
                OUT.positionHCS = float4(IN.positionOS.xy, UNITY_NEAR_CLIP_VALUE, 1.0);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                // _ScaledScreenParams, not _ScreenParams: with dynamic resolution the render
                // target is only partly used, and the wrong divisor makes the whole image
                // slide as the resolution changes.
                float2 uv = IN.positionHCS.xy / _ScaledScreenParams.xy;

                float rawDepth = SampleSceneDepth(uv);

            #if UNITY_REVERSED_Z
                bool isBackground = rawDepth <= 0.0;
            #else
                bool isBackground = rawDepth >= 1.0;
            #endif

                if (isBackground)
                    return half4(0.0, 0.0, 0.0, 1.0);

                // UNITY_MATRIX_I_VP is the current eye's inverse view-projection under
                // single-pass instanced stereo, so each eye reconstructs its own rays and
                // both land on the same world point.
                float3 positionWS = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);

                half3 grid = frac(positionWS / max(_GridMetres, 0.01));
                return half4(grid, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
