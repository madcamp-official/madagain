namespace Game.Sim
{
    /// <summary>
    /// 몹 AI 세부 수치. ★ AI 세션 소유. 전부 잠정·튜닝. DOOM 비공개 수치는 상대값/텔레그래프로.
    ///
    /// ── const가 아니라 static인 이유 ──
    /// 밸런스는 "플레이하면서 슬라이더로 맞추는" 작업이라, const면 값 하나 바꿀 때마다 재컴파일이다.
    /// CombatConfig와 같은 방식으로 static으로 두고 F7 패널에서 실시간 조절한다.
    ///
    /// ★ <b>예측(예지)이 도는 중에는 값을 바꾸지 말 것.</b>
    ///   예측은 이 값들을 그대로 재실행하므로 결정론 자체는 유지되지만,
    ///   포크 도중에 바뀌면 앞뒤 틱이 다른 규칙으로 굴러 결과가 어긋난다.
    ///   (CombatConfig에 있는 것과 같은 제약)
    ///
    /// 틱 환산: <b>60틱 = 1초</b>.
    /// </summary>
    public static class AIConfig
    {
        // ── 근접 그런트 ──
        // 사거리 = 적 반경(개별) + 플레이어 반경 + 팔 길이. 대형몹은 반경이 커 사거리도 자동으로 커짐.
        //
        // ※ MeleeReach·MeleeHitExtra는 <b>확정값이 아니다 — 튜닝 대상</b>.
        //   붙어서 맞아보며 "닿을 듯 말 듯"한 지점을 찾아야 한다. 지금 값은 예전 잠정치를
        //   그대로 둔 것뿐이니 확정된 수치로 믿지 말 것.
        public static float MeleeReach         = 0.8f;  // 팔 길이   ★ 미확정 — 튜닝 대상
        public static float MeleeHitExtra      = 0.3f;  // 판정 여유 ★ 미확정 — 튜닝 대상
        public static int   MeleeWindupTicks   = 24;    // 0.40s 선딜·텔레그래프(committed 조준)
        public static int   MeleeActiveTicks   = 6;     // 0.10s 판정
        public static int   MeleeRecoveryTicks = 48;    // 0.80s 후딜
        public static float MeleeHitHalfAngle  = 50f;   // committed 방향 부채꼴
        public static int   MeleeDamage        = 1;

        /// <summary>개별 반경 기반 근접 사거리(대형몹 자동 반영).</summary>
        public static float MeleeRangeFor(float enemyRadius) => enemyRadius + SimConfig.PlayerRadius + MeleeReach;

        // ── 돌진 (핑키형) — 근접 × Charge. 완주(피격으로 안 끊김) ──
        // 반경 배율 — 1.0 = grunt(근접)와 완전히 동일한 히트박스. (예전 1.5는 옆으로 넓히던 값, 요청으로 원복)
        public static float ChargeRadiusMul    = 1.0f;
        // 돌진몹 몸집 배율 — ★ 이제 <b>렌더 전용</b>이다(히트박스엔 안 들어감).
        // EntityViews.visualScale / Dismemberment에서만 곱해 모델만 키운다. 히트박스는 grunt와 같다.
        public static float ChargeBodyMul      = 1.35f;
        public static float ChargeMinRange     = 3f;    // 이 안 + 시야면 돌진 개시
        public static int   ChargeWindupTicks  = 45;    // 0.75s 텔레그래프(committed)
        public static float ChargeSpeed        = 12f;   // 최고 속도(즉시 도달이 아니라 서서히 수렴 — 아래 참고)
        // ── 돌진 가속 ──
        // 기존: 첫 틱부터 ChargeSpeed 그대로 → 순간적으로 튀어나가는 느낌.
        // 신규: 초반에 느리다가 기하급수적으로 최고 속도에 수렴한다.
        //         v(t) = ChargeSpeed × (1 − e^(−k·t))
        //   k가 클수록 빨리 최고속에 도달. k=6이면 약 0.5초에 95% 도달.
        //
        // ★ Sim 값이라 켜면 <b>예지 결과가 달라진다</b>. 기존 동작을 되돌릴 수 있게 토글로 둔다
        //   (CombatConfig.LungeDoomStyle와 같은 방식).
        public static bool  ChargeAccelOn = true;
        public static float ChargeAccelK  = 6f;    // 수렴 속도. 클수록 빨리 최고속

        /// <summary>돌진 시작 후 t초 시점의 속도(m/s).</summary>
        public static float ChargeSpeedAt(float t) =>
            ChargeAccelOn
                ? ChargeSpeed * (1f - UnityEngine.Mathf.Exp(-UnityEngine.Mathf.Max(0.01f, ChargeAccelK) * t))
                : ChargeSpeed;

        /// <summary>돌진 시작 후 t초까지 나아간 거리(m). v(t)의 적분 — 누적 거리 판정에 쓴다.</summary>
        public static float ChargeDistAt(float t)
        {
            if (!ChargeAccelOn) return ChargeSpeed * t;
            float k = UnityEngine.Mathf.Max(0.01f, ChargeAccelK);
            // ∫₀ᵗ v = S·(t − (1 − e^(−k·t))/k)
            return ChargeSpeed * (t - (1f - UnityEngine.Mathf.Exp(-k * t)) / k);
        }
        public static float ChargeMaxDist      = 10f;   // 돌진 사거리
        public static int   ChargeDamage       = 1;     // 접촉 피해
        public static int   ChargeHitRecovery  = 90;    // 1.50s 명중 후 휘청(연출: 회복 내내 자세 잡음)
        public static int   ChargeMissRecovery = 120;   // 2.00s 빗나감 후 휘청(더 김)
        public static float ChargeWallStopFrac = 0.4f;  // 이번 틱 이동이 의도의 이 비율 미만 = 벽 정지
        // 평소(돌진 커밋 전) 추격 속도만 낮춤 — 실물 모델 Walk 애니메이션이 SimConfig.EnemyMoveSpeed
        // 전속력엔 못 따라가 미끄러지듯 보였다. ChargeRun 자체 속도(ChargeSpeed)는 그대로 둔다.
        // ※ <b>보류</b> — 발걸음 애니메이션 속도와 같이 봐야 확정할 수 있다.
        // 확정(2026-07-22) — 보폭 실측 후 배속 기준으로 역산했다.
        //   추격 속도 = EnemyMoveSpeed(4.5) × 0.591 = 2.66 m/s
        //   걷기 클립 실측 보폭 1.33 m/s → 재생 배속 2.0배 (요청값)
        public static float ChargeChaseSpeedMul = 0.591f;

        // 근접 그런트(mobility=Ground, combat=Melee) 추격 속도 배율. 돌진몹(combat도 Melee지만
        // mobility=Charge)에는 안 걸리게 WalkTowards가 mobility로 구분한다.
        //   2026-07-22 요청: 0.7배 → 추격 속도 = EnemyMoveSpeed(4.5) × 0.7 = 3.15 m/s
        public static float MeleeChaseMul = 0.7f;

        // ── 몹 분리(boids Rule 1): 겹치기 전에 이웃 반대방향으로 미리 조향. 결정론(난수 X) ──
        public static float SeparationRadius  = 1.6f;  // 몸(반경 합) 밖으로 이만큼까지 개인공간
        public static float SeparationWeight  = 0.9f;  // 추격/이동 대비 분리 세기
        public static float SeparationMaxPush = 2.5f;  // 과밀 시 분리벡터 폭주 방지 클램프
        // 개체 고정 개성값(EnemySim.personality 0~1)이 분리 세기를 이 값~1배 사이로 낮춘다.
        // 전부 같은 가중치면 정면으로 마주칠 때 밀어내는 힘이 대칭이라 거울처럼 진동한다(ADR-0004 개정).
        public static float SeparationScaleMin = 0.55f;

        // ── 공중 원거리 (커코데몬형) — 원거리 × Flying. 낮게 부유 ──
        //    벽은 MoveHorizontal 슬라이드, 몹끼리는 분리 스티어링이 담당(클래식 난수 우회 폐기).
        //
        // ★ 공격 타이밍을 지상 원거리와 <b>분리했다</b>(요청). 지금은 값이 같지만 공중은 회피 난이도가
        //   달라 따로 굴려야 한다. 지상 값(RangedAimTicks 등)을 바꿔도 여기는 안 따라간다.
        // ※ 아래 두 값은 <b>미확정</b> — 일단 이 값으로 두고 플레이하며 맞춘다(F10 공중 탭).
        public static float FlyHoverOffset  = 2.3f;  // 플레이어 y + 이만큼 위를 유지(기준값) ★ 미확정
        // 개체마다 호버 높이를 흩뜨린다 — 전부 같은 높이에 뜨면 한 줄로 늘어선 것처럼 보인다.
        // EnemySim.personality(id 해시, 0~1)를 쓰므로 <b>같은 몹은 항상 같은 높이</b>이고 결정론도 유지된다.
        public static float FlyHoverJitter  = 0.4f;  // ±이만큼 (기준 2.3m면 1.9~2.7m) ★ 미확정

        /// <summary>이 개체가 유지할 호버 높이(플레이어 y 기준). personality 0~1 → −jitter~+jitter.</summary>
        public static float FlyHoverFor(float personality)
            => FlyHoverOffset + (personality * 2f - 1f) * FlyHoverJitter;
        public static float FlySpeed        = 3.5f;  // 느린 부유(수평·수직 공통)
        public static float FlyBandMin      = 3f;    // 이보다 가까우면 수평 후퇴
        public static float FlyBandMax      = 10f;   // 이보다 멀면 수평 접근
        public static float FlyMinClearance = 1f;    // 지면 위 최소 여유(안 꺼지게)
        public static int   FlyAimTicks     = 120;   // 2.00s 조준 (지상과 분리 — 시작 값만 같게)
        public static int   FlyCooldown     = 90;    // 1.50s 발사 후 정비

        // ── 공중 관성 이동 ──
        // 기존: 원하는 방향으로 즉시 FlySpeed로 움직임 → 방향을 꺾으면 그 자리에서 딱 꺾인다(기계적).
        // 신규: 속도를 상태로 들고 가감속한다. 급선회하면 <b>원래 가던 방향으로 미끄러진다</b>.
        //         가속: v += (목표속도 − v) × accel × dt
        //         감속: 이동 의도가 없으면 v *= (1 − drag×dt)  → 관성으로 밀려 나감
        //
        // ★ Sim 값이라 켜면 <b>예지 결과가 달라진다</b>. 기존 동작으로 되돌릴 수 있게 토글로 둔다.
        public static bool  FlyInertiaOn = true;
        public static float FlyAccel     = 2.2f;   // 목표 속도를 따라잡는 힘. 낮을수록 굼뜨고 많이 미끄러짐
        public static float FlyDrag      = 1.1f;   // 의도가 없을 때 감속. 낮을수록 오래 미끄러짐
        public static float FlyMaxSpeed  = 6f;     // 관성으로 붙은 속도의 상한(FlySpeed보다 커야 의미 있음)
        // 수직도 같은 방식으로 가감속한다 — 목표 높이가 갑자기 바뀌어도(플레이어가 점프·낙하)
        // 즉시 따라붙지 않고 지나쳤다가 되돌아온다. 값이 크면 수평보다 민첩하게 반응.
        public static float FlyAccelY    = 2.6f;   // 수직 가속
        public static float FlyDragY     = 1.4f;   // 수직 감속(목표에 닿았을 때 남은 속도 정리)
        public static float FlyMaxSpeedY = 5f;     // 수직 속도 상한

        // ── 지각(perception) ──
        public static float EnemyEyeHeight = 0.8f;   // LOS 레이 원점(적)·발사 원점
        public static float PlayerTorso    = 0.7f;   // LOS 겨냥점(플레이어 몸통)

        // ── 원거리 솔저 (플라즈마) — 단발 저격형 ──
        // 조준 동안 제자리(Plant)에 고정된다. 단 <b>시야는 잠기지 않는다</b> —
        // e.yaw는 매 틱 플레이어를 따라가고, 발사 방향(committedDir)만 조준 시작 시 고정된다.
        // ※ RangedMoveSpeed는 <b>보류</b> — 발걸음 애니메이션과 같이 봐야 확정할 수 있다.
        public static float RangedMoveSpeed = 4f;    // ★ 보류 — 보폭 맞춘 뒤 확정
        public static float RangedBandMin   = 2f;    // 이보다 가까우면 후퇴
        public static float RangedBandMax   = 9f;    // 이보다 멀면 접근
        public static int   RangedAimTicks  = 120;   // 2.00s 큰 텔레그래프(committed) — 제자리 고정
        public static int   RangedCooldown  = 90;    // 1.50s 발사 후 재발사까지
        public static int   RangedDamage    = 1;

        // 투사체 (유도 없음 → 회피 가능)
        public static float ProjectileSpeed  = 22.8f;   // [2026-07-22] 상향(12→19), [추가] ×1.2 = 22.8 — 더 빠르게
        public static float ProjectileRadius = 0.25f;
        public static int   ProjectileTtl    = 300;  // 5s 안전 소멸

        // 속도 빗맞힘 (DOOM): 발사 확정 시 플레이어가 대시 중이면 일부러 빗나가게
        public static float MissOffsetDeg = 18f;

        // 리드(예측) 조준: 플레이어 속도로 투사체 도달시간만큼 앞을 겨냥하되,
        // 완벽 리드(1)는 불공정 → "아주 약간"만(0.5). 0=현재위치(리드 없음), 1=완벽. 핵심 튜닝값.
        public static float LeadFactor = 0.5f;

        // ── 보스 (빛나는 구 코어 · 추적 레이저) — mobility=Orb. 고정 포탑 + 3페이즈 (2026-07-23 개편) ──
        // 이동하지 않는다(추격 없음). 스폰 지점(EnemyAI.anchor) 기준 BossRevealYOffset에 떠서
        // 충전(5s) → 페이즈별 레이저(2.5/4.0/5.0s, 빔만 55도/s 추적) → 쿨(12s)을 반복한다.
        // 누적 피해가 BossPhaseHp(3)씩 깎일 때마다 y를 BossHideYOffset까지 내려 30s 숨고
        // (그동안 EMP 해제 = 예지 사용 가능) 다음 페이즈로 재등장. 총 HP 33 = 11×3,
        // 페이즈3에서 소진되면 사망(연출 미정). 레이저 충전~발사 동안만 EMP로 예지 무력화(BossQuery.EmpActive).
        public static int   BossMaxHp         = 33;     // 페이즈당 11 × 3페이즈 (EnemySim.Spawn이 사용)
        public static int   BossPhaseHp       = 11;     // 이만큼 깎일 때마다 숨음(BossCanHide=true일 때만). 경계 22/11/0
        public static bool  BossCanHide       = true;   // 숨김 페이즈 사용 여부. true=피해 경계마다 아래로 내려가 '모습은 보인 채' 몸을 피함(그동안 예지 사용 가능).
        public static float BossRevealYOffset = 0f;     // 활동 y = anchor.y. ★스폰 지점 = 싸우는 위치(그대로 싸움).
        public static float BossHideYOffset   = -16f;   // 숨을 때 y = anchor.y - 16 (아래로 16m 내려가 엄폐). 아레나4: 스폰 20.75 → 엄폐 4.75.
        public static float BossHideMoveSpeed = 8f;     // 숨기/재등장 수직 이동 속도(m/s)
        public static int   BossHideTicks     = 1800;   // 30.0s 숨어 있는 시간(예지 사용 가능 창)
        public static float BossEmitterHeight = 0f;     // 오브 코어 발사점(e.pos 기준 위). 중심이 e.pos면 0
        public static float BossRadius        = 4.3f;   // 보스 구(sphere) 히트박스 반경 = 비주얼 오브 반경. 히트·충돌·렌더 공용(캡슐 아님).
        public static int   BossChargeTicks   = 300;    // 5.0s 차지 텔레그래프(엄폐 시간)
        public static int   BossFireTicksP1   = 150;    // 2.5s — 페이즈1 빔 지속
        public static int   BossFireTicksP2   = 240;    // 4.0s — 페이즈2 빔 지속
        public static int   BossFireTicksP3   = 300;    // 5.0s — 페이즈3 빔 지속
        public static int   BossRecoverTicks  = 720;    // 12.0s 발사 후 쿨(플레이어 딜 타임)
        public static float BossChargeTurnRate = 90f;   // 도/s — 차지 중 예비 추적(조준 맞춰둠)
        public static float BossBeamTurnRate  = 55f;    // 도/s — ★ 발사 중 추적 각속도(전 페이즈 동일, 회피 난이도 다이얼)
        public static float BossBeamRange     = 45f;    // 빔 최대 길이(아그로 40보다 길게)
        public static float BossBeamRadius    = 1.8f;   // 굵은 빔 반경(판정·시각 공통). 부분 차단 방식.
        public static int   BossBeamDamage    = 1;      // 접촉 피해(무적시간이 간격 조절)

        /// <summary>보스 현재 페이즈(1~3) — HP에서 파생(45~31=1, 30~16=2, 15~1=3). 별도 필드·해시 불필요.</summary>
        public static int BossPhase(int health)
            => health > BossPhaseHp * 2 ? 1 : health > BossPhaseHp ? 2 : 3;

        /// <summary>페이즈별 빔 지속 틱.</summary>
        public static int BossFireTicksFor(int phase)
            => phase <= 1 ? BossFireTicksP1 : phase == 2 ? BossFireTicksP2 : BossFireTicksP3;
    }
}
