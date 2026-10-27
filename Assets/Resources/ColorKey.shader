Shader "Hidden/ColorKey"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Cutoff ("Black cutoff", Range(0, 1)) = 0.04
        _Softness ("Black softness", Range(0.0001, 0.5)) = 0.05
    }
    SubShader
    {
        // A flat sky card carries the black surround its artwork was saved on, which reads as a
        // hard-edged rectangle against anything that is not the exact same black. Punched out here
        // instead, so the sky behind shows through. Cull off keeps a card readable from behind and
        // ZWrite off lets nearer terrain occlude it on the depth test rather than fighting it.
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" }
        Cull Off ZWrite Off ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Color;
            float _Cutoff;
            float _Softness;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv);

                // Alpha keyed off luminance, so only the flat black surround fades away and
                // anything actually drawn keeps full opacity. The soft band doubles as a feather
                // for the antialiased rim, which replaces the hard circular cut a black-backed
                // image would otherwise leave.
                float lum = dot(col.rgb, float3(0.299, 0.587, 0.114));
                float keyed = saturate((lum - _Cutoff) / max(_Softness, 0.0001));

                return fixed4(col.rgb * _Color.rgb, col.a * _Color.a * keyed);
            }
            ENDCG
        }
    }
    Fallback Off
}
