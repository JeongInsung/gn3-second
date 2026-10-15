// 전투 화면(오버레이 UI)에 마을 2D 조명 대신 빛 색을 곱하는 막. 아래에 그려진 것 × 정점 색.
// BattleLighting이 시간대·날씨에 맞춰 색을 바꾼다(흰색이면 그대로, 어두울수록 어두워진다).
Shader "GN3/UIMultiply"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "CanUseSpriteAtlas" = "True" }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend DstColor Zero

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return fixed4(i.color.rgb, 1);
            }
            ENDCG
        }
    }
}
