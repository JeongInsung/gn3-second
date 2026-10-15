// 물체 그림(스프라이트)을 발밑 바닥선을 축으로 눕혀 그리는 그림자.
// URP 2D 조명 그림자는 빛이 닿는 끝까지 무한히 늘어나 길이를 못 정해서, 이 방식으로 시간별 길이/방향을 직접 정한다.
// _GN3ShadowDir / _GN3ShadowAlpha 는 DayNightCycle 이 전역으로, _BaseY 는 ProjectedShadow 가 물체별로 넣는다.
Shader "GN3/ProjectedShadow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _BaseY ("Base Y (world)", Float) = 0
        _HeightScale ("Height Scale", Float) = 1
        _Lift ("Lift Height (world)", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        // 그림자끼리 겹친 곳이 두 번 어두워지지 않게 한 픽셀에 한 번만 그린다.
        Stencil
        {
            Ref 1
            Comp NotEqual
            Pass Replace
        }

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
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float _BaseY;
                float _HeightScale; // 그림 세로 중 실제 높이 비율(3/4뷰라 건물 그림의 절반은 지붕)
                float _Lift;        // 위에서 본 납작한 물체(벤치·분수)의 실제 높이: 기울이지 않고 통째로 이만큼 민다
            CBUFFER_END

            float4 _GN3ShadowDir;   // xy: 실제 높이 1당 그림자가 뻗는 지면 벡터 = cot(태양고도) × 해 반대 방향
            float _GN3ShadowAlpha;  // 0이면 그림자 없음(밤)

            Varyings vert (Attributes input)
            {
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                // 기준선 아래 픽셀은 반대쪽으로 뻗어 본체와 어긋나서, 제자리(본체 밑)에 둔다.
                float above = max(world.y - _BaseY, 0.0);
                float height = above * _HeightScale + _Lift;
                // 제자리에서 높이만큼 해 반대쪽으로 민다. 발밑 줄(높이 0)은 본체와 같은 자리라 그림자가 본체 테두리에서 이어져 나온다.
                // (예전엔 선 물체를 발밑 선 위로 눕혀서 집 그림자가 옆면이 아니라 집 앞 잔디에 띠로 깔려 떨어져 보였다)
                // 납작한 물체(_HeightScale = 0, 위에서 본 벤치·울타리)는 _Lift만큼 통째로 밀리고, 빈틈은 ProjectedShadow의 사본이 메운다.
                world.xy += height * _GN3ShadowDir.xy;

                Varyings output;
                output.positionCS = TransformWorldToHClip(world);
                output.uv = input.uv;
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                half alpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                clip(alpha - 0.5h);
                return half4(0, 0, 0, _GN3ShadowAlpha);
            }
            ENDHLSL
        }
    }
}
