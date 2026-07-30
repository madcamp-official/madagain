// 화면 덮개 전용 치지직 — ScreenVeil이 쓴다. (사망_부활_연출_설계 §2)
//
// ★ MINDHEXER/HackGlitch와 무엇이 다른가
//   HackGlitch는 월드 좌표로 줄을 만든다(Tripo 메시 UV가 못 쓰는 상태라 그렇게 만들어졌다).
//   그건 월드에 놓인 물체에는 맞지만 카메라에 붙은 쿼드에는 두 가지가 깨진다.
//     1) 법선의 우세축으로 UV 평면을 고르므로, 둘러보면 평면이 Z↔X↔Y로 스왑되며 줄이 기울어진다.
//     2) 쿼드가 월드에서 5~10cm라 220줄/m이면 화면에 11~22줄뿐 — 줄 하나가 화면의 5~9%로 굵어진다.
//   그래서 덮개는 화면 좌표(SV_POSITION)로 줄을 만든다. 카메라가 어딜 보든 항상 정확히 수평이고,
//   밀도가 화면 픽셀 기준이라 얇게 낼 수 있다.
//
// 강도 모델은 HackGlitch와 같게 유지한다 — 강도는 알파를 흐리는 게 아니라 "몇 줄이 켜지는지"를 바꾼다.
// 게임 안에서 두 치지직이 같은 성격으로 읽혀야 하기 때문이다.
Shader "MINDHEXER/VeilGlitch"
{
    Properties
    {
        _GlitchColor    ("치지직 색", Color) = (1, 1, 1, 1)
        _RowCount       ("가로줄 수(화면 높이 전체 기준)", Float) = 220
        _ScrollSpeed    ("줄 갱신 속도(초당 스텝)", Float) = 30
        _TearChance     ("트래킹 에러 확률(0~1)", Range(0,1)) = 0.08
        _LineAlpha      ("켜진 줄의 불투명도", Range(0,1)) = 0.95
        [HideInInspector] _GlitchIntensity ("강도(런타임 세팅)", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Overlay" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _GlitchColor;
                float  _RowCount;
                float  _ScrollSpeed;
                float  _TearChance;
                float  _LineAlpha;
                float  _GlitchIntensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            float hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                if (_GlitchIntensity <= 0.0001) discard;

                // 프래그먼트 단계의 SV_POSITION = 화면 픽셀 좌표. VR 단일 패스에서도 눈별 좌표라 정확하다.
                float2 px = IN.positionCS.xy;
                float2 uv = px / max(_ScreenParams.xy, 1.0);

                // 시간을 스텝으로 끊는다 — 연속적으로 흐르면 '흐르는 무늬'가 되고, 끊으면 '치지직'이 된다.
                float timeStep = floor(_Time.y * _ScrollSpeed);

                float row = floor(uv.y * _RowCount);
                float rowNoise = hash13(float3(row, timeStep, 0.0));

                // 트래킹 에러 — 이따금 그 줄만 가로로 튄다.
                float tear = step(1.0 - _TearChance, hash13(float3(row, timeStep, 5.0)));
                float xShift = tear * (hash13(float3(row, timeStep, 9.0)) - 0.5) * 40.0;
                float fine = hash13(float3(floor(uv.x * 120.0 + xShift), row, timeStep));

                float val = lerp(rowNoise, fine, 0.35);

                // 강도 = 문턱. 낮으면 드문드문, 높으면 촘촘히 켜진다(알파가 아니라 개수가 바뀐다).
                float on = step(val, _GlitchIntensity);
                if (on < 0.5) discard;

                return half4(_GlitchColor.rgb, _LineAlpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
