using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 돌진몹(ObsidianSentinel) 전용 절차 애니메이션 — <b>상체(척추·머리)만</b> 구동한다.
    ///
    /// 기본(<see cref="UseRushLegs"/>=true): Animator를 <b>끄지 않고</b> 다리는 러쉬/Idle 클립이 굴리게 두고,
    ///   매 프레임 척추·머리만 rest로 되돌린 뒤 절차 자세로 덮는다(레이어링).
    /// UseRushLegs=false: Animator를 꺼서 다리를 rest로 고정 — 러쉬를 한 줄로 제거하는 폴백.
    ///
    /// 자세:
    ///   준비 = 등을 말아 코일 + <b>머리를 스프링으로 빡 내리고</b> 크게 들이마심(가슴 부풀). 그 뒤 돌진.
    ///   돌진 = 코일 유지 + 머리 아래로 박은 채 러쉬 다리로 전진.
    ///   이후 = 큰 임펄스 + 지속 진동으로 <b>회복 시간 내내</b> 휘청이며 자세를 잡는다(끝에서 정착).
    ///
    /// 전환 스냅 방지: 단계가 끝나도 즉시 릴리즈하지 않고 코일·머리·휘청을 0으로 감쇠(<see cref="Settling"/>)시키며
    ///   상체 오버라이드를 잠깐 더 유지 → 러쉬/걷기 클립으로 부드럽게 복귀한다.
    ///
    /// 굴곡은 몸의 좌우축(view.right) 기준 월드 회전(Flex). bone.localRotation만 건드려 루트·물리 무영향.
    /// </summary>
    public struct ChargeAnim
    {
        public static bool UseRushLegs = true;

        Transform hips, spineLo, spineMi, spineUp, head;
        Transform legUL, legLL, legUR, legLR, legFL, legFR, armUL, armUR;
        Animator  anim;

        Quaternion rSpineLo, rSpineMi, rSpineUp, rHead;
        Quaternion rLegUL, rLegLL, rLegUR, rLegLR, rLegFL, rLegFR, rArmUL, rArmUR;

        public bool bound;
        bool  animOff;
        float phase;
        byte  prevState;
        float tPhase;
        float sCurl, sBreath;      // 스무딩된 코일 / 들숨
        float hOff, hVel;          // 머리 스프링(빡 내림)
        Vector2 kick, kickVel;     // 휘청 스프링

        public string Label { get; private set; }

        /// <summary>단계가 끝나도 아직 자세가 남아 감쇠 중인가(꼬리 페이드 유지 조건).</summary>
        public bool Settling => bound &&
            (Mathf.Abs(sCurl) > 0.5f || Mathf.Abs(hOff) > 0.5f ||
             Mathf.Abs(sBreath) > 0.5f || kick.sqrMagnitude > 0.25f);

        public void BindRest(Animator a)
        {
            anim = a; bound = false;
            if (a == null) return;
            hips    = a.GetBoneTransform(HumanBodyBones.Hips);
            spineLo = a.GetBoneTransform(HumanBodyBones.Spine);
            spineMi = a.GetBoneTransform(HumanBodyBones.Chest);
            spineUp = a.GetBoneTransform(HumanBodyBones.UpperChest);
            head    = a.GetBoneTransform(HumanBodyBones.Head);
            legUL = a.GetBoneTransform(HumanBodyBones.LeftUpperLeg);  legLL = a.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            legUR = a.GetBoneTransform(HumanBodyBones.RightUpperLeg); legLR = a.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            legFL = a.GetBoneTransform(HumanBodyBones.LeftFoot);      legFR = a.GetBoneTransform(HumanBodyBones.RightFoot);
            armUL = a.GetBoneTransform(HumanBodyBones.LeftUpperArm);  armUR = a.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (spineLo == null && hips == null) return;

            rSpineLo = Loc(spineLo); rSpineMi = Loc(spineMi); rSpineUp = Loc(spineUp); rHead = Loc(head);
            rLegUL = Loc(legUL); rLegLL = Loc(legLL); rLegUR = Loc(legUR); rLegLR = Loc(legLR);
            rLegFL = Loc(legFL); rLegFR = Loc(legFR); rArmUL = Loc(armUL); rArmUR = Loc(armUR);
            bound = true;
        }

        static Quaternion Loc(Transform t) => t != null ? t.localRotation : Quaternion.identity;

        public bool HasBones => bound && (spineLo != null || hips != null);

        public static bool IsChargePhase(in EnemySim e)
            => e.ai.mobility == MobilityType.Charge &&
               (e.ai.state == EnemyState.Windup ||
                e.ai.state == EnemyState.ChargeRun ||
                e.ai.state == EnemyState.Recovery);

        public void Release()
        {
            if (animOff && anim != null) anim.enabled = true;
            animOff = false;
            sCurl = sBreath = hOff = hVel = 0f; kick = kickVel = Vector2.zero; tPhase = 0f;
            Label = "";
        }

        public void Apply(Transform view, in EnemySim e, in ChargeAnimSettings s, float dt, float now)
        {
            Label = "";
            if (!HasBones) return;
            if (!s.enabled) { Release(); return; }
            phase = e.personality * 6.2831853f;

            var ai = e.ai;
            bool phaseActive = IsChargePhase(in e);
            bool freezeLegs  = !UseRushLegs && phaseActive;   // 폴백: 다리 고정 모드에서만 Animator를 끈다

            if (freezeLegs) { if (anim != null && anim.enabled) { anim.enabled = false; animOff = true; } }
            else if (animOff && anim != null) { anim.enabled = true; animOff = false; }

            bool entered = prevState != (byte)ai.state;
            if (entered) tPhase = 0f;
            tPhase += dt;

            // ── 단계별 목표 ──
            float tCurl = 0f, tBreath = 0f, hTarget = 0f, tremor = 0f;
            float swayPitch = 0f, swayRoll = 0f;
            if (phaseActive)
            {
                switch (ai.state)
                {
                    case EnemyState.Windup:
                    {
                        float dur = Mathf.Max(0.05f, AIConfig.ChargeWindupTicks / 60f);
                        float p = Mathf.Clamp01(tPhase / dur);
                        tCurl  = s.wuCurl * (p * p);
                        tBreath = s.wuBreath * p;             // 들숨: 가슴 부풀며 최고조로
                        hTarget = s.wuHeadDown;               // 머리는 즉시 최대 목표 → 스프링이 빡 내림
                        tremor = Mathf.Sin(now * s.wuShakeRate + phase) * s.wuShake * p;
                        Label = $"돌진준비 {p * 100f:0}%";
                        break;
                    }
                    case EnemyState.ChargeRun:
                        tCurl = s.runCurl; hTarget = s.runHeadDown;
                        tremor = Mathf.Sin(now * s.boostShakeRate + phase) * s.boostShake;
                        Label = "돌진!";
                        break;
                    case EnemyState.Recovery:
                    {
                        if (entered)
                        {
                            float k = ai.hitDone ? s.stagHit : s.stagMiss;
                            kickVel.x -= k;
                            kickVel.y += (e.personality > 0.5f ? 1f : -1f) * k * s.stagSide;
                        }
                        // 회복 내내 흔들리다 정착 — 지속 진동을 시간에 따라 감쇠시켜 끝에서 잦아든다.
                        float dur = Mathf.Max(0.1f, (ai.hitDone ? AIConfig.ChargeHitRecovery : AIConfig.ChargeMissRecovery) / 60f);
                        float fade = 1f - Mathf.Clamp01(tPhase / dur);
                        swayPitch = Mathf.Sin(now * s.recWobbleRate + phase)        * s.recSwayAmp  * fade;
                        swayRoll  = Mathf.Sin(now * s.recWobbleRate * 0.8f + phase)  * s.recRollAmp  * fade;
                        Label = ai.hitDone ? "휘청(명중)" : "휘청(빗나감)";
                        break;
                    }
                }
            }
            // (phaseActive=false → 목표 전부 0: 꼬리 페이드로 부드럽게 복귀)

            // ── 스무딩 / 스프링 ──
            sCurl   = Approach(sCurl,   tCurl,   s.smoothRate, dt);
            sBreath = Approach(sBreath, tBreath, s.smoothRate, dt);
            hVel += (-(hOff - hTarget) * s.hStiff - hVel * s.hDamp) * dt;   // 머리 스프링(빠른 급강하+살짝 오버슛)
            hOff += hVel * dt;
            kickVel += (-kick * s.kickStiff - kickVel * s.kickDamp) * dt;
            kick += kickVel * dt;

            // ── 상체만 rest로 되돌린 뒤 재구성 ──
            Set(spineLo, rSpineLo); Set(spineMi, rSpineMi); Set(spineUp, rSpineUp); Set(head, rHead);
            if (freezeLegs)
            {
                Set(legUL, rLegUL); Set(legLL, rLegLL); Set(legUR, rLegUR); Set(legLR, rLegLR);
                Set(legFL, rLegFL); Set(legFR, rLegFR); Set(armUL, rArmUL); Set(armUR, rArmUR);
            }

            Vector3 side = view.right;

            // 척추 코일 + 진동 + 휘청(앞뒤 스프링 + 지속 진동)
            float curl = sCurl + tremor + kick.x + swayPitch;
            Flex(spineLo, side, curl * s.cShareLo);
            Flex(spineMi, side, curl * s.cShareMi);
            Flex(spineUp, side, curl * s.cShareUp - sBreath);   // 들숨: 가슴(위쪽)만 살짝 뒤로 열림
            Roll(spineMi, kick.y + swayRoll);                   // 휘청 좌우 꺾임

            // 머리 : 스프링으로 빡 내림(+ 휘청 성분)
            Flex(head, side, hOff + (kick.x + swayPitch) * s.cShareUp);

            prevState = (byte)ai.state;
        }

        static float Approach(float cur, float target, float rate, float dt)
            => Mathf.Lerp(cur, target, 1f - Mathf.Exp(-rate * dt));

        static void Flex(Transform t, Vector3 worldSideAxis, float deg)
        {
            if (t == null || Mathf.Abs(deg) < 1e-4f) return;
            Transform p = t.parent;
            Vector3 axis = p != null ? p.InverseTransformDirection(worldSideAxis) : worldSideAxis;
            t.localRotation = Quaternion.AngleAxis(deg, axis) * t.localRotation;
        }

        static void Roll(Transform t, float deg)
        {
            if (t == null || Mathf.Abs(deg) < 1e-4f) return;
            Transform p = t.parent;
            Vector3 axis = p != null ? p.InverseTransformDirection(t.up) : t.up;
            t.localRotation = Quaternion.AngleAxis(deg, axis) * t.localRotation;
        }

        static void Set(Transform t, Quaternion q) { if (t != null) t.localRotation = q; }
    }

    /// <summary>돌진 상체 절차 튜닝값.</summary>
    [System.Serializable]
    public struct ChargeAnimSettings
    {
        public bool  enabled;
        public float smoothRate;

        // C1 준비
        public float wuCurl, wuHeadDown, wuBreath, wuShake, wuShakeRate;
        // C2 돌진
        public float runCurl, runHeadDown, boostShake, boostShakeRate;
        // 머리 스프링(빡 내림)
        public float hStiff, hDamp;
        // C3 이후 — 큰 임펄스 + 회복 내내 지속 진동
        public float stagHit, stagMiss, stagSide, kickStiff, kickDamp;
        public float recWobbleRate, recSwayAmp, recRollAmp;
        // 척추 코일 분산
        public float cShareLo, cShareMi, cShareUp;

        public static ChargeAnimSettings Default => new ChargeAnimSettings
        {
            enabled = true,
            smoothRate = 15f,

            // 준비 : 완전히 말고(95) 머리 빡(55) + 크게 들숨(가슴 15° 열림)
            wuCurl = 95f, wuHeadDown = 55f, wuBreath = 15f, wuShake = 4f, wuShakeRate = 24f,
            // 돌진 : 더 감고 머리 더 박음
            runCurl = 110f, runHeadDown = 60f, boostShake = 4f, boostShakeRate = 26f,

            // 머리 스프링 : ωn≈32rad/s(≈0.1s), ζ≈0.5 → 빠르게 툭 내려가 살짝 튕김
            hStiff = 1000f, hDamp = 32f,

            // 이후 : 큰 임펄스(3배↑) + 낮은 감쇠로 여러 번 흔들림 + 회복 내내 지속 진동
            stagHit = 1800f, stagMiss = 2400f, stagSide = 0.8f,
            kickStiff = 260f, kickDamp = 12f,
            recWobbleRate = 9f, recSwayAmp = 14f, recRollAmp = 10f,

            cShareLo = 0.32f, cShareMi = 0.34f, cShareUp = 0.34f,
        };
    }
}
