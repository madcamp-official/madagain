Shader "Game/EnemyOutline"
{
    // 인버티드 헐(inverted hull) 윤곽선 — 메시를 노멀 방향으로 살짝 부풀리고 앞면을 컬링해서
    // 실루엣 가장자리에만 단색이 보이게 한다. 런지 타깃 강조(흰 테두리)에 쓴다.
    // 스킨드 메시 복제 렌더러에 이 머티리얼을 물려 켜고 끈다(EnemyLungeOutline.cs).
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1,1,1,1)
        _OutlineWidth ("Outline Width (world meters)", Range(0, 0.2)) = 0.01
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+1" }

        Pass
        {
            Name "Outline"
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float  _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                // 월드 공간에서 노멀 방향으로 _OutlineWidth(미터)만큼 부풀린다 — 모델 임포트
                // 스케일이 제각각(0.008~0.6배)이라 오브젝트 공간으로 하면 두께가 들쭉날쭉하다.
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nWS   = normalize(TransformObjectToWorldNormal(IN.normalOS));
                posWS += nWS * _OutlineWidth;
                OUT.positionHCS = TransformWorldToHClip(posWS);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
