Shader "Game/EnemyBody"
{
    // 몹 전용 셰이더.
    //  ① 베이스 텍스처에서 "빨간 부분만" 골라 발광시킨다 — 에미션 맵이 없는 모델도 동작한다.
    //     (Meshy 모델은 몸체가 어두운 회색, 장식 라인이 순수 빨강이라 분리가 아주 잘 된다)
    //  ② 그런지(녹·얼룩)를 절차적으로 얹어 개체마다 다르게 더럽힌다 — 텍스처 추가 없이.
    //
    // 모든 파라미터는 MaterialPropertyBlock으로 개체별 지정 가능 → 배칭 유지.
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor]   _BaseColor ("Base Color", Color) = (1,1,1,1)
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1
        _MetallicGlossMap ("Metallic Map", 2D) = "white" {}
        _Metallic ("Metallic", Range(0,1)) = 0.6
        _Smoothness ("Smoothness", Range(0,1)) = 0.35

        [Header(Red Emission)]
        _GlowIntensity ("Glow Intensity", Float) = 0            // EnemyGlow가 런타임에 씀
        _GlowColor ("Glow Tint", Color) = (1,1,1,1)
        _RedThreshold ("Red Threshold", Range(0,1)) = 0.25      // 이 이상 붉어야 발광 대상
        _RedSoftness ("Red Softness", Range(0.01,0.5)) = 0.15   // 경계 부드러움

        [Header(Grunge)]
        // 어두운 배경에서는 "어두운 얼룩"이 원리적으로 안 보인다.
        // 그래서 ① 반사 대비(매끄러움 차이)와 ② 밝은 쪽 오염 두 가지로 간다.
        _GrungeAmount ("Grunge Amount", Range(0,1)) = 0         // 개체마다 다르게
        _GrungeScale ("Grunge Scale", Float) = 9                 // 낮을수록 얼룩이 큼
        _GrungeSeed ("Grunge Seed", Float) = 0                  // 개체별 무늬 오프셋
        _RustColor ("Rust (밝은 황토)", Color) = (0.72,0.55,0.34,1)
        _RustDark ("Rust Dark (탁한 회백)", Color) = (0.55,0.50,0.45,1)
        _RustBoost ("Rust Boost", Range(0,3)) = 1.35            // 녹이 얼마나 밝게 올라오는가
        _Darken ("Darken", Range(0,1)) = 0.10                   // 어둡게는 거의 안 한다(묻히므로)
        _StreakStrength ("Streak (흘러내린 자국)", Range(0,1)) = 0.6

        [Header(Reflection Contrast)]
        // 어둠 속에서 색보다 잘 읽히는 신호 — 깨끗한 금속은 번쩍이고 녹슨 곳은 완전 무광.
        _CleanSmoothness ("Clean Smoothness", Range(0,1)) = 0.85
        _RustSmoothness ("Rust Smoothness", Range(0,1)) = 0.04
        _RustMetallic ("Rust Metallic", Range(0,1)) = 0.05      // 녹은 금속이 아니다
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float4 tangentWS  : TEXCOORD3;
                float  fogCoord   : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_BaseMap);          SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);          SAMPLER(sampler_BumpMap);
            TEXTURE2D(_MetallicGlossMap); SAMPLER(sampler_MetallicGlossMap);

            // 개체별로 달라지는 값은 전부 인스턴싱 버퍼에 — MaterialPropertyBlock으로 덮어써도 배칭 유지
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _GlowColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _RustColor)
                UNITY_DEFINE_INSTANCED_PROP(float4, _RustDark)
                UNITY_DEFINE_INSTANCED_PROP(float,  _GlowIntensity)
                UNITY_DEFINE_INSTANCED_PROP(float,  _GrungeAmount)
                UNITY_DEFINE_INSTANCED_PROP(float,  _GrungeSeed)
                // 아래는 F8 패널이 런타임에 밀어넣는다 — 인스턴싱 버퍼에 둬야 MPB 지정이 먹는다
                UNITY_DEFINE_INSTANCED_PROP(float,  _RustBoost)
                UNITY_DEFINE_INSTANCED_PROP(float,  _Darken)
                UNITY_DEFINE_INSTANCED_PROP(float,  _GrungeScale)
                UNITY_DEFINE_INSTANCED_PROP(float,  _StreakStrength)
                UNITY_DEFINE_INSTANCED_PROP(float,  _CleanSmoothness)
                UNITY_DEFINE_INSTANCED_PROP(float,  _RustSmoothness)
                UNITY_DEFINE_INSTANCED_PROP(float,  _RustMetallic)
                UNITY_DEFINE_INSTANCED_PROP(float,  _RedThreshold)
                UNITY_DEFINE_INSTANCED_PROP(float,  _RedSoftness)
            UNITY_INSTANCING_BUFFER_END(Props)

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float  _BumpScale;
                float  _Metallic;
                float  _Smoothness;
            CBUFFER_END

            // 값싼 절차 노이즈 — 텍스처 없이 얼룩을 만든다
            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }
            float ValueNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i), b = Hash21(i + float2(1,0));
                float c = Hash21(i + float2(0,1)), d = Hash21(i + float2(1,1));
                return lerp(lerp(a,b,f.x), lerp(c,d,f.x), f.y);
            }
            float Fbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                for (int i = 0; i < 4; i++) { v += a * ValueNoise(p); p *= 2.03; a *= 0.5; }
                return v;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   nrm = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS   = nrm.normalWS;
                OUT.tangentWS  = float4(nrm.tangentWS, IN.tangentOS.w * GetOddNegativeScale());
                OUT.uv         = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.fogCoord   = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                float4 tint    = UNITY_ACCESS_INSTANCED_PROP(Props, _BaseColor);
                float3 albedo  = baseTex.rgb * tint.rgb;

                // 런타임 조절값(F8 패널) — 인스턴싱 버퍼에서 읽는다
                float redThr    = UNITY_ACCESS_INSTANCED_PROP(Props, _RedThreshold);
                float redSoft   = UNITY_ACCESS_INSTANCED_PROP(Props, _RedSoftness);
                float rustBoost = UNITY_ACCESS_INSTANCED_PROP(Props, _RustBoost);
                float darken    = UNITY_ACCESS_INSTANCED_PROP(Props, _Darken);
                float texScale  = UNITY_ACCESS_INSTANCED_PROP(Props, _GrungeScale);
                float streakStr = UNITY_ACCESS_INSTANCED_PROP(Props, _StreakStrength);
                float cleanSm   = UNITY_ACCESS_INSTANCED_PROP(Props, _CleanSmoothness);
                float rustSm    = UNITY_ACCESS_INSTANCED_PROP(Props, _RustSmoothness);
                float rustMt    = UNITY_ACCESS_INSTANCED_PROP(Props, _RustMetallic);

                // ── 빨강 추출 ──
                // 순수 빨강일수록 r이 크고 g·b가 작다. 그 차이를 마스크로 쓴다.
                float redness = baseTex.r - max(baseTex.g, baseTex.b);
                float redMask = smoothstep(redThr, redThr + max(0.01, redSoft), redness);

                // ── 그런지(녹) ──
                // 한 색으로 섞으면 어두운 몸체에 묻혀 안 보인다. 두 층으로 쌓는다:
                //   ① 넓게 퍼진 밝은 주황녹  ② 그 안에 뭉친 짙은 갈색(패임·고인 자국)
                // 여기에 세로로 흘러내린 줄무늬를 더해 "빗물에 녹이 흘러내린" 금속을 만든다.
                float grungeAmt = UNITY_ACCESS_INSTANCED_PROP(Props, _GrungeAmount);
                float3 rustLit  = UNITY_ACCESS_INSTANCED_PROP(Props, _RustColor).rgb;
                float3 rustDark = UNITY_ACCESS_INSTANCED_PROP(Props, _RustDark).rgb;
                float  seed     = UNITY_ACCESS_INSTANCED_PROP(Props, _GrungeSeed);
                float  dirt     = 0.0;
                if (grungeAmt > 0.001)
                {
                    float2 p = IN.uv * max(0.5, texScale) + seed * 37.7;

                    // ① 넓은 얼룩 — 임계를 낮춰 면적을 넓힌다(예전엔 0.42~0.78이라 너무 좁았다)
                    float broad = Fbm(p);
                    broad = smoothstep(0.30, 0.62, broad);

                    // ② 흘러내린 줄무늬 — v축을 늘려 세로로 길게 뽑는다
                    float streak = Fbm(float2(p.x * 2.6, p.y * 0.32) + 11.3);
                    streak = smoothstep(0.40, 0.75, streak) * streakStr;

                    // ③ 뭉친 짙은 코어 — 넓은 얼룩 안쪽에만
                    float core = smoothstep(0.55, 0.85, Fbm(p * 2.1 + 5.7));

                    dirt = saturate(max(broad, streak)) * grungeAmt;
                    // 발광 라인은 덜 더럽힌다 — 식별 기능이므로 가려지면 안 된다
                    dirt *= (1.0 - redMask * 0.75);

                    // ★ 어두운 쪽이 아니라 <b>밝은 쪽</b>으로 오염시킨다.
                    //   어두운 배경 + 어두운 몸체에 어두운 얼룩을 얹으면 대비가 0이라 안 보인다.
                    //   실제로도 산화·먼지는 표면을 밝고 탁하게 만든다.
                    float3 rustMix = lerp(rustLit, rustDark, core);
                    albedo = lerp(albedo, rustMix * rustBoost, saturate(dirt * 0.95));
                    albedo *= (1.0 - dirt * core * darken);   // 아주 약하게만
                }

                float3 normalTS = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv), _BumpScale);
                float sgn = IN.tangentWS.w;
                float3 bitangent = sgn * cross(IN.normalWS, IN.tangentWS.xyz);
                float3x3 tbn = float3x3(IN.tangentWS.xyz, bitangent, IN.normalWS);
                float3 normalWS = normalize(mul(normalTS, tbn));

                // ── 반사 대비 ★ 어둠 속에서 색보다 잘 읽히는 신호 ──
                // 깨끗한 금속은 매끄러움을 극단으로 올려 광원이 스칠 때 번쩍이게 하고,
                // 녹슨 부분은 완전 무광으로 죽인다. 밝기 대비가 없는 어두운 환경에서도
                // "빛나는 곳 / 안 빛나는 곳"은 확실히 구분된다.
                float4 mg = SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, IN.uv);
                float cleanSmooth = max(_Smoothness, cleanSm);   // 깨끗한 곳은 확실히 번쩍이게
                float smoothness = lerp(cleanSmooth, rustSm, saturate(dirt * 1.2));
                float metallic   = lerp(mg.r * _Metallic, rustMt, saturate(dirt * 1.2));

                SurfaceData sd = (SurfaceData)0;
                sd.albedo    = albedo;
                sd.metallic  = metallic;
                sd.smoothness= smoothness;
                sd.normalTS  = normalTS;
                sd.occlusion = 1.0;
                sd.alpha     = 1.0;

                // ── 발광: 빨간 부분만 ──
                float  gi   = UNITY_ACCESS_INSTANCED_PROP(Props, _GlowIntensity);
                float3 gcol = UNITY_ACCESS_INSTANCED_PROP(Props, _GlowColor).rgb;
                sd.emission = baseTex.rgb * gcol * (redMask * gi);

                InputData id = (InputData)0;
                id.positionWS = IN.positionWS;
                id.normalWS   = normalWS;
                id.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                id.shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                id.fogCoord = IN.fogCoord;
                id.bakedGI = SampleSH(normalWS);

                half4 color = UniversalFragmentPBR(id, sd);
                color.rgb = MixFog(color.rgb, IN.fogCoord);
                return color;
            }
            ENDHLSL
        }

        // 그림자·깊이는 URP 기본 패스를 그대로 쓴다
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
    FallBack "Universal Render Pipeline/Lit"
}
