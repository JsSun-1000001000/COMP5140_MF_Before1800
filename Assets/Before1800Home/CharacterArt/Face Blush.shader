Shader "Before1800/Mita Face Blush"
{
    Properties
    {
        _MainTex ("Original expression atlas", 2D) = "black" {}
        _Opacity ("Blush strength", Range(0,1)) = 0.55
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            half _Opacity;
            fixed4 frag(v2f_img input) : SV_Target
            {
                half4 ink = tex2D(_MainTex, input.uv);
                // The neutral overlay in the source game is disabled. Its atlas also has
                // pink cheek strokes baked over a translucent black dramatic expression.
                #ifndef UNITY_COLORSPACE_GAMMA
                    ink.rgb = LinearToGammaSpace(ink.rgb);
                #endif
                half pink = ink.r;
                half3 colour = saturate(ink.rgb / max(pink, .001h));
                #ifndef UNITY_COLORSPACE_GAMMA
                    colour = GammaToLinearSpace(colour);
                #endif
                return half4(colour, ink.a * pink * _Opacity);
            }
            ENDCG
        }
    }
}
