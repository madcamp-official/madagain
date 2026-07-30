// 빙의 화면 테두리 — 흰 선 + 검은 가로선 노이즈. (기초_설계안 §6.3)
//
// "내가 이 몸 안에 들어가 있다"를 <b>화면 프레임</b>으로 표현한다. 조종(마리오네트 실)과 달리
// 빙의는 시야 자체가 남의 것이므로, 물체에 붙는 표시가 아니라 <b>화면 가장자리</b>가 신호가 된다.
//
// 모든 값이 <b>화면 픽셀 기준</b>이다 — 해상도가 달라도 같은 굵기로 보이고, FOV가 변해도 흔들리지
// 않는다(전체화면 오버레이라 애초에 3D와 무관).
//
// ★ 흑백만 쓴다. 예전 설계는 '파란 실'이었지만 아트가 흑백으로 통일되면서 흰 선으로 바뀌었다.
//   조종 실(손↔대상)의 파랑은 그대로 남는다 — 두 상태를 색이 아니라 <b>형태</b>로 구분한다.
Shader "MINDHEXER/PossessionFrame"
{
    Properties
    {
        // ★ UI(Image)에 쓰려면 _MainTex가 반드시 있어야 한다. CanvasRenderer가 스프라이트 텍스처를
        //   이 이름으로 밀어넣기 때문에, 없으면 매 프레임
        //   "doesn't have a texture property '_MainTex'" 경고가 쏟아진다.
        //   테두리는 절차적으로 그리므로 값은 쓰지 않는다 — 선언만 해 둔다.
        [HideInInspector] _MainTex ("Sprite Texture (미사용)", 2D) = "white" {}

        _Inset      ("테두리 안쪽 들어간 거리(px). 0=화면 가장자리", Float) = 0
        _Thickness  ("흰 선 두께(px)", Float) = 6
        _Noise      ("검은 가로선 노이즈 세기(0~1 = 검게 지워지는 줄 비율)", Range(0, 1)) = 0.1
        _ScanCell   ("가로선 한 줄의 두께(px)", Range(1, 16)) = 3
        _Fps        ("노이즈 재추첨 속도(회/초)", Range(0, 60)) = 24
        _Opacity    ("전체 불투명도", Range(0, 1)) = 1
        _White      ("흰 선 밝기", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Overlay" "IgnoreProjector"="True" }

        Pass
        {
            Name "PossessionFrame"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Inset;
                float _Thickness;
                half  _Noise;
                half  _ScanCell;
                half  _Fps;
                half  _Opacity;
                half  _White;
            CBUFFER_END

            // 선언만 한다(UI가 요구). 테두리는 화면 좌표로 직접 그리므로 샘플하지 않는다.
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // 화면 가장자리까지의 거리(px). uv가 아니라 픽셀로 재야 가로·세로 두께가 같다.
                float2 px = IN.uv * _ScreenParams.xy;
                float d = min(min(px.x, _ScreenParams.x - px.x),
                              min(px.y, _ScreenParams.y - px.y));

                // _Inset부터 두께만큼이 띠. _Inset=0이면 화면 가장자리에 딱 붙는다.
                float inner = _Inset;
                float outer = _Inset + max(1.0, _Thickness);
                float band = step(inner, d) * step(d, outer);
                if (band < 0.5) return half4(0, 0, 0, 0);

                // 가로선 노이즈 — 줄 단위로 뽑아 일정 비율을 검게 지운다.
                //   _Noise=0 이면 순수 흰 선, 1이면 거의 다 검게 끊긴다.
                float row = floor(px.y / max(1.0, _ScanCell));
                float tick = _Fps > 0.0h ? floor(_Time.y * _Fps) : 0.0;
                float n = Hash(float2(row, tick));

                half lit = n < _Noise ? 0.0h : _White;   // 지워진 줄은 검정
                return half4(lit, lit, lit, _Opacity);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
