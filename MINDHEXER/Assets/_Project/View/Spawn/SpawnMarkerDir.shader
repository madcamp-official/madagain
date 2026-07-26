Shader "Precog/SpawnMarkerDir"
{
    // 스폰 마커 큐브의 "몹 나오는 방향"을 큐브 표면에 직접 칠하는 URP 언릿 셰이더.
    // 로컬 +Z = forward = 출구 방향. 오브젝트 공간 좌표 기준이라 회전·스케일에 무관하게 정확.
    //  - 옆/위/아래 면: 뒤(어둡게) → 앞(밝게) 그라데이션 → 밝은 쪽이 항상 forward.
    //  - 앞면(+Z 노멀): 강조색으로 채움 → "이 면이 출구".
    Properties
    {
        _BackColor  ("뒤 색",          Color) = (0.10, 0.10, 0.13, 1)
        _FrontColor ("앞 색(출구방향)", Color) = (0.05, 0.85, 1.00, 1)
        _FaceColor  ("앞면 강조",       Color) = (1.00, 0.82, 0.10, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Unlit"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionOS  : TEXCOORD0;
                float3 normalOS    : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BackColor;
                float4 _FrontColor;
                float4 _FaceColor;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.positionOS  = IN.positionOS.xyz;   // 단위큐브 = ±0.5 (스케일 무관)
                OUT.normalOS    = IN.normalOS;         // 면별 축정렬 노멀 (±X/±Y/±Z)
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // 로컬 z: -0.5(뒤) ~ +0.5(앞). t=1이 forward.
                float t = saturate(IN.positionOS.z + 0.5);
                half3 col = lerp(_BackColor.rgb, _FrontColor.rgb, t);

                // 앞면(+Z 노멀)은 강조색 = 몹 출구 방향을 확실히 표시
                if (IN.normalOS.z > 0.5)
                    col = _FaceColor.rgb;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}
