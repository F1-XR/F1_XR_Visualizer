Shader "F1XR/ShellPassthroughMask"
{
    // MR -> VR: a shell fragment as a passthrough mask, not a colour.
    //
    // The fragment writes depth so it occludes the VR environment behind it, and outputs
    // alpha 0 so the Meta passthrough compositor shows the real room at those pixels
    // (the project's passthrough layer blends on SourceAlpha). Where a fragment sits the
    // user sees MR; where a fragment has fallen away the VR environment shows through.
    //
    // _Alpha is driven per fragment by a MaterialPropertyBlock so a piece can still fade:
    // as _Alpha rises above 0 the piece starts occluding less / letting VR bleed in, and
    // the fade-out reads as the MR chunk dissolving.
    Properties
    {
        _Alpha ("Mask Alpha (0 = full passthrough)", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry-1" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "PassthroughMask"
            // Write depth to occlude VR behind the shell. Draw both sides: fragments are
            // thin and rotate.
            ZWrite On
            ZTest LEqual
            Cull Off
            // Keep the eye-buffer alpha low so passthrough shows here.
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // Required so both eyes render correctly under single-pass instanced stereo on
            // Quest. Without it the shader draws to one eye and the views mismatch.
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

            float _Alpha;

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                // Colour is irrelevant; only the alpha matters to the compositor. Alpha 0
                // = passthrough shows; a fade raises it so the chunk dissolves.
                return half4(0.0, 0.0, 0.0, _Alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
