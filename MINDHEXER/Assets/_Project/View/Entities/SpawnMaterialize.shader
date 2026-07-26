Shader "Precog/SpawnMaterialize"
{
    // 스폰 실체화 오버레이(설계 §4-⑥). 진짜 몹 위에 겹쳐 렌더되는 '디졸브 복제본' 전용.
    //  · 위에서부터 아래로 훑는 X레이 스캔선(_Reveal 0→1)
    //  · 아직 실체화 안 된 아래쪽은 프레넬 실루엣(반투명 외곽선)
    //  · 이미 지나간 위쪽은 사라져 진짜 몹이 보임
    // 발광처럼 보이도록 가산 블렌드(Blend SrcAlpha One).
    Properties
    {
        _EdgeColor ("스캔선 색(HDR)",   Color) = (3.4, 0.5, 0.35, 1)
        _SilColor  ("실루엣 색(HDR)",   Color) = (1.8, 0.18, 0.12, 1)
        _Reveal    ("실체화 진행",       Range(0,1)) = 0
        _MinY      ("메시 최소Y(로컬)",  Float) = -0.5
        _MaxY      ("메시 최대Y(로컬)",  Float) = 0.5
        _EdgeWidth ("스캔선 두께",       Range(0.01,0.4)) = 0.14
        _SilAlpha  ("가림 불투명도",     Range(0,1)) = 1.0
        _BaseFill  ("정면 채움(0=프레넬만)", Range(0,1)) = 0.55
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+50" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha   // 알파블렌드 — 미실체 부분을 '가린다'(진짜 몹 위에 덮음)
            ZWrite Off
            ZTest LEqual
            Offset -2, -2                     // 진짜 몹과 좌표가 겹쳐 z-fight → 카메라 쪽으로 당겨 확실히 위에 그림
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionOS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _EdgeColor;
                float4 _SilColor;
                float  _Reveal, _MinY, _MaxY, _EdgeWidth, _SilAlpha, _BaseFill;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                o.positionOS  = IN.positionOS.xyz;
                o.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                o.positionWS  = TransformObjectToWorld(IN.positionOS.xyz);
                return o;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float h  = saturate((IN.positionOS.y - _MinY) / max(1e-4, (_MaxY - _MinY))); // 0=바닥 1=위
                float rl = 1.0 - _Reveal;                     // 진행 0→1 이면 경계가 위(1)→아래(0)
                float d  = h - rl;                            // >0 위(이미 실체화·투명) · <0 아래(아직·가림)

                // 가림(alpha): 경계 아래는 불투명(진짜 몹 덮음) → 경계에서 부드럽게 투명으로.
                // 걷히는 경계가 위→아래로 지나가며 진짜 몹이 그 자리부터 드러난다(페이드인).
                float cover = 1.0 - smoothstep(0.0, _EdgeWidth, d);

                float edge = 1.0 - saturate(abs(d) / _EdgeWidth);   // 스캔선
                edge = edge * edge;

                float3 vd  = normalize(_WorldSpaceCameraPos.xyz - IN.positionWS);
                float fres = pow(1.0 - saturate(dot(normalize(IN.normalWS), vd)), 1.4);

                // 가리는 부분 색 = X레이 실루엣(정면 채움 + 프레넬), 경계엔 밝은 스캔선.
                float3 xray = _SilColor.rgb * (_BaseFill + (1.0 - _BaseFill) * fres);
                float3 col  = xray + _EdgeColor.rgb * edge * 1.5;
                // 아래(미실체)는 완전 불투명으로 진짜 몹을 '완전히' 가린다 → 걷히며 처음 등장(페이드인).
                // _SilAlpha는 전체 세기 상한(1이면 완전 가림).
                float  a    = saturate((cover + edge)) * _SilAlpha;
                return half4(col, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
