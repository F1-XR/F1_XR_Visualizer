Shader "F1XR/NeonFlowRibbon"
{
    // A constant-width neon ribbon drawn along a CPU-generated polyline (see PlayPanelBuilder).
    // UV.x carries the normalised arc-length (0..1 head->tail), UV.y goes across the ribbon (0..1).
    // A 6-stop gradient (cyan -> green -> yellow -> orange -> pink -> purple, wrapping) is scrolled
    // along the arc-length to give the slow "flow". Additive blending so it reads as emissive neon
    // over the dark glass panel. Per-instance glow / flow phase are pushed via MaterialPropertyBlock.
    Properties
    {
        [HDR] _Col0 ("Stop 0 (cyan)",   Color) = (0.00, 1.00, 1.00, 1)
        [HDR] _Col1 ("Stop 1 (green)",  Color) = (0.10, 1.00, 0.35, 1)
        [HDR] _Col2 ("Stop 2 (yellow)", Color) = (1.00, 0.92, 0.10, 1)
        [HDR] _Col3 ("Stop 3 (orange)", Color) = (1.00, 0.48, 0.05, 1)
        [HDR] _Col4 ("Stop 4 (pink)",   Color) = (1.00, 0.20, 0.60, 1)
        [HDR] _Col5 ("Stop 5 (purple)", Color) = (0.60, 0.12, 1.00, 1)

        _Repeat ("Gradient repeats along path", Float) = 1
        _FlowSpeed ("Flow speed", Float) = 0.06
        _PhaseOffset ("Phase offset (path join continuity)", Float) = 0
        _Glow ("Glow intensity", Float) = 1
        _EdgeSoftness ("Edge softness (0..1)", Range(0.02, 1.0)) = 0.5
        _CapSoftness ("End-cap softness (arc-len)", Range(0.0, 0.2)) = 0.03
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "NeonAdditive"
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
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

            CBUFFER_START(UnityPerMaterial)
                float4 _Col0; float4 _Col1; float4 _Col2;
                float4 _Col3; float4 _Col4; float4 _Col5;
                float _Repeat;
                float _FlowSpeed;
                float _PhaseOffset;
                float _Glow;
                float _EdgeSoftness;
                float _CapSoftness;
            CBUFFER_END

            // Repeating 6-stop ramp; f wraps in [0,1). Stop 5 blends back into stop 0.
            float3 Ramp(float f)
            {
                f = frac(f) * 6.0;
                int i = (int)floor(f);
                float t = f - i;
                float3 a, b;
                if      (i == 0) { a = _Col0.rgb; b = _Col1.rgb; }
                else if (i == 1) { a = _Col1.rgb; b = _Col2.rgb; }
                else if (i == 2) { a = _Col2.rgb; b = _Col3.rgb; }
                else if (i == 3) { a = _Col3.rgb; b = _Col4.rgb; }
                else if (i == 4) { a = _Col4.rgb; b = _Col5.rgb; }
                else             { a = _Col5.rgb; b = _Col0.rgb; }
                return lerp(a, b, t);
            }

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                // Across-ribbon soft edge (round tube look): 1 at centre -> 0 at edges.
                float d = abs(IN.uv.y - 0.5) * 2.0;
                float edge = 1.0 - smoothstep(1.0 - _EdgeSoftness, 1.0, d);

                // Soft round caps at the two open ends of the path.
                float cap = smoothstep(0.0, _CapSoftness, IN.uv.x) *
                            smoothstep(0.0, _CapSoftness, 1.0 - IN.uv.x);

                float mask = edge * cap;

                float flow = IN.uv.x * _Repeat - _Time.y * _FlowSpeed + _PhaseOffset;
                float3 col = Ramp(flow) * _Glow;

                return half4(col * mask, mask);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
