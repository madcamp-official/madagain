using UnityEngine;   // AtkJudge가 Mathf를 쓴다

namespace Game.Sim
{
    /// <summary>
    /// 전투 세부 수치. static = F1 튜닝 패널에서 실시간 조정(예측 중 변경 금지),
    /// const = 구조 상수(단계 식별자 등 — 변경 불가). 막기·가드·칼등치기·질풍참·스턴은 폐기 사양.
    /// </summary>
    public static class CombatConfig
    {
        // 평타 단계 (구조 상수 — switch 라벨용)
        public const byte PhNone = 0, PhWindup = 1, PhActive = 2, PhRecovery = 3;

        // 평타 (좌클릭): 부채꼴 광역, 이동보정 없음. 판정은 Active 진입 시 1회.
        //
        // ── 2연타 콤보 ──
        // 평타1 → (콤보창 안에 다시 좌클릭) → 평타2 → 긴 후딜 → 초기화.
        // 콤보창은 평타1이 <b>완전히 끝난 뒤</b>부터 열린다(후딜 캔슬 없음).
        // 창이 만료되면 comboStep이 0으로 돌아가 다음 좌클릭은 다시 평타1이다.
        public static int Atk1WindupTicks   = 5;
        public static int Atk1ActiveTicks   = 2;
        public static int Atk1RecoveryTicks = 5;    // 짧게 — 바로 이어치기 위함
        public static int Atk2WindupTicks   = 5;
        public static int Atk2ActiveTicks   = 2;
        public static int Atk2RecoveryTicks = 14;   // 2연타 마무리 후딜
        public static int ComboWindowTicks  = 18;   // 0.30초 — 이 안에 눌러야 평타2

        /// <summary>콤보 단계(0=평타1, 1=평타2)별 페이즈 틱.</summary>
        public static int AtkWindup(byte step)   => step == 1 ? Atk2WindupTicks   : Atk1WindupTicks;
        public static int AtkActive(byte step)   => step == 1 ? Atk2ActiveTicks   : Atk1ActiveTicks;
        public static int AtkRecovery(byte step) => step == 1 ? Atk2RecoveryTicks : Atk1RecoveryTicks;

        // 구 단일 평타 값 — 뷰/예측 쪽 잔존 참조 호환용(평타1 기준).
        public static int AttackWindupTicks   { get => Atk1WindupTicks;   set => Atk1WindupTicks = value; }
        public static int AttackActiveTicks   { get => Atk1ActiveTicks;   set => Atk1ActiveTicks = value; }
        public static int AttackRecoveryTicks { get => Atk1RecoveryTicks; set => Atk1RecoveryTicks = value; }
        public static float AttackConeRange       = 3.25f;
        public static float AttackConeHalfAngle   = 55f;
        public static float AttackHeightTolerance = 1.0f;   // 높이차 허용

        // ── 근접 판정 방식 ──
        // 부채꼴(기존)은 수평 평면에서만 각도를 재고 높이는 컷오프뿐이라, 위아래를 봐도 판정이 같다.
        // 오버워치식은 각도 개념 없이 <b>시선 앞에 구를 놓고 겹침</b>만 본다(피치 그대로 반영).
        //   커뮤니티 역측정: 퀵 멜리 = 전방 1.5m에 반지름 1.0m 구 → 실효 사거리 2.5m
        //   활성 시간 동안 매 틱 갱신되어 구가 시선을 따라간다(휘두르며 조준 수정 가능).
        // ── 즉발 판정 ──
        // true면 판정창이 공격 시작(0틱)부터 열린다. 선딜은 연출 타이밍일 뿐 판정을 막지 않는다.
        //   · 클릭한 틱에 바로 맞는다 → 좌클·우클·좌클 캔슬 콤보가 성립
        //   · 판정창이 선딜과 겹치므로 <b>판정 틱을 늘려도 동작이 길어지지 않는다</b>
        //     (총 길이 = 선딜 + 후딜)
        // false면 예전 방식 — 선딜이 끝난 뒤 Active 페이즈에서만 판정(총 길이에 판정도 더해짐).
        public static bool AttackInstantJudge = true;

        /// <summary>즉발 모드에서 공격 시작부터 판정이 살아 있는 틱 수(단계별).</summary>
        public static int AtkJudge(byte step) => Mathf.Max(1, step == 1 ? Atk2ActiveTicks : Atk1ActiveTicks);

        /// <summary>공격 총 길이(틱). 즉발이면 판정창은 겹치므로 더하지 않는다.</summary>
        public static int AtkTotal(byte step) =>
            AttackInstantJudge ? AtkWindup(step) + AtkRecovery(step)
                               : AtkWindup(step) + AtkActive(step) + AtkRecovery(step);

        public static bool  UseSphereMelee = true;   // false면 기존 부채꼴
        public static float MeleeOffset    = 1.5f;   // 눈에서 시선 방향으로 이만큼 앞
        public static float MeleeRadius    = 1.0f;   // 구 반지름
        public static float MeleeEyeHeight = 1.15f;  // 발밑 pos 기준 눈높이

        /// <summary>구 방식의 실효 사거리(표시용).</summary>
        public static float MeleeReach => MeleeOffset + MeleeRadius;

        // 공통 대미지
        public const int Damage = 1;

        // ── 적 경직(스턴) ──
        // 규칙: 스턴 = 그 공격의 히트스톱 + StunExtraTicks.
        // 히트스톱 동안은 sim이 통째로 멈춰 스턴 카운터도 안 줄어드니, 얼음이 풀린 뒤
        // 실제로 굳어 있는 시간이 StunExtraTicks가 된다.
        //   평타  히트스톱 0 → 스턴 9틱(0.15초)
        //   찌르기 히트스톱 7 → 스턴 16틱(0.267초)
        // 이미 경직 중인 적을 또 때리면 <b>더 긴 쪽으로 덮어쓴다</b>(누적하면 콤보로 영구 락).
        public static int StunExtraTicks = 9;      // 0.15초 @60Hz

        /// <summary>평타 단계별 히트스톱(틱). 0 = 안 멈춤 — 연타 리듬을 위해 기본 0.</summary>
        public static int Atk1HitStopTicks = 0;
        public static int Atk2HitStopTicks = 0;
        public static int AtkHitStop(byte step) => step == 1 ? Atk2HitStopTicks : Atk1HitStopTicks;

        /// <summary>평타 명중 시 적에게 줄 경직(틱).</summary>
        public static int AtkStun(byte step) => Mathf.Max(0, AtkHitStop(step) + StunExtraTicks);

        /// <summary>찌르기 명중 시 적에게 줄 경직(틱).</summary>
        public static int LungeStun => Mathf.Max(0, LungeHitStopTicks + StunExtraTicks);

        // 플레이어 체력·피격
        public static int PlayerMaxHp        = 3;
        public static int PlayerHitStunTicks = 0;   // 임시: 피격 경직 0(원래 30). 구조는 유지
        // 피격 순간부터 이 틱 동안 무적(들어오는 히트 무시). 빔 등 연속피해가 매 틱 들어와도
        // 이 간격마다 1대씩만 맞게 된다. HP=3 기준 0.75초 → 빔 완전노출 시 ~1.33dmg/s. 핵심 튜닝값.
        public static int PlayerInvulnTicks  = 45;  // 0.75초 @60Hz

        // ── 타깃 런지 (우클릭): 둠 글로리킬식. 순간이동급 블링크 → 아래→위 베기. 블링크 틱만 잠금 ──
        public const byte LgNone = 0, LgWindup = 1, LgTravel = 2, LgRecovery = 3;
        public static int   LungeWindupTicks    = 0;     // 없음(즉시 발동)
        // ── 찌르기 연출 방식 ──
        // 기존(블링크): 3틱 등속 순간이동. 예측 튜닝 데이터가 이 값을 전제로 만들어져 있다.
        // 둠식(돌진)  : 8틱 ease-out. 쏘아져 나가 적 앞에 감속하며 서는 게 눈에 보인다.
        //
        // ★ 이건 <b>Sim 값</b>이라 바꾸면 예지 결과도 함께 바뀐다(예측이 같은 코드를 재실행하므로
        //   결정론은 유지되지만, 팀원의 기존 튜닝 결과와는 달라진다). 그래서 통째로 되돌릴 수 있게
        //   두 벌을 따로 보존하고 토글로 고른다. 카메라 연출은 View 전용이라 예지에 무해하다.
        public static bool  LungeDoomStyle       = true;
        public static int   LungeTravelTicks     = 3;    // 기존 — 블링크(순간이동급)
        public static int   LungeTravelTicksDoom = 8;    // 둠식 — 돌진이 보이는 길이

        /// <summary>실제로 쓰이는 이동 틱.</summary>
        public static int LungeTravel => LungeDoomStyle ? LungeTravelTicksDoom : LungeTravelTicks;

        // ── 찌르기 포물선 경로 ──
        // 직선으로 돌진하면 도착 직전 거리가 급감해 "대상을 보는 각도"의 각속도가 폭발한다
        // (7m에서 0.5m 어긋남=4°, 1m에서는 27°). 그래서 카메라가 마지막에 홱 돌아 끊겨 보였다.
        //
        // 경로를 <b>대상 높이 방향으로 볼록한 호</b>로 만들면, 이동 방향과 시선 방향이 처음부터
        // 어긋나지 않아 각속도가 완만해진다. 위 적이면 위로, 아래 적이면 아래로 부푼다
        // (찌르는 동작이므로 항상 위로 솟는 건 어색하다).
        //
        // ★ Sim 값이라 예지 결과도 함께 바뀐다. LungeDoomStyle이 꺼져 있으면 예전 직선 그대로다.
        [Tooltip("호의 크기 — 시작~도착 높이차에 이 비율을 곱한 만큼 부푼다")]
        public static float LungeArcAmount = 0.55f;
        [Tooltip("높이차가 작아도 최소 이만큼은 부푼다(m). 수평 대상에서도 밋밋하지 않게")]
        public static float LungeArcMinBulge = 0.35f;
        [Tooltip("호의 최대 크기(m) — 너무 크게 돌아가지 않도록 제한")]
        public static float LungeArcMaxBulge = 2.5f;
        public static int   LungeRecoveryTicks  = 0;     // 없음(도착 즉시 조작 복귀)
        public static int   LungeCooldownTicks  = 15;    // 0.25초 연발 제한
        public static int   LungeMaxStacks      = 2;     // 스택 상한(2). 처치로 +1 충전, 발동 1 소모
        public static int   LungeReserveWindow  = 10;    // 쿨 막판 이 틱 이내(≈0.17초) 클릭 → 예약
        public static float LungeMinRange       = 1.2f;
        public static float LungeMaxRange       = 7f;
        public static float LungeAimRadius      = 2.0f;  // 조준 레이 수직 보정 반경(판정 핵심)
        public static float LungeStopDistance   = 0.9f;  // 적 앞 이 거리 지점으로 이동
        public static float LungeHeightTolerance = 6f;   // 위/아래 허용 높이차(공중 대상 포함)
        public static float LungeAimUp          = 0.4f;  // 도착점을 적보다 살짝 위로(딱 붙는 느낌)
        public static int   LungeBindExtraTicks = 4;     // 바인드 = 블링크+이 여유
        // 임팩트 쫀득함 (View 전용 — 예측 무해)
        public static int   LungeHitStopTicks   = 7;     // 접촉 순간 프리즈(글로리킬 느낌)

        /// <summary>
        /// 개발용: 대상이 없어도 우클릭으로 찌르기가 나가고 스택·쿨다운을 무시한다.
        /// 몹 없이 애니메이션만 확인할 때 쓴다(콘솔 <c>lunge on</c>).
        /// ★ Sim 동작을 바꾸므로 예지(포크) 결과도 같이 바뀐다. 테스트 전용으로만 켤 것.
        /// </summary>
        public static bool  DevLungeFree = false;
        /// <summary>DevLungeFree 상태에서 대상이 없을 때 전방으로 이동하는 거리(m). 0이면 제자리.</summary>
        public static float DevLungeBlinkDist = 4f;
        public static float LungeFovKick        = 12f;   // 접촉 순간 FOV 킥(도)

        // ── 대형몹 글로리킬 처형 (막타 → 컷신). 진행 중 플레이어 무적·조작잠금 ──
        public const byte  GlNone = 0, GlSlash1 = 1, GlSlash2 = 2, GlDash = 3;
        public static int   GlorySlashTicks = 7;    // 슬래시 1·2 각각(빠르게). 7+7+10=24틱 ≈ 0.4초
        public static int   GloryDashTicks  = 10;   // 피니시 올려베기
        public static float GloryDashSpeed  = 40f;  // 러쉬 속도
    }
}
