Shader "F1XR/Skybox/Cubemap Blend"
{
    Properties
    {
        _TexA ("Skybox A", Cube) = "" {}
        _TexB ("Skybox B", Cube) = "" {}
        _TintA ("Tint A", Color) = (0.5, 0.5, 0.5, 0.5)
        _TintB ("Tint B", Color) = (0.5, 0.5, 0.5, 0.5)
        _ExposureA ("Exposure A", Range(0, 8)) = 1
        _ExposureB ("Exposure B", Range(0, 8)) = 1
        _Blend ("Blend", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            samplerCUBE _TexA;
            samplerCUBE _TexB;
            half4 _TintA;
            half4 _TintB;
            half _ExposureA;
            half _ExposureB;
            half _Blend;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.direction = v.vertex.xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half3 direction = normalize(i.direction);
                half3 a = texCUBE(_TexA, direction).rgb * _TintA.rgb *
                    unity_ColorSpaceDouble.rgb * _ExposureA;
                half3 b = texCUBE(_TexB, direction).rgb * _TintB.rgb *
                    unity_ColorSpaceDouble.rgb * _ExposureB;
                return half4(lerp(a, b, _Blend), 1);
            }
            ENDCG
        }
    }
}
