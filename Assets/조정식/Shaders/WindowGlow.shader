// 창 유리 마스크(알파)만 따뜻한 빛으로 더한다(가산). 밤 계수·깜빡임은 WindowGlow.cs 가 _Intensity 로 넣는다.
// 조명을 받지 않는 언릿이라 주변이 어두워도 창만 밝게 보이고, 1을 넘으면 Bloom 이 번지게 한다.
Shader "GN3/WindowGlow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Window Mask", 2D) = "white" {}
        _GlowColor ("Glow Color", Color) = (1, 0.75, 0.4, 1)
        _Intensity ("Intensity", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Blend One One
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
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _GlowColor;
                float _Intensity;
            CBUFFER_END

            Varyings vert (Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                half mask = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                return half4(_GlowColor.rgb * (_Intensity * mask), 0);
            }
            ENDHLSL
        }
    }
}
