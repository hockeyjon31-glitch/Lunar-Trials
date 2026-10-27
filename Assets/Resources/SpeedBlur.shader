Shader "Hidden/SpeedBlur"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Amount;
            int _Taps;
            float _MaxStrength;

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
                // Radial speed streak: drag the frame outward from the screen centre. The rim
                // (ground flying by at the frame edges) streaks hardest, the centre (where the
                // buggy sits) stays cleanest - the classic boost look.
                float2 centre = _ScreenParams.xy * 0.5;
                float2 disp = i.uv * _ScreenParams.xy - centre;
                float dist = length(disp);
                float2 dir = dist > 0.0001 ? disp / dist : float2(1.0, 0.0);
                float edge = dist / (min(_ScreenParams.x, _ScreenParams.y) * 0.5);
                float rim = saturate(edge);

                int taps = max(2, _Taps);
                float strength = _Amount * _MaxStrength * (0.22 + 0.78 * rim);
                float2 stepUV = -dir * _MainTex_TexelSize.xy * (strength / (float)(taps - 1));

                fixed4 col = tex2D(_MainTex, i.uv);
                float2 uv = i.uv;
                for (int t = 1; t < taps; t++)
                {
                    uv += stepUV;
                    col += tex2D(_MainTex, uv);
                }
                return col / (float)taps;
            }
            ENDCG
        }
    }
    Fallback Off
}