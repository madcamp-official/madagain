// 위험 구역 표시 — 흑백 정사각형 노이즈. (기초_설계안 §7)
//
// 플레이어를 죽일 수 있는 것은 <b>경비병 부채꼴</b>과 <b>터렛 사선</b> 둘뿐이고, 둘 다 이 재질
// 하나를 쓴다. 지오메트리만 다르고 보이는 언어는 같다 — "이 격자 위에 서면 죽는다".
//
// ★ <b>격자는 화면 픽셀 기준이다</b>(스크린 스페이스 UV). 월드 UV로 하면 멀리 있는 구역의 격자가
//   잘게 뭉개져 노이즈가 아니라 회색 면으로 보인다. 화면 기준이면 거리와 무관하게 격자 크기가
//   일정해 어디서 봐도 같은 밀도로 읽힌다. §7의 "3D에 붙지만 플레이어 시점에선 2D로 보인다"는
//   해킹 테두리와 같은 규칙이다.
//
// ★ <b>Unlit이다.</b> 조명을 받으면 흑백 2치가 회색으로 무너진다. 위험 표시는 조명 상황과 무관하게
//   항상 같은 대비여야 한다.
//
// 완전 정지시키면 위험해 보이지 않고, 매 프레임 재추첨하면 눈이 아프다 → _Fps로 초당 몇 번만 바꾼다.
Shader "MINDHEXER/DangerNoise"
{
    Properties
    {
        // 4 → 2.4 (3/5). 정사각형 한 변이 작아져 노이즈가 촘촘해진다.
        _CellPixels ("격자 크기(화면 픽셀)", Range(1, 16)) = 2.4
        _Fps        ("재추첨 속도(회/초). 0=정지", Range(0, 60)) = 10
        _Coverage   ("검정 비율", Range(0, 1)) = 0.5
        _White      ("흰색 밝기 — 블룸 문턱보다 낮게 두면 안 번진다", Range(0, 2)) = 1
        _Black      ("검정 밝기", Range(0, 1)) = 0
        _Alpha      ("불투명도", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }

        Pass
        {
            Name "DangerNoise"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off            // 공중에 뜬 부채꼴을 아래에서도 봐야 한다
            Offset -1, -1       // 바닥에 붙여 그리므로 z-파이팅을 밀어낸다

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _CellPixels;
                half _Fps;
                half _Coverage;
                half _White;
                half _Black;
                half _Alpha;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            // 격자 하나당 난수 하나. 시간 항을 정수로 끊어 넣어 "초당 _Fps회만" 바뀐다.
            float Hash(float3 p)
            {
                return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float cellSize = max(1.0, _CellPixels);
                float2 cell = floor(IN.positionCS.xy / cellSize);   // 화면 픽셀 → 격자 좌표
                float tick = _Fps > 0.0h ? floor(_Time.y * _Fps) : 0.0;

                float r = Hash(float3(cell, tick));
                half v = r < _Coverage ? _Black : _White;
                return half4(v, v, v, _Alpha);
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
