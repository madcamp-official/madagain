Shader "Game/RadialInvert"
{
    // 예측 정지(F) 진입: 화면 중심에서 퍼지는 원 안쪽을 맵 윤곽선 사이버 스페이스로 전환.
    // RadialInvertFeature(URP RendererFeature, AfterRenderingPostProcessing)의 passMaterial로 연결.
    // _Radius/_Softness는 PredictionController가 진입 진행률에 맞춰 매 프레임
    // RadialInvertFx.SetRadius로 갱신한다(Assets/_Project/View/Prediction/RadialInvertFeature.cs).
    //
    // [2026-07-22] 색을 HLSL에 박아두지 않고 프로퍼티로 뺐다 — 톤을 바꿀 때 셰이더를 건드리지
    // 않고 머티리얼에서 조절하기 위함. 머티리얼에 오버라이드가 없으면 아래 기본값이 쓰인다.
    //
    // 흰색으로 바꿔봤다가 네온 그린으로 되돌렸고(2026-07-22), 대신 <b>대비</b>를 키웠다 —
    // 윤곽선/링 세기를 올리고 바탕을 원래보다 더 어둡게 눌러서 선만 도드라지게 한다.
    // 원래 값: Line 2.5 / Base(0.005,0.035,0.02) / BaseLum(0.015,0.09,0.045).
    Properties
    {
        _Radius   ("반경 (0=중심점, 1.35=전체)", Range(0, 1.5)) = 0
        _Softness ("경계 부드러움", Range(0.001, 0.3)) = 0.08
        [HDR] _LineColor ("윤곽선 색", Color) = (0.12, 1, 0.45, 1)
        [HDR] _RingColor ("확산 링 색", Color) = (0.25, 1, 0.65, 1)
        _BaseColor ("바탕 색(어두운 쪽)", Color) = (0.002, 0.012, 0.006, 1)
        _BaseLumColor ("바탕 밝기 반영 색", Color) = (0.005, 0.03, 0.015, 1)
        _LineGain ("윤곽선 세기", Range(0.5, 8)) = 4.4
        _RingGain ("링 세기", Range(0.5, 8)) = 2.8
        _EdgeSharp ("윤곽선 예민도 (높을수록 선이 많이 잡힘)", Range(1, 8)) = 4.2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "RadialInvert"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Radius;
            float _Softness;
            half4 _LineColor;
            half4 _RingColor;
            half4 _BaseColor;
            half4 _BaseLumColor;
            float _LineGain;
            float _RingGain;
            float _EdgeSharp;

            float GetLum(float2 uv)
            {
                half4 c = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                return dot(c.rgb, half3(0.2126, 0.7152, 0.0722));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 col = FragBlit(input, sampler_LinearClamp);

                if (_Radius <= 0.001)
                    return col;

                // 화면비 보정: 넓은 화면에서 원이 타원으로 찌그러지는 것을 방지
                float2 uv = input.texcoord - 0.5;
                uv.x *= _ScreenParams.x / _ScreenParams.y;
                float dist = length(uv);

                // _Radius가 작을 때 화면 중앙에 마스크 잔여 원이 남아 떠오르지 않도록 안전 보정
                float innerRad = max(0.0, _Radius - _Softness);
                float outerRad = _Radius + _Softness;
                float mask = 1.0 - smoothstep(innerRad, outerRad, dist);
                if (_Radius < _Softness)
                {
                    mask *= saturate(_Radius / _Softness);
                }

                if (mask <= 0.001)
                    return col;

                // 1. Sobel 필터 기반 맵 전체 윤곽선(Edge Detection) 추출
                float2 texelSize = _ScreenParams.zw - 1.0;
                float2 offset = texelSize * 1.25;

                float l_tl = GetLum(input.texcoord + float2(-offset.x, -offset.y));
                float l_tr = GetLum(input.texcoord + float2( offset.x, -offset.y));
                float l_bl = GetLum(input.texcoord + float2(-offset.x,  offset.y));
                float l_br = GetLum(input.texcoord + float2( offset.x,  offset.y));
                float l_l  = GetLum(input.texcoord + float2(-offset.x,  0.0));
                float l_r  = GetLum(input.texcoord + float2( offset.x,  0.0));
                float l_t  = GetLum(input.texcoord + float2( 0.0,       -offset.y));
                float l_b  = GetLum(input.texcoord + float2( 0.0,        offset.y));

                float gx = (-1.0 * l_tl) + (1.0 * l_tr) + (-2.0 * l_l) + (2.0 * l_r) + (-1.0 * l_bl) + (1.0 * l_br);
                float gy = (-1.0 * l_tl) + (-2.0 * l_t) + (-1.0 * l_tr) + (1.0 * l_bl) + (2.0 * l_b) + (1.0 * l_br);
                float edge = saturate(sqrt(gx * gx + gy * gy) * _EdgeSharp);

                // 2. 어두운 사이버 스페이스 바탕 (Dark Cyber Base)
                float centerLum = dot(col.rgb, half3(0.2126, 0.7152, 0.0722));
                half3 cyberBase = _BaseColor.rgb + centerLum * _BaseLumColor.rgb;

                // 3. 맵 윤곽선을 발광하는 네온 그린 사인으로 전환
                half3 outline = _LineColor.rgb * edge * _LineGain;
                half3 cyberSpace = cyberBase + outline;

                // 4. 충격파 링 (Shockwave Ring Pulse) & 스캔라인 효과
                float ringMask = smoothstep(_Radius - _Softness * 3.0, _Radius, dist)
                               - smoothstep(_Radius, _Radius + _Softness * 0.8, dist);
                half3 ringPulse = _RingColor.rgb * _RingGain * ringMask;

                float scanline = 0.95 + 0.05 * sin(input.texcoord.y * _ScreenParams.y * 2.0);
                cyberSpace *= scanline;

                half3 finalColor = lerp(col.rgb, cyberSpace + ringPulse, mask);
                return half4(finalColor, col.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}



