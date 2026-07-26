using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 절차적 전투 SFX. ★ combat 소유·독립. Resources/Sfx에 실제 클립 있으면 우선 사용, 없으면 AudioClip.Create로 합성.
    /// 정적 접근자(CombatAudio.Swing() 등)를 SwordView·CombatFeedback이 호출.
    /// 임시방편 아님(이 세션 확정 방식) — 다만 진짜 폴리 사운드보단 거칠다.
    /// </summary>
    public class CombatAudio : MonoBehaviour
    {
        const int SR = 44100;

        static CombatAudio inst;
        AudioSource src;
        AudioClip[] swing, hit, dash, guardRaise, block, backstrike, death;   // [되돌림] hit = 예전 금속 검격음
        // [2026-07-22] 점프·이단점프·착지·발소리 — 실제 에셋(Resources/Sfx)로 드롭인.
        AudioClip[] jump, doubleJump, landing, playerStep;
        // 몹 SFX. [2026-07-22] 합성음 → Resources/Sfx 드롭인 우선(로봇 사운드 에셋). 없으면 합성 폴백.
        AudioClip[] enWindup, enMelee, enAim, enFire, enPain, enStep;
        // [2026-07-22] 원거리 발사음 — 공중/지상 다른 레이저. 발사한 몹 종류로 갈라 재생(ProjectileView).
        AudioClip[] enFireAir, enFireGround;
        // [2026-07-22] 돌진몹이 벽/플레이어에 박는 순간 — 모래 섞인 박치기음.
        AudioClip[] chargeImpact;
        // [2026-07-22] 소환 팬이 천장에서 하강할 때 — 기계식 팬 상승/하강음.
        AudioClip[] fanMove;
        AudioClip[] playerHurt;   // 플레이어 피격("억" + 저음 임팩트)
        AudioClip[] prediction;   // 예지 발동(시간정지식 상승 시머)
        AudioClip cutsceneLanding;   // 등장 컷신 착지 — 저음 붐 + 파편(이동 착지 landing과 별개, 훨씬 무겁다)

        void Awake()
        {
            if (inst != null && inst != this) { Destroy(gameObject); return; }
            inst = this;

            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;   // 2D (1인칭이라 위치 무관)

            // 씬에 리스너 없으면 카메라에 부착
            if (Object.FindFirstObjectByType<AudioListener>() == null)
            {
                var cam = Camera.main;
                if (cam != null && cam.GetComponent<AudioListener>() == null)
                    cam.gameObject.AddComponent<AudioListener>();
            }

            swing      = LoadVariants("Sfx/Swing", BuildSwing);
            hit        = LoadVariants("Sfx/PlayerHit", BuildHit);   // 평타1/2피격 랜덤(최근 에셋)
            dash       = LoadVariants("Sfx/Dash", BuildDash);
            guardRaise = LoadVariants("Sfx/GuardRaise", BuildGuardRaise);
            block      = LoadVariants("Sfx/Block", BuildBlock);
            // [2026-07-22] 슬롯명을 Lunge로 분리 — 낡은 오디오 폴더(_Project/Audio/Resources/Sfx/Backstrike)의
            // HitFlesh_02와 병합돼 찌르기 소리가 50% 확률로 살점음으로 바뀌던 문제 회피.
            backstrike = LoadVariants("Sfx/Lunge", BuildBackstrike);
            death      = LoadVariants("Sfx/Death", BuildDeath);

            jump       = LoadVariants("Sfx/Jump", () => null);
            doubleJump = LoadVariants("Sfx/DoubleJump", () => null);
            landing    = LoadVariants("Sfx/Landing", () => null);
            playerStep = LoadVariants("Sfx/PlayerStep", () => null);

            enWindup = LoadVariants("Sfx/EnWindup", BuildEnemyWindup);
            enMelee  = LoadVariants("Sfx/EnMelee",  BuildEnemyMelee);
            enAim    = LoadVariants("Sfx/EnAim",    BuildEnemyAim);
            enFire   = LoadVariants("Sfx/EnFire",   BuildEnemyFire);
            enPain   = LoadVariants("Sfx/EnPain",   BuildEnemyPain);   // 로봇피격 11종(최근 에셋)
            enStep   = LoadVariants("Sfx/EnStep",   BuildEnemyStep);
            enFireAir    = LoadVariants("Sfx/EnFireAir",    BuildEnemyFire);
            enFireGround = LoadVariants("Sfx/EnFireGround", BuildEnemyFire);
            chargeImpact = LoadVariants("Sfx/ChargeImpact", () => null);
            fanMove      = LoadVariants("Sfx/FanMove", () => null);

            playerHurt = LoadVariants("Sfx/PlayerHurt", BuildPlayerHurt);
            prediction = LoadVariants("Sfx/Prediction", BuildPrediction);
            cutsceneLanding = BuildLanding();

            // [2026-07-22] 몹 발소리 에셋(Sfx/EnStep)이 준비됐으므로 발자국 사운드를 켠다
            // (FootstepDetector가 발 딛는 순간 CombatAudio.EnemyStep()을 부르게 된다).
            FootstepEvents.UseFallbackSound = true;
        }

        /// <summary>Resources/{folder}에 여러 클립(테이크) 있으면 그걸, 파일 하나뿐이면 그거, 없으면 합성 클립으로 폴백.</summary>
        static AudioClip[] LoadVariants(string folder, System.Func<AudioClip> fallback)
        {
            var many = Resources.LoadAll<AudioClip>(folder);
            if (many != null && many.Length > 0) return many;
            var single = Resources.Load<AudioClip>(folder);
            return new[] { single != null ? single : fallback() };
        }

        // ── 정적 접근자 (null 안전) ──
        public static void Swing()      => Play(inst?.swing,      10.0f, 0.06f);   // 허공 베기(살짝 키움)
        public static void Hit()        => Play(inst?.hit,        2.00f, 0.10f);   // 평타1/2피격 랜덤(볼륨 낮춤)
        public static void Dash()       => Play(inst?.dash,       7.50f,  0.05f);
        public static void GuardRaise() => Play(inst?.guardRaise, 0.35f, 0.06f);  // 막기 켜는 소리(스윽)
        public static void Block()      => Play(inst?.block,      0.45f, 0.05f);  // 실제 방어 성공(챙) — 적 공격 생기면 사용
        public static void Backstrike() => Play(inst?.backstrike, 2.80f,  0.05f);   // 찌르기(볼륨 상향)
        public static void Death()      => Play(inst?.death,      0.8f,  0.08f);

        // ── 이동 SFX ──
        public static void Jump()       => Play(inst?.jump,       0.50f, 0.05f);
        public static void DoubleJump() => Play(inst?.doubleJump, 0.35f,  0.05f);
        public static void Landing()    => Play(inst?.landing,    0.35f,  0.05f);
        /// <summary>등장 컷신 착지 "쿠웅". 이동 착지(Landing)와 별개 — 연출용 무거운 붐.</summary>
        public static void CutsceneLanding() => Play(inst?.cutsceneLanding, 1.0f, 0.01f);
        /// <summary>플레이어 발소리. 자주 나므로 볼륨 낮게, 피치 편차 넓게.</summary>
        public static void PlayerStep() => Play(inst?.playerStep, 0.14f, 0.14f);

        // ── 몹 SFX. 몹 공격 SIM 상태 생기면 호출: ──
        //  근접 그런트: Windup 진입 → EnemyWindup(), Active 판정 → EnemyMelee()
        //  원거리 솔저: Aim 진입 → EnemyAim(), Fire → EnemyFire()
        //  피격 신음(EnemyPain)은 CombatFeedback이 이미 연결.
        public static void EnemyWindup() => Play(inst?.enWindup, 0.8f,  0.05f);
        public static void EnemyMelee()  => Play(inst?.enMelee,  0.6f,  0.05f);   // 일반 근접(박치기는 ChargeImpact로 분리됨 → 여긴 합성음)
        public static void EnemyAim()    => Play(inst?.enAim,    0.22f, 0.03f);   // 조준 차징(에셋 교체 + 볼륨 낮춤)
        public static void EnemyFire()   => Play(inst?.enFire,   0.6f,  0.05f);
        public static void EnemyFireAir()    => Play(inst?.enFireAir,    0.32f, 0.05f);  // 공중 원거리 발사(볼륨 낮춤)
        public static void EnemyFireGround() => Play(inst?.enFireGround, 0.32f, 0.05f);  // 지상 원거리 발사(볼륨 낮춤)
        /// <summary>[2026-07-22] 위치 기반 발사음 — 가까울수록 크게, 멀면 작게(발소리보다 멀리 들림).</summary>
        public static void EnemyFireAir(Vector3 pos)
        {
            float a = DistAtten(pos, 6f, 34f);
            if (a > 0.02f) Play(inst?.enFireAir, 0.32f * a, 0.05f);
        }
        public static void EnemyFireGround(Vector3 pos)
        {
            float a = DistAtten(pos, 6f, 34f);
            if (a > 0.02f) Play(inst?.enFireGround, 0.32f * a, 0.05f);
        }

        /// <summary>카메라 기준 거리 감쇠 0~1. full 이내 최대, silent 넘으면 0.</summary>
        static float DistAtten(Vector3 pos, float full, float silent)
        {
            if (inst == null) return 0f;
            var camT = Camera.main != null ? Camera.main.transform : null;
            float dist = camT != null ? Vector3.Distance(camT.position, pos) : 0f;
            return Mathf.Clamp01(1f - (dist - full) / Mathf.Max(0.01f, silent - full));
        }
        /// <summary>돌진몹이 벽/플레이어에 박는 순간의 박치기음.</summary>
        public static void ChargeImpact() => Play(inst?.chargeImpact, 0.75f, 0.05f);
        /// <summary>소환 팬 하강 순간의 기계음(위치 기반 — 멀면 작게).
        /// 한 웨이브에 팬이 여러 개면 동시에 내려와 소리가 겹쳐 커진다 — 짧은 쿨다운으로 한 번만.</summary>
        static float fanCooldownUntil;
        public static void FanMove(Vector3 pos)
        {
            if (Time.unscaledTime < fanCooldownUntil) return;   // 겹침 방지(여러 팬 동시 하강 = 1회)
            fanCooldownUntil = Time.unscaledTime + 0.5f;
            float a = DistAtten(pos, 15f, 60f);
            if (a > 0.02f) Play(inst?.fanMove, 0.88f * a, 0.03f);
        }
        public static void EnemyPain()   => Play(inst?.enPain,   2.80f,  0.10f);   // 로봇피격 11종(최근 에셋)
        /// <summary>몹이 발을 딛는 소리. 자주 나므로 볼륨을 낮게, 피치 편차를 넓게 준다.</summary>
        public static void EnemyStep()   => Play(inst?.enStep,   0.22f, 0.18f);
        /// <summary>[2026-07-22] 위치 기반 발소리 — 카메라에서 가까울수록 크게, 멀면 무음.
        /// 몹이 많을 때 발소리가 뭉쳐 시끄럽던 문제 대응(가까운 적만 들리게).</summary>
        public static void EnemyStep(Vector3 pos)
        {
            if (inst == null) return;
            var camT = Camera.main != null ? Camera.main.transform : null;
            float dist = camT != null ? Vector3.Distance(camT.position, pos) : 0f;
            const float full = 3f, silent = 12f;   // 3m 이내 최대, 12m 넘으면 무음
            float atten = Mathf.Clamp01(1f - (dist - full) / (silent - full));
            if (atten <= 0.02f) return;
            Play(inst.enStep, 0.22f * atten, 0.18f);
        }
        public static void PlayerHurt()  => Play(inst?.playerHurt, 0.75f, 0.06f);  // 플레이어 피격
        public static void Prediction()  => Play(inst?.prediction, 0.5f,  0.02f);  // 예지 발동

        static void Play(AudioClip[] clips, float vol, float pitchJitter)
        {
            if (inst == null || clips == null || clips.Length == 0) return;
            var clip = clips[Random.Range(0, clips.Length)];
            if (clip == null) return;
            inst.src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            inst.src.PlayOneShot(clip, vol);
        }

        static void Play(AudioClip clip, float vol, float pitchJitter)
        {
            if (inst == null || clip == null) return;
            inst.src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            inst.src.PlayOneShot(clip, vol);
        }

        // ── 합성 ──

        /// <summary>평타 스윙 휙: 노이즈 로우패스 스윕 + 빠른 어택/감쇠.</summary>
        static AudioClip BuildSwing()
        {
            const float dur = 0.20f;
            int n = (int)(dur * SR);
            var s = new float[n];
            var rng = new System.Random(1);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float white = (float)(rng.NextDouble() * 2 - 1);
                float a = Mathf.Lerp(0.5f, 0.04f, t);        // 밝음→둔탁
                lp += (white - lp) * a;
                float env = Mathf.Clamp01(t * 18f) * Mathf.Pow(1f - t, 1.6f);
                s[i] = lp * env * 0.6f;
            }
            return Clip("cai_swing", s);
        }

        /// <summary>타격 퍽: 짧은 메탈릭 트랜지언트 + 노이즈 버스트.</summary>
        static AudioClip BuildHit()
        {
            const float dur = 0.14f;
            int n = (int)(dur * SR);
            var s = new float[n];
            var rng = new System.Random(2);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float ti = (float)i / SR;
                float env = Mathf.Exp(-t * 34f);
                float metal = Mathf.Sin(6.2832f * 190f * ti)
                            + 0.6f * Mathf.Sin(6.2832f * 330f * ti)
                            + 0.4f * Mathf.Sin(6.2832f * 92f * ti);
                float noise = (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-t * 60f);
                s[i] = (metal * 0.45f + noise * 0.7f) * env;
            }
            return Clip("cai_hit", s);
        }

        /// <summary>질풍참 샤악: 밝은 노이즈 휘슬 + 지속.</summary>
        static AudioClip BuildDash()
        {
            const float dur = 0.28f;
            int n = (int)(dur * SR);
            var s = new float[n];
            var rng = new System.Random(3);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float white = (float)(rng.NextDouble() * 2 - 1);
                float a = Mathf.Lerp(0.35f, 0.12f, t);
                lp += (white - lp) * a;
                float env = Mathf.Clamp01(t * 12f) * Mathf.Pow(1f - t, 1.1f);
                s[i] = lp * env * 0.55f;
            }
            return Clip("cai_dash", s);
        }

        /// <summary>막기 켜는 소리(스윽): 부드러운 저음 노이즈 휘슬. 금속 트랜지언트 없음, 조용함.</summary>
        static AudioClip BuildGuardRaise()
        {
            const float dur = 0.14f;
            int n = (int)(dur * SR);
            var s = new float[n];
            var rng = new System.Random(11);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float white = (float)(rng.NextDouble() * 2 - 1);
                float a = Mathf.Lerp(0.25f, 0.06f, t);         // 둔탁하게
                lp += (white - lp) * a;
                float env = Mathf.Sin(Mathf.PI * t);           // 부드러운 인/아웃(날카로운 어택 없음)
                s[i] = lp * env * 0.35f;
            }
            return Clip("cai_guardraise", s);
        }

        /// <summary>막기 챙(실제 방어 성공용): 고음 메탈 링잉. 지금은 미사용(적 공격 생기면).</summary>
        static AudioClip BuildBlock()
        {
            const float dur = 0.30f;
            int n = (int)(dur * SR);
            var s = new float[n];
            var rng = new System.Random(4);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float ti = (float)i / SR;
                float env = Mathf.Exp(-t * 9f);
                float ring = Mathf.Sin(6.2832f * 1300f * ti)
                           + 0.5f * Mathf.Sin(6.2832f * 1950f * ti)
                           + 0.3f * Mathf.Sin(6.2832f * 2600f * ti);
                float atk = t < 0.02f ? (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-t * 200f) : 0f;
                s[i] = ring * env * 0.32f + atk * 0.5f;
            }
            return Clip("cai_block", s);
        }

        /// <summary>칼등치기 쾅: 육중한 저음 바디 + 노이즈.</summary>
        static AudioClip BuildBackstrike()
        {
            const float dur = 0.35f;
            int n = (int)(dur * SR);
            var s = new float[n];
            var rng = new System.Random(5);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float ti = (float)i / SR;
                float env = Mathf.Exp(-t * 10f);
                float body = Mathf.Sin(6.2832f * 88f * ti) + 0.6f * Mathf.Sin(6.2832f * 140f * ti);
                float noise = (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-t * 24f);
                s[i] = (body * 0.7f + noise * 0.6f) * env;
            }
            return Clip("cai_backstrike", s);
        }

        /// <summary>처치 크런치: 하강 노이즈.</summary>
        static AudioClip BuildDeath()
        {
            const float dur = 0.22f;
            int n = (int)(dur * SR);
            var s = new float[n];
            var rng = new System.Random(6);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float white = (float)(rng.NextDouble() * 2 - 1);
                float a = Mathf.Lerp(0.3f, 0.05f, t);
                lp += (white - lp) * a;
                float env = Mathf.Exp(-t * 17f);
                s[i] = lp * env * 0.7f;
            }
            return Clip("cai_death", s);
        }

        // ── 몹 합성 (전부 러프·잠정) ──

        /// <summary>근접 그런트 선딜 텔레그래프: 저음 그르렁이 점점 커지며 상승(읽기 쉬움).</summary>
        static AudioClip BuildEnemyWindup()
        {
            const float dur = 0.40f;
            int n = (int)(dur * SR);
            var s = new float[n];
            var rng = new System.Random(7);
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float f = Mathf.Lerp(70f, 150f, t);          // 상승
                phase += 6.2832f * f / SR;
                float tone = Mathf.Sin(phase) + 0.4f * Mathf.Sin(phase * 2f);
                float noise = (float)(rng.NextDouble() * 2 - 1) * 0.3f;
                float env = Mathf.Clamp01(t * 3f) * Mathf.Clamp01((1f - t) * 4f) * Mathf.Lerp(0.5f, 1f, t);
                s[i] = (tone * 0.5f + noise) * env * 0.5f;
            }
            return Clip("cai_en_windup", s);
        }

        /// <summary>근접 타격 스윙: 둔탁한 휙 + 저음 툭.</summary>
        static AudioClip BuildEnemyMelee()
        {
            const float dur = 0.20f;
            int n = (int)(dur * SR);
            var s = new float[n];
            var rng = new System.Random(8);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float ti = (float)i / SR;
                float white = (float)(rng.NextDouble() * 2 - 1);
                float a = Mathf.Lerp(0.3f, 0.03f, t);
                lp += (white - lp) * a;
                float thud = Mathf.Sin(6.2832f * 80f * ti) * Mathf.Exp(-t * 15f);
                float env = Mathf.Clamp01(t * 15f) * Mathf.Pow(1f - t, 1.3f);
                s[i] = (lp * env + thud * 0.5f) * 0.6f;
            }
            return Clip("cai_en_melee", s);
        }

        /// <summary>원거리 조준 차징: 전기 휘파람이 가속 상승(큰 텔레그래프).</summary>
        static AudioClip BuildEnemyAim()
        {
            const float dur = 0.60f;
            int n = (int)(dur * SR);
            var s = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float ti = (float)i / SR;
                float f = Mathf.Lerp(300f, 900f, t * t);     // 가속 상승
                phase += 6.2832f * f / SR;
                float tone = (Mathf.Sin(phase) + 0.3f * Mathf.Sin(phase * 1.5f))
                             * (1f + 0.1f * Mathf.Sin(6.2832f * 18f * ti));  // 미세 트레몰로
                float env = Mathf.Clamp01(t * 2f) * Mathf.Lerp(0.3f, 1f, t) * Mathf.Clamp01((1f - t) * 8f);
                s[i] = tone * env * 0.25f;
            }
            return Clip("cai_en_aim", s);
        }

        /// <summary>플라즈마 발사: 하강 피치 지잽 + 노이즈 버스트.</summary>
        static AudioClip BuildEnemyFire()
        {
            const float dur = 0.25f;
            int n = (int)(dur * SR);
            var s = new float[n];
            var rng = new System.Random(9);
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float f = Mathf.Lerp(1200f, 200f, Mathf.Sqrt(t));   // 하강
                phase += 6.2832f * f / SR;
                float tone = Mathf.Sin(phase);
                float noise = (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-t * 30f);
                float env = Mathf.Exp(-t * 10f);
                s[i] = (tone * 0.6f + noise * 0.5f) * env;
            }
            return Clip("cai_en_fire", s);
        }

        /// <summary>피격 신음: 짧은 유기적 그런트(하강).</summary>
        /// <summary>
        /// 발 딛는 소리 — 무거운 금속 발이 바닥을 치는 짧은 "쿵". 저음 임팩트 + 금속 잔향.
        /// ★ 임시 합성음이다. 실제 에셋이 오면 이 함수 대신 클립을 물리면 된다.
        /// </summary>
        static AudioClip BuildEnemyStep()
        {
            const float dur = 0.13f;
            int n = (int)(dur * SR);
            var s = new float[n];
            var rng = new System.Random(31);
            float ph = 0f, ph2 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                // 저음 쿵 — 빠르게 떨어지는 사인
                float f = Mathf.Lerp(120f, 55f, t);
                ph += 6.2832f * f / SR;
                float thud = Mathf.Sin(ph) * Mathf.Exp(-t * 26f);
                // 금속 잔향 — 높은 배음이 짧게
                ph2 += 6.2832f * 1900f / SR;
                float ring = Mathf.Sin(ph2) * Mathf.Exp(-t * 55f) * 0.18f;
                // 접촉 순간의 잡음
                float grit = (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-t * 90f) * 0.22f;
                s[i] = (thud + ring + grit) * 0.8f;
            }
            return Clip("cai_en_step", s);
        }

        static AudioClip BuildEnemyPain()
        {
            const float dur = 0.15f;
            int n = (int)(dur * SR);
            var s = new float[n];
            var rng = new System.Random(10);
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float f = Mathf.Lerp(220f, 150f, t);         // 하강
                phase += 6.2832f * f / SR;
                float am = 0.6f + 0.4f * (float)(rng.NextDouble() * 2 - 1);   // 거친 진폭변조
                float env = Mathf.Clamp01(t * 20f) * Mathf.Exp(-t * 12f);
                s[i] = Mathf.Sin(phase) * am * env * 0.6f;
            }
            return Clip("cai_en_pain", s);
        }

        /// <summary>플레이어 피격: 낮은 "억" 신음 + 저음 임팩트(적 신음보다 더 낮고 묵직).</summary>
        static AudioClip BuildPlayerHurt()
        {
            const float dur = 0.18f;
            int n = (int)(dur * SR);
            var s = new float[n];
            var rng = new System.Random(12);
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float ti = (float)i / SR;
                float f = Mathf.Lerp(180f, 110f, t);         // 하강(적 신음 220→150보다 낮음)
                phase += 6.2832f * f / SR;
                float am = 0.6f + 0.4f * (float)(rng.NextDouble() * 2 - 1);
                float thud = Mathf.Sin(6.2832f * 70f * ti) * Mathf.Exp(-t * 20f);   // 묵직한 저음
                float env = Mathf.Clamp01(t * 20f) * Mathf.Exp(-t * 10f);
                s[i] = (Mathf.Sin(phase) * am * 0.6f + thud * 0.5f) * env;
            }
            return Clip("cai_player_hurt", s);
        }

        /// <summary>예지 발동: 살짝 상승하는 화음 스웰 + 고음 시머(시간정지 느낌). 전투음과 구분.</summary>
        static AudioClip BuildPrediction()
        {
            const float dur = 0.45f;
            int n = (int)(dur * SR);
            var s = new float[n];
            float p1 = 0f, p2 = 0f, p3 = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float bend = Mathf.Lerp(1f, 1.06f, t);          // 살짝 상승
                p1 += 6.2832f * 392f * bend / SR;
                p2 += 6.2832f * 587f * bend / SR;
                p3 += 6.2832f * 784f * bend / SR;
                float chord = Mathf.Sin(p1) + 0.7f * Mathf.Sin(p2) + 0.5f * Mathf.Sin(p3);
                float shimmer = 0.15f * Mathf.Sin(6.2832f * 40f * t) * Mathf.Sin(p3 * 1.5f);
                float env = t < 0.7f ? Mathf.Pow(t / 0.7f, 0.7f)      // 스웰 인
                                     : Mathf.Lerp(1f, 0f, (t - 0.7f) / 0.3f);
                s[i] = (chord * 0.22f + shimmer) * env;
            }
            return Clip("cai_prediction", s);
        }

        /// <summary>
        /// 히어로 랜딩 쿠웅: 아래로 훑는 사인 붐(60→28Hz) + 파편 노이즈 꼬리.
        /// 전투 타격음(0.14초)보다 열 배 길게 끌어야 "무거운 것이 떨어졌다"로 들린다.
        /// (이동 착지음 landing과 별개 — 이건 등장 컷신 전용.)
        /// </summary>
        static AudioClip BuildLanding()
        {
            const float dur = 1.4f;
            int n = (int)(dur * SR);
            var s = new float[n];
            var rng = new System.Random(11);
            float phase = 0f, lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;

                // 붐 — 주파수를 아래로 훑으면 같은 음량에서도 훨씬 크게 들린다(폭발음의 기본형).
                float f = Mathf.Lerp(62f, 28f, Mathf.Sqrt(t));
                phase += 6.2832f * f / SR;
                float boom = Mathf.Sin(phase) * Mathf.Exp(-t * 4.2f);

                // 트랜지언트 — 맨 앞 20ms의 딱딱한 충돌음. 이게 없으면 붐이 흐물흐물하다.
                float crack = (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-t * 160f);

                // 파편 — 저역으로 눌린 노이즈가 천천히 사라진다(먼지가 가라앉는 소리).
                float white = (float)(rng.NextDouble() * 2 - 1);
                lp += (white - lp) * 0.10f;
                float debris = lp * Mathf.Exp(-t * 7f) * 0.5f;

                s[i] = boom * 0.85f + crack * 0.7f + debris;
            }
            return Clip("cai_landing", s);
        }

        static AudioClip Clip(string name, float[] samples)
        {
            // 클릭 방지: 끝 2ms 페이드아웃
            int fade = Mathf.Min(samples.Length, (int)(0.002f * SR));
            for (int i = 0; i < fade; i++)
                samples[samples.Length - 1 - i] *= (float)i / fade;
            for (int i = 0; i < samples.Length; i++)
                samples[i] = Mathf.Clamp(samples[i], -1f, 1f);

            var c = AudioClip.Create(name, samples.Length, 1, SR, false);
            c.SetData(samples, 0);
            return c;
        }
    }

    /// <summary>Play 시 오디오 자동 부착.</summary>
    public static class CombatAudioBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<CombatAudio>() == null)
                new GameObject("[CombatAudio]").AddComponent<CombatAudio>();
        }
    }
}
