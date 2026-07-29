// 해킹 대상 "치지직" 오버레이 — 기존 머티리얼은 안 건드리고 렌더러의 추가 머티리얼 슬롯으로 덧그린다.
// UV를 쓰지 않는다(Tripo 메시 UV가 못 쓰는 상태 — TriplanarGrunge와 같은 이유). 대신 월드 좌표로 패턴을 만든다.
//
// 노이즈는 "가로줄 밴딩" 방식(VHS/CRT 스캔 노이즈의 표준 기법) — 블록 스태틱이 아니라
// 가로 줄(row) 단위로 밝기가 바뀌고, 이따금 그 줄의 미세 노이즈가 옆으로 튀는 "트래킹 에러"를 섞는다.
//
// 방사형 플레어·블룸은 셰이더로 직접 그리지 않는다 — 조준(3D) 모드에서 색을 HDR(1.0 초과)로 내보내
// 프로젝트에 이미 있는 URP Bloom 포스트프로세싱이 자연스럽게 번지게 만든다(EnemyGlow의 MPB+HDR 방식과 동일).
//
// 2D→3D 전환: 림 글로우·팝 변위·플리커·HDR 부스트는 전부 뺐다(눈아프다는 피드백 — 원인이었다).
// 대신 가로선 자체를 좌우로 흔들어 "꼬불꼬불"하게 왜곡하는 단일 신호만 쓴다.
// 진폭·속도를 3D일 때만 키워서(_Wave3DMult) 2D보다 더 격렬하게 일렁이도록 구분한다.
//
// 강도 = 밀도(선 두께·불투명도는 항상 고정, "몇 개가 켜져 있는지"만 강도로 바꾼다).
// val(0~1, row/wave/scanline이 합쳐진 노이즈장)을 강도와 비교해 그 지점을 켤지/끌지 이진 판정한다
// (onMask = val < _GlitchIntensity). 강도가 낮으면 문턱을 넘는 지점이 적어 선이 드문드문,
// 강도가 높으면 더 많이 켜진다 — 강도가 알파를 흐리는 게 아니라 개수를 바꾼다.
// val의 확률분포상 강도가 아무리 낮아도 그 비율만큼은 자연히 켜지므로 최소치가 저절로 체감된다.
//
// _GlitchIntensity(0~1)·_GlitchMode(0=2D/1=3D)는 HackableGlitchManager가 인스턴스별로 MPB로 넣는다.
Shader "MINDHEXER/HackGlitch"
{
    Properties
    {
        _GlitchColor    ("치지직 색(인광 톤)", Color) = (0.3, 1.4, 0.5, 1)
        _RowCount       ("가로줄 밀도(화면비 기준)", Float) = 220
        _ScrollSpeed    ("줄 갱신 속도(초당 스텝)", Float) = 18
        _TearChance     ("트래킹 에러 확률(0~1)", Range(0,1)) = 0.06

        _WaveAmp        ("가로선 꼬불거림 진폭(uv 단위)", Float) = 0.03
        _WaveFreq       ("꼬불거림 공간 주파수(1/m)", Float) = 6
        _WaveSpeed      ("꼬불거림 속도", Float) = 2.5
        _Wave3DMult     ("3D(조준) 시 진폭·속도 배율", Float) = 1.6

        _LineBrightness ("켜진 선의 밝기(강도와 무관하게 고정)", Float) = 1.0
        _LineAlpha      ("켜진 선의 불투명도(강도와 무관하게 고정)", Range(0,1)) = 0.9

        [HideInInspector] _GlitchIntensity ("강도(런타임 세팅)", Range(0,1)) = 0
        [HideInInspector] _GlitchMode      ("모드(런타임 세팅) 0=2D 1=3D", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        Pass
        {
            Name "GlitchOverlay"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha   // 프리멀티플라이 — HDR 색이 밝아도 알파와 무관하게 값 그대로 더해짐
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // SRP Batcher 호환 — 전 인스턴스가 같은 CBUFFER 레이아웃을 가져야 MPB가 배치를 안 깬다.
            CBUFFER_START(UnityPerMaterial)
                half4 _GlitchColor;
                float _RowCount;
                float _ScrollSpeed;
                float _TearChance;
                float _WaveAmp;
                float _WaveFreq;
                float _WaveSpeed;
                float _Wave3DMult;
                float _LineBrightness;
                float _LineAlpha;
                float _GlitchIntensity;
                float _GlitchMode;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // 해시 기반 값 노이즈 — 텍스처 샘플러 불필요(UV 없는 메시라도 동작).
            float hash13(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nrmWS = TransformObjectToWorldNormal(IN.normalOS);

                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.positionWS = posWS;
                OUT.normalWS = nrmWS;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                if (_GlitchIntensity <= 0.001) discard;

                bool intense = _GlitchMode > 0.5;

                // 표면 좌표 대용(UV 없음) — 법선이 가장 강한 축을 눌러 평평한 면에서 줄이 곧게 보이게 한다.
                float3 an = abs(IN.normalWS);
                float2 uv = (an.y >= an.x && an.y >= an.z) ? IN.positionWS.xz
                          : (an.x >= an.z)                  ? IN.positionWS.zy
                                                              : IN.positionWS.xy;

                float t = _Time.y * _ScrollSpeed;
                float timeStep = floor(t);

                // 가로선을 좌우로 흔들어 꼬불꼬불하게 왜곡 — 2D는 무조건 직선(웨이브 0), 3D(조준)만 흔든다.
                float waveMul = intense ? _Wave3DMult : 0.0;
                float wave = sin(uv.x * _WaveFreq + _Time.y * _WaveSpeed * waveMul) * _WaveAmp * waveMul;
                float2 wuv = float2(uv.x, uv.y + wave);

                // ── 가로줄 밴딩(VHS 표준 기법): 줄(row) 하나당 랜덤 밝기 하나. 세로로 훑으면 줄무늬. ──
                float row = floor(wuv.y * _RowCount);
                float rowNoise = hash13(float3(row, timeStep, 0.0));

                // ── 트래킹 에러: 이따금 그 줄의 미세 노이즈가 가로로 튄다(줄이 옆으로 밀리는 느낌). ──
                float tear = step(1.0 - _TearChance, hash13(float3(row, timeStep, 5.0)));
                float xShift = tear * (hash13(float3(row, timeStep, 9.0)) - 0.5) * 40.0;
                float fine = hash13(float3(floor(wuv.x * 60.0 + xShift), row, timeStep));

                float val = lerp(rowNoise, fine, 0.35);

                // 얇은 밝은 스캔 헤어라인 하나가 위에서 아래로 스크롤 — 화면 전체가 아니라 표면 로컬 기준.
                float scanline = 1.0 - smoothstep(0.0, 0.02, abs(frac(wuv.y * 6.0 - t * 0.5) - 0.5) - 0.48);
                val = saturate(val + scanline * 0.5);

                // 강도는 밀도로만 쓴다 — val이 강도보다 작은 지점만 "켜짐". 켜진 지점의 밝기·알파는 고정.
                if (val >= _GlitchIntensity) discard;

                // 프리멀티플라이(Blend One OneMinusSrcAlpha) — 알파를 색에도 곱해야 알파가 실제로
                // 투명도로 작동한다. 안 곱하면 알파를 낮춰도 덧셈(One)은 그대로라 안 흐려진다.
                half3 col = _GlitchColor.rgb * _LineBrightness;
                return half4(col * _LineAlpha, _LineAlpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
