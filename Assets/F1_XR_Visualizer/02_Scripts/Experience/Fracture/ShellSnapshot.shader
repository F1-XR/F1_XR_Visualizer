Shader "F1XR/ShellSnapshot"
{
    // VR -> MR: a shell fragment carrying a frozen piece of the VR view.
    //
    // At the moment the return starts, the VR camera is captured once into a RenderTexture.
    // Each fragment's mesh UVs are baked to the screen position that fragment occupied at
    // capture time, so sampling _SnapshotTex at those UVs shows exactly the VR pixels that
    // were there. The UVs are frozen, so the piece keeps its VR appearance as it moves.
    //
    // Output alpha comes from _Alpha (per fragment, via MaterialPropertyBlock). While the
    // fragment is present it is opaque and hides the passthrough behind it. Wall and floor
    // pieces keep alpha 1 until their fall completes; only ceiling pieces fade in place.
    Properties
    {
        _SnapshotTex ("VR Snapshot", 2D) = "black" {}
        _Alpha ("Alpha", Range(0,1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Snapshot"
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // Single-pass instanced stereo support, so both eyes render correctly on Quest.
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_SnapshotTex);
            SAMPLER(sampler_SnapshotTex);
            float _Alpha;

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;   // baked screen-space UV
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                half3 vr = SAMPLE_TEXTURE2D(_SnapshotTex, sampler_SnapshotTex, IN.uv).rgb;
                return half4(vr, _Alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
