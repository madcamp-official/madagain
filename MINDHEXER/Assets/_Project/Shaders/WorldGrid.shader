Shader "Game/WorldGrid"
{
    // 월드 좌표 1m 격자 + 각 큐브(오브젝트)의 모서리 외곽선.
    // 격자는 거리 감각, 외곽선은 블록 경계 가독성. 둘 다 화면 해상도 기반 안티에일리어싱.
    // 블록아웃 보조 머티리얼(에디터). 삼면(triplanar) 라인 + 오브젝트공간 모서리.
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.30, 0.32, 0.38, 1)
        _LineColor ("Grid Line Color", Color) = (0.55, 0.62, 0.75, 1)
        _EdgeColor ("Edge Color", Color) = (0.0, 0.0, 0.0, 1)
        _GridSize  ("Grid Size (m)", Float) = 1.0
        _LineWidth ("Grid Line Width (m)", Float) = 0.025
        _EdgePixels("Edge Width (px)", Float) = 3.0
        [Toggle(_EDGE_FROM_UV)] _EdgeFromUV ("모서리 외곽선: UV2 사용(결합 메시)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma shader_feature_local _EDGE_FROM_UV
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // edgeUV(TEXCOORD2): 결합 메시가 박스별 단위좌표(±0.5)를 넣어줌. 프리미티브는 미사용.
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float3 edgeUV : TEXCOORD2; };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 positionOS  : TEXCOORD2;   // 모서리 외곽선용(단위큐브 면이 ±0.5)
                float3 edgeUV      : TEXCOORD3;   // 결합 메시 외곽선용(박스별 ±0.5)
            };

            float4 _BaseColor;
            float4 _LineColor;
            float4 _EdgeColor;
            float  _GridSize;
            float  _LineWidth;
            float  _EdgePixels;

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS  = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionOS  = IN.positionOS.xyz;
                OUT.edgeUV      = IN.edgeUV;
                return OUT;
            }

            // 월드 격자: 한 축 좌표가 칸 경계에 얼마나 가까운지 → 0(선) ~ 1(칸 내부)
            float LineMask(float coord)
            {
                float g = max(_GridSize, 0.001);
                float distToLine = abs(frac(coord / g) - 0.5) * g;
                float halfCell = g * 0.5 - _LineWidth;
                float aa = fwidth(coord) + 1e-4;
                return smoothstep(halfCell - aa, halfCell + aa, distToLine);
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float3 p = IN.positionWS;
                float3 n = abs(normalize(IN.normalWS));

                // ── 월드 격자 (삼면 투영) ──
                float gx = min(LineMask(p.y), LineMask(p.z));
                float gy = min(LineMask(p.x), LineMask(p.z));
                float gz = min(LineMask(p.x), LineMask(p.y));
                float inside = gx * n.x + gy * n.y + gz * n.z;
                float gridLine = 1.0 - saturate(inside);

                // ── 오브젝트 모서리 외곽선 ──
                // 단위큐브 면은 |os|=0.5. 각 축의 0.5까지 거리 중 "두 번째로 작은 값"이 0에 가까우면
                // = 두 면이 만나는 모서리. fwidth로 화면 일정 두께.
                // 결합 메시는 positionOS가 ±0.5 밖(스케일 구움)이라 그대로 쓰면 표면 전체를 모서리로
                // 오판 → _EDGE_FROM_UV면 박스별 단위좌표(edgeUV)를 대신 써서 판마다 외곽선을 낸다.
                #ifdef _EDGE_FROM_UV
                float3 eos = IN.edgeUV;
                #else
                float3 eos = IN.positionOS;
                #endif
                float3 dE = 0.5 - abs(eos);
                float mn  = min(dE.x, min(dE.y, dE.z));
                float mx  = max(dE.x, max(dE.y, dE.z));
                float mid = dE.x + dE.y + dE.z - mn - mx;   // 두 번째로 작은 값 = 모서리 근접도
                float aaE = fwidth(mid) * max(_EdgePixels, 0.1) + 1e-5;
                float edge = 1.0 - smoothstep(0.0, aaE, mid);

                // 합성: 바탕 → 격자선 → 모서리(최상위)
                float3 col = lerp(_BaseColor.rgb, _LineColor.rgb, gridLine);
                col = lerp(col, _EdgeColor.rgb, edge);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}
