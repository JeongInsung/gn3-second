// 전투 화면(오버레이 UI)의 그림자: 원본 그림의 실루엣을 검게 칠한다. 기울이기는 BattleShadow(메시 효과)가 한다.
// 마을 GN3/ProjectedShadow와 같이 알파 0.5 미만은 버리고, 그림자끼리 겹친 곳이 두 번 어두워지지 않게
// 스텐실 비트 128로 한 픽셀에 한 번만 칠한다(UI 마스크가 쓰는 아래 비트는 건드리지 않는다).
Shader "GN3/UIProjectedShadow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "CanUseSpriteAtlas" = "True" }

        Stencil
        {
            Ref 128
            ReadMask 128
            WriteMask 128
            Comp NotEqual
            Pass Replace
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

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
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed alpha = tex2D(_MainTex, i.uv).a;
                clip(alpha - 0.5);
                return fixed4(0, 0, 0, i.color.a);
            }
            ENDCG
        }
    }
}
