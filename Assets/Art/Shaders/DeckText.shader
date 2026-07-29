// Text painted on world geometry — depth-tested, so things standing on it occlude it.
//
// Unity's built-in font material (Font.material, shader "GUI/Text Shader") is ZTest Always. That is
// correct for its intended job: a floating name tag or a debug overlay should be readable through
// whatever is in front of it. It is exactly wrong for a number painted on the floor, which is why
// the tier numbers showed THROUGH the creatures standing on them.
//
// Otherwise identical to the built-in: unlit, alpha from the font atlas, vertex colour times _Color
// so TextMesh.color still works.
Shader "DinoBattle/DeckText"
{
    Properties
    {
        _MainTex ("Font Texture", 2D) = "white" {}
        _Color ("Text Color", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }

        Lighting Off
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = i.color;

                // The font atlas carries the glyph in alpha only.
                col.a *= tex2D(_MainTex, i.texcoord).a;
                return col;
            }
            ENDCG
        }
    }

    Fallback "GUI/Text Shader"
}
