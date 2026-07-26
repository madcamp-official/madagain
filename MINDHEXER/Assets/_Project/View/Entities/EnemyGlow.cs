using System.Collections.Generic;
using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 몹 발광 제어 — <b>어두운 맵에서 몹을 식별하기 위한 가독성 기능</b>이지 장식이 아니다.
    ///
    /// 머티리얼의 에미션을 켜고(에셋은 꺼진 채로 들어옴) HDR 강도로 띄운다.
    /// Bloom threshold가 1.05라 <b>1을 넘겨야</b> 실제로 번진다.
    ///
    /// 상태별로 밝기를 바꿔 <b>텔레그래프</b>로도 쓴다 — 예지 게임에서 소리·이펙트 없이
    /// "무엇을 하려는가"를 실루엣만으로 전달하는 수단.
    ///
    /// MaterialPropertyBlock을 쓰므로 머티리얼 인스턴스가 생기지 않아 배칭이 유지된다.
    /// (r.material 을 건드리면 개체마다 사본이 생겨 드로우콜이 늘어난다)
    /// </summary>
    public struct EnemyGlow
    {
        // 전용 셰이더(Game/EnemyBody) 프로퍼티. 빨간 부분만 발광 + 개체별 그런지.
        static readonly int IdGlowInt   = Shader.PropertyToID("_GlowIntensity");
        static readonly int IdGlowColor = Shader.PropertyToID("_GlowColor");
        static readonly int IdGrunge    = Shader.PropertyToID("_GrungeAmount");
        static readonly int IdGrungeSeed= Shader.PropertyToID("_GrungeSeed");
        // 오염·반사 — F8 패널에서 조절한 값을 매 프레임 밀어넣어 몹 4종에 동시 반영
        static readonly int IdRust      = Shader.PropertyToID("_RustColor");
        static readonly int IdRustDark  = Shader.PropertyToID("_RustDark");
        static readonly int IdRustBoost = Shader.PropertyToID("_RustBoost");
        static readonly int IdDarken    = Shader.PropertyToID("_Darken");
        static readonly int IdTexScale  = Shader.PropertyToID("_GrungeScale");
        static readonly int IdStreak    = Shader.PropertyToID("_StreakStrength");
        static readonly int IdCleanSm   = Shader.PropertyToID("_CleanSmoothness");
        static readonly int IdRustSm    = Shader.PropertyToID("_RustSmoothness");
        static readonly int IdRustMt    = Shader.PropertyToID("_RustMetallic");
        static readonly int IdRedThr    = Shader.PropertyToID("_RedThreshold");
        static readonly int IdRedSoft   = Shader.PropertyToID("_RedSoftness");
        // 폴백(URP Lit) — 전용 셰이더가 아닌 개체는 기존 방식으로.
        static readonly int IdEmission  = Shader.PropertyToID("_EmissionColor");

        Renderer[] rends;
        MaterialPropertyBlock mpb;
        public bool bound;
        bool customShader;   // Game/EnemyBody 를 쓰는가

        float cur;          // 현재 강도(스프링으로 목표를 추적)
        float flash;        // 순간 깜빡(피격) — 별도 채널
        float hueJitter;    // 개체별 색 편차
        float pulsePhase;

        /// <summary>렌더러를 모으고 개체별 오염도를 정한다. 개체당 1회.</summary>
        public void Bind(Transform root, System.Random rng)
        {
            bound = true;
            if (root == null) { rends = null; return; }

            var list = new List<Renderer>();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;   // 스파크 등은 제외
                list.Add(r);
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    if (m.shader != null && m.shader.name == "Game/EnemyBody") customShader = true;
                    else
                    {
                        // 폴백 경로: URP Lit 은 _EMISSION 키워드가 꺼져 있으면 _EmissionColor 를 통째로 무시한다.
                        m.EnableKeyword("_EMISSION");
                        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    }
                }
            }
            rends = list.ToArray();
            mpb = new MaterialPropertyBlock();
            hueJitter = rng != null ? (float)rng.NextDouble() : 0.5f;
            grungeAmt = rng != null ? (float)rng.NextDouble() : 0.5f;
            grungeSeed = rng != null ? (float)rng.NextDouble() * 100f : 0f;
            cur = 0f; flash = 0f; pulsePhase = 0f;
        }

        float grungeAmt, grungeSeed;   // 개체별 오염 — 같은 몹도 다르게 더럽다

        public bool HasRenderers => rends != null && rends.Length > 0;

        /// <summary>피격 순간 깜빡.</summary>
        public void Flash(float amount) { flash = Mathf.Max(flash, amount); }

        /// <summary>
        /// 상태를 읽어 목표 강도를 정하고 스프링으로 추적한 뒤 적용한다.
        /// camDist = 카메라 거리(멀수록 밝게 보정해 원거리 식별을 살린다).
        /// </summary>
        public void Apply(in EnemySim e, in EnemyGlowSettings s, in EnemyDirtSettings d, float camDist, float dt)
        {
            if (rends == null || rends.Length == 0) return;

            // ── 상태별 목표 강도 ──
            float target = s.baseIntensity;
            Color tint = s.baseColor;

            var st = e.ai.state;
            if (e.combat.gloryStage > 0)                 // 처형 가능 — 색을 바꿔 눈에 띄게
            { target = s.gloryIntensity; tint = s.gloryColor; }
            else if (e.combat.stunTicks > 0)             // 경직 — 불규칙 점멸
            {
                pulsePhase += dt * s.stunFlickerSpeed;
                float n = Mathf.PerlinNoise(pulsePhase, 0f);
                target = Mathf.Lerp(s.baseIntensity * 0.2f, s.baseIntensity, n);
            }
            else if (st == EnemyState.Windup)            // ★ 공격 예비 — 급격히 밝아짐(텔레그래프)
            { target = s.windupIntensity; tint = s.windupColor; }
            else if (st == EnemyState.Active)
            { target = s.attackIntensity; tint = s.windupColor; }
            else if (st == EnemyState.ChargeRun)         // 돌진 — 밝기 + 맥동
            {
                pulsePhase += dt * s.chargePulseSpeed;
                target = s.chargeIntensity * Mathf.Lerp(0.75f, 1.25f, 0.5f + 0.5f * Mathf.Sin(pulsePhase));
                tint = s.windupColor;
            }
            else if (st == EnemyState.Aim)               // 조준 — 살짝 밝게
            { target = s.aimIntensity; tint = s.windupColor; }

            // ── 거리 보정 — 멀면 어두운 맵에서 안 보인다 ──
            if (s.distanceBoost > 0f)
            {
                float k = Mathf.Clamp01((camDist - s.boostStart) / Mathf.Max(0.1f, s.boostRange));
                target *= 1f + k * s.distanceBoost;
            }

            // 상태 전이는 빠르게 붙어야 텔레그래프가 산다(느리면 이미 맞은 뒤에 밝아진다)
            cur = Mathf.Lerp(cur, target, 1f - Mathf.Exp(-s.followSpeed * dt));

            // 피격 깜빡은 별도 감쇠
            if (flash > 0f) flash = Mathf.Max(0f, flash - dt * s.flashDecay);

            float inten = cur + flash * s.flashIntensity;

            // ── 개체별 색 편차 — 무리 속에서 개체 구분 ──
            float j = (hueJitter - 0.5f) * 2f * s.colorJitter;
            Color c = new Color(
                Mathf.Max(0f, tint.r + j),
                Mathf.Max(0f, tint.g + j * 0.35f),
                Mathf.Max(0f, tint.b - j * 0.35f)) * inten;

            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i];
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                if (customShader)
                {
                    // 셰이더가 베이스 텍스처에서 빨간 부분만 골라 이 강도로 빛낸다.
                    mpb.SetFloat(IdGlowInt, inten);
                    mpb.SetColor(IdGlowColor, new Color(
                        Mathf.Max(0f, 1f + j), Mathf.Max(0f, 1f + j * 0.35f), Mathf.Max(0f, 1f - j * 0.35f), 1f));
                    mpb.SetFloat(IdGrunge, Mathf.Clamp01(grungeAmt * s.grungeScale));
                    mpb.SetFloat(IdGrungeSeed, grungeSeed);
                    // 오염·반사·빨강추출 — 패널에서 만진 값이 몹 전체에 즉시 반영된다
                    mpb.SetColor(IdRust,      d.rustColor);
                    mpb.SetColor(IdRustDark,  d.rustDark);
                    mpb.SetFloat(IdRustBoost, d.rustBoost);
                    mpb.SetFloat(IdDarken,    d.darken);
                    mpb.SetFloat(IdTexScale,  d.grungeTexScale);
                    mpb.SetFloat(IdStreak,    d.streak);
                    mpb.SetFloat(IdCleanSm,   d.cleanSmoothness);
                    mpb.SetFloat(IdRustSm,    d.rustSmoothness);
                    mpb.SetFloat(IdRustMt,    d.rustMetallic);
                    mpb.SetFloat(IdRedThr,    d.redThreshold);
                    mpb.SetFloat(IdRedSoft,   d.redSoftness);
                }
                else mpb.SetColor(IdEmission, c);   // 폴백: 전 표면 균일 발광
                r.SetPropertyBlock(mpb);
            }
        }

        /// <summary>발광을 완전히 끈다(전역 off).</summary>
        public void Clear()
        {
            if (rends == null || mpb == null) return;
            foreach (var r in rends)
            {
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor(IdEmission, Color.black);
                r.SetPropertyBlock(mpb);
            }
            cur = 0f; flash = 0f;
        }

        public float CurrentIntensity => cur + flash;
    }

    /// <summary>발광 튜닝값. 콘솔·패널에서 실시간 조절한다.</summary>
    [System.Serializable]
    public struct EnemyGlowSettings
    {
        public float baseIntensity;      // 평상시
        public float windupIntensity;    // 공격 예비 ★ 텔레그래프
        public float attackIntensity;    // 타격 순간
        public float chargeIntensity;    // 돌진
        public float aimIntensity;       // 조준
        public float gloryIntensity;     // 처형 가능

        public Color baseColor;
        public Color windupColor;
        public Color gloryColor;

        public float followSpeed;        // 상태 전이 추종 속도(빨라야 텔레그래프가 산다)
        public float chargePulseSpeed;
        public float stunFlickerSpeed;

        public float flashIntensity;     // 피격 깜빡 세기
        public float flashDecay;

        public float colorJitter;        // 개체별 색 편차
        public float grungeScale;        // 오염 전체 배수(0=깨끗, 1=최대). 개체별 난수에 곱해진다

        public float distanceBoost;      // 원거리 가산 배수(0=끔)
        public float boostStart;         // 이 거리부터 보정 시작(m)
        public float boostRange;         // 이 거리에 걸쳐 최대까지

        /// <summary>어두운 맵 기준 초기값 — 일단 아주 밝게 잡았다(보면서 낮추는 편이 쉽다).</summary>
        public static EnemyGlowSettings Default => new EnemyGlowSettings
        {
            baseIntensity   = 8f,
            windupIntensity = 30f,
            attackIntensity = 45f,
            chargeIntensity = 22f,
            aimIntensity    = 16f,
            gloryIntensity  = 26f,

            baseColor   = new Color(1f, 0.18f, 0.10f),   // 붉은 기본
            windupColor = new Color(1f, 0.75f, 0.15f),   // 예비는 노란빛 — 색으로도 구분
            gloryColor  = new Color(0.35f, 1f, 0.55f),   // 처형은 초록

            followSpeed      = 18f,
            chargePulseSpeed = 9f,
            stunFlickerSpeed = 14f,

            flashIntensity = 40f,
            flashDecay     = 6f,

            colorJitter = 0.10f,
            grungeScale = 0.65f,

            distanceBoost = 1.2f,
            boostStart    = 12f,
            boostRange    = 25f,
        };
    }
}
