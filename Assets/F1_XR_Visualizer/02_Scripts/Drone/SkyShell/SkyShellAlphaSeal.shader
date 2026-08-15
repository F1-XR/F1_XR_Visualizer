Shader "F1XR/SkyShellAlphaSeal"
{
    // Takes the alpha channel away from the scene.
    //
    // Meta passthrough is an underlay: the framebuffer's alpha decides, per pixel, how much of
    // the real room is blended in. Nothing in this project guarantees that channel. The
    // circuit's three hundred glTF materials run through a Shader Graph whose alpha output is
    // the base map's own alpha, and the depth fix sets their alpha blend to One/Zero, so a
    // texel anywhere between the cutoff and one writes that value straight into the
    // framebuffer. Switch the underlay on and the room appears through the track in exactly
    // those places, which is what made the whole circuit look hazy and washed out.
    //
    // So the alpha is overwritten wholesale after everything has drawn, and only then are
    // holes punched back into it. From here on the alpha channel means what this transition
    // says it means, not what a texture author happened to leave in an unused channel.
    //
    // ColorMask A: the colour on screen must not change by a single bit. If entering this
    // state is visible at all, it has failed.
    SubShader
    {
        Tags
        {
            "RenderType" = "Overlay"
            "Queue" = "Overlay"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "SkyShellAlphaSeal"

            ZWrite Off
            ZTest Always
            Cull Off
            Blend One Zero
            ColorMask A

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

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                // Straight to clip space, ignoring every matrix. A quad placed in front of the
                // camera has to be sized against the frustum, and the two eyes have asymmetric
                // frustums - a quad that exactly covers one leaves a wedge uncovered in the
                // other, which on an underlay is a strip of the real room down the edge of one
                // eye. Emitting the clip-space corners covers each eye's whole viewport by
                // construction, whatever the projection is and wherever the head is.
                OUT.positionHCS = float4(IN.positionOS.xy, UNITY_NEAR_CLIP_VALUE, 1.0);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                // Only the alpha lands; ColorMask A discards the rest.
                return half4(0, 0, 0, 1);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
