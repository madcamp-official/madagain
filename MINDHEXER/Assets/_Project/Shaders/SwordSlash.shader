// 베기 이펙트 — 프로 슬래시 VFX 구조를 따름.
//   형태마스크(부드럽게 번짐) × 노이즈1(스크롤) × 노이즈2(스크롤) → 강도
//   강도로 컬러 램프(녹색 → 노랑 → 흰색) → 가산 + Bloom
//
// 두께감은 기하학이 아니라 "부드러운 마스크 + 곱해진 노이즈 + 다중 레이어"에서 나온다.
// 메시는 정적(칼이 훑고 간 3D 곡면), 움직임은 노이즈 스크롤이 만든다.
//
// uv.x = 스윙 방향, uv.y = 반경(0=손잡이, 1=칼끝)
Shader "Precog/SwordSlash"
{
    Properties
    {
        _ShapeTex ("형태 마스크", 2D) = "white" {}
        _Noise1   ("노이즈 1",    2D) = "gray" {}
        _Noise2   ("노이즈 2",    2D) = "gray" {}

        _NoiseTile1   ("노이즈1 타일(xy)",   Vector) = (2, 1, 0, 0)
        _NoiseScroll1 ("노이즈1 스크롤(xy)", Vector) = (-0.9, 0.15, 0, 0)
        _NoiseTile2   ("노이즈2 타일(xy)",   Vector) = (5, 1.6, 0, 0)
        _NoiseScroll2 ("노이즈2 스크롤(xy)", Vector) = (-1.7, -0.1, 0, 0)
        _NoiseOffset  ("노이즈 오프셋(레이어별)", Vector) = (0, 0, 0, 0)

        _NoiseAmount ("노이즈 영향력(0=매끈한 면, 1=완전 부서짐)", Range(0,1)) = 0.5
        _Contrast  ("대비",   Range(0.2, 6)) = 1.8
        _Intensity ("강도",   Range(0, 8))   = 2.4

        [HDR]_ColorLow  ("낮은 강도(녹색)", Color) = (0.12, 1.0, 0.10, 1)
        [HDR]_ColorMid  ("중간(노랑)",      Color) = (1.6, 1.15, 0.05, 1)
        [HDR]_ColorHigh ("높은 강도(흰색)", Color) = (3.2, 3.2, 2.6, 1)
        _Ramp1 ("램프 1", Range(0,1)) = 0.18
        _Ramp2 ("램프 2", Range(0,1)) = 0.55
        _Ramp3 ("램프 3", Range(0,1)) = 0.85

        _Reveal     ("그어짐 진행도", Range(0,1)) = 1
        _RevealSoft ("선단 부드러움", Range(0.001,1)) = 0.12
        _TailFade   ("꼬리 페이드",   Range(0,1)) = 0.45
        _Fade       ("전체 페이드",   Range(0,1)) = 1

        // ── 합성 방식 ──
        // 가산(One One)만으로는 <b>검정을 칠할 수 없다</b>. 더하기만 하므로 검정=투명이다.
        // 그래서 블렌드 인자를 재질에서 지정할 수 있게 열어두고, 곱셈 모드용 출력도 따로 둔다.
        //   가산   Src=One(1)      Dst=One(1)              — 빛나는 검기
        //   알파   Src=One(1)      Dst=OneMinusSrcAlpha(10) — 프리멀티플라이드. 검정이 검정으로 찍힘
        //   곱셈   Src=DstColor(2) Dst=Zero(0)              — 화면을 어둡게 깎아냄(가장 새까맣다)
        [HideInInspector] _SrcBlend ("Src", Float) = 1
        [HideInInspector] _DstBlend ("Dst", Float) = 1
        [HideInInspector] _MulMode  ("곱셈 모드", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+20" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "Slash"
            Blend [_SrcBlend] [_DstBlend]   // 기본은 가산(One One) — 재질에서 바꾼다
            ZWrite Off
            Cull Off           // 곡면이라 양면
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            TEXTURE2D(_ShapeTex); SAMPLER(sampler_ShapeTex);
            TEXTURE2D(_Noise1);   SAMPLER(sampler_Noise1);
            TEXTURE2D(_Noise2);   SAMPLER(sampler_Noise2);

            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseTile1, _NoiseScroll1, _NoiseTile2, _NoiseScroll2, _NoiseOffset;
                float  _NoiseAmount, _Contrast, _Intensity;
                float4 _ColorLow, _ColorMid, _ColorHigh;
                float  _Ramp1, _Ramp2, _Ramp3;
                float  _Reveal, _RevealSoft, _TailFade, _Fade;
                float  _SrcBlend, _DstBlend, _MulMode;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                o.uv    = IN.uv;
                o.color = IN.color;
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv;
                float t = _Time.y;

                // 형태 마스크 — 경계가 넓게 번져 있어 볼륨감의 바탕
                float shape = SAMPLE_TEXTURE2D(_ShapeTex, sampler_ShapeTex, uv).r;

                // 노이즈 2장을 서로 다른 타일·속도로 흘려서 곱한다 → 너울거리는 결
                float2 uv1 = uv * _NoiseTile1.xy + _NoiseScroll1.xy * t + _NoiseOffset.xy;
                float2 uv2 = uv * _NoiseTile2.xy + _NoiseScroll2.xy * t + _NoiseOffset.zw;
                float n1 = SAMPLE_TEXTURE2D(_Noise1, sampler_Noise1, uv1).r;
                float n2 = SAMPLE_TEXTURE2D(_Noise2, sampler_Noise2, uv2).r;
                // 노이즈는 "면을 부수는" 게 아니라 "결을 얹는" 정도로 — 1(매끈)과 섞는다
                float detail = lerp(1.0, saturate(n1 * n2 * 2.2), _NoiseAmount);

                // 최종 강도
                float m = shape * detail;
                m = pow(saturate(m), _Contrast) * _Intensity;

                // 강도 기반 컬러 램프: 옅은 곳 녹색 → 진한 곳 노랑 → 가장 밝은 심 흰색
                float3 col = lerp(_ColorLow.rgb, _ColorMid.rgb,  smoothstep(_Ramp1, _Ramp2, m));
                col        = lerp(col,           _ColorHigh.rgb, smoothstep(_Ramp2, _Ramp3, m));

                // 스윙 진행 + 꼬리 페이드
                float rev  = saturate((_Reveal - uv.x) / _RevealSoft);
                float tail = smoothstep(0.0, max(_TailFade, 0.001), _Reveal - uv.x);

                float a = saturate(m) * rev * tail * _Fade * IN.color.a;

                // 곱셈 모드 — 화면을 col 쪽으로 깎아낸다. col이 검정이면 새까맣게 눌린다.
                //   a=0 → 1(원본 유지) · a=1 → col
                if (_MulMode > 0.5) return half4(lerp(float3(1,1,1), col, a), 1.0);

                // 그 외는 프리멀티플라이드 출력 — 가산(One One)과 알파(One OneMinusSrcAlpha)를 겸한다.
                //   가산: dst + col*a          (검정 col은 아무 영향 없음 = 투명)
                //   알파: dst*(1-a) + col*a    (검정 col이 검정으로 찍힘)
                return half4(col * a, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
