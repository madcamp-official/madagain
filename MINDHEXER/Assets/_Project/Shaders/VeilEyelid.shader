// 눈꺼풀 마스크 — ScreenVeil이 쓴다. (사망_부활_연출_설계 §3)
//
// 레퍼런스: Unreal 포럼 "Waking Up effect in First Person"의 결론이 카메라 움직임이 아니라
// **화면 마스크**였다 — 반경 그라디언트(spheremask) 비네트를 좁게 닫아 두고 값을 애니메이션해
// 원이 넓어지게 한다. Skyrim·RE7류의 "눈을 뜬다"가 전부 이 계열이다.
//
// 여기서는 원이 아니라 **가로로 눌린 타원**을 쓴다. 진짜 눈꺼풀은 위아래에서 닫히므로,
// 닫힐수록 세로만 좁아져 가로 슬릿이 되어야 한다. 완전히 닫히면 화면이 검다.
//
// _Open  0 = 완전히 감음, 1 = 완전히 뜸
// _Vignette  다 뜬 뒤에도 남는 코너 어둠(0이면 없음) — 아직 정신이 덜 든 느낌의 여운
Shader "MINDHEXER/VeilEyelid"
{
    Properties
    {
        _Open      ("뜬 정도(0 감음 ~ 1 뜸)", Range(0,1)) = 1
        _Vignette  ("잔여 코너 어둠", Range(0,1)) = 0
        _Feather   ("가장자리 부드러움", Range(0.01,1)) = 0.35
        _AspectBias("가로로 넓은 정도(눈 모양)", Float) = 0.8
        _Color     ("마스크 색", Color) = (0,0,0,1)
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
                float  _Open;
                float  _Vignette;
                float  _Feather;
                float  _AspectBias;
                float4 _Color;
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

            half4 frag(Varyings IN) : SV_Target
            {
                // 화면 좌표 기준 — 카메라가 어딜 보든 눈꺼풀은 화면 위아래에서 닫힌다.
                float2 uv = IN.positionCS.xy / max(_ScreenParams.xy, 1.0);
                float2 c = uv * 2.0 - 1.0;   // -1..1, 화면 중앙이 0

                // 세로 개구부: 감을수록(_Open→0) 분모가 작아져 값이 커진다 = 위아래가 빨리 검어진다.
                // 완전히 감으면 화면 전체가 마스크 밖이 되어 검정.
                float open = max(_Open, 1e-3);
                float v = c.y / open;

                // 가로는 덜 좁힌다 — 눈은 가로로 넓다. 다 떠도 코너는 조금 남아 비네트가 된다.
                float h = c.x * _AspectBias;

                float r = length(float2(h, v));

                // 다 떴을 때 코너가 얼마나 어두울지. _Vignette=0이면 코너까지 완전히 열린다.
                float edge = lerp(1.45, 1.0, saturate(_Vignette));

                float a = smoothstep(edge - _Feather, edge, r);
                if (a <= 0.001) discard;

                return half4(_Color.rgb, a * _Color.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
