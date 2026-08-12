Shader "F1XR/ShellPassthroughReveal"
{
    // VR -> MR spatial reveal. The live VR colour remains untouched. Each detached
    // world-space Voronoi cell runs late and overwrites only framebuffer alpha, allowing
    // the Meta Passthrough underlay to appear through that exact projected cell.
    Properties
    {
        _OutputAlpha ("Output Alpha", Range(0,1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Overlay+100"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "SpatialPassthroughReveal"
            ZWrite Off
            // LEqual, not Always. The fragment that came out of this hole is real opaque
            // geometry lifted slightly towards the viewer, so while it still covers its own
            // hole it wins the depth test and the hole stays shut. Always would punch alpha 0
            // straight through the fragment and make the piece look transparent, and it would
            // also open holes through walls the user cannot see past.
            ZTest LEqual
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

            float _OutputAlpha;

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
                return half4(0, 0, 0, saturate(_OutputAlpha));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
