namespace Game.Sim
{
    /// <summary>
    /// 시뮬레이션 전역 상수. Time.fixedDeltaTime 대신 TickDelta만 쓴다.
    /// 전투 수치는 여기 없다(전투는 다음 단계). 이동·충돌 값만.
    /// </summary>
    public static class SimConfig
    {
        public const int   TickRate  = 60;
        public const float TickDelta = 1f / TickRate;

        // 웨이브 시스템(배관 다수 동시 방출) 대응으로 64 → 128.
        // ※ 비용: Snapshot.Clone이 이 배열을 통째 복사하므로 예측 포크 비용이 그대로 2배가 된다.
        //    예측 성능이 문제되면 다시 낮추거나 복사 범위를 enemyCount로 제한하는 최적화가 필요하다.
        public const int   MaxEnemies = 128;

        public const float Gravity = -25f;

        // ── 스폰 펄스(웨이브 배관에서 튀어나옴). 설계 §4 ──
        // 전부 고정값 = 결정론 유지(Random 금지). 스프레드가 필요하면 sim 상태로 시드할 것.
        public const float SpawnLaunchSpeed     = 9f;    // 배관 바깥(마커 forward) 방향 초기 속도
        public const float SpawnLaunchUp        = 3.5f;  // 위쪽 성분(아치를 만들어 벽을 벗어나게)
        public const float SpawnLaunchStartGap  = 0.8f;  // 배관 면에서 이만큼 앞에서 출발(초기 겹침 방지)
        public const int   SpawnLaunchMinTicks  = 8;     // 최소 체공 — 스폰 즉시 착지 판정 방지
        public const int   SpawnLaunchMaxTicks  = 180;   // 안전 타임아웃(3초) — 어디 걸려도 반드시 해제
        public const int   SpawnLaunchFlyTicks  = 30;    // 공중몹: 착지 개념이 없어 이 틱 뒤 정상 AI
        public const float SpawnFlyingDownSpeed = 3f;    // 공중몹 스폰 펄스: 아래 성분(팬에서 흘러나오듯)
        public const float SpawnFlyingSideSpeed = 1.5f;  // 공중몹 스폰 펄스: 사선 성분(순번 기반 방향 — 결정론)

        // 이동 시 넘을 수 있는 수직 턱 높이(step-up). 이 이하 턱은 올라타고, 초과면 벽으로 막힘.
        // NavMesh가 잇는 작은 턱(잔단차 0.3 등)을 모터도 넘게 해 "경로는 있는데 몸이 낌"을 방지.
        // Cover(0.8)·mantle(1.5)는 초과라 여전히 못 넘음(의도대로).
        public const float StepHeight = 0.4f;

        // 플레이어 (캡슐: 발밑 pos 기준, 위로 Height). 크기는 구조 상수(런타임 변경 금지).
        public const float PlayerRadius    = 0.28f;
        public const float PlayerHeight    = 1.4375f;   // 1.15 × 1.25 (키 상향 실험)

        // ── 이동·점프 (static = F1 튜닝 패널에서 실시간 조정. 예측 중 변경 금지) ──
        //    2026-07-18 F1 튜닝으로 확정한 기본값.
        public static float PlayerMoveSpeed = 9.17f;
        public static float PlayerJumpSpeed = 11.25f;
        public static int   JumpBufferTicks = 12;    // 착지 직전 점프 선입력 허용
        public static float AirJumpBoost    = 9.34f; // 2단 점프 시 입력 방향 수평 임펄스(추가 속도)
        public static int   AirJumpBoostTicks = 12;  // 임펄스 지속(감쇠)

        // ── 4방향 대시 (진짜 임펄스: 초기 속도 부여 → 매 틱 드래그로 감쇠. 이동 전용) ──
        //    총 거리 = InitialSpeed·dt·(1-decay^N)/(1-decay) 로 자동 산출(F1 패널에 표시). ≈7.74m
        public static float DashInitialSpeed  = 32.82f; // 튀어나가는 힘(m/s) — 첫 틱이 가장 강함
        public static float DashDecay         = 0.95f;  // 틱별 속도 유지율(드래그). 낮을수록 빨리 멈춤
        public static int   DashDurationTicks = 24;     // 최대 지속(속도가 죽어도 이 틱에 종료)
        public static int   DashMaxCharges    = 2;      // 둠식 2스택
        public static int   DashRechargeTicks = 60;     // 스택당 1초
        public static int   DashReserveWindow = 9;      // 대시 막판 이 틱 이내 입력 → 예약(끝나면 즉시 다음 대시)

        // 적. 크기 축소(부피 ~1/4), 튜닝 대상
        // ★ F10(몹 밸런스) 패널에서 실시간으로 만지므로 static이다(PlayerMoveSpeed와 같은 이유).
        //   예지가 도는 중에는 바꾸지 말 것 — 포크 앞뒤 틱이 다른 규칙으로 굴러 결과가 어긋난다.
        // 6 → 4.5 : 걷기 클립의 실측 보폭(1.33 m/s)에 비해 너무 빨라 다리가 팽이처럼 돌았다.
        // 4.5면 배속 3.4배 — 여전히 빠르지만 걷기 클립 하나로 버틸 수 있는 선.
        // (근본 해결은 달리기 클립 추가 → 컨트롤러에 IsRunning 붙이면 코드는 이미 배선돼 있다)
        public static float EnemyMoveSpeed = 4.5f;  // 근접 그런트 = 플레이어 9.17의 ~0.49× (원거리는 자체 4)
        public const float EnemyRadius     = 0.32f;
        public const float EnemyHeight     = 1.4375f;   // 1.15 × 1.25 (키 상향 실험, 모든 몹 비례 확대)
        public const float EnemyAggroRange = 40f;
        public const int   EnemyRepathTicks = 15;     // 경로 재계산 주기
        public const float EnemyArriveDist  = 0.6f;   // 코너 도달 판정
        public const float EnemyNavClampDist = 2f;    // 틱 끝에 지상몹을 navmesh로 되당기는 최대 거리(다리 낙하 방지)

        // 캐릭터끼리 겹침 분리 (대칭)
        public const float SeparationPush = 0.5f;     // 겹친 만큼 * 이 비율씩 양쪽으로

        // ── 전투 (combat 세션이 튜닝. rebuild는 스폰·해시에만 씀) ──
        public const int EnemyNormalHp = 2;   // 일반몹 HP
        public const int EnemyMidHp    = 3;   // 중형몹 HP
        public const int EnemyLargeHp  = 4;   // 대형몹 HP (크기 3배)
        public const float EnemyNormalScale = 1.2f; // 일반몹 크기 배율(대형 제외)
        public const float EnemyLargeScale = 3f;   // 대형몹 크기 배율
        //  스킬 세부 틱(윈드업/액티브/스턴 등)은 combat 소유 파일에 둔다.

        // 절벽 낙하 (자연 낙하). off-mesh link를 큰 낙차로 감지 → 착지점으로 걸어 나가 떨어짐.
        public const float DropDetectMinHeight = 2f;    // 다음 코너가 이만큼 아래면 낙하 후보
        public const float DropDetectRatio     = 1.5f;  // 낙차 > 수평거리 * 이 값 이면 절벽(경사로와 구분)
        // ── 그래프 층이동(traversal) — 팀원 예측 모델. 몹이 Drop/Boost 링크를 Pause→Airborne(포물선)→Recovery로 실행 ──
        public const int   TraversalPauseTicks     = 12;    // 도약 전 멈칫(주저)
        public const int   TraversalRecoveryTicks  = 15;    // 착지 후 회복(경직)
        public const int   TraversalDefaultAirTicks = 30;   // 링크에 틱 지정 없을 때 공중 시간
        public const float TraversalArcHeight       = 1.25f;// 포물선 apex(기본)
        // 내 둠식 도약 튜닝값(Phase C에서 traversal Airborne 곡선에 반영). 지금은 보관.
        public const float DropArcHeight      = 3f;    // 솟구침 높이(크게 뜀)
        public const float DropLaunchFrac     = 0.35f; // 상승:하강 비율(가속낙하)

        // ── 층이동 개편(탄도 모델). docs/shared/층이동_개편_설계.md 참조 ──
        // 마커(TraversalLink)가 층 전환의 유일한 권위. 궤적은 TraversalBallistics가 해석한다.
        public const float TraversalGravity      = 22f;   // 도약 중력(클수록 스냅하게 떨어짐)
        public const float TraversalMinClearance = 0.6f;  // 최소 여유 높이(항상 솟는 느낌 보장)
        public const float TraversalClearanceRatio = 0.18f; // 기본 clearance = 링크 길이 × 이 값
        public const float TraversalMaxClearance = 4f;    // clearance 상한

        // 도약 속도 배분(연출). 궤적 모양은 그대로 두고 그 위를 지나는 속도만 바꾼다.
        // 1 = 등속(순수 물리), 클수록 치우침이 강해진다. static = F1에서 조절.
        // 2.2는 과했다 — 마지막 20% 시간에 경로의 3%만 가서 끝에서 기어간다. 1.5 근처가 적당.
        public static float TraversalAscendShape  = 1.5f; // 상승: 초반을 크게 가속(박차고 오름)
        public static float TraversalDescendShape = 1.9f; // 하강: 막판을 크게 가속(쿵 내리꽂힘)

        // 주저·멈칫 = 링크 직선 길이 비례(데드존 없음 — 짧아도 최소값). 곡선은 가속형(exponent>1).
        // static = F1 튜닝 패널에서 실시간 조정(0으로 내려 "주저·멈칫 없음"도 시험 가능). 예측 중 변경 금지.
        public static int   TraversalPauseMin      = 3;
        public static int   TraversalPauseMax      = 22;
        public static int   TraversalRecoverMin    = 4;
        public static int   TraversalRecoverMax    = 30;
        public static float TraversalLengthRef     = 14f;   // 이 길이에서 최대치에 도달
        public static float TraversalLengthExp     = 1.9f;  // >1 = 길수록 급격히 증가

        // 착지 슬롯(동시 도약 혼잡 방지)
        public const int   TraversalSlotMax       = 6;    // 링크당 최대 슬롯 수
        public const float TraversalSlotGapMul    = 2.2f; // 슬롯 간격 = 최대 몹 반경 × 이 값
        public const int   TraversalLinkCapacity  = 2;    // 동시 비행 허용 수(초과 시 발판 대기)
        // (레거시 자연낙하 상수 — NavMeshPathfinder가 참조. 그래프 전환 후 정리 예정)
        public const float DropCommitDist      = 2f;
        public const int   DropWindupTicks     = 10;
        public const int   DropDurationTicks   = 16;
        public const int   DropLandHoldTicks   = 10;
        public const float DescentLandEpsilon  = 0.3f;
        public const int   DescentMaxTicks     = 120;

        // 소환 (지정 지점 + 일정 간격)
        public const int SpawnIntervalTicks = 45;   // 0.75초마다 한 마리
        public const int SpawnCap           = 12;   // 최대 동시 적 수
    }
}
