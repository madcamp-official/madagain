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

        // ★ 전역값 세트 선택 — 손·거미(플레이어)와 해킹 대상은 서로 다른 대비가 필요하다.
        //   플레이어 팔은 거의 검은 재질이고 해킹 대상은 밝은 금속이라, 한 세트로 맞추면 한쪽이 죽는다.
        [Toggle] _HackSet ("해킹 대상 세트 사용(끄면 플레이어 세트)", Float) = 0

        // ★ 고정 키 조명 — 손·거미 전용. 켜면 <b>씬 조명과 환경광을 전혀 읽지 않고</b>
        //   카메라 기준으로 고정된 키 방향 하나만으로 음영을 만든다.
        //   왜: 손은 스테이지마다 환경광이 달라(스튜디오 0.212 vs 스테이지 0.04, 5배) 같은 손이
        //   전혀 다르게 보였다. 밝은 쪽에서는 흰색으로 날아가며 AI 생성 메시의 결함이 드러난다.
        //   키를 뷰 공간에 고정하면 고개를 돌려도 스테이지가 바뀌어도 음영이 변하지 않는다.
        [Toggle] _FixedLight ("고정 키 조명(씬 조명 무시)", Float) = 0

        _Levels     ("계단 수(2=완전 흑백)", Range(2,8)) = 4
        _InBlack    ("입력 검정점",          Range(0,1)) = 0
        _InWhite    ("입력 흰색점",          Range(0,1)) = 0.5
        [Toggle] _Invert ("반전",           Float)      = 0
        _Dither     ("디더(계단 경계 흩기)", Range(0,1)) = 0
        _LightWrap  ("라이트 랩(형태 유지)", Range(0,1)) = 0.35

        // ★★★ 비활성화됨(사용자 지시) — 씬 조명과 무관하게 밝아지는 게 원치 않는 동작이었다
        //     (작업용 조명을 꺼도 안 어두워짐). 슬라이더는 남겨 두되 프로퍼티 기본값을 0으로 낮춰
        //     새로 만드는 재질이 이 영향을 안 받게 한다. 실제 강제는 OneBitControl.Apply()가 한다
        //     (전역값을 매 프레임 0으로 덮어씀) — 되돌리려면 그쪽 강제만 풀면 된다.
        _AmbientFloor ("자체 밝기 바닥 (비활성화됨)", Range(0,2)) = 0

        // ★ 금속(스페큘러 워크플로) 재질을 위한 보조 입력.
        //   금속은 자기 색이 거의 없어 알베도를 검정으로 두고 형태를 스페큘러 맵에 담는다
        //   (실측: probe base 의 _BaseMap 표준편차 0.0000, _SpecGlossMap 표준편차 0.1148).
        //   알베도만 읽으면 그런 재질은 통째로 검정이 된다 — 실제로 그랬다.
        //   기본값을 "black"으로 둬야 스페큘러 맵이 없는 재질(플레이어 손 등)이 영향을 안 받는다.
        //   ⚠️ "white"로 두면 맵 없는 재질이 전부 하얗게 날아간다.
        _SpecGlossMap ("스페큘러 맵(형태 보조)", 2D) = "black" {}
        _SpecWeight   ("스페큘러 가중", Range(0,2)) = 1
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
                half   _HackSet;
                half   _Levels;
                half   _InBlack;
                half   _InWhite;
                half   _Invert;
                half   _Dither;
                half   _LightWrap;
                half   _AmbientFloor;
                half   _SpecWeight;
                half   _FixedLight;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            TEXTURE2D(_SpecGlossMap);
            SAMPLER(sampler_SpecGlossMap);

            // 전역 — CBUFFER 밖에 둬야 SRP Batcher와 싸우지 않는다.
            // 두 세트: 접두사 없음 = 플레이어(손·거미), H = 해킹 대상.
            half _OneBitLevels;
            half _OneBitInBlack;
            half _OneBitInWhite;
            half _OneBitInvert;
            half _OneBitDither;
            half _OneBitLightWrap;
            half _OneBitAmbient;

            // 고정 키 조명(플레이어 세트 전용). 방향은 <b>뷰 공간</b>이다 — 월드로 두면 고개를 돌릴 때
            // 손의 음영이 돌아 "일정하게 보인다"가 깨진다.
            half3 _OneBitKeyDirVS;
            half  _OneBitKeyIntensity;
            half  _OneBitKeyFloor;      // 키를 등진 면의 최소 밝기. 0이면 반대편이 완전히 죽는다.

            half _OneBitHLevels;
            half _OneBitHInBlack;
            half _OneBitHInWhite;
            half _OneBitHInvert;
            half _OneBitHDither;
            half _OneBitHLightWrap;
            half _OneBitHAmbient;

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
                // 어느 전역 세트를 읽을지 먼저 고른다(플레이어 / 해킹 대상).
                bool hackSet = _HackSet > 0.5h;
                half gLevels   = hackSet ? _OneBitHLevels    : _OneBitLevels;
                half gInBlack  = hackSet ? _OneBitHInBlack   : _OneBitInBlack;
                half gInWhite  = hackSet ? _OneBitHInWhite   : _OneBitInWhite;
                half gInvert   = hackSet ? _OneBitHInvert    : _OneBitInvert;
                half gDither   = hackSet ? _OneBitHDither    : _OneBitDither;
                half gWrap     = hackSet ? _OneBitHLightWrap : _OneBitLightWrap;
                half gAmbient  = hackSet ? _OneBitHAmbient   : _OneBitAmbient;

                half levels  = _UseGlobal > 0.5h ? gLevels  : _Levels;
                half inBlack = _UseGlobal > 0.5h ? gInBlack : _InBlack;
                half inWhite = _UseGlobal > 0.5h ? gInWhite : _InWhite;
                half invert  = _UseGlobal > 0.5h ? gInvert  : _Invert;
                half dither  = _UseGlobal > 0.5h ? gDither  : _Dither;
                half wrap    = _UseGlobal > 0.5h ? gWrap    : _LightWrap;
                half ambient = _UseGlobal > 0.5h ? gAmbient : _AmbientFloor;

                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb * _BaseColor.rgb;

                // ★ 금속 재질은 알베도가 검정이고 형태가 스페큘러 맵에 있다. 둘 중 밝은 쪽을 쓴다.
                //   · 알베도가 있는 재질(플레이어 손): 스페큘러 맵이 없어 검정이므로 albedo가 이긴다
                //   · 금속 재질(probe base): 알베도가 0이므로 스페큘러가 이긴다
                //   더하지 않고 max를 쓰는 이유 — 둘 다 있는 재질에서 밝기가 두 배로 뜨지 않게.
                half3 spec = SAMPLE_TEXTURE2D(_SpecGlossMap, sampler_SpecGlossMap, IN.uv).rgb * _SpecWeight;
                albedo = max(albedo, spec);
                float3 N = normalize(IN.normalWS);

                // ★ 고정 키 조명 — 씬 조명·환경광을 <b>전혀 읽지 않는다</b>.
                //   키 방향이 뷰 공간이라 카메라를 돌려도 음영이 따라 돌지 않는다. 스테이지가 바뀌어도,
                //   손전등을 켜도, PC든 VR이든 손은 항상 같은 밝기로 보인다.
                //   (손전등은 원래도 cullingMask로 뷰모델을 제외하고 있어 잃는 것이 없다.)
                half3 lit;
                if (_FixedLight > 0.5h)
                {
                    // 뷰 공간 키 → 월드. UNITY_MATRIX_I_V의 회전부만 쓴다.
                    float3 keyW = normalize(mul((float3x3)UNITY_MATRIX_I_V, normalize(_OneBitKeyDirVS)));
                    half   k    = saturate((dot(N, keyW) + wrap) / (1.0h + wrap));
                    lit = albedo * (_OneBitKeyFloor + k * _OneBitKeyIntensity);
                }
                else
                {

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

                // ★ ambient(자체 밝기 바닥)를 더한다 — 씬에 라이트가 없어도 알베도의 명암이 그대로
                //   휘도로 올라오므로 형태가 보인다. 이게 없으면 환경광(예: 0.04)만 남아 휘도가 0.02
                //   근처에 깔리고, inWhite를 아무리 내려도 통째로 검정이 된다(실제로 겪음).
                //   조명은 '형태를 더하는' 역할이 되고, 보일지 말지는 이 값이 보장한다.
                lit = albedo * (SampleSH(N) + ambient + light);

                }   // else — 씬 조명 경로 끝

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
