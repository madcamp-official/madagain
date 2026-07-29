// 계조 축소 재질 — 명암을 몇 단계로만 끊어 낸다. (손·펫 거미 전용)
//
// 화면 전체 흑백은 후처리(채도 -100)가 담당하고, 이 셰이더는 그 위에서 <b>손과 거미만</b>
// 불연속으로 만들어 배경과 분리한다. 반전(_Invert)은 이 둘에만 걸린다.
//
// <b>_Levels</b>가 계단 수다. 2면 완전한 흑백 2치, 4~5면 "조금만 불연속"이 된다.
//
// ★ <b>입력 범위(_InBlack/_InWhite)가 핵심이다.</b> 팔처럼 거의 검은 재질은 휘도가 바닥에 몰려 있어
//   그냥 자르면 계단이 안 생기고 통째로 한 색이 된다 — 실제로 처음엔 팔 전체가 흰색이 됐다.
//   흰색점을 낮춰 어두운 구간을 펼친 뒤에 잘라야 한다.
//
// 셰이더 이름은 `OneBit`으로 둔다 — 이미 머티리얼 127개에 물려 있어 바꾸면 전부 다시 교체해야 한다.
//
// 값은 기본적으로 <b>전역 프로퍼티</b>(_OneBit*)로 구동한다 — 머티리얼이 100개가 넘어 개별 조절이
// 불가능하기 때문. OneBitControl 컴포넌트가 슬라이더 하나로 전부를 움직인다.
Shader "MINDHEXER/OneBit"
{
    Properties
    {
        [MainTexture] _BaseMap   ("Base Map", 2D)    = "white" {}
        [MainColor]   _BaseColor ("Base Color", Color) = (1,1,1,1)

        [Toggle] _UseGlobal ("전역 값 사용(패널로 조절)", Float) = 1

        _Levels     ("계단 수(2=완전 흑백)", Range(2,8)) = 4
        _InBlack    ("입력 검정점",          Range(0,1)) = 0
        _InWhite    ("입력 흰색점",          Range(0,1)) = 0.5
        [Toggle] _Invert ("반전",           Float)      = 1
        _Dither     ("디더(계단 경계 흩기)", Range(0,1)) = 0
        _LightWrap  ("라이트 랩(형태 유지)", Range(0,1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            // ★ 포인트·스팟(=additional light)을 받기 위한 키워드. 없으면 손전등이 손·거미에
            //   전혀 안 걸린다 — 디렉셔널을 지운 순간 통째로 검게 죽는다.
            //   _CLUSTER_LIGHT_LOOP은 Forward+ 경로. URP 17에서 이름이 _FORWARD_PLUS에서 바뀌었다.
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4  _BaseColor;
                half   _UseGlobal;
                half   _Levels;
                half   _InBlack;
                half   _InWhite;
                half   _Invert;
                half   _Dither;
                half   _LightWrap;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            // 전역 — CBUFFER 밖에 둬야 SRP Batcher와 싸우지 않는다.
            half _OneBitLevels;
            half _OneBitInBlack;
            half _OneBitInWhite;
            half _OneBitInvert;
            half _OneBitDither;
            half _OneBitLightWrap;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv         = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            // 4x4 오더드(Bayer) 디더 — 흑·백 2색만 쓰면서 계조를 흉내낸다.
            half DitherMask(float2 screenPos)
            {
                int x = (int)fmod(screenPos.x, 4.0);
                int y = (int)fmod(screenPos.y, 4.0);
                float m[16] = { 0,  8,  2, 10,
                               12,  4, 14,  6,
                                3, 11,  1,  9,
                               15,  7, 13,  5 };
                return (m[y * 4 + x] + 0.5) / 16.0;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half levels  = _UseGlobal > 0.5h ? _OneBitLevels    : _Levels;
                half inBlack = _UseGlobal > 0.5h ? _OneBitInBlack   : _InBlack;
                half inWhite = _UseGlobal > 0.5h ? _OneBitInWhite   : _InWhite;
                half invert  = _UseGlobal > 0.5h ? _OneBitInvert    : _Invert;
                half dither  = _UseGlobal > 0.5h ? _OneBitDither    : _Dither;
                half wrap    = _UseGlobal > 0.5h ? _OneBitLightWrap : _LightWrap;

                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb * _BaseColor.rgb;
                float3 N = normalize(IN.normalWS);

                // 조명 — 형태를 남기기 위한 것이지 색을 위한 게 아니다.
                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light main = GetMainLight(shadowCoord);

                // 라이트 랩: 어두운 면이 통째로 검게 죽는 것을 막아 형태를 살린다.
                half ndl = saturate((dot(N, main.direction) + wrap) / (1.0h + wrap));
                half3 light = main.color * (ndl * main.shadowAttenuation);

                // 추가 광원(포인트·스팟). 손전등 리그가 여기로 들어온다.
                // 랩을 메인과 <b>똑같이</b> 적용한다 — 다르게 주면 손전등 안팎에서 손의 계조가 튄다.
                #if defined(_ADDITIONAL_LIGHTS)
                {
                    // 클러스터(Forward+) 경로는 LIGHT_LOOP_BEGIN이 inputData를 직접 참조한다.
                    InputData inputData = (InputData)0;
                    inputData.positionWS = IN.positionWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);

                    uint count = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(count)
                        Light add = GetAdditionalLight(lightIndex, IN.positionWS, half4(1, 1, 1, 1));
                        half addNdl = saturate((dot(N, add.direction) + wrap) / (1.0h + wrap));
                        light += add.color * (addNdl * add.distanceAttenuation * add.shadowAttenuation);
                    LIGHT_LOOP_END
                }
                #endif

                half3 lit = albedo * (SampleSH(N) + light);

                half lum = dot(lit, half3(0.2126h, 0.7152h, 0.0722h));

                // ★ 입력 범위를 먼저 펼친다. 팔처럼 거의 검은 재질은 휘도가 바닥에 몰려 있어
                //   그냥 자르면 계단이 하나도 안 생기고 통째로 한 색이 된다(실제로 전부 흰색이 됐다).
                half t = saturate((lum - inBlack) / max(1e-4h, inWhite - inBlack));

                half steps = max(2.0h, levels);
                half last  = steps - 1.0h;

                // 디더는 계단 하나 폭만큼만 흔든다 — 경계가 딱 떨어지지 않고 섞인다.
                t = saturate(t + (DitherMask(IN.positionCS.xy) - 0.5h) * dither / last);

                // 계조 축소: steps개의 값만 남는다. steps=2면 완전한 흑백 2치.
                half q = saturate(round(t * last) / last);
                q = lerp(q, 1.0h - q, saturate(invert));

                return half4(q, q, q, 1.0h);
            }
            ENDHLSL
        }

        // 그림자·깊이는 URP 표준 패스를 그대로 빌린다(자체 구현할 이유가 없다).
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }

    Fallback "Universal Render Pipeline/Unlit"
}
