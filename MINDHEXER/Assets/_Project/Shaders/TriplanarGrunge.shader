// 폐공장 절차 재질 — UV를 쓰지 않는다.
//
// 왜: Tripo 생성 메시의 UV가 면적 없이 찌부러져 있어(UV면적:표면적 = 1:7900) 어떤 텍스처도
//     붙일 수 없다. 그래서 텍스처 대신 월드 좌표 기반 절차 노이즈로 질감을 만든다.
//     UV가 없어도 되고, 모델이 바뀌어도 그대로 쓰며, 전 기계가 이 셰이더 하나를 공유한다.
//
// 구성: fbm 그런지(명도) + 높이 기반 녹 + 윗면 먼지 + 아랫면 캐비티 + 절차 요철(노멀 퍼터베이션)
// 흑백 아트 방향에 맞춰 색이 아니라 명도·러프니스로만 표현한다.
Shader "MINDHEXER/TriplanarGrunge"
{
    Properties
    {
        [Header(Base)]
        _BaseColor      ("기본 색(회색)", Color) = (0.55, 0.55, 0.55, 1)
        _Metallic       ("Metallic", Range(0,1)) = 0.85
        _Smoothness     ("Smoothness(깨끗한 금속)", Range(0,1)) = 0.55

        [Header(Grunge)]
        _NoiseScale     ("그런지 크기(월드 m)", Float) = 1.2
        _GrungeAmount   ("그런지 세기", Range(0,1)) = 0.6
        _GrungeValue    ("그런지 명도", Range(0,1)) = 0.35
        _GrungeRough    ("그런지 거칠기", Range(0,1)) = 0.85

        [Header(Rust by height)]
        _RustHeight     ("녹 시작 높이(월드 Y)", Float) = 4.0
        _RustFade       ("녹 페이드 범위", Float) = 5.0
        _RustAmount     ("녹 세기", Range(0,1)) = 0.7

        [Header(Dust and Cavity)]
        _DustAmount     ("윗면 먼지 세기", Range(0,1)) = 0.5
        _DustValue      ("먼지 명도", Range(0,1)) = 0.78
        _CavityAmount   ("아랫면 어두움", Range(0,1)) = 0.4

        [Header(Surface detail)]
        _BumpStrength   ("절차 요철 세기", Range(0,2)) = 0.6
        _BumpScale      ("요철 크기(클수록 잘게)", Float) = 4.0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // SRP Batcher 호환: 모든 패스가 동일한 CBUFFER를 가져야 한다.
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float  _Metallic;
            float  _Smoothness;
            float  _NoiseScale;
            float  _GrungeAmount;
            float  _GrungeValue;
            float  _GrungeRough;
            float  _RustHeight;
            float  _RustFade;
            float  _RustAmount;
            float  _DustAmount;
            float  _DustValue;
            float  _CavityAmount;
            float  _BumpStrength;
            float  _BumpScale;
        CBUFFER_END

        // 해시 기반 3D 값 노이즈. 텍스처를 쓰지 않으므로 샘플러가 필요 없다.
        float hash13(float3 p)
        {
            p = frac(p * 0.3183099 + 0.1);
            p *= 17.0;
            return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }

        float vnoise(float3 x)
        {
            float3 i = floor(x);
            float3 f = frac(x);
            f = f * f * (3.0 - 2.0 * f);          // smoothstep 보간
            float n000 = hash13(i + float3(0,0,0));
            float n100 = hash13(i + float3(1,0,0));
            float n010 = hash13(i + float3(0,1,0));
            float n110 = hash13(i + float3(1,1,0));
            float n001 = hash13(i + float3(0,0,1));
            float n101 = hash13(i + float3(1,0,1));
            float n011 = hash13(i + float3(0,1,1));
            float n111 = hash13(i + float3(1,1,1));
            return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                        lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
        }

        // 3옥타브면 모바일에서도 감당 가능하고 질감은 충분히 나온다.
        float fbm3(float3 p)
        {
            float s = 0.0, a = 0.5;
            s += a * vnoise(p); p *= 2.03; a *= 0.5;
            s += a * vnoise(p); p *= 2.01; a *= 0.5;
            s += a * vnoise(p);
            return s / 0.875;                      // 대략 0~1로 정규화
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  fogCoord   : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   nrm = GetVertexNormalInputs(IN.normalOS);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS   = nrm.normalWS;
                OUT.fogCoord   = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float3 posW = IN.positionWS;
                float3 N    = normalize(IN.normalWS);

                // --- 절차 요철: 노이즈 기울기로 노멀을 흔든다(탄젠트 불필요) ---
                float  e  = 0.06;
                float3 bp = posW * max(0.01, _BumpScale) * 0.25;
                float  h0 = fbm3(bp);
                float3 grad = float3(fbm3(bp + float3(e,0,0)) - h0,
                                     fbm3(bp + float3(0,e,0)) - h0,
                                     fbm3(bp + float3(0,0,e)) - h0) / e;
                float3 tangentGrad = grad - N * dot(grad, N);   // 표면 접선 성분만
                N = normalize(N - tangentGrad * _BumpStrength * 0.12);

                // --- 그런지(때·얼룩) ---
                float g      = fbm3(posW / max(0.01, _NoiseScale));
                float grunge = saturate((g - 0.42) * 2.4) * _GrungeAmount;

                // --- 높이 기반 녹: 아래쪽일수록 심하다 ---
                float rustH = 1.0 - saturate((posW.y - _RustHeight) / max(0.01, _RustFade));
                float rust  = saturate(rustH * _RustAmount * (0.4 + 0.9 * g));

                // --- 윗면 먼지 / 아랫면 캐비티 ---
                float up   = saturate(N.y);
                float dust = saturate(up * up * _DustAmount * (0.5 + 0.8 * g));
                float down = saturate(-N.y);

                // --- 명도 조합 (흑백이라 색이 아니라 값으로 표현) ---
                float v = 1.0;
                v = lerp(v, _GrungeValue,        grunge);
                v = lerp(v, _GrungeValue * 0.8,  rust);
                v = lerp(v, _DustValue,          dust);
                v *= 1.0 - down * _CavityAmount * 0.5;

                half3 albedo = _BaseColor.rgb * v;

                // 녹·먼지는 거칠고 금속성이 낮다. 이 대비가 "폐공장"의 핵심.
                float wear  = max(grunge, max(rust, dust));
                float rough = lerp(1.0 - _Smoothness, _GrungeRough, wear);
                float metal = _Metallic * (1.0 - saturate(max(rust * 0.9, dust * 0.7)));

                InputData inputData = (InputData)0;
                inputData.positionWS      = posW;
                inputData.normalWS        = N;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(posW);
                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                    inputData.shadowCoord = TransformWorldToShadowCoord(posW);
                #else
                    inputData.shadowCoord = float4(0, 0, 0, 0);
                #endif
                inputData.fogCoord                 = IN.fogCoord;
                inputData.bakedGI                  = SampleSH(N);
                inputData.normalizedScreenSpaceUV  = GetNormalizedScreenSpaceUV(IN.positionCS);
                inputData.shadowMask               = half4(1, 1, 1, 1);

                SurfaceData surf = (SurfaceData)0;
                surf.albedo     = albedo;
                surf.metallic   = metal;
                surf.smoothness = 1.0 - rough;
                surf.occlusion  = 1.0;
                surf.alpha      = 1.0;

                half4 col = UniversalFragmentPBR(inputData, surf);
                col.rgb = MixFog(col.rgb, IN.fogCoord);
                return col;
            }
            ENDHLSL
        }

        // URP Lit의 패스를 include하면 _BaseMap/_Cutoff 같은 프로퍼티를 요구하므로 직접 작성한다.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct V { float4 positionCS : SV_POSITION; };

            V shadowVert(A IN)
            {
                V OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nrmWS = TransformObjectToWorldNormal(IN.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nrmWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionCS = cs;
                return OUT;
            }
            half4 shadowFrag(V IN) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask 0

            HLSLPROGRAM
            #pragma vertex depthVert
            #pragma fragment depthFrag
            #pragma multi_compile_instancing

            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; };

            V depthVert(A IN) { V o; o.positionCS = TransformObjectToHClip(IN.positionOS.xyz); return o; }
            half4 depthFrag(V IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
