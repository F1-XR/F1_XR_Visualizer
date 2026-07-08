Shader "F1XR/RealWorldDepthOnly"
{
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" }

        Pass
        {
            ZWrite On
            ColorMask 0
        }
    }
}
