// 칼날 클리핑 — 찌르기로 적에 박힌 만큼 칼끝을 잘라 "관통해 안 보이는" 연출.
//   로봇 칼이라 단면이 잘려도 자연스럽다. 실제 거리는 안 바꾸므로 밸런스에 무해(순수 뷰).
//
// 칼날은 오브젝트 로컬 +X 를 따라 뻗어 있다(측정값). _ClipX 이 "손잡이(0)~칼끝(1)" 중
// 어디까지 그릴지를 정한다. 그 너머(칼끝 쪽)는 clip()으로 버린다.
//   _ClipX = 1  → 안 자름(평소)
//   _ClipX = 0.6 → 앞 40% 안 보임(박힌 만큼)
//
// URP Lit 전부를 흉내 내지 않고, 칼에 필요한 것만 담은 경량 Lit(메인 라이트 + 앰비언트 + 이미시브).
Shader "Precog/KatanaClip"
{
    Properties
    {
        _BaseMap        ("Albedo", 2D) = "white" {}
        _BaseColor      ("Color", Color) = (1,1,1,1)
        [Normal]_BumpMap("Normal", 2D) = "bump" {}
        _MetallicGlossMap ("Metallic", 2D) = "white" {}
        _Metallic       ("Metallic", Range(0,1)) = 1
        _Smoothness     ("Smoothness", Range(0,1)) = 0.5
        _EmissionMap    ("Emission", 2D) = "black" {}
        [HDR]_EmissionColor ("Emission Color", Color) = (0,0,0,1)

        // ── 클리핑 ──
        // 칼날 로컬 축(기본 +X). 축이 다르면 (1,0,0)을 (0,1,0)/(0,0,1)로.
        _BladeAxis  ("칼날 축(로컬)", Vector) = (1,0,0,0)
        // 로컬 좌표에서 손잡이 끝·칼끝 끝의 축 성분(측정값으로 자동 세팅되지만 수동도 가능)
        _BladeMin   ("손잡이 축값", Float) = -0.0095
        _BladeMax   ("칼끝 축값",   Float) =  0.0095
        _ClipX      ("그릴 비율(1=전체)", Range(0,1)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);        SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);        SAMPLER(sampler_BumpMap);
            TEXTURE2D(_MetallicGlossMap); SAMPLER(sampler_MetallicGlossMap);
            TEXTURE2D(_EmissionMap);    SAMPLER(sampler_EmissionMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float  _Metallic, _Smoothness;
                float4 _EmissionColor;
                float4 _BladeAxis;
                float  _BladeMin, _BladeMax, _ClipX;
            CBUFFER_END

            struct Attributes { float4 pos:POSITION; float3 nrm:NORMAL; float4 tan:TANGENT; float2 uv:TEXCOORD0; };
            struct Varyings {
                float4 posCS:SV_POSITION; float2 uv:TEXCOORD0;
                float3 posWS:TEXCOORD1; float3 nrmWS:TEXCOORD2; float3 tanWS:TEXCOORD3; float3 bitWS:TEXCOORD4;
                float  bladeT:TEXCOORD5;   // 칼날 축 위 위치(0=손잡이,1=칼끝)
            };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(IN.pos.xyz);
                VertexNormalInputs   n = GetVertexNormalInputs(IN.nrm, IN.tan);
                o.posCS = p.positionCS; o.posWS = p.positionWS;
                o.nrmWS = n.normalWS; o.tanWS = n.tangentWS; o.bitWS = n.bitangentWS;
                o.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                // 오브젝트 로컬에서 칼날 축 성분을 0~1로 정규화
                float axisVal = dot(IN.pos.xyz, normalize(_BladeAxis.xyz));
                o.bladeT = saturate((axisVal - _BladeMin) / max(1e-5, _BladeMax - _BladeMin));
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // ★ 칼끝(bladeT 큰 쪽)을 _ClipX 너머부터 버린다
                clip(_ClipX - IN.bladeT);

                float2 uv = IN.uv;
                half4 baseCol = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;

                float3 nTS = UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv));
                float3x3 tbn = float3x3(normalize(IN.tanWS), normalize(IN.bitWS), normalize(IN.nrmWS));
                float3 N = normalize(mul(nTS, tbn));

                half metallic = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv).r * _Metallic;

                SurfaceData sd = (SurfaceData)0;
                sd.albedo = baseCol.rgb; sd.metallic = metallic; sd.smoothness = _Smoothness;
                sd.occlusion = 1;
                sd.emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, uv).rgb * _EmissionColor.rgb;
                sd.alpha = 1;

                InputData id = (InputData)0;
                id.positionWS = IN.posWS;
                id.normalWS = N;
                id.viewDirectionWS = normalize(GetWorldSpaceViewDir(IN.posWS));
                id.shadowCoord = TransformWorldToShadowCoord(IN.posWS);
                id.bakedGI = SampleSH(N);

                return UniversalFragmentPBR(id, sd);
            }
            ENDHLSL
        }

        // 그림자 캐스터 — 클리핑을 그림자에도 적용해야 잘린 칼끝 그림자가 안 남는다
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ColorMask 0

            HLSLPROGRAM
            #pragma vertex vertSh
            #pragma fragment fragSh
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST; float4 _BaseColor; float _Metallic,_Smoothness;
                float4 _EmissionColor; float4 _BladeAxis; float _BladeMin,_BladeMax,_ClipX;
            CBUFFER_END

            float3 _LightDirection;
            struct A { float4 pos:POSITION; float3 nrm:NORMAL; };
            struct V { float4 posCS:SV_POSITION; float bladeT:TEXCOORD0; };

            V vertSh(A IN)
            {
                V o;
                float3 ws = TransformObjectToWorld(IN.pos.xyz);
                float3 nw = TransformObjectToWorldNormal(IN.nrm);
                o.posCS = TransformWorldToHClip(ApplyShadowBias(ws, nw, _LightDirection));
                #if UNITY_REVERSED_Z
                    o.posCS.z = min(o.posCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    o.posCS.z = max(o.posCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                float axisVal = dot(IN.pos.xyz, normalize(_BladeAxis.xyz));
                o.bladeT = saturate((axisVal - _BladeMin) / max(1e-5, _BladeMax - _BladeMin));
                return o;
            }
            half4 fragSh(V IN):SV_Target { clip(_ClipX - IN.bladeT); return 0; }
            ENDHLSL
        }
    }
}
