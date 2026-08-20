Shader "F1XR/SkyShellIntact"
{
    // The black of virtual space, as geometry rather than as a clear colour.
    //
    // Drawn in the Background queue with depth writes off, so it behaves exactly like a
    // skybox: everything else in the scene paints over it no matter how far away that
    // something actually is. That is the whole point. The shell is a twenty metre sphere
    // around the viewer's head while the circuit around it is a kilometre across, so if this
    // were ordinary opaque geometry the sphere would cut through the track and read as a
    // curtain being drawn in front of the world - which is exactly what it did.
    //
    // Alpha is forced to 1. The camera clears to alpha 0 so the Meta passthrough underlay is
    // live underneath the whole time; this surface is the only thing hiding the real room.
    // Remove a piece of it and the room is simply there, with no state to change and no
    // composition layer to bring back at the moment it is needed.
    //
    // Unlit on purpose. Intact, this must be indistinguishable from the flat background it
    // replaces; a lit surface picks up ambient and a light direction and immediately reads as
    // a large object surrounding the viewer.
    Properties
    {
        // Plain Color, never [HDR]. An HDR colour property skips the gamma to linear
        // conversion, while Camera.backgroundColor does not - so the same three numbers came
        // out about thirteen times brighter here than in the clear they were copied from, and
        // the sky read as navy against a near-black background instead of being invisible
        // against it.
        _BaseColor ("Base Color", Color) = (0.015, 0.02, 0.04, 1)
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

            // Off, so the track and everything else passes its own depth test against a
            // cleared buffer and draws on top.
            ZWrite Off
            ZTest LEqual
            Cull Back
            Blend One Zero
            ColorMask RGBA

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
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                // Alpha 1, always. This is what keeps the real room hidden while the shell is
                // whole, and dropping it to anything less lets passthrough bleed through the
                // sky in the middle of the flight.
                return half4(_BaseColor.rgb, 1);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
