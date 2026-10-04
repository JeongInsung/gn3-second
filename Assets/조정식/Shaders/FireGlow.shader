// 화덕 불꽃: 불꽃 픽셀 마스크 안에서 위로 흘러가는 노이즈로 도트마다 밝기·색이 일렁이는 가산 빛.
// 노이즈를 텍셀 격자(도트) 단위로 계산해 부드럽게 번지지 않고 도트답게 반짝인다. 1을 넘는 밝기는 Bloom으로 번진다.
// _FireTime·_Intensity 는 ForgeFire.cs 가, _ForgeUV(x=불꽃 아래 v, y=불꽃 높이 v)는 빌더가 넣는다.
Shader "GN3/FireGlow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Fire Mask", 2D) = "white" {}
        _HotColor ("Hot (bottom)", Color) = (1, 0.85, 0.35, 1)
        _CoolColor ("Cool (top)", Color) = (1, 0.3, 0.08, 1)
        _Intensity ("Intensity", Float) = 1
        _FireTime ("Fire Time", Float) = 0
        _ForgeUV ("Forge UV (bottom, height)", Vector) = (0, 1, 0, 0)
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
            float4 _MainTex_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                float4 _HotColor;
                float4 _CoolColor;
                float _Intensity;
                float _FireTime;
                float4 _ForgeUV;
            CBUFFER_END

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash(i), b = Hash(i + float2(1, 0)), c = Hash(i + float2(0, 1)), d = Hash(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

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
                if (mask < 0.5h) return 0;

                // 도트(텍셀) 단위 좌표. 노이즈가 위로 흘러가 불길이 올라가는 것처럼 보인다.
                float2 cell = floor(input.uv * _MainTex_TexelSize.zw);
                float n = ValueNoise(float2(cell.x * 0.45, cell.y * 0.35 - _FireTime * 3.2)) * 0.65
                        + ValueNoise(float2(cell.x * 0.9 + 17.0, cell.y * 0.8 - _FireTime * 6.5)) * 0.35;

                // 불꽃 아래(뜨거운 노랑) → 위(붉은 주황). 노이즈가 높은 도트는 더 뜨겁게.
                float h = saturate((input.uv.y - _ForgeUV.x) / max(_ForgeUV.y, 1e-4));
                float heat = saturate(1.0 - h * 0.9 + (n - 0.5) * 0.8);
                float3 color = lerp(_CoolColor.rgb, _HotColor.rgb, heat);
                float brightness = 0.35 + n * 0.9;
                return half4(color * brightness * _Intensity, 0);
            }
            ENDHLSL
        }
    }
}
