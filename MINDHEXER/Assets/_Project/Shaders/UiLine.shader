// 절차적 UI 메시 전용 — 정점 색 그대로 찍는 언릿.
//
// <b>왜 전용 셰이더인가</b> — 이 UI는 선과 점뿐이고 텍스처도 조명도 없다. 정점 색만 통과시키면
// 테두리·연결선·패턴선·점을 <b>메시 하나·드로우콜 하나</b>로 전부 그릴 수 있다.
//
// ★ <b>ZTest Always</b> — HUD는 벽 뒤에 가려지면 안 된다. 다만 <b>후처리보다는 앞에서</b> 그려져야
//   한다(투명 큐라 그렇다) — 그래야 흑백/흑빨 그레이딩을 UI도 함께 탄다. ScreenSpaceOverlay였다면
//   후처리 뒤에 합성돼 배경이 빨개져도 UI만 흰색으로 남는다.
Shader "MINDHEXER/UiLine"
{
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+100" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "UiLine"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color * _Color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return IN.color;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
