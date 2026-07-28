// 빙의 시야 부채꼴 마스크 — 픽셀마다 시선 방향을 눈(eye) 로컬로 변환해 좌우/상하 각도를 재고,
// 허용 범위 안이면 discard(그대로 보임), 밖이면 단색으로 덮는다.
//
// 지오메트리로 가리지 않으므로 대상 모델 스케일·near clip·주변 지형에 전혀 영향받지 않는다.
// ZTest Always + Overlay 큐라 항상 맨 위에 그려진다.
Shader "MINDHEXER/ViewConeMask"
{
    Properties
    {
        _Color     ("Color", Color) = (0.15, 0.85, 0.25, 1)
        _PanRange  ("Pan Range (deg)", Float) = 45
        _TiltRange ("Tilt Range (deg)", Float) = 45
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Overlay" }

        Pass
        {
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4   _Color;
            float    _PanRange;
            float    _TiltRange;
            float4x4 _EyeWorldToLocal;   // 눈의 world→local 행렬(ViewConeMask.cs가 매 프레임 넣음)

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // 이 픽셀이 향하는 시선 방향(쿼드가 카메라 바로 앞에 있으므로 곧 뷰 레이)
                float3 dirWS = normalize(IN.positionWS - _WorldSpaceCameraPos);
                float3 d = normalize(mul((float3x3)_EyeWorldToLocal, dirWS));

                float pan  = degrees(atan2(d.x, d.z));                    // 좌우
                float tilt = degrees(asin(clamp(d.y, -1.0, 1.0)));        // 상하

                if (abs(pan) <= _PanRange && abs(tilt) <= _TiltRange)
                    discard;                                             // 시야 안 — 그대로 보임

                return _Color;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
