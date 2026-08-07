Shader "F1XR/PanelSelectBorderUI"
{
    // 판넬(월드 스페이스 Canvas) 위에 덮어 그리는 테두리. 선택되면 빨간 빛 한 점이 테두리를 따라
    // 계속 돕니다. _Size 는 판넬 RectTransform 크기(px)로, 스크립트가 매 프레임 넣어 줍니다.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _EmissionColor ("Border Color", Color) = (1, 0.06, 0.03, 1)
        _Size ("Rect Size (x,y)", Vector) = (100, 100, 0, 0)
        _Radius ("Corner Radius (same unit as Size)", Float) = 12
        _Thickness ("Thickness (same unit as Size)", Float) = 2
        _Speed ("Chase Speed (laps/sec)", Float) = 0.6
        _Tail ("Comet Tail (0..1 of perimeter)", Range(0.01, 1)) = 0.18
        _Intensity ("Emission Intensity", Float) = 3
        _BaseAlpha ("Idle Border Alpha", Range(0, 1)) = 0.18
        _Amount ("Select Amount", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "PreviewType"="Plane"
        }

        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha One   // 가산 합성 - 빛나는 테두리

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            half4 _EmissionColor;
            float4 _Size;
            float _Radius;
            float _Thickness;
            float _Speed;
            float _Tail;
            float _Intensity;
            float _BaseAlpha;
            float _Amount;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float2 sz = max(_Size.xy, 1e-4);
                float2 p = i.uv * sz;

                // 둥근 사각형 SDF. 판넬이 9-slice 라운드 코너라, 직각 테두리로 그리면 모서리가 뜬다.
                float2 halfSize = sz * 0.5;
                float radius = clamp(_Radius, 0.0, min(halfSize.x, halfSize.y));
                float2 q = abs(p - halfSize) - (halfSize - radius);
                float sd = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius; // <0 = 안쪽
                float inset = -sd;                                                 // 테두리까지 남은 거리

                float aa = fwidth(inset) + 1e-5;
                float ring = (1.0 - smoothstep(_Thickness - aa, _Thickness, inset))
                           * smoothstep(-aa, aa, inset);   // 모양 바깥은 잘라낸다
                if (ring <= 0.001)
                    discard;

                // 둘레 좌표는 라운드 전 사각형 기준으로 잡는다(코너에서 아주 살짝 빨라질 뿐 눈에 안 띔).
                float dl = p.x, dr = sz.x - p.x, db = p.y, dt = sz.y - p.y;
                float m = min(min(dl, dr), min(db, dt));

                // 테두리를 한 바퀴 도는 0..1 좌표(아래 -> 오른쪽 -> 위 -> 왼쪽).
                float perim = 2.0 * (sz.x + sz.y);
                float s;
                if (m == db)      s = p.x;
                else if (m == dr) s = sz.x + p.y;
                else if (m == dt) s = sz.x + sz.y + (sz.x - p.x);
                else              s = 2.0 * sz.x + sz.y + (sz.y - p.y);
                float t = s / perim;

                float head = frac(_Time.y * _Speed);
                float behind = frac(t - head);              // 머리 바로 뒤일수록 0
                float comet = exp(-behind / max(_Tail, 1e-3));

                half3 col = _EmissionColor.rgb * _Intensity * (0.35 + comet);
                half a = saturate(ring * (_BaseAlpha + comet) * _Amount) * _EmissionColor.a * i.color.a;
                return half4(col, a);
            }
            ENDCG
        }
    }
}
