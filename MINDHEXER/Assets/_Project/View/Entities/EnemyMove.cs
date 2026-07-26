using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 이동·상황 절차 자세 (2~4순위). 텔레그래프(EnemyPose)와 분리해 둔 이유는
    /// <b>텔레그래프는 게임플레이 정보</b>고 이쪽은 <b>생동감</b>이라, 끄고 켜는 기준이 다르기 때문이다.
    ///
    ///   A1 급선회   — 방향이 꺾이면 원래 방향으로 남았다가 따라옴(바깥으로 기욺)
    ///   A2 가감속   — 출발 시 뒤로 남고, 멈출 때 앞으로 쏠림
    ///   A3 경사     — 빠를수록 상체가 앞으로
    ///   A4 걸음     — 속도 위상으로 좌우 무게 이동
    ///   A6 벽막힘   — 의도만큼 못 가면 상체가 <b>뒤로</b> 젖혀짐(부딪히면 상체가 먼저 선다)
    ///   C3 휘청     — 돌진 후딜. 명중/빗나감으로 세기가 다름
    ///   D2 반동     — 발사 순간 뒤로 킥
    ///   E3 붙잡힘   — 찌르기 표적으로 고정된 동안 버둥거림
    ///   E5 저체력   — 체력이 낮을수록 늘어짐
    ///   F1 뱅킹     — 공중몹이 속도 방향으로 기욺
    ///   F2 고도     — 상승 시 세우고 하강 시 숙임
    ///   G2 스폰펄스 — 날아오는 동안 허우적
    ///   G5 대형     — 모든 반응을 무겁게(지연·감쇠 증가)
    ///   G6 개성     — personality로 전체 강도에 편차
    ///
    /// EnemyPose와 같은 규약으로 <c>localRotation *= Euler(...)</c> 로 얹는다.
    /// </summary>
    public struct EnemyMove
    {
        public bool bound;
        Transform hips, spine1, spine2;
        // 공중몹 전용 — 이 모델(CrimsonSentinelBiped)은 <b>날개 본이 없는 인간형</b>이라
        // 팔을 날개처럼 쓴다. 없으면 그냥 건너뛴다.
        Transform armL, armR;

        // 상태
        Vector3 prevDir;            // 직전 이동 방향(급선회 감지)
        float   prevSpeed;          // 직전 속도(가감속 감지)
        float   bobPhase;
        float   turnCur, accelCur;  // 스프링 추적값(도)
        Vector2 kickOff, kickVel;   // 충격(휘청·반동·벽) 스프링 — x=pitch, y=roll
        byte    prevState;
        int     prevBind;
        float   personality;
        float   heavy;              // 대형이면 1, 아니면 0 (반응을 무겁게)

        public void Bind(Transform root, in EnemySim e)
        {
            bound = true;
            hips = spine1 = spine2 = null;
            prevDir = Vector3.zero; prevSpeed = 0f;
            turnCur = accelCur = 0f;
            kickOff = kickVel = Vector2.zero;
            prevState = 255; prevBind = 0;
            personality = e.personality;
            heavy = e.ai.size == SizeClass.Large ? 1f : 0f;
            if (root == null) return;

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                if (hips == null && (n.Contains("hips") || n.Contains("pelvis"))) hips = t;
                else if (spine2 == null && (n.Contains("spine02") || n.Contains("spine2"))) spine2 = t;
                else if (spine1 == null && (n.Contains("spine01") || n.Contains("spine1"))) spine1 = t;
                // 위팔만 — "leftforearm"은 "leftarm"을 포함하지 않으므로 이 검사로 아래팔과 구분된다.
                else if (armL == null && n.Contains("leftarm"))  armL = t;
                else if (armR == null && n.Contains("rightarm")) armR = t;
            }
            if (spine1 == null && spine2 == null)
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name.ToLowerInvariant().Contains("spine")) { spine1 = t; break; }
        }

        public bool HasBones => hips != null || spine1 != null || spine2 != null;
        public string Label { get; private set; }

        /// <summary>
        /// speed·dir·vertSpeed는 EntityViews가 틱 차분으로 잰 실측값이다
        /// (e.vel 수평 성분은 공중몹 말고는 항상 0이라 못 쓴다).
        /// </summary>
        public void Apply(Transform view, in EnemySim e, ViewKindTag kind,
                          float speed, Vector3 dir, float vertSpeed,
                          in EnemyMoveSettings s, float dt, float now)
        {
            Label = "";
            if (!s.enabled) { Relax(dt); return; }

            // 개체 편차 + 대형 무게 — 모든 반응에 곱해지는 배율(G5·G6)
            float ind = Mathf.Lerp(1f - s.jitter, 1f + s.jitter, personality);
            float slow = 1f / (1f + heavy * s.heavyLag);      // 대형은 반응이 굼뜸
            float amp  = ind * (1f + heavy * s.heavyAmp);     // 대형은 폭이 큼

            float pitch = 0f, roll = 0f, yaw = 0f;

            // ── A1 급선회 : 방향 변화율만큼 바깥으로 기욺 ──
            if (dir.sqrMagnitude > 1e-6f && prevDir.sqrMagnitude > 1e-6f && speed > s.moveMin)
            {
                // 부호 있는 회전각(도/초) — 왼쪽으로 꺾으면 음수
                float turnRate = Vector3.SignedAngle(prevDir, dir, Vector3.up) / Mathf.Max(1e-4f, dt);
                float want = Mathf.Clamp(-turnRate * s.turnLean, -s.turnMax, s.turnMax);
                turnCur = Mathf.Lerp(turnCur, want, Mathf.Clamp01(s.turnSpring * slow * dt));
            }
            else turnCur = Mathf.Lerp(turnCur, 0f, Mathf.Clamp01(s.turnSpring * slow * dt));
            roll += turnCur * amp;
            if (dir.sqrMagnitude > 1e-6f) prevDir = dir;

            // ── A2 가감속 : 속도 변화율. 출발=뒤로 남음 / 정지=앞으로 쏠림 ──
            float accel = (speed - prevSpeed) / Mathf.Max(1e-4f, dt);
            prevSpeed = speed;
            {
                float want = Mathf.Clamp(-accel * s.accelLean, -s.accelMax, s.accelMax);
                accelCur = Mathf.Lerp(accelCur, want, Mathf.Clamp01(s.accelSpring * slow * dt));
            }
            pitch += accelCur * amp;

            // ── A3 경사 : 빠를수록 앞으로 ──
            float sp01 = Mathf.Clamp01(speed / Mathf.Max(0.1f, s.refSpeed));
            pitch += s.runLean * sp01 * amp;

            // ── A4 걸음 : 좌우 무게 이동(3인칭이라 상하보다 이게 잘 보인다) ──
            if (sp01 > 0.01f && e.grounded)
            {
                bobPhase += dt * speed * s.bobRate;
                roll += Mathf.Sin(bobPhase) * s.bobRoll * sp01 * amp;
                pitch += Mathf.Sin(bobPhase * 2f) * s.bobPitch * sp01 * amp;
            }

            // ── A6 벽막힘 : 가려던 것보다 훨씬 못 갔으면 상체가 뒤로 젖혀진다 ──
            // (부딪히면 상체가 먼저 서고 하체가 계속 와서 뒤로 밀린다 — 감속과 방향이 반대)
            if (s.wallLean > 0f && e.hasWaypoint && speed < s.moveMin && prevSpeed > s.moveMin * 2f)
                kickVel.x -= s.wallLean * amp;

            // ── 상태 전이 ──
            byte st = (byte)e.ai.state;
            if (st != prevState)
            {
                // C3 휘청 : 돌진 후딜 진입. 명중이면 짧고 세게, 빗나감(=벽/헛침)이면 더 크게
                if (e.ai.mobility == MobilityType.Charge && e.ai.state == EnemyState.Recovery)
                {
                    // 돌진 관성이 그대로 몸에 실린 채 급정거 — 앞뒤로 크게 젖혀지고 좌우로도 크게 꺾인다.
                    // 세기는 크게, 감쇠(kickDamp)는 그대로 높아 한 번 크게 휘청였다 정착한다(버둥 금지).
                    float k = e.ai.hitDone ? s.staggerHit : s.staggerMiss;
                    kickVel.x -= k * amp;
                    kickVel.y += (personality > 0.5f ? 1f : -1f) * k * 0.85f * amp;   // 좌우 비틀림 강화
                    Label = e.ai.hitDone ? "휘청(명중)" : "휘청(빗나감)";
                }
                // D2 반동 : 발사 순간
                if (e.ai.state == EnemyState.Fire)
                {
                    kickVel.x -= s.recoil * amp;
                    Label = "반동";
                }
                prevState = st;
            }

            // ── E3 붙잡힘 : 찌르기 표적으로 고정된 동안 버둥거림 ──
            if (e.combat.bindTicks > 0)
            {
                float f = Mathf.Sin(now * s.bindRate + personality * 6.283f);
                roll  += f * s.bindShake * amp;
                pitch += Mathf.Sin(now * s.bindRate * 1.7f) * s.bindShake * 0.6f * amp;
                Label = "붙잡힘";
            }
            prevBind = e.combat.bindTicks;

            // ── E5 저체력 : 체력이 낮을수록 늘어진다 ──
            if (s.lowHpDroop > 0f)
            {
                int max = Mathf.Max(1, MaxHpOf(e));
                float hp01 = Mathf.Clamp01(e.combat.health / (float)max);
                float droop = (1f - hp01) * s.lowHpDroop;
                pitch += droop * amp;                                   // 앞으로 수그림
                roll  += Mathf.Sin(now * 1.3f + personality * 6.283f) * droop * 0.25f;
                if (hp01 < 0.5f && Label == "") Label = "저체력";
            }

            // ── F1·F2 공중 : 진행 방향으로 굽고, 좌우로 기울고, 팔(=날개)이 따라 돈다 ──
            // 이 모델은 날개 본이 없는 인간형이라 위팔을 날개처럼 쓴다.
            if (kind == ViewKindTag.Flying)
            {
                // 공중몹은 e.vel 수평 성분을 실제로 유지한다(관성 이동) — 그대로 쓴다
                Vector3 v = new Vector3(e.vel.x, 0f, e.vel.z);
                float yawRad = e.yaw * Mathf.Deg2Rad;
                Vector3 fwd   = new Vector3(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad));
                Vector3 right = new Vector3(Mathf.Cos(yawRad), 0f, -Mathf.Sin(yawRad));

                float side = 0f, ahead = 0f;
                if (v.sqrMagnitude > 1e-4f)
                {
                    side  = Vector3.Dot(v, right);
                    ahead = Vector3.Dot(v, fwd);
                    roll += Mathf.Clamp(side * s.bankLean, -s.bankMax, s.bankMax) * amp;
                }
                // 진행 방향으로 몸을 굽힌다 — 앞으로 갈수록 숙이고, 뒤로 물러나면 젖힌다
                pitch += Mathf.Clamp(ahead * s.flyDiveLean, -s.flyDiveMax, s.flyDiveMax) * amp;
                // 고도 변화 — 오르면 세우고 내리면 숙인다
                pitch += Mathf.Clamp(-vertSpeed * s.climbLean, -s.climbMax, s.climbMax) * amp;

                // ── 팔(날개) ──
                // 선회하면 바깥쪽 팔을 들고 안쪽 팔을 내린다(뱅킹하는 새처럼).
                // 빠를수록 뒤로 젖혀 접는다(항력).
                if (armL != null || armR != null)
                {
                    float bank  = Mathf.Clamp(side * s.wingBank, -s.wingMax, s.wingMax) * amp;
                    float sweep = Mathf.Clamp(Mathf.Abs(ahead) * s.wingSweep, 0f, s.wingMax) * amp;
                    float flap  = Mathf.Sin(now * s.wingIdleRate + personality * 6.283f) * s.wingIdle;
                    Rot(armL, sweep, 0f,  bank + flap);
                    Rot(armR, sweep, 0f, -bank - flap);
                }
            }

            // ── G2 스폰 펄스 : 날아오는 동안 허우적 ──
            if (e.launchTicks > 0)
            {
                roll  += Mathf.Sin(now * s.launchRate) * s.launchShake * amp;
                pitch += Mathf.Sin(now * s.launchRate * 1.4f + 1f) * s.launchShake * amp;
                Label = "발사중";
            }

            // ── 충격 스프링(휘청·반동·벽) ──
            kickVel += (-kickOff * s.kickStiff - kickVel * (s.kickDamp * (1f + heavy * 0.5f))) * dt;
            kickOff += kickVel * dt;
            pitch += kickOff.x;
            roll  += kickOff.y;

            // ── 본에 분산 ──
            Rot(hips,   pitch * s.shareHips,   yaw, roll * s.shareHips);
            Rot(spine1, pitch * s.shareSpine1, yaw, roll * s.shareSpine1);
            Rot(spine2, pitch * s.shareSpine2, yaw, roll * s.shareSpine2);
        }

        /// <summary>몹 크기별 최대 체력(스폰값과 같은 규칙 — 저체력 비율 계산용).</summary>
        static int MaxHpOf(in EnemySim e) => e.ai.size == SizeClass.Large ? 4 : 2;

        void Relax(float dt)
        {
            float k = Mathf.Clamp01(8f * dt);
            turnCur = Mathf.Lerp(turnCur, 0f, k);
            accelCur = Mathf.Lerp(accelCur, 0f, k);
            kickOff = Vector2.Lerp(kickOff, Vector2.zero, k);
            kickVel = Vector2.zero;
        }

        static void Rot(Transform t, float pitch, float yaw, float roll)
        {
            if (t == null) return;
            if (Mathf.Abs(pitch) < 1e-4f && Mathf.Abs(yaw) < 1e-4f && Mathf.Abs(roll) < 1e-4f) return;
            t.localRotation = t.localRotation * Quaternion.Euler(pitch, yaw, roll);
        }
    }

    /// <summary>EntityViews의 내부 enum을 EnemyMove가 볼 수 있게 하는 최소 태그.</summary>
    public enum ViewKindTag : byte { Other = 0, Flying = 1, Charge = 2 }

    /// <summary>이동·상황 자세 튜닝값. 콘솔 <c>mv</c>가 만진다.</summary>
    [System.Serializable]
    public struct EnemyMoveSettings
    {
        public bool  enabled;
        public float moveMin;        // 이 속도 이하는 "정지"로 본다

        // A1 급선회
        public float turnLean, turnMax, turnSpring;
        // A2 가감속
        public float accelLean, accelMax, accelSpring;
        // A3 경사
        public float runLean, refSpeed;
        // A4 걸음
        public float bobRoll, bobPitch, bobRate;
        // A6 벽막힘
        public float wallLean;
        // C3 휘청 / D2 반동
        public float staggerHit, staggerMiss, recoil;
        // E3 붙잡힘
        public float bindShake, bindRate;
        // E5 저체력
        public float lowHpDroop;
        // F1·F2 공중
        public float bankLean, bankMax, climbLean, climbMax;
        // 공중 — 진행 방향 굽힘 + 팔(날개). 이 모델은 날개 본이 없어 위팔을 쓴다.
        public float flyDiveLean, flyDiveMax;      // 나아가는 방향으로 숙임
        public float wingBank, wingSweep, wingMax; // 선회 시 좌우 팔 높낮이 / 속도에 따른 뒤로 젖힘
        public float wingIdle, wingIdleRate;       // 정지 중 아주 미세한 흔들림(살아 있는 느낌)
        // G2 스폰 펄스
        public float launchShake, launchRate;
        // 충격 스프링 공통
        public float kickStiff, kickDamp;
        // G5 대형 / G6 개성
        public float heavyLag, heavyAmp, jitter;
        // 본 분산
        public float shareHips, shareSpine1, shareSpine2;

        public static EnemyMoveSettings Default => new EnemyMoveSettings
        {
            enabled = true, moveMin = 0.4f,

            turnLean = 0.035f, turnMax = 14f, turnSpring = 6f,
            accelLean = 0.5f,  accelMax = 10f, accelSpring = 7f,
            runLean = 7f, refSpeed = 5f,
            bobRoll = 2.2f, bobPitch = 1.0f, bobRate = 0.9f,
            wallLean = 60f,

            // 명중 280 / 빗나감 400 — 돌진 급정거를 훨씬 과격하게(기존 150/230에서 대폭 상향).
            staggerHit = 280f, staggerMiss = 400f, recoil = 130f,
            bindShake = 4.5f, bindRate = 17f,
            lowHpDroop = 6f,

            bankLean = 3.2f, bankMax = 18f, climbLean = 2.4f, climbMax = 14f,
            flyDiveLean = 2.8f, flyDiveMax = 16f,
            wingBank = 5f, wingSweep = 3.5f, wingMax = 28f,
            wingIdle = 1.6f, wingIdleRate = 1.9f,
            launchShake = 9f, launchRate = 12f,

            // 감쇠비 ≈ 0.72 — 한 번 크게 흔들리고 정착(버둥거리지 않게)
            kickStiff = 260f, kickDamp = 23f,

            heavyLag = 0.8f, heavyAmp = 0.35f, jitter = 0.25f,
            shareHips = 0.3f, shareSpine1 = 0.33f, shareSpine2 = 0.37f,
        };
    }
}
