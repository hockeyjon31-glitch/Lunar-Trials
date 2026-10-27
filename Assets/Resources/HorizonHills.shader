Shader "Hidden/HorizonHills"
{
    // A flat unlit shader for the distant horizon hill ring. Deliberately ignores Unity's
    // distance fog: the playfield's exponential fog would erase the ring entirely long before
    // the camera's far plane, so instead the ring fades to a per-world haze colour in its own
    // vertex-stage distance term. Colour comes from baked vertex colours (slope shading times
    // the world regolith tint) so the range reads as rolling hills rather than a flat band.
    Properties
    {
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _HazeColor ("Haze", Color) = (0.04, 0.04, 0.06, 1)
        _HazeStart ("Haze Start", Float) = 1400
        _HazeEnd ("Haze End", Float) = 2600
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Off ZWrite On ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float haze : TEXCOORD0;
            };

            fixed4 _Color;
            fixed4 _HazeColor;
            float _HazeStart;
            float _HazeEnd;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color * _Color;

                float3 viewPos = UnityObjectToViewPos(v.vertex);
                float dist = length(viewPos);
                o.haze = saturate((dist - _HazeStart) / max(1.0, _HazeEnd - _HazeStart));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed3 c = lerp(i.color.rgb, _HazeColor.rgb, i.haze);
                return fixed4(c, 1);
            }
            ENDCG
        }
    }
    Fallback Off
}
