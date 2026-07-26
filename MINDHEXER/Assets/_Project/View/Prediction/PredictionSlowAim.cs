using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Game.Sim;
using Game.Prediction;

namespace Game.View
{
    // >>> [슬로우 조준(Slow Aim), 2026-07-22] 11번 추적 방식. 슬로우 모션을 적극적으로 쓰는 안.
    //
    // 클릭 체인(7번)과 겉모습은 비슷하지만 <b>포켓에서 하는 일이 다르다</b>. 7번은 화면의 잔상을
    // 커서로 찍는다(선택). 여기서는 <b>몸을 돌린다</b>(조준) — 포켓 동안 시선 조작권이 통째로
    // 사용자에게 넘어가고, 원하는 방향을 잡은 뒤 클릭하면 액션이 원래 속도로 터진다.
    //
    // 설계 원칙은 "초보자도 할 수 있게"다. 구체적으로:
    //
    //   · <b>감속은 미리, 천천히</b> — 노드에 닿는 순간 뚝 멈추지 않는다. 노드 도달 전
    //     SlowLeadTicks부터 서서히 느려져서 "브레이크가 걸리는" 느낌으로 들어간다. 반대로
    //     클릭 후 복귀는 빠르게 — 액션은 시원해야 한다(감속 6배 느리게, 복귀 4배 빠르게).
    //
    //   · <b>판정은 널널하게</b> — 사용자가 잡은 각도가 예측 노드와 정확히 같을 필요가 없다.
    //     대시·점프는 아주 넉넉하고(±75°), 런지·평타만 조금 좁다(±40°). 통과하면 <b>기록된
    //     입력을 그대로</b> 재생하므로 경로는 어긋나지 않는다 — 조준은 "의도 확인"이지
    //     "정밀 조작 시험"이 아니다.
    //
    //   · <b>더블 점프는 한 번에</b> — 연속된 점프 노드마다 슬로우를 걸면 답답하다. 가까이 붙은
    //     점프 두 개는 노드 하나로 합쳐서, 한 번 조준하고 클릭하면 둘 다 나간다.
    //
    //   · <b>시간 압박 없음</b> — 노드별 제한 시간이 아니라 전체가 공유하는 예지 게이지가
    //     <b>슬로우가 걸린 동안에만</b> 닳는다. 오래 고민하면 남은 예지가 줄 뿐 실패하지 않는다.
    //
    // 포켓 동안 Main이 마우스를 폴링하고 카메라가 input.Yaw/Pitch를 따라가야 하므로
    // <see cref="IFollowMode.AllowsLiveLook"/>이 포켓 중에만 true가 된다. 포켓 진입 시
    // Main.SetLookYaw/Pitch로 현재 카메라 각도를 심어줘야 시선이 튀지 않는다.
    // <<< [슬로우 조준 끝]

    public enum SlowAimNodeState : byte { Pending, Shattering, Gone }

    /// <summary>슬로우 조준 상태 기계. <see cref="SlowAimFollowMode"/>가 감싼다.</summary>
    public sealed class PredictionSlowAim
    {
        struct Node
        {
            public int tick;
            public PredictedActionType type;
            public int targetId;
            public Vector3 anchor;
            /// <summary>이 노드를 발동하면 여기까지는 포켓 없이 쭉 재생한다.
            /// 더블 점프를 하나로 합칠 때 두 번째 점프의 틱이 들어간다.</summary>
            public int throughTick;
            public SlowAimNodeState state;
            public float shatterStart;
            public bool merged;   // 더블 점프처럼 둘 이상이 합쳐진 노드인가
            public int markerIndex;   // 이 노드를 만든 첫 액션 마커의 인덱스(잔상 강조용)
            /// <summary>
            /// 이 노드가 "탐색"인가 "연타"인가.
            ///
            /// [2026-07-22 버그 수정] 실측상 액션 마커는 5~20틱(0.08~0.33초) 간격이다. 그런데
            /// 감속을 30틱 전부터 걸었으니 <b>거의 항상 슬로우가 켜져 있었다</b> — "액션 취할 때
            /// 계속 중간중간 슬로모션 되는" 증상의 정체. 특히 런지→평타(LungeStrike 콤보)는
            /// 5~10틱 간격이라 한 동작 안에서 두 번 느려졌다.
            ///
            /// 앞 노드와 충분히 벌어진 노드만 true(=탐색: 감속 + 시선 넘김 + 각도 판정).
            /// 붙어 있는 노드는 false(=연타: 속도 그대로, 키만 빠르게 눌러 잇는다).
            /// </summary>
            public bool slowAim;
            /// <summary>
            /// 입력 없이 저절로 지나가는 노드. 좌/우/후방 대시(A·S·D)가 여기 해당한다 —
            /// 방향이 기록된 cmd로 고정돼 있어 사용자가 바꿀 수 없으므로, 키를 묻는 게
            /// 선택이 아니라 확인 절차일 뿐이었다. 그냥 자동 이동으로 흘려보낸다.
            /// (전방 대시 W는 "돌진"이라 액션으로 남긴다 — <see cref="IsAutoDash"/> 참고.)
            /// </summary>
            public bool auto;
        }

        /// <summary>자동으로 흘려보낼 대시인가. 전방 대시(W)만 사용자 입력을 요구한다.</summary>
        static bool IsAutoDash(PredictedActionType t)
            => t == PredictedActionType.DashLeft
               || t == PredictedActionType.DashRight
               || t == PredictedActionType.DashBackward;

        /// <summary>대시 종류(4방향 전부)인가 — 대시 슬로우 재생 창 판정용.</summary>
        static bool IsDash(PredictedActionType t)
            => t == PredictedActionType.DashForward || IsAutoDash(t);

        readonly List<Node> nodes = new List<Node>();

        // [중요] 더블 점프를 합치면서 노드 개수가 원본 액션 마커 개수보다 적어진다. 그런데
        // 컨트롤러의 잔상 표시는 <b>마커 인덱스</b>로 묻는다(UpdateGhostMarks가 actionMarkers와
        // 같은 인덱스로 순회). 그대로 두면 합친 지점부터 잔상이 통째로 밀린다 —
        // 마커 → 노드 매핑과 마커별 원래 좌표를 따로 들고 그 축을 맞춘다.
        readonly List<int> markerToNode = new List<int>();
        readonly List<Vector3> markerAnchors = new List<Vector3>();

        public bool Active { get; private set; }
        public int Cursor { get; private set; }
        public int NodeCount => nodes.Count;
        public int FiredCount { get; private set; }

        public float TimeScale { get; private set; } = 1f;
        public bool WantsExit { get; private set; }

        /// <summary>전체가 공유하는 예지 게이지 0~1. 슬로우가 걸린 동안에만 닳는다.</summary>
        public float Gauge { get; private set; } = 1f;

        /// <summary>지금 조준 포켓이 열려 있는가(재생을 붙잡고 사용자 조준을 기다리는 중).</summary>
        public bool PocketOpen { get; private set; }
        /// <summary>포켓은 아직 아니지만 감속이 시작됐는가(노드 접근 중).</summary>
        public bool Approaching { get; private set; }

        int currentTick;
        /// <summary>다음 노드까지 남은 틱. 점진 감속이 이 값으로 배속을 만든다.</summary>
        int remainTicks = int.MaxValue;
        /// <summary>이 틱까지는 무조건 정상 속도 이상으로 달린다(액션 직후 구간).</summary>
        int burstUntilTick;
        /// <summary>[2026-07-22] 이 틱까지는 대시가 눈에 보이도록 느리게 재생한다(대시 발동 시 설정).</summary>
        int dashViewUntilTick = int.MinValue;
        float burstScale = 1f;
        /// <summary>연속 성공 수 — 좌클릭이 연달아 나올 때 "쳤다"를 세어서 보여준다.</summary>
        public int Streak { get; private set; }
        float hitFlashUntil;
        bool lastWasPerfect;
        bool lookSeeded;
        float aimError;          // 현재 조준과 목표 방향의 각도차(도) — HUD·판정 공용
        float targetYaw;
        Vector3 aimTargetWorld;  // 조준 대상 적의 몸 중앙(월드) — 리티클을 여기에 얹는다
        bool hasTarget;
        string feedback = "";
        float feedbackUntil;
        Texture2D bar, ring, glow;

        // ───────────────────────── 생명주기 ─────────────────────────

        public void Begin(PredictedRoute route, in SimWorld w)
        {
            nodes.Clear();
            Cursor = 0;
            FiredCount = 0;
            TimeScale = 1f;
            WantsExit = false;
            Gauge = 1f;
            PocketOpen = false;
            Approaching = false;
            currentTick = 0;
            burstUntilTick = 0;
            dashViewUntilTick = int.MinValue;
            burstScale = 1f;
            Streak = 0;
            hitFlashUntil = 0f;
            lastWasPerfect = false;
            lookSeeded = false;
            hasTarget = false;
            aimError = 0f;
            feedback = "";
            feedbackUntil = 0f;

            if (route != null) BuildNodes(route);

            Active = nodes.Count > 0;
            if (!Active) Debug.LogWarning("[슬로우 조준] 액션 노드가 없는 경로 — 건너뜁니다.");
            else Debug.Log($"[슬로우 조준] 노드 {nodes.Count}개 " +
                           $"(원본 마커 {route.actionMarkers.Count}개 — 합쳐진 것 있음)");
        }

        /// <summary>
        /// 노드 생성. 가까이 붙은 점프 두 개는 하나로 합친다 — 더블 점프마다 슬로우를 걸면
        /// 답답하다는 요구 사항. 합쳐진 노드는 throughTick까지 포켓 없이 재생된다.
        /// </summary>
        void BuildNodes(PredictedRoute route)
        {
            var markers = route.actionMarkers;
            int lastLungeTarget = -1;
            markerToNode.Clear();
            markerAnchors.Clear();
            for (int i = 0; i < markers.Count; i++)
            {
                markerToNode.Add(-1);
                markerAnchors.Add(markers[i].position);
            }

            for (int i = 0; i < markers.Count; i++)
            {
                ActionMarker m = markers[i];
                int through = m.tick;
                bool merged = false;
                int nodeIndex = nodes.Count;
                int firstMarker = i;
                markerToNode[i] = nodeIndex;

                // 점프 + (곧바로) 점프 = 더블 점프. 뒤쪽을 흡수한다.
                while (m.type == PredictedActionType.Jump
                       && i + 1 < markers.Count
                       && markers[i + 1].type == PredictedActionType.Jump
                       && markers[i + 1].tick - through <= PredictionConfig.SlowAimDoubleJumpMergeTicks)
                {
                    through = markers[i + 1].tick;
                    merged = true;
                    i++;
                    markerToNode[i] = nodeIndex;   // 흡수된 마커도 같은 노드를 가리킨다
                }

                // [2026-07-22 재수정] 처음엔 간격(틱)만으로 갈랐더니 실제 경로에서 13개 중
                // 12개가 연타로 분류돼 조준 순간이 사라졌다 — 반대 극단. 간격은 런지 너프가
                // 오면 또 바뀌는 값이라 기준으로 삼기에 불안정하기도 하다.
                //
                // 그래서 <b>의미</b>로 가른다: 새로운 적에게 붙는 런지는 "다음 표적을 본다"는
                // 뜻이므로 항상 조준(탐색) 대상이고, 그 뒤에 붙는 평타는 같은 동작의 마무리라
                // 연타다. 간격 기준은 그 외 경우의 보조로만 남긴다.
                int gap = nodes.Count == 0
                    ? int.MaxValue
                    : m.tick - nodes[nodes.Count - 1].throughTick;
                bool newTarget = m.type == PredictedActionType.Lunge
                                 && m.targetId >= 0 && m.targetId != lastLungeTarget;
                if (m.type == PredictedActionType.Lunge && m.targetId >= 0)
                    lastLungeTarget = m.targetId;

                nodes.Add(new Node
                {
                    tick = m.tick,
                    type = m.type,
                    targetId = m.targetId,
                    anchor = m.position,
                    throughTick = through,
                    state = SlowAimNodeState.Pending,
                    merged = merged,
                    markerIndex = firstMarker,
                    auto = IsAutoDash(m.type),
                    // 자동 노드는 조준 대상이 될 수 없다 — 멈출 이유가 없다.
                    slowAim = !IsAutoDash(m.type)
                              && (newTarget || gap >= PredictionConfig.SlowAimMinGapTicks),
                });
            }
        }

        /// <summary>지금 노드가 탐색(감속·조준) 대상인가. 아니면 연타로 잇는 노드다.</summary>
        public bool CursorWantsAim => Cursor < nodes.Count && nodes[Cursor].slowAim;

        public void End()
        {
            if (Active)
                Debug.Log($"[슬로우 조준] 종료 — 노드 {FiredCount}/{nodes.Count}, " +
                          $"남은 예지 {Gauge * 100f:0}%");
            Active = false;
            nodes.Clear();
            PocketOpen = false;
            Approaching = false;
            TimeScale = 1f;
        }

        // ───────────────────────── 매 sim 틱: 재생 게이트 ─────────────────────────

        /// <summary>
        /// [2026-07-22 수정] 예전엔 노드 틱에 <b>도달해서야</b> 포켓을 열었고, 그 순간부터
        /// false를 돌려 재생을 막았다. 그런데 Main.FixedUpdate는 false를 받으면 그 틱을 통째로
        /// 건너뛰므로 <b>sim이 완전히 정지</b>했다 — timeScale은 뷰 연출에만 걸리고 세계는
        /// 얼어붙는다. "완전히 멈추는 건 아니어야 한다"는 요구와 어긋났다.
        ///
        /// 지금은 노드 도달 <b>전에</b> 포켓을 열고 그 구간은 계속 true를 준다. Main의 sim
        /// 누적기가 <c>Time.deltaTime</c>(스케일 적용)을 쓰므로, timeScale이 0.06이면 sim이
        /// 실제로 1/16 속도로 <b>계속 흐른다</b> — 적도 움직이고 투사체도 날아간다.
        /// 완전 정지는 노드 틱에 닿은 마지막 순간뿐이고, 그건 액션이 입력 없이 나가버리는 걸
        /// 막기 위해 반드시 필요하다.
        /// </summary>
        public bool TryAdvanceReplay(int tick, in SimWorld w)
        {
            currentTick = tick;
            if (!Active) return true;
            if (Cursor >= nodes.Count) { ClosePocket(); Approaching = false; return true; }

            // 자동 노드(A·S·D 대시)는 붙잡지 않는다 — 도달하면 스스로 소진되고 재생이 이어진다.
            while (Cursor < nodes.Count && nodes[Cursor].auto && tick >= nodes[Cursor].tick)
            {
                Fire(Cursor, silent: true);
                if (Cursor >= nodes.Count) { ClosePocket(); Approaching = false; return true; }
            }

            Node n = nodes[Cursor];
            int remain = n.tick - tick;
            remainTicks = Mathf.Max(0, remain);

            if (remain > 0)
            {
                // 연타 노드는 감속하지 않는다 — 액션 사이를 느리게 만들지 않는 게 핵심.
                Approaching = n.slowAim
                              && remain <= PredictionConfig.SlowAimSlowLeadTicks
                              && tick >= burstUntilTick;

                int lead = n.slowAim
                    ? PredictionConfig.SlowAimPocketLeadTicks
                    : PredictionConfig.SlowAimQuickPocketLeadTicks;
                if (remain <= lead)
                {
                    if (!PocketOpen) { PocketOpen = true; lookSeeded = false; }
                }
                else ClosePocket();
                return true;   // ★ 여기서 계속 true — 느리지만 세계는 흐른다
            }

            // 노드 틱 도달 — 여기서만 완전히 붙잡는다(입력 없이 액션이 나가면 안 되므로).
            if (!PocketOpen) { PocketOpen = true; lookSeeded = false; }
            Approaching = false;
            return false;
        }

        void ClosePocket()
        {
            PocketOpen = false;
            lookSeeded = false;
        }

        /// <summary>
        /// 재생을 붙잡은 동안 세계를 굴릴 중립 명령.
        ///
        /// [2026-07-22 수정] 예전엔 그냥 false를 돌려 재생을 막았고, 그러면 Main이 그 틱을
        /// 통째로 건너뛰어 <b>적·투사체까지 전부 얼어붙었다</b>("아예 멈춰 있는 듯" 증상).
        /// 지금은 재생 인덱스만 붙잡고 sim은 계속 굴린다 — 적은 다가오고 공격하고 투사체도
        /// 날아간다. 플레이어만 그 자리에서 다음 행동을 고르고 있는 상태가 된다.
        ///
        /// 시선은 사용자 것으로 넘긴다(조준 포켓). 그래야 몸이 카메라를 따라 돌아서 조준이
        /// 화면과 일치한다.
        /// </summary>
        public bool TryGetHoldCommand(in SimWorld w, out InputCmd cmd)
        {
            cmd = InputCmd.Empty;
            if (!Active) return false;

            // [2026-07-22 버그] 공중에서 붙잡으면 sim이 계속 도는 만큼 <b>중력으로 떨어진다</b> —
            // 조준하는 사이에 발판/적 위에서 미끄러져 내려와 경로가 통째로 깨진다("중간에
            // 떨어져서 방향전환 할 시간이 없다"). 공중에서는 예전처럼 완전히 얼린다.
            // 지상에서만 세계를 굴린다 — 어차피 떨어질 게 없으니 안전하고, 적이 다가오는 걸
            // 보여주려던 목적은 지상 교전에서 대부분 달성된다.
            if (!w.player.grounded) return false;
            if (w.player.combat.lungePhase != CombatConfig.LgNone) return false;   // 블링크 중엔 손대지 않는다

            bool userAiming = Main.Instance != null && PocketOpen && CursorWantsAim;
            cmd.yaw = userAiming ? Main.Instance.LookYaw : w.player.yaw;
            cmd.pitch = userAiming ? Main.Instance.LookPitch : 0f;
            return true;
        }

        // ───────────────────────── 매 프레임 ─────────────────────────

        public void UpdateFrame(in SimWorld w, Camera cam)
        {
            if (!Active) return;

            AdvanceShatters();
            SeedLook(cam);
            UpdateAim(in w);
            UpdateTimeScale();
            DrainGauge();

            if (Cursor >= nodes.Count && !PocketOpen)
            {
                if (!WantsExit) Debug.Log("[슬로우 조준] 완주 — 직접 조작으로 돌아갑니다.");
                WantsExit = true;
            }
        }

        /// <summary>
        /// 포켓이 열리는 첫 프레임에 현재 카메라 각도를 입력 상태에 심는다. 안 하면 사용자가
        /// 마우스를 건드리는 순간 예전 누적값으로 시선이 튄다(입력 쪽이 자기 yaw/pitch를
        /// 계속 들고 있으므로).
        /// </summary>
        void SeedLook(Camera cam)
        {
            if (!PocketOpen || lookSeeded || cam == null || Main.Instance == null) return;
            lookSeeded = true;
            Vector3 e = cam.transform.eulerAngles;
            Main.Instance.SetLookYaw(e.y);
            Main.Instance.SetLookPitch(Mathf.DeltaAngle(0f, e.x));
        }

        /// <summary>목표 방향과 현재 조준의 각도차를 갱신한다.</summary>
        void UpdateAim(in SimWorld w)
        {
            hasTarget = false;
            // 연타 노드는 방향을 묻지 않는다 — 조준할 시간도 없고, 물으면 콤보가 끊긴다.
            if (!PocketOpen || Cursor >= nodes.Count || !nodes[Cursor].slowAim) return;

            Node n = nodes[Cursor];

            // [2026-07-22 수정] 예전엔 대상 없는 노드(대시·점프)도 "다음에 갈 자리"를 목표로
            // 잡아 각도를 판정했다. 그런데 <b>대시 방향은 기록된 cmd가 정한다</b> —
            // dashDirection(4방향)과 그 순간의 yaw가 재생에 그대로 들어가므로, 사용자가 아무리
            // 돌려도 대시는 예측이 계산한 그 방향으로 나간다. 즉 "돌려서 맞춰라"고 요구해놓고
            // 결과는 그걸 무시하는 거짓 게이트였다.
            //
            // 사용자 각도로 대시 방향을 바꿀 수는 없다 — 그러면 그 순간부터 남은 기록 경로가
            // 통째로 무효가 된다(예측이 가정한 위치에 안 서게 되므로). 4방향은 이산값이라
            // 월드 방향을 보존하며 재매핑하는 것도 90° 단위가 아니면 불가능하다.
            //
            // 그래서 <b>대상이 있는 액션(런지·평타)만</b> 각도를 묻는다. 대시·점프는 키만 누르면
            // 되고, 회전은 "다음 표적을 미리 봐두는" 자유로만 남는다.
            //
            // [2026-07-22 추가] 평타(L-CLICK)도 런지처럼 표적 원 + 조준 게이트를 준다. 평타는
            // targetId를 안 들고 있으므로(런지 전용) 노드 자리에서 가장 가까운 적을 표적으로
            // 잡는다 — "정확히 미래를 그대로 따라간다"는 확정감을 위해 자유도를 주지 않는다.
            Vector3 targetCenter;   // 적 몸 중앙(월드) — 조준 리티클을 여기에 얹는다
            if (n.targetId >= 0)
            {
                int ti = PlayerCombat.FindEnemyIndex(in w, n.targetId);
                if (ti < 0) return;
                targetCenter = w.enemies[ti].pos + Vector3.up * (w.enemies[ti].height * 0.5f);
            }
            else if (n.type == PredictedActionType.Attack)
            {
                if (!TryNearestEnemy(in w, n.anchor, out targetCenter)) return;
            }
            else return;   // 대시·점프는 방향을 묻지 않는다(위 주석 참고)

            aimTargetWorld = targetCenter;   // DrawAimGuide가 이 위치(적 몸 중앙)에 리티클을 그린다

            Vector3 to = targetCenter - w.player.pos;

            to.y = 0f;
            if (to.sqrMagnitude < 0.04f) return;   // 거의 제자리 — 방향을 물을 게 없다

            targetYaw = Quaternion.LookRotation(to.normalized).eulerAngles.y;
            float aim = Main.Instance != null ? Main.Instance.LookYaw : w.player.yaw;
            aimError = Mathf.Abs(Mathf.DeltaAngle(aim, targetYaw));
            hasTarget = true;
        }

        /// <summary>
        /// 현재 커서 노드의 "표적" 월드 위치. 표적 마커·방향 글로우가 이 지점을 가리킨다.
        /// [2026-07-22] 예전엔 노드 anchor(=내가 설 자리)를 썼는데, 도착할수록 그 지점이 카메라
        /// 코앞·뒤로 와서 화면 안에 적이 보이는데도 엉뚱한 모서리에 글로우가 떴다. 대상이 있는
        /// 노드(런지·평타)는 <b>적 몸 중앙</b>을, 그 외(대시·점프)는 노드 자리를 가리키게 바꾼다.
        /// </summary>
        Vector3 MarkerWorld(in SimWorld w)
        {
            Node n = nodes[Cursor];
            if (n.targetId >= 0)
            {
                int ti = PlayerCombat.FindEnemyIndex(in w, n.targetId);
                if (ti >= 0) return w.enemies[ti].pos + Vector3.up * (w.enemies[ti].height * 0.5f);
            }
            if (n.type == PredictedActionType.Attack && TryNearestEnemy(in w, n.anchor, out Vector3 c))
                return c;
            return n.anchor + Vector3.up * PredictionConfig.ClickChainAimHeight;
        }

        /// <summary>노드 자리(from)에서 가장 가까운 살아있는 적의 <b>몸 중앙</b>을 찾는다. 평타 표적
        /// 추정용(평타는 런지와 달리 targetId를 기록하지 않으므로 위치로 되짚는다).</summary>
        static bool TryNearestEnemy(in SimWorld w, Vector3 from, out Vector3 center)
        {
            center = Vector3.zero;
            float best = float.MaxValue;
            for (int i = 0; i < w.enemyCount; i++)
            {
                if (!w.enemies[i].alive) continue;
                Vector3 d = w.enemies[i].pos - from; d.y = 0f;
                float sq = d.sqrMagnitude;
                if (sq >= best) continue;
                best = sq;
                center = w.enemies[i].pos + Vector3.up * (w.enemies[i].height * 0.5f);
            }
            return best < float.MaxValue;
        }

        /// <summary>
        /// 감속은 천천히, 복귀는 빠르게. 이 비대칭이 "브레이크 걸고 → 시원하게 터진다"를 만든다.
        /// </summary>
        void UpdateTimeScale()
        {
            float target;

            if (currentTick <= dashViewUntilTick)
            {
                // 대시 재생 구간 — 최우선. 이동이 빠른 만큼 느리게 봐서 방향이 읽히게 한다.
                target = PredictionConfig.SlowAimDashTimeScale;
            }
            else if (currentTick < burstUntilTick)
            {
                // 액션 직후 — 무조건 빠르게. 다음 노드가 코앞이어도 여기서는 안 느려진다.
                // 정확히 맞춘 경우엔 1배속을 넘겨 가속한다(보상).
                target = burstScale;
            }
            else if (PocketOpen)
            {
                // 탐색 노드만 느려진다. 연타 노드의 대기는 정상 속도 — "액션과 액션 사이에서
                // 탐색할 때만 슬로우"라는 규칙 그대로.
                target = CursorWantsAim
                    ? PredictionConfig.SlowAimPocketTimeScale
                    : PredictionConfig.SlowAimRunTimeScale;
            }
            else if (Approaching)
            {
                // [2026-07-22] 예전엔 접근 구간 내내 고정 배속(0.42)이라 "툭 하고 한 단 떨어지는"
                // 느낌이었다. 지금은 남은 거리에 따라 <b>연속적으로</b> 줄인다 — 멀면 거의
                // 제 속도, 가까울수록 포켓 배속까지 부드럽게 수렴한다.
                float u = Mathf.Clamp01(remainTicks / (float)PredictionConfig.SlowAimSlowLeadTicks);
                target = Mathf.Lerp(PredictionConfig.SlowAimPocketTimeScale,
                                    PredictionConfig.SlowAimRunTimeScale,
                                    Mathf.SmoothStep(0f, 1f, u));
            }
            else target = PredictionConfig.SlowAimRunTimeScale;

            float rate = target < TimeScale
                ? PredictionConfig.SlowAimSlowDownRate    // 느려질 때 — 천천히
                : PredictionConfig.SlowAimSpeedUpRate;    // 빨라질 때 — 빠르게
            // 대시 구간은 짧으므로 천천히 브레이크를 걸면 대시가 끝난 뒤에야 느려진다 —
            // 진입/복귀 모두 빠른 rate로 스냅해 대시 지속 동안 확실히 슬로우가 걸리게 한다.
            if (currentTick <= dashViewUntilTick)
                rate = PredictionConfig.SlowAimSpeedUpRate;
            TimeScale = Mathf.MoveTowards(TimeScale, target, rate * Time.unscaledDeltaTime);
        }

        /// <summary>게이지는 <b>슬로우가 걸린 동안에만</b> 닳는다 — 고민한 만큼만 쓴다.</summary>
        void DrainGauge()
        {
            // 탐색(회전)하는 동안에만 닳는다 — 연타로 잇는 구간은 공짜다. 그래야 "시간에
            // 매달리지 않는다"가 성립하고, 게이지가 곧 "고민한 양"이 된다.
            if (!PocketOpen || !CursorWantsAim) return;
            Gauge -= PredictionConfig.SlowAimGaugeDrainPerSecond * Time.unscaledDeltaTime;
            if (Gauge > 0f) return;

            Gauge = 0f;
            if (!WantsExit) Debug.Log("[슬로우 조준] 예지 게이지 소진 — 예지가 풀립니다.");
            WantsExit = true;
        }

        void AdvanceShatters()
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].state != SlowAimNodeState.Shattering) continue;
                if (now - nodes[i].shatterStart < PredictionConfig.FreerunShatterSeconds) continue;
                Node n = nodes[i];
                n.state = SlowAimNodeState.Gone;
                nodes[i] = n;
            }
        }

        // ───────────────────────── 입력 ─────────────────────────

        public void CaptureInput(Keyboard kb, Mouse mouse, in SimWorld w)
        {
            if (!Active || !PocketOpen || Cursor >= nodes.Count) return;

            // [2026-07-22 수정] 예전엔 전부 좌클릭이었다. 지금은 잔상이 요구하는 액션의
            // 실제 키를 그대로 눌러야 한다 — 평소 조작과 같은 키라 따로 배울 게 없고,
            // "무엇을 하는 액션인지"가 손으로도 전달된다.
            if (!PressedFor(nodes[Cursor].type, kb, mouse)) return;

            // 방향을 물을 게 없는 노드(제자리 액션)는 그냥 통과시킨다 — 못 맞출 각도가 없다.
            if (hasTarget && aimError > ToleranceOf(nodes[Cursor].type))
            {
                Feedback("돌려서 맞춰", 0.4f);
                return;   // 벌점 없음 — 게이지만 계속 닳는다
            }

            Fire(Cursor);
        }

        /// <summary>이 액션에 해당하는 실제 조작 키가 이번 프레임에 눌렸는가.</summary>
        static bool PressedFor(PredictedActionType t, Keyboard kb, Mouse mouse)
        {
            switch (t)
            {
                case PredictedActionType.Jump:
                    return kb != null && kb.spaceKey.wasPressedThisFrame;
                case PredictedActionType.Attack:
                    return mouse != null && mouse.leftButton.wasPressedThisFrame;
                case PredictedActionType.Lunge:
                    return mouse != null && mouse.rightButton.wasPressedThisFrame;
                case PredictedActionType.DashForward:
                    // [2026-07-22] 앞 대시는 W+Shift로 확정한다(질주 느낌). 누르는 순서 무관 —
                    // W를 새로 누를 때 Shift가 눌려있거나, Shift를 새로 누를 때 W가 눌려있으면 발동.
                    if (kb == null) return false;
                    bool shiftHeld = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                    bool shiftNow = kb.leftShiftKey.wasPressedThisFrame || kb.rightShiftKey.wasPressedThisFrame;
                    return (kb.wKey.wasPressedThisFrame && shiftHeld)
                           || (shiftNow && kb.wKey.isPressed);
                case PredictedActionType.DashBackward:
                    return kb != null && kb.sKey.wasPressedThisFrame;
                case PredictedActionType.DashLeft:
                    return kb != null && kb.aKey.wasPressedThisFrame;
                case PredictedActionType.DashRight:
                    return kb != null && kb.dKey.wasPressedThisFrame;
                default:
                    return mouse != null && mouse.leftButton.wasPressedThisFrame;
            }
        }

        /// <summary>화면에 크게 띄울 키 이름.</summary>
        static string KeyLabelOf(PredictedActionType t)
        {
            switch (t)
            {
                case PredictedActionType.Jump: return "SPACE";
                case PredictedActionType.Attack: return "L-CLICK";
                case PredictedActionType.Lunge: return "R-CLICK";
                case PredictedActionType.DashForward: return "W+SHIFT";
                case PredictedActionType.DashBackward: return "S";
                case PredictedActionType.DashLeft: return "A";
                case PredictedActionType.DashRight: return "D";
                default: return "L-CLICK";
            }
        }

        /// <summary>
        /// 각도 허용치(도). 지금은 대상이 있는 액션(런지)에만 각도를 묻고, 대시·점프는
        /// 아예 안 묻는다(<see cref="UpdateAim"/> 주석 참고) — 그래서 값이 하나다.
        /// </summary>
        static float ToleranceOf(PredictedActionType t)
            => PredictionConfig.SlowAimToleranceStrike;

        void Fire(int index) => Fire(index, silent: false);

        /// <param name="silent">자동 소진(A·S·D 대시) — 성공 연출·연속 카운터를 건드리지 않는다.
        /// 안 누른 걸 성공으로 세면 CHAIN 숫자가 거짓말이 된다.</param>
        void Fire(int index, bool silent)
        {
            if (index < 0 || index >= nodes.Count) return;

            Node n = nodes[index];
            n.state = SlowAimNodeState.Shattering;
            n.shatterStart = Time.unscaledTime;
            nodes[index] = n;

            // 잔상과 딱 맞췄으면 가속으로 보상한다. 각도를 물을 게 없는 노드(연타·제자리)는
            // 정확도를 따질 게 없으니 정타로 쳐준다 — 못 맞출 걸 못 맞췄다고 깎지 않는다.
            bool perfect = !hasTarget || aimError <= PredictionConfig.SlowAimPerfectAngle;
            lastWasPerfect = perfect;

            // 액션 구간은 무조건 빠르게. throughTick을 기준으로 잡아야 더블 점프처럼 합쳐진
            // 노드도 끝까지 안 느려진다.
            burstUntilTick = n.throughTick + PredictionConfig.SlowAimBurstTicks;
            burstScale = perfect
                ? PredictionConfig.SlowAimBurstBoost
                : PredictionConfig.SlowAimRunTimeScale;

            // [2026-07-22] 대시(좌·우·뒤·앞)는 재생 구간을 느리게 봐서 "옆으로 대시했다"가 읽히게.
            // UpdateTimeScale이 이 창을 최우선으로 처리한다.
            if (IsDash(n.type))
                dashViewUntilTick = n.throughTick + PredictionConfig.SlowAimDashViewTicks;

            Cursor = index + 1;
            AdvanceCursor();
            ClosePocket();
            FiredCount++;

            if (silent) return;

            Streak++;
            hitFlashUntil = Time.unscaledTime + PredictionConfig.SlowAimHitFlashSeconds;
            CombatAudio.Hit();
            Feedback(n.merged ? "DOUBLE JUMP" : LabelOf(n.type), 0.4f);
        }

        void AdvanceCursor()
        {
            while (Cursor < nodes.Count && nodes[Cursor].state != SlowAimNodeState.Pending) Cursor++;
        }

        void Feedback(string text, float seconds)
        {
            feedback = text;
            feedbackUntil = Time.unscaledTime + seconds;
        }

        // ───────────────────────── 표시 ─────────────────────────

        // 아래 셋은 모두 <b>마커 인덱스</b>를 받는다(컨트롤러의 잔상 축). 합쳐진 마커는
        // 같은 노드의 상태를 공유하되 좌표는 각자 원래 자리를 쓴다 — 더블 점프의 두 잔상이
        // 한 지점으로 겹쳐 보이면 "두 번 뛴다"는 정보가 사라지므로.
        int NodeOf(int markerIndex)
            => markerIndex >= 0 && markerIndex < markerToNode.Count ? markerToNode[markerIndex] : -1;

        public Vector3 NodeWorldPosition(int markerIndex, in SimWorld w)
            => markerIndex >= 0 && markerIndex < markerAnchors.Count
                ? markerAnchors[markerIndex] : Vector3.zero;

        public SlowAimNodeState StateOf(int markerIndex)
        {
            int n = NodeOf(markerIndex);
            return n >= 0 && n < nodes.Count ? nodes[n].state : SlowAimNodeState.Gone;
        }

        public float ShatterProgress(int markerIndex)
        {
            int n = NodeOf(markerIndex);
            if (n < 0 || n >= nodes.Count) return 0f;
            if (nodes[n].state != SlowAimNodeState.Shattering) return 0f;
            return Mathf.Clamp01(
                (Time.unscaledTime - nodes[n].shatterStart)
                / Mathf.Max(0.01f, PredictionConfig.FreerunShatterSeconds));
        }

        /// <summary>
        /// [2026-07-22 추가] 가까이 간 잔상은 깨져서 사라진다.
        ///
        /// 이 모드는 기록 경로를 그대로 재생하므로 플레이어가 <b>잔상 자리에 정확히 도착</b>한다 —
        /// 즉 조준해야 할 그 순간 목표 잔상이 카메라 바로 앞에 서서 시야를 통째로 가린다.
        /// 거리로 깨뜨려서 치운다. 조준 대상이 뭔지는 화면 HUD(키 표시)가 알려주므로 월드
        /// 잔상이 사라져도 정보는 잃지 않는다.
        ///
        /// 반환: 0=멀쩡, 1=완전히 깨짐(숨김).
        /// </summary>
        /// <param name="isNext">지금 향하고 있는 목표 잔상인가.
        /// [2026-07-22 버그] 액션 간격이 0.2초라 <b>다음 노드는 늘 3m 안쪽</b>이다. 그래서
        /// 모든 잔상에 같은 반경(3.2m)을 쓰면 목표 잔상이 이동 내내 부서진 상태로 흐려져
        /// "누를 때가 돼서야 흰색이 되는" 것처럼 보였다. 목표만 훨씬 좁은 반경을 써서
        /// 코앞에 올 때까지 또렷하게 남긴다 — 시야를 가리는 건 이미 지나친 잔상들이지
        /// 지금 가야 할 그것이 아니다.</param>
        public static float ProximityBreak(Vector3 nodePosition, Vector3 playerPosition, bool isNext)
        {
            float clear = isNext
                ? PredictionConfig.SlowAimNextGhostClearRadius
                : PredictionConfig.SlowAimGhostClearRadius;
            float breakAt = isNext
                ? PredictionConfig.SlowAimNextGhostBreakRadius
                : PredictionConfig.SlowAimGhostBreakRadius;

            float d = Vector3.Distance(nodePosition, playerPosition);
            return 1f - Mathf.Clamp01((d - breakAt) / Mathf.Max(0.01f, clear - breakAt));
        }

        /// <summary>지금 강조할 잔상 = 커서 노드의 첫 마커 인덱스.</summary>
        public int NextIndex => Cursor < nodes.Count ? nodes[Cursor].markerIndex : -1;

        public bool TryGetNext(in SimWorld w, out Vector3 position)
        {
            if (Cursor >= nodes.Count) { position = Vector3.zero; return false; }
            position = nodes[Cursor].anchor;
            return true;
        }

        // ───────────────────────── HUD ─────────────────────────

        public void DrawHud(in SimWorld w, Camera cam)
        {
            EnsureTextures();
            // [2026-07-22] 조준 중엔 화면 안 표적 링(흰 원)을 생략한다 — 조준 가이드(적 위 원)와
            // 겹쳐 원이 둘로 보인다는 피드백. 단, 화면 밖 표적 방향 빛번짐은 조준 중에도 유지한다
            // (등 뒤 표적을 찾는 데 필요) — DrawTargetMarker가 showRing=false여도 모서리 글로우는 그린다.
            bool aiming = PocketOpen && CursorWantsAim && hasTarget;
            DrawTargetMarker(in w, cam, showRing: !aiming);
            DrawGauge();
            DrawHitFlash();
            if (PocketOpen)
            {
                if (CursorWantsAim) DrawAimGuide(in w, cam);   // 연타 노드엔 조준 가이드 없음
                DrawPrompt();
            }
            // [2026-07-22] 순간 피드백 문구(DrawFeedback) 제거 — 조준 원 색·프롬프트 힌트로 대체됨.
        }

        /// <summary>
        /// 다음 목표가 어디 있는지 <b>항상</b> 알려주는 화면 표지.
        ///
        /// 잔상 색을 바꾸는 방식은 롤백했다(경로 그라데이션과 따로 놀아 어색했다). 대신
        /// 화면에 표지를 띄운다 — 잔상이 근접해서 깨졌거나, 지형에 가렸거나, <b>카메라 정반대
        /// 뒤에 있어도</b> 방향을 잃지 않는다. 원형 포위 경로는 다음 대상이 등 뒤인 구간이
        /// 반드시 생기므로(경로 진단에서 확인) 이게 없으면 매번 헤맨다.
        ///
        ///   · 화면 안 → 그 자리에 맥동하는 링 + 거리
        ///   · 화면 밖·뒤 → 화면 가장자리로 밀어붙인 삼각 화살표가 그쪽을 가리킨다
        /// </summary>
        void DrawTargetMarker(in SimWorld w, Camera cam, bool showRing)
        {
            if (cam == null || Cursor >= nodes.Count) return;

            Vector3 world = MarkerWorld(in w);
            Vector3 sp = cam.WorldToScreenPoint(world);
            float distance = Vector3.Distance(w.player.pos, world);

            // 깜박임 — 잔상 맥동과 같은 박자로 뛰게 해서 둘이 한 몸으로 읽히게 한다.
            float blink = 0.5f + 0.5f * Mathf.Sin(
                Time.unscaledTime * PredictionConfig.SlowAimMarkerBlinkHz * Mathf.PI * 2f);
            Color c = Color.Lerp(PredictionConfig.SlowAimMarkerDim,
                                 PredictionConfig.SlowAimMarkerBright, blink);

            bool behind = sp.z <= 0f;
            bool onScreen = !behind && sp.x >= 0f && sp.x <= Screen.width
                            && sp.y >= 0f && sp.y <= Screen.height;

            Color old = GUI.color;

            if (onScreen)
            {
                // 조준 중이면 화면 안 링은 생략(조준 가이드가 대신) — 화면 안이므로 글로우도 불필요.
                if (!showRing) return;
                float gx = sp.x;
                float gy = Screen.height - sp.y;
                float size = Mathf.Clamp(Screen.height * 0.055f, 34f, 74f);
                GUI.color = c;
                GUI.DrawTexture(Centered(gx, gy, size * 2f), ring);
                GUI.color = old;

                var st = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.UpperCenter,
                    fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.018f, 12f, 19f)),
                    richText = true,
                };
                GUI.Label(new Rect(gx - 100f, gy + size + 2f, 200f, 24f),
                          $"<color=#FFFFFFCC>{distance:0.0}m</color>", st);
                GUI.color = old;
                return;
            }

            // ── 화면 밖(뒤 포함) — 그 방향 화면 모서리를 빛번짐으로 밝힌다(화살표 대신) ──
            // [2026-07-22] 배틀그라운드 피격 방향 표시처럼, 표적이 있는 쪽 가장자리가 은은하게
            // 빛나 "저쪽에 표적이 있다"를 직관적으로 알린다. 카메라 뒤면 투영이 뒤집히므로 방향 반전.
            var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 dir = new Vector2(sp.x, sp.y) - center;
            if (behind) dir = -dir;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector2.down;
            dir.Normalize();

            // 실제 화면 경계(마진 0) 위의 점 — 글로우 중심을 여기 두면 절반이 화면 밖으로
            // 잘려 "가장자리에서 안쪽으로 번지는" 느낌이 난다.
            float hx = Screen.width * 0.5f;
            float hy = Screen.height * 0.5f;
            float scale = Mathf.Min(
                hx / Mathf.Max(0.0001f, Mathf.Abs(dir.x)),
                hy / Mathf.Max(0.0001f, Mathf.Abs(dir.y)));
            float ex = center.x + dir.x * scale;
            float ey = Screen.height - (center.y + dir.y * scale);

            // 큰 소프트 글로우 — 화면 높이에 비례. 맥동으로 은은하게 숨쉬게 한다.
            float glowSize = Mathf.Clamp(Screen.height * 0.6f, 300f, 760f);
            float pulse = 0.72f + 0.28f * blink;
            Color glowCol = new Color(0.45f, 1f, 0.82f);   // 민트-시안(표적 방향)
            GUI.color = new Color(glowCol.r, glowCol.g, glowCol.b, 0.55f * pulse);
            GUI.DrawTexture(Centered(ex, ey, glowSize), glow);
            GUI.color = old;
        }

        void DrawGauge()
        {
            float width = Mathf.Clamp(Screen.width * 0.32f, 260f, 560f);
            float height = Mathf.Clamp(Screen.height * 0.017f, 10f, 20f);
            float x = (Screen.width - width) * 0.5f;
            float y = Screen.height * 0.845f;

            Color old = GUI.color;
            GUI.color = PredictionConfig.MagnetGaugeBack;
            GUI.DrawTexture(new Rect(x, y, width, height), bar);
            GUI.color = Color.Lerp(PredictionConfig.MagnetGaugeLow, PredictionConfig.MagnetGaugeHigh,
                                   Mathf.SmoothStep(0f, 1f, Gauge));
            GUI.DrawTexture(new Rect(x, y, width * Mathf.Clamp01(Gauge), height), bar);
            GUI.color = old;

            // [2026-07-22] HUD 간소화 — FORESIGHT % 만 남긴다(진행도 0/16·상태 문구 제거).
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperCenter,
                fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.019f, 13f, 20f)),
                richText = true,
            };
            GUI.Label(new Rect(x - 90f, y + height + 4f, width + 180f, 26f),
                      $"<color=#9FE6D2>예측 게이지 {Gauge * 100f:0}%</color>", style);
        }

        /// <summary>
        /// 목표 방향을 화면 중앙 기준 좌우 편차로 보여준다. 나침반처럼 "얼마나 더 돌려야
        /// 하는지"만 알려주면 되므로 3D 마커보다 이쪽이 읽기 쉽다.
        /// </summary>
        void DrawAimGuide(in SimWorld w, Camera cam)
        {
            if (!hasTarget) return;

            float aim = Main.Instance != null ? Main.Instance.LookYaw : w.player.yaw;
            float delta = Mathf.DeltaAngle(aim, targetYaw);        // +면 오른쪽으로 더 돌려야 함
            float tolerance = ToleranceOf(nodes[Cursor].type);
            bool ok = Mathf.Abs(delta) <= tolerance;

            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;

            // [2026-07-22] 리티클을 화면 중앙 높이에 고정하지 않고 <b>대상 적의 몸 중앙</b> 화면
            // 위치에 얹는다 — 공중 적처럼 높이 뜬 적도 그 몸에 표적이 가서, 카메라가 위로 치켜올라
            // "위쪽만 보이는" 문제가 사라진다. 화면 밖(뒤)이면 예전처럼 중앙 높이 + 좌우 편차로 폴백.
            float size = Mathf.Clamp(Screen.height * 0.1f, 70f, 150f);
            float rx, ry;
            Vector3 sp = cam != null ? cam.WorldToScreenPoint(aimTargetWorld) : new Vector3(0, 0, -1);
            if (sp.z > 0f)
            {
                rx = sp.x;
                ry = Screen.height - sp.y;   // GUI 좌표는 y가 위→아래
            }
            else
            {
                float halfFov = cam != null ? cam.fieldOfView * cam.aspect * 0.5f : 45f;
                rx = cx + Mathf.Clamp(delta / Mathf.Max(1f, halfFov), -1.15f, 1.15f) * (Screen.width * 0.5f);
                ry = cy;
            }

            Color old = GUI.color;
            GUI.color = ok ? PredictionConfig.ClickChainReticleBright
                           : new Color(1f, 0.72f, 0.4f, 0.9f);
            GUI.DrawTexture(new Rect(rx - size * 0.5f, ry - size * 0.5f, size, size), ring);
            GUI.color = old;
        }

        /// <summary>
        /// 눌러야 할 키를 크게 띄운다. 잔상이 가까이서 깨져 사라지므로 "지금 뭘 해야 하는지"는
        /// 전적으로 이 표시가 담당한다 — 작으면 안 된다.
        /// </summary>
        void DrawPrompt()
        {
            Node n = nodes[Mathf.Min(Cursor, nodes.Count - 1)];
            bool ok = !hasTarget || aimError <= ToleranceOf(n.type);
            string key = KeyLabelOf(n.type);
            string action = n.merged ? "더블 점프" : LabelOf(n.type);

            float cx = Screen.width * 0.5f;
            float y = Screen.height * 0.68f;
            float keySize = Mathf.Clamp(Screen.height * 0.062f, 40f, 76f);

            // 각도가 맞으면 초록으로 켜지고 맥동한다 — "지금 눌러라"가 색으로 읽힌다.
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3.4f * Mathf.PI * 2f);
            Color keyColor = ok
                ? Color.Lerp(new Color(0.35f, 1f, 0.78f, 0.8f), Color.white, pulse)
                : new Color(1f, 0.72f, 0.4f, 0.75f);

            // 키 배지 배경 — 어두운 판을 깔아야 밝은 배경에서도 글자가 읽힌다.
            float boxW = Mathf.Max(keySize * 3.4f, key.Length * keySize * 0.62f);
            var box = new Rect(cx - boxW * 0.5f, y, boxW, keySize * 1.32f);
            Color old = GUI.color;
            GUI.color = new Color(0.04f, 0.09f, 0.08f, 0.72f);
            GUI.DrawTexture(box, bar);
            GUI.color = keyColor;
            GUI.DrawTexture(new Rect(box.x, box.y, box.width, 3f), bar);
            GUI.DrawTexture(new Rect(box.x, box.yMax - 3f, box.width, 3f), bar);
            GUI.color = old;

            var keyStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(keySize * 0.62f),
                fontStyle = FontStyle.Bold,
                richText = true,
            };
            GUI.Label(box, $"<color=#{ColorUtility.ToHtmlStringRGB(keyColor)}>{key}</color>", keyStyle);

            var subStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperCenter,
                fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.021f, 14f, 22f)),
                fontStyle = FontStyle.Bold,
                richText = true,
            };
            string sub = ok
                ? $"<color=#7CFFD0>{action}</color>"
                : $"<color=#FFC46B>{action}</color>  <color=#8FB3AB>· 표적으로 화면을 이동하세요</color>";
            GUI.Label(new Rect(cx - 300f, box.yMax + 6f, 600f, 30f), sub, subStyle);
        }

        /// <summary>
        /// 성공 순간의 확인 표시. 좌클릭이 연달아 나오는 구간에서는 "방금 게 먹혔나?"가
        /// 안 보이면 사용자가 같은 입력을 계속 두들기게 된다 — 링이 확 터지고 연속 성공
        /// 수가 올라가는 걸로 즉시 답한다.
        /// </summary>
        void DrawHitFlash()
        {
            float left = hitFlashUntil - Time.unscaledTime;
            if (left <= 0f) return;

            float u = Mathf.Clamp01(left / Mathf.Max(0.01f, PredictionConfig.SlowAimHitFlashSeconds));
            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            float size = Mathf.Lerp(Screen.height * 0.42f, Screen.height * 0.12f, u);

            Color old = GUI.color;
            Color c = lastWasPerfect
                ? new Color(1f, 0.92f, 0.55f, 0.9f)      // 정타 — 금색
                : new Color(0.49f, 1f, 0.82f, 0.75f);
            c.a *= u;
            GUI.color = c;
            GUI.DrawTexture(new Rect(cx - size * 0.5f, cy - size * 0.5f, size, size), ring);
            GUI.color = old;
            // [2026-07-22] CHAIN 콤보 카운터 제거 — 점수 시스템 미확정이라 장식일 뿐이었다.
        }

        void DrawFeedback()
        {
            if (Time.unscaledTime >= feedbackUntil || string.IsNullOrEmpty(feedback)) return;
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.032f, 22f, 38f)),
                fontStyle = FontStyle.Bold,
                richText = true,
            };
            GUI.Label(new Rect(Screen.width * 0.5f - 250f, Screen.height * 0.7f, 500f, 46f),
                      $"<color=#7CFFD0>{feedback}</color>", style);
        }

        static Rect Centered(float x, float y, float size)
            => new Rect(x - size * 0.5f, y - size * 0.5f, size, size);

        void EnsureTextures()
        {
            if (bar == null)
            {
                bar = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "PredictionSlowAimBar" };
                bar.SetPixel(0, 0, Color.white);
                bar.Apply();
            }
            if (ring != null) return;

            const int size = 128;
            ring = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "PredictionSlowAimRing",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size - 0.5f;
                    float dy = (y + 0.5f) / size - 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01((0.5f - d) / 0.014f) * Mathf.Clamp01((d - 0.42f) / 0.014f);
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            ring.SetPixels(px);
            ring.Apply();

            // 표적 방향 모서리 빛번짐용 소프트 글로우 — 중심이 밝고 부드럽게 사라지는 원형.
            const int gsize = 128;
            glow = new Texture2D(gsize, gsize, TextureFormat.RGBA32, false)
            {
                name = "PredictionSlowAimGlow",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var gp = new Color[gsize * gsize];
            for (int y = 0; y < gsize; y++)
            {
                for (int x = 0; x < gsize; x++)
                {
                    float dx = (x + 0.5f) / gsize - 0.5f;
                    float dy = (y + 0.5f) / gsize - 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;   // 0=중심, 1=가장자리
                    // 부드러운 감쇠(가운데 밝고 바깥으로 번지듯 사라짐).
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a;   // 더 부드럽게
                    gp[y * gsize + x] = new Color(1f, 1f, 1f, a);
                }
            }
            glow.SetPixels(gp);
            glow.Apply();
        }

        static string LabelOf(PredictedActionType t)
        {
            switch (t)
            {
                case PredictedActionType.Jump: return "점프";
                case PredictedActionType.Attack: return "베기";
                case PredictedActionType.Lunge: return "찌르기";
                case PredictedActionType.DashForward: return "대시 ↑";
                case PredictedActionType.DashBackward: return "대시 ↓";
                case PredictedActionType.DashLeft: return "대시 ←";
                case PredictedActionType.DashRight: return "대시 →";
                default: return t.ToString().ToUpperInvariant();
            }
        }
    }

    /// <summary>모드 11 래퍼.</summary>
    public sealed class SlowAimFollowMode : IFollowMode
    {
        readonly PredictionSlowAim runtime;
        public SlowAimFollowMode(PredictionSlowAim runtime) { this.runtime = runtime; }

        public PredictionRhythmMode Id => PredictionRhythmMode.SlowAim;
        public string Name => RhythmModeRuntime.ModeName(Id);
        public string Hint => RhythmModeRuntime.ModeHint(Id);
        public bool Active => runtime.Active;
        public FollowInputOwnership Ownership => FollowInputOwnership.GatedReplay;

        public void Begin(PredictedRoute route, in SimWorld w) => runtime.Begin(route, in w);
        public void End() => runtime.End();
        public bool WantsExit => runtime.WantsExit;
        public void UpdateFrame(in SimWorld w, Camera cam) => runtime.UpdateFrame(in w, cam);

        public bool OwnsTimeScale => true;
        public float TimeScale => runtime.TimeScale;

        public bool TryInject(in SimWorld w, ref InputCmd cmd) => false;
        public bool TryAdvanceReplay(int tick, in SimWorld w) => runtime.TryAdvanceReplay(tick, in w);
        public void CaptureInput(Keyboard kb, Mouse mouse, in SimWorld w)
            => runtime.CaptureInput(kb, mouse, in w);
        public bool TryGetHoldCommand(in SimWorld w, out InputCmd cmd)
            => runtime.TryGetHoldCommand(in w, out cmd);
        /// <summary>런지가 거의 매 노드라 히트스톱이 계속 걸려 적이 얼어붙어 보인다 — 끈다.</summary>
        public bool SuppressesHitStop => true;

        public FollowCameraMode CameraMode => FollowCameraMode.FirstPerson;
        public bool ShowsPlayerBody => false;
        public bool TryGetCameraYaw(in SimWorld w, out float yaw) { yaw = 0f; return false; }
        /// <summary>탐색 포켓 동안에만 시선이 사용자 것. 연타 노드에서는 넘기지 않는다 —
        /// 0.2초마다 카메라 소유권이 오가면 화면이 덜컹거린다.</summary>
        public bool AllowsLiveLook => runtime.PocketOpen && runtime.CursorWantsAim;

        public int HighlightIndex => runtime.NextIndex;

        public bool TryGetNodeVisual(int index, in SimWorld w, out FollowNodeVisual visual)
        {
            if (!runtime.Active) { visual = default; return false; }

            Vector3 position = runtime.NodeWorldPosition(index, in w);
            bool isNext = index == runtime.NextIndex;
            // 발동해서 깨지는 연출과, 가까이 가서 깨지는 연출 중 더 깨진 쪽을 쓴다.
            float shatter = Mathf.Max(
                runtime.ShatterProgress(index),
                PredictionSlowAim.ProximityBreak(position, w.player.pos, isNext));

            // [2026-07-22 롤백] 목표 잔상을 흰색으로 덮어쓰던 걸 되돌린다 — 색을 갈아끼우니
            // 경로 그라데이션과 따로 놀아 어색했다. 색은 컨트롤러 기본 규칙(그라데이션 +
            // 다음 잔상 맥동 GhostNextPulse*)에 맡기고, "어디가 목표인가"는 화면 표지
            // (DrawTargetMarker)로 알린다. HighlightIndex를 내주고 있으므로 기본 맥동은
            // 자동으로 이 노드에 걸린다.
            visual = new FollowNodeVisual
            {
                visible = runtime.StateOf(index) != SlowAimNodeState.Gone && shatter < 1f,
                position = position,
                shatter = shatter,
                hasTint = false,
            };
            return true;
        }

        /// <summary>빛기둥은 쓰지 않는다 — 1인칭에서 시야를 가리고, 어차피 재생이 알아서
        /// 그 자리로 데려다주므로 "어디로 갈지" 안내가 필요 없다.</summary>
        public bool TryGetWorldGuide(in SimWorld w, out Vector3 position)
        { position = default; return false; }

        public bool WantsCursorVisible => false;
        public bool ReplacesDefaultHud => true;
        public void DrawHud(in SimWorld w, Camera cam) => runtime.DrawHud(in w, cam);
    }
}
