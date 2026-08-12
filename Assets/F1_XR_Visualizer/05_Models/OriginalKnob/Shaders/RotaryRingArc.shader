Shader "F1XR/RotaryRingArc"
{
    // A single circular arc drawn with a signed-distance field so the ends are naturally round-capped.
    // One shared material drives every arc; per-arc geometry is pushed via MaterialPropertyBlock.
    Properties
    {
        [HDR] _RingColor ("Ring Color", Color) = (1,1,1,1)
        _Radius ("Radius (0..1)", Range(0,1)) = 0.79
        _RadiusOffset ("Radius Offset", Range(-0.3,0.3)) = 0
        _LineWidth ("Line Half-Width", Range(0.0005,0.15)) = 0.013
        _Softness ("Edge Softness", Range(0.0005,0.1)) = 0.006
        _StartAngle ("Start Angle (deg)", Range(0,360)) = 120
        _RotationOffset ("Rotation Offset (deg)", Float) = 0
        _ArcLength ("Arc Length (deg)", Range(0,360)) = 210
        _Intensity ("Emission Intensity", Float) = 1
        _Alpha ("Alpha", Range(0,1)) = 1
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
            Name "ArcAdditive"
            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // Required so the arc renders correctly in XR Single Pass Instanced (both eyes).
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
                float4 _RingColor;
                float _Radius;
                float _RadiusOffset;
                float _LineWidth;
                float _Softness;
                float _StartAngle;
                float _RotationOffset;
                float _ArcLength;
                float _Intensity;
                float _Alpha;
            CBUFFER_END

            float2 Rotate(float2 v, float aDeg)
            {
                float a = radians(aDeg);
                float s = sin(a);
                float c = cos(a);
                return float2(c * v.x - s * v.y, s * v.x + c * v.y);
            }

            // Signed distance to a circular arc centred on +Y, symmetric over the half-aperture.
            // sc = (sin, cos) of the half-aperture. Subtracting rb gives a rounded (capsule) band -> round caps.
            float sdArc(float2 p, float2 sc, float ra, float rb)
            {
                p.x = abs(p.x);
                float k = (sc.y * p.x > sc.x * p.y) ? dot(p, sc) : length(p);
                return sqrt(max(dot(p, p) + ra * ra - 2.0 * ra * k, 0.0)) - rb;
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

                // Quad UVs -> centred coords; r == 1 at the mid-edge of the quad.
                float2 p = (IN.uv - 0.5) * 2.0;

                float arcLen = clamp(_ArcLength, 0.0, 360.0);
                float effStart = _StartAngle + _RotationOffset;
                float centerDeg = effStart + arcLen * 0.5;

                // Rotate so the arc centre lands on +Y, matching sdArc's orientation.
                float2 pr = Rotate(p, 90.0 - centerDeg);

                float halfAp = radians(arcLen * 0.5);
                float2 sc = float2(sin(halfAp), cos(halfAp));
                float ra = _Radius + _RadiusOffset;

                float dist = sdArc(pr, sc, ra, _LineWidth);
                float mask = 1.0 - smoothstep(0.0, _Softness, dist);
                mask *= _Alpha;

                float3 col = _RingColor.rgb * _Intensity;
                return half4(col * mask, mask);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
