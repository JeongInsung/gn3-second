// 스프라이트 모양(알파)만 따서 한 가지 색으로 칠하는 실루엣. 조명을 받지 않는다.
// VillageWanderer가 파츠 뒤에 8방향으로 살짝 밀어 그려 캐릭터 바깥 테두리(호버 표시)를 만든다.
// Resources에 있어 Resources.Load<Shader>("SpriteSilhouette")로 불러오고 빌드에도 포함된다.
Shader "GN3/SpriteSilhouette"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END

            Varyings vert (Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color; // SpriteRenderer.color (숨쉬는 밝기에 쓴다)
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                clip(alpha - 0.5h);
                return half4(_Color.rgb * input.color.rgb, _Color.a * input.color.a);
            }
            ENDHLSL
        }
    }
}
