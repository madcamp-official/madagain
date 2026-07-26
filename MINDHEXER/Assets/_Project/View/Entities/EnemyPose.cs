using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 상태 기반 절차 자세 — <b>텔레그래프</b>가 핵심이다.
    ///
    /// 지금 몹은 공격 예비·판정·후딜이 한 클립이라 "언제 피해야 하는지" 시각 단서가 발광뿐이다.
    /// Sim에는 이미 충분한 시간 창이 있으므로(근접 선딜 0.4s · 돌진 준비 0.75s · 조준 2.0s)
    /// 그 진행도를 읽어 자세를 얹는다.
    ///
    ///   B1 근접 예비  — 뒤로 젖히며 힘을 모음
    ///   C1 돌진 준비  — 더 크게 젖히고 웅크림 + 진동
    ///   D1 차징       — 버티는 자세 + 진동이 점점 커짐
    ///   E1 피격       — 맞은 방향으로 젖혀졌다 스프링 복귀
    ///
    /// ── 적용 방식 ──
    /// EnemyMotion.Rot과 같은 규약: <c>t.localRotation *= Euler(...)</c> 로 <b>곱해서 얹는다</b>.
    /// 그래서 시선 추적(LateSync에서 먼저 실행) 결과를 지우지 않고 그 위에 쌓인다.
    /// 위치(웅크림)는 뷰 트랜스폼에 더한다 — 다음 프레임 Sync가 다시 써주므로 누적되지 않는다.
    ///
    /// 실행 순서: 발광 → 시선 추적 → <b>이 자세</b> → 녹슨 관절(이 결과를 삐걱대며 뒤따름)
    /// </summary>
    public struct EnemyPose
    {
        public bool bound;
        Transform hips, spine1, spine2;

        // 피격 반동 스프링(도) — 각도 오프셋과 각속도
        Vector2 hitOff, hitVel;     // x=pitch(앞뒤), y=yaw(좌우)
        int     prevHealth;
        float   phase;              // 개체별 진동 위상(personality 고정 → 군무처럼 안 보임)

        /// <summary>척추 계열 본을 찾아 캐시. 개체당 1회. 이름 규칙은 EnemyMotion과 같은 방식.</summary>
        public void Bind(Transform root, float personality)
        {
            bound = true;
            hips = spine1 = spine2 = null;
            prevHealth = int.MinValue;
            phase = personality * 6.283f;
            if (root == null) return;

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                if (hips == null && (Has(n, "hips") || Has(n, "pelvis"))) hips = t;
                else if (spine2 == null && (Has(n, "spine02") || Has(n, "spine2"))) spine2 = t;
                else if (spine1 == null && (Has(n, "spine01") || Has(n, "spine1"))) spine1 = t;
            }
            // Spine이 하나뿐인 리그면 그것을 spine1으로
            if (spine1 == null && spine2 == null)
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (Has(t.name.ToLowerInvariant(), "spine")) { spine1 = t; break; }
        }

        static bool Has(string n, string k) => n.Contains(k);
        public bool HasBones => hips != null || spine1 != null || spine2 != null;

        /// <summary>지금 무슨 연출이 걸려 있는지(디버그 표시용).</summary>
        public string Label { get; private set; }

        /// <summary>
        /// 상태를 읽어 자세를 얹는다. 반드시 <b>시선 추적 뒤</b>(LateSync)에서 부를 것.
        /// </summary>
        /// <param name="aimTwist">
        /// 조준 중 상체 비틀림(도). 발은 고정된 채 총구를 따라가는 각으로, EntityViews가 계산해 넘긴다.
        /// 0이면 비틀지 않는다.
        /// </param>
        public void Apply(Transform view, in EnemySim e, in EnemyPoseSettings s, float dt, float now,
                          float aimTwist = 0f)
        {
            Label = "";
            if (!s.enabled) { Decay(dt); return; }

            // ── E1 피격: 체력이 줄면 뒤로 젖히는 임펄스 ──
            int hp = e.combat.health;
            if (prevHealth != int.MinValue && hp < prevHealth)
            {
                // 맞은 세기를 잃은 체력에 비례시킨다(한 번에 여러 대 맞을 수 있다)
                float k = Mathf.Min(3, prevHealth - hp) * s.hitKick;
                hitVel.x -= k;                                   // 뒤로 젖힘
                hitVel.y += (phase % 1f > 0.5f ? 1f : -1f) * k * 0.4f;   // 좌우로도 살짝(개체마다 방향 고정)
            }
            prevHealth = hp;

            // 스프링 감쇠 — 한 번 크게 젖혔다 돌아온다(버둥거리지 않게 감쇠를 높게)
            hitVel += (-hitOff * s.hitStiff - hitVel * s.hitDamp) * dt;
            hitOff += hitVel * dt;

            float pitch = hitOff.x, yaw = hitOff.y, drop = 0f;
            if (Mathf.Abs(pitch) > 0.5f) Label = "피격";

            // ── 상태별 텔레그래프 ──
            var ai = e.ai;
            switch (ai.state)
            {
                case EnemyState.ChargeRun:
                    if (ai.mobility == MobilityType.Charge)
                    {
                        // C2 돌진 부스트 — 준비에서 뒤로 장전한 몸을 앞으로 폭발시킨다(라인하르트 돌진).
                        //   상체를 크게 앞으로 다이브시키고 낮게 깔아, 뒤에서 밀려 날아가는 실루엣을 만든다.
                        //   진입 직후 최대치로 튀도록 짧게(≈0.15s)만 ease-in 한다.
                        float t = Prog(ai.stateTicks, Mathf.Max(1, AIConfig.ChargeWindupTicks / 5));
                        pitch += s.chargeBoostLean * t;                   // 앞으로 크게 숙임(양수 = 앞)
                        drop  += -s.chargeBoostDrop * t;                  // 낮게 깔림
                        pitch += Shake(now, s.chargeBoostShakeRate) * s.chargeBoostShake; // 전방 추진 진동
                        Label = "돌진!";
                    }
                    break;

                case EnemyState.Windup:
                    if (ai.mobility == MobilityType.Charge)
                    {
                        // C1 돌진 준비 — 뒤로 크게 장전하며 깊게 웅크린다. 끝으로 갈수록 진동이 커진다.
                        float t = Prog(ai.stateTicks, AIConfig.ChargeWindupTicks);
                        float ease = Ease(t);
                        pitch += -s.chargeLean * ease;                    // 뒤로 젖힘(스프링 장전)
                        drop  += -s.chargeCrouch * ease;                  // 깊게 웅크림
                        pitch += Shake(now, s.chargeShakeRate) * s.chargeShake * ease;
                        Label = $"돌진준비 {t * 100f:0}%";
                    }
                    else
                    {
                        // B1 근접 예비 — 무기를 드는 예비. 돌진보다 작고 빠르다.
                        float t = Prog(ai.stateTicks, AIConfig.MeleeWindupTicks);
                        float ease = Ease(t);
                        pitch += -s.meleeLean * ease;
                        pitch += Shake(now, s.meleeShakeRate) * s.meleeShake * ease;
                        Label = $"근접예비 {t * 100f:0}%";
                    }
                    break;

                case EnemyState.Aim:
                {
                    // D1 차징 — 2초는 길다. 서서히 버티는 자세로 기울며 진동이 커진다.
                    int total = ai.mobility == MobilityType.Flying
                              ? AIConfig.FlyAimTicks : AIConfig.RangedAimTicks;
                    float t = Prog(ai.stateTicks, total);
                    float ease = Ease(t);
                    pitch += -s.aimLean * ease;                           // 반동 대비해 뒤로
                    pitch += Shake(now, s.aimShakeRate) * s.aimShake * ease;
                    Label = $"차징 {t * 100f:0}%";
                    break;
                }
            }

            // ── 조준 중 상체 비틀기 ──
            // 발은 EntityViews가 고정해 두었고, 여기서 그 차이를 척추에 나눠 준다.
            // 허리보다 가슴이 더 비틀려야 자연스럽다(사람이 몸을 트는 방식).
            if (Mathf.Abs(aimTwist) > 0.01f)
            {
                yaw += aimTwist * s.twistShare;
                Rot(spine2, 0f, aimTwist * (1f - s.twistShare));   // 나머지는 가슴에 몰아준다
            }

            // ── 본에 얹기 ──
            // 척추 위쪽일수록 많이 기울어야 자연스럽다(허리보다 가슴이 더 젖혀진다).
            Rot(hips,   pitch * s.shareHips,   yaw * s.shareHips);
            Rot(spine1, pitch * s.shareSpine1, yaw * s.shareSpine1);
            Rot(spine2, pitch * s.shareSpine2, yaw * s.shareSpine2);

            if (view != null && Mathf.Abs(drop) > 1e-4f)
                view.position += Vector3.up * drop;   // Sync가 매 프레임 다시 쓰므로 누적되지 않는다
        }

        /// <summary>기능이 꺼져 있을 때 — 남은 반동만 정리한다(켤 때 튀지 않게).</summary>
        void Decay(float dt)
        {
            hitOff = Vector2.Lerp(hitOff, Vector2.zero, Mathf.Clamp01(8f * dt));
            hitVel = Vector2.zero;
        }

        static float Prog(int ticks, int total) => Mathf.Clamp01(ticks / (float)Mathf.Max(1, total));

        /// <summary>예비 동작 곡선 — 천천히 시작해 끝에서 최대(가속). 해방 직전이 가장 크다.</summary>
        static float Ease(float t) => t * t;

        /// <summary>결정론적 진동. 개체 위상이 섞여 여러 마리가 같은 박자로 떨지 않는다.</summary>
        float Shake(float now, float rate) => Mathf.Sin(now * rate + phase);

        static void Rot(Transform t, float pitch, float yaw)
        {
            if (t == null || (Mathf.Abs(pitch) < 1e-4f && Mathf.Abs(yaw) < 1e-4f)) return;
            // EnemyMotion.Rot과 같은 규약 — 이미 써 있는 회전 위에 곱해서 얹는다.
            t.localRotation = t.localRotation * Quaternion.Euler(pitch, yaw, 0f);
        }
    }

    /// <summary>상태 자세 튜닝값. 콘솔 <c>tele</c> · F10 패널이 만진다.</summary>
    [System.Serializable]
    public struct EnemyPoseSettings
    {
        public bool  enabled;

        // B1 근접 예비
        public float meleeLean;        // 뒤로 젖히는 각(도)
        public float meleeShake;       // 진동 폭(도)
        public float meleeShakeRate;   // 진동 속도

        // C1 돌진 준비
        public float chargeLean;
        public float chargeCrouch;     // 웅크리며 내려가는 양(m)
        public float chargeShake;
        public float chargeShakeRate;

        // C2 돌진 부스트(ChargeRun) — 앞으로 다이브 + 낮게 깔림
        public float chargeBoostLean;      // 앞으로 숙이는 각(도)
        public float chargeBoostDrop;      // 낮게 깔리는 양(m)
        public float chargeBoostShake;     // 전방 추진 진동 폭(도)
        public float chargeBoostShakeRate;

        // D1 차징
        public float aimLean;
        public float aimShake;
        public float aimShakeRate;

        // E1 피격
        public float hitKick;          // 임펄스 세기(도/초)
        public float hitStiff;         // 복귀 강성
        public float hitDamp;          // 감쇠 — 높게 잡아 한 번만 젖혔다 돌아오게(버둥거림 방지)

        // 본 분산 — 위쪽 척추가 더 많이 기운다
        public float shareHips, shareSpine1, shareSpine2;
        // 조준 비틀림을 아래 척추(hips·spine1)가 가져가는 비율. 나머지는 가슴(spine2)이 받는다.
        public float twistShare;

        public static EnemyPoseSettings Default => new EnemyPoseSettings
        {
            enabled = true,

            meleeLean = 14f, meleeShake = 1.2f, meleeShakeRate = 34f,

            // 뒤로 크게 장전(35°) + 깊게 웅크림(0.35m). 진동도 키워 "터지기 직전" 긴장감.
            chargeLean = 35f, chargeCrouch = 0.35f, chargeShake = 3.5f, chargeShakeRate = 28f,
            // 돌진 순간 앞으로 28° 다이브 + 0.15m 낮게. drop은 발이 바닥을 뚫지 않게 보수적으로 시작.
            chargeBoostLean = 28f, chargeBoostDrop = 0.15f, chargeBoostShake = 2f, chargeBoostShakeRate = 34f,

            aimLean = 9f, aimShake = 1.6f, aimShakeRate = 30f,

            // 감쇠비 = damp / (2√stiff) = 26 / (2√300) ≈ 0.75 → 살짝만 흔들리고 곧 정착
            hitKick = 190f, hitStiff = 300f, hitDamp = 26f,

            shareHips = 0.25f, shareSpine1 = 0.35f, shareSpine2 = 0.4f,
            twistShare = 0.45f,   // 45%는 허리·하부척추, 55%는 가슴이 비튼다
        };
    }
}
