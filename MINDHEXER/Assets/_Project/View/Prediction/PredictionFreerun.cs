using System.Collections.Generic;
using UnityEngine;
using Game.Sim;
using Game.Prediction;

namespace Game.View
{
    // >>> [자유 주행(Freerun), 2026-07-22] "예측 경로를 따라가는 게 밋밋하다"의 구조적 대응.
    // 기존 Following은 기록된 입력(CandidatePath.controls)을 매 틱 그대로 재생하고 사용자는
    // 정해진 틱에 정해진 키를 누르는 역할만 했다(시간축 판정). 여기서는 그 축을 바꾼다:
    //
    //   · 이동은 사용자가 직접 한다(기록 입력 재생 없음).
    //   · 예측이 만든 액션 잔상은 월드에 그대로 떠 있고, 그 잔상에 <b>닿으면</b> 그 액션이
    //     자동으로 터진다(공간축 판정). 시간 압박이 없어서 난이도가 크게 낮다.
    //   · 대상이 있는 노드(런지)는 그 적이 움직이면 잔상도 따라간다 — 좌표에 못 박아두면
    //     사용자가 다르게 움직인 탓에 적이 그 자리에 없어서 성립하지 않는다.
    //   · 노드는 순서대로 소진하되 하나까지 건너뛸 수 있다(Miss 개념 없음).
    //
    // 판정기(RhythmJudge)·기록 재생 경로는 그대로 두고, 이 모드일 때만 우회한다. 마음에 안
    // 들면 이 파일과 PredictionRhythmMode.Freerun 분기만 지우면 기존 동작이 무손실로 남는다.

    /// <summary>자유 주행 중 액션 노드 하나의 표시 상태.</summary>
    public enum FreerunNodeState : byte
    {
        /// <summary>아직 안 닿음.</summary>
        Pending,
        /// <summary>닿아서 발동 — 깨지는 연출 중.</summary>
        Shattering,
        /// <summary>연출까지 끝나 사라짐(발동했거나 건너뜀).</summary>
        Gone,
    }

    /// <summary>
    /// 자유 주행 실행 상태 기계. PredictionController가 소유하고 아래 훅으로만 호출한다.
    ///   Begin / End / UpdateFrame(매 프레임) / TryInject(매 sim 틱) / NodeWorldPosition / DrawHud
    /// Time.timeScale은 PredictionController가 소유하므로 여기서는 값(<see cref="TimeScale"/>)만
    /// 내주고 직접 쓰지 않는다.
    /// </summary>
    public sealed class PredictionFreerun
    {
        struct Node
        {
            public int tick;
            public PredictedActionType type;
            public int targetId;
            public Vector3 anchor;          // 예측이 이 액션을 시작한 위치
            public FreerunNodeState state;
            public float shatterStart;      // Shattering 진입 시각(unscaled)
            public bool skipped;            // 닿아서 깬 게 아니라 건너뛴 것
            /// <summary>한 번이라도 반경 밖에 있었는가. "닿음"은 겹침이 아니라 <b>진입</b>이라
            /// 이게 참이어야 발동한다 — 시작 지점 발밑에 있는 노드가 가만히 서 있는데도
            /// 줄줄이 터져서 예전 자동 재생처럼 보이는 것을 막는다.</summary>
            public bool armed;
        }

        readonly List<Node> nodes = new List<Node>();
        readonly HashSet<int> defeatedSeen = new HashSet<int>();

        public bool Active { get; private set; }
        /// <summary>다음에 닿아야 할 노드 인덱스. 잔상 강조가 이 값을 쓴다.</summary>
        public int Cursor { get; private set; }
        public int NodeCount => nodes.Count;
        public int FiredCount { get; private set; }
        public int KillCount { get; private set; }

        /// <summary>PredictionController가 매 프레임 Time.timeScale에 반영할 값.</summary>
        public float TimeScale { get; private set; } = 1f;
        /// <summary>모든 노드를 소진했거나 제한 시간을 넘겼다 — 컨트롤러가 Exit해야 한다.</summary>
        public bool WantsExit { get; private set; }

        float startRealTime;
        float budgetSeconds;
        float allNodesDoneAt = -1f;
        int cooldownTicks;
        string feedback = "";
        float feedbackUntil;
        // 슬로모는 두 곳에서 걸린다 — 노드 발동 직후(다음 목표를 찾을 여유)와 처치 확정 직후
        // (한 방 들어간 걸 보여주는 연출). 겹치면 더 느린 쪽·더 늦게 끝나는 쪽이 이긴다.
        float slowUntil;
        float slowScale = 1f;
        float slowSeconds;

        public void Begin(PredictedRoute route, in SimWorld w)
        {
            nodes.Clear();
            defeatedSeen.Clear();
            Cursor = 0;
            FiredCount = 0;
            KillCount = 0;
            TimeScale = 1f;
            WantsExit = false;
            allNodesDoneAt = -1f;
            cooldownTicks = 0;
            feedback = "";
            feedbackUntil = 0f;
            slowUntil = 0f;
            slowScale = 1f;
            slowSeconds = 0f;
            startRealTime = Time.unscaledTime;

            if (route != null)
            {
                for (int i = 0; i < route.actionMarkers.Count; i++)
                {
                    ActionMarker m = route.actionMarkers[i];
                    nodes.Add(new Node
                    {
                        tick = m.tick,
                        type = m.type,
                        targetId = m.targetId,
                        anchor = m.position,
                        state = FreerunNodeState.Pending,
                        shatterStart = 0f,
                    });
                }
            }

            // 예측 지평보다 넉넉하게 준다 — 직접 걸어가면 예측(최적 궤적)보다 느릴 수밖에 없다.
            float routeSeconds = route != null ? route.seconds : 3f;
            budgetSeconds = routeSeconds * PredictionConfig.FreerunTimeBudgetMul
                            + PredictionConfig.FreerunTimeBudgetPad;

            // 시작 시점에 이미 결과가 잠긴 적은 "내가 잡은 것"으로 세지 않는다.
            for (int i = 0; i < w.enemyCount; i++)
                if (!w.enemies[i].alive || w.enemies[i].combat.gloryStage > 0)
                    defeatedSeen.Add(w.enemies[i].id);

            Active = nodes.Count > 0;
            if (!Active) Debug.LogWarning("[자유 주행] 액션 노드가 없는 경로 — 자유 주행을 건너뜁니다.");
        }

        public void End()
        {
            if (Active)
                Debug.Log($"[자유 주행] 종료 — 노드 {FiredCount}/{nodes.Count} 발동, 처치 {KillCount}");
            Active = false;
            nodes.Clear();
            TimeScale = 1f;
            WantsExit = false;
        }

        // ───────────────────────── 매 프레임 ─────────────────────────

        /// <summary>실시간 갱신 — 처치 감지·슬로모·깨짐 연출 진행·제한 시간.</summary>
        public void UpdateFrame(in SimWorld w)
        {
            if (!Active) return;

            DetectDefeats(in w);
            AdvanceShatters();
            ArmNodes(in w);

            float now = Time.unscaledTime;
            if (now < slowUntil && slowSeconds > 0f)
            {
                // u: 0(끝) → 1(막 시작). 시작 직후 가장 느리고 부드럽게 1배속으로 돌아온다.
                float u = Mathf.InverseLerp(slowUntil, slowUntil - slowSeconds, now);
                TimeScale = Mathf.Lerp(1f, slowScale, Mathf.SmoothStep(0f, 1f, u));
            }
            else { TimeScale = 1f; slowScale = 1f; slowSeconds = 0f; }

            if (Cursor >= nodes.Count && allNodesDoneAt < 0f) allNodesDoneAt = now;
            if (allNodesDoneAt >= 0f && now - allNodesDoneAt >= PredictionConfig.FreerunFinishLingerSeconds)
                WantsExit = true;
            if (now - startRealTime >= budgetSeconds)
            {
                if (!WantsExit) Debug.Log("[자유 주행] 제한 시간 종료 — 직접 조작으로 돌아갑니다.");
                WantsExit = true;
            }
        }

        void DetectDefeats(in SimWorld w)
        {
            for (int i = 0; i < w.enemyCount; i++)
            {
                EnemySim e = w.enemies[i];
                bool defeated = !e.alive || e.combat.gloryStage > 0;
                if (!defeated || defeatedSeen.Contains(e.id)) continue;
                defeatedSeen.Add(e.id);
                KillCount++;
                Slow(PredictionConfig.FreerunKillSlowSeconds, PredictionConfig.FreerunKillSlowScale);
                Feedback("KILL", 0.6f);
            }
        }

        /// <summary>반경 밖으로 나간 적이 있는 노드에 장전 표시를 한다(위 <c>Node.armed</c> 참고).</summary>
        void ArmNodes(in SimWorld w)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].armed || nodes[i].state != FreerunNodeState.Pending) continue;
                if (WithinTriggerRange(i, in w)) continue;
                Node n = nodes[i];
                n.armed = true;
                nodes[i] = n;
            }
        }

        void AdvanceShatters()
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].state != FreerunNodeState.Shattering) continue;
                if (now - nodes[i].shatterStart < PredictionConfig.FreerunShatterSeconds) continue;
                Node n = nodes[i];
                n.state = FreerunNodeState.Gone;
                nodes[i] = n;
            }
        }

        // ───────────────────────── 매 sim 틱 ─────────────────────────

        /// <summary>
        /// 사용자의 실시간 입력 위에 노드 발동을 얹는다. Main.FixedUpdate가 틱마다 한 번 부른다.
        /// 노드에 닿았으면 해당 액션 입력을 주입하고 true.
        /// </summary>
        public bool TryInject(in SimWorld w, ref InputCmd cmd)
        {
            if (!Active) return false;
            if (cooldownTicks > 0) { cooldownTicks--; return false; }
            if (!CanAcceptAction(in w)) return false;

            // 다음 노드와 그 다음 노드까지만 본다 — 하나는 건너뛸 수 있게 해서 "놓쳐도 실패가
            // 아니게" 만들되, 경로를 통째로 건너뛰고 마지막 노드만 치는 건 막는다.
            int last = Mathf.Min(Cursor + PredictionConfig.FreerunLookaheadNodes, nodes.Count - 1);
            for (int i = Cursor; i <= last; i++)
            {
                if (i < 0 || nodes[i].state != FreerunNodeState.Pending) continue;
                if (!IsReachable(i, in w)) continue;

                Fire(i, in w, ref cmd);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 지금 이 틱에 액션 입력을 넣어도 실제로 개시되는가.
        ///
        /// [버그 수정, 2026-07-22] 공중 콤보(런지로 붙어서 곧바로 좌클릭)가 실패하던 원인.
        /// 런지 대상에 닿아 Lunge 노드가 터지면 플레이어는 <b>블링크로 대상 옆까지 순간 이동</b>
        /// 하는데, 그 도착점이 바로 다음 Attack 노드의 위치다. 그래서 다음 틱에 Attack 노드가
        /// 즉시 반경 안에 들어와 발동하지만, 그때 플레이어는 아직 LgTravel/LgRecovery 중이라
        /// <b>PlayerCombat이 그 좌클릭을 그냥 버린다</b> — 노드만 소진되고 공격은 안 나간다.
        /// 공중 적은 이 콤보로만 잡히므로 "공중을 아예 못 잡는다"로 보였다.
        ///
        /// 예측 쪽 매크로는 이 문제가 없었다 — LungeStrike가 좌클릭 서브틱을
        /// <c>LungeTravel + LungeRecoveryTicks + 2</c>로 잡아 상태머신이 비는 순간을 정확히
        /// 노린다(MacroAction.LungeStrikeAttackTick). 자유 주행은 틱이 아니라 위치로 터지니
        /// 그 규칙을 못 쓰고, 대신 "지금 받아주는 상태인가"를 직접 물어야 한다.
        /// </summary>
        static bool CanAcceptAction(in SimWorld w)
        {
            PlayerCombatState c = w.player.combat;
            if (c.gloryPhase != CombatConfig.GlNone) return false;   // 처형 컷신 — 조작 잠금
            if (c.lungePhase != CombatConfig.LgNone) return false;   // 런지 이동/경직 중
            if (c.attackPhase == CombatConfig.PhWindup
                || c.attackPhase == CombatConfig.PhActive) return false;   // 휘두르는 중
            return true;
        }

        bool IsReachable(int index, in SimWorld w)
        {
            Node n = nodes[index];
            if (!n.armed) return false;   // 아직 반경 밖으로 나가본 적이 없다 — 진입이 아니다

            // 대상이 있는 노드는 그 적이 아직 유효해야 성립한다 — 이미 죽었으면 건너뛴다.
            if (n.targetId >= 0)
            {
                int ti = PlayerCombat.FindEnemyIndex(in w, n.targetId);
                if (ti < 0 || !w.enemies[ti].alive || w.enemies[ti].combat.gloryStage > 0)
                {
                    Skip(index);
                    return false;
                }
            }

            return WithinTriggerRange(index, in w);
        }

        /// <summary>수평은 좁게, 수직은 넉넉하게 — 공중 노드는 점프 궤적을 정확히 맞출 수 없다.</summary>
        bool WithinTriggerRange(int index, in SimWorld w)
        {
            Vector3 nodePos = NodeWorldPosition(index, in w);
            Vector3 p = w.player.pos;
            float horizontal = new Vector2(p.x - nodePos.x, p.z - nodePos.z).magnitude;
            float vertical = Mathf.Abs(p.y - nodePos.y);
            return horizontal <= PredictionConfig.FreerunNodeRadius
                   && vertical <= PredictionConfig.FreerunNodeVerticalRadius;
        }

        void Skip(int index)
        {
            Node n = nodes[index];
            if (n.state != FreerunNodeState.Pending) return;
            n.state = FreerunNodeState.Gone;
            n.skipped = true;
            nodes[index] = n;
            if (index == Cursor) AdvanceCursor();
        }

        void Fire(int index, in SimWorld w, ref InputCmd cmd)
        {
            Node n = nodes[index];

            // 건너뛴 앞 노드들을 정리한다(닿지 않고 지나간 것).
            for (int i = Cursor; i < index; i++)
            {
                if (nodes[i].state != FreerunNodeState.Pending) continue;
                Node s = nodes[i];
                s.state = FreerunNodeState.Gone;
                s.skipped = true;
                nodes[i] = s;
            }

            n.state = FreerunNodeState.Shattering;
            n.shatterStart = Time.unscaledTime;
            nodes[index] = n;
            Cursor = index + 1;
            AdvanceCursor();
            FiredCount++;
            cooldownTicks = PredictionConfig.FreerunNodeCooldownTicks;

            ApplyAction(n, in w, ref cmd);
            CombatAudio.Hit();
            Feedback(LabelOf(n.type), 0.4f);
            // 닿은 직후 짧게 느려진다 — "다음 목표를 어디서 찾지"에 쓸 여유. 처벌이 아니라
            // 판독 시간이라 매번 걸린다(처치 슬로모보다 짧고 덜 깊게).
            Slow(PredictionConfig.FreerunNodeSlowSeconds, PredictionConfig.FreerunNodeSlowScale);
        }

        /// <summary>슬로모 요청. 이미 걸려 있으면 더 느린 쪽·더 늦게 끝나는 쪽으로 합친다.</summary>
        void Slow(float seconds, float scale)
        {
            bool active = Time.unscaledTime < slowUntil;
            slowScale = active ? Mathf.Min(slowScale, scale) : scale;
            slowSeconds = active ? Mathf.Max(slowSeconds, seconds) : seconds;
            slowUntil = active
                ? Mathf.Max(slowUntil, Time.unscaledTime + seconds)
                : Time.unscaledTime + seconds;
        }

        void AdvanceCursor()
        {
            while (Cursor < nodes.Count && nodes[Cursor].state != FreerunNodeState.Pending) Cursor++;
        }

        void ApplyAction(Node n, in SimWorld w, ref InputCmd cmd)
        {
            switch (n.type)
            {
                case PredictedActionType.Jump:
                    cmd.jump = true;
                    break;

                case PredictedActionType.Attack:
                    cmd.attack = true;
                    AimAt(NearestStrikeTarget(in w), in w, ref cmd);
                    break;

                case PredictedActionType.Lunge:
                    cmd.lunge = true;
                    cmd.lungeTargetId = n.targetId;
                    AimAt(n.targetId, in w, ref cmd);
                    break;

                case PredictedActionType.DashForward:
                    cmd.dash = true; cmd.dashDirection = DashDirection.Forward; break;
                case PredictedActionType.DashBackward:
                    cmd.dash = true; cmd.dashDirection = DashDirection.Backward; break;
                case PredictedActionType.DashLeft:
                    cmd.dash = true; cmd.dashDirection = DashDirection.Left; break;
                case PredictedActionType.DashRight:
                    cmd.dash = true; cmd.dashDirection = DashDirection.Right; break;
            }
        }

        /// <summary>
        /// 자동 공격이 허공을 가르면 허무하므로, 발동 순간의 조준만 대상 쪽으로 스냅한다.
        /// 이동·시점의 소유권은 계속 사용자에게 있고 이 한 틱만 덮어쓴다.
        /// </summary>
        void AimAt(int targetId, in SimWorld w, ref InputCmd cmd)
        {
            if (targetId < 0) return;
            int ti = PlayerCombat.FindEnemyIndex(in w, targetId);
            if (ti < 0) return;

            EnemySim e = w.enemies[ti];
            Vector3 from = w.player.pos + Vector3.up * (SimConfig.PlayerHeight * 0.5f);
            Vector3 to = e.pos + Vector3.up * (e.height * 0.5f);
            Vector3 dir = to - from;
            if (dir.sqrMagnitude < 1e-6f) return;

            Vector3 euler = Quaternion.LookRotation(dir.normalized).eulerAngles;
            cmd.yaw = euler.y;
            cmd.pitch = Mathf.DeltaAngle(0f, euler.x);
        }

        /// <summary>평타 노드는 대상 id가 없다(targetId=-1) — 사거리 안에서 가장 가까운 적을 본다.</summary>
        int NearestStrikeTarget(in SimWorld w)
        {
            int best = -1;
            float bestDistance = PredictionConfig.FreerunAttackAssistRange;
            for (int i = 0; i < w.enemyCount; i++)
            {
                EnemySim e = w.enemies[i];
                if (!e.alive || e.combat.gloryStage > 0) continue;
                float d = Vector3.Distance(w.player.pos, e.pos);
                if (d >= bestDistance) continue;
                bestDistance = d;
                best = e.id;
            }
            return best;
        }

        // ───────────────────────── 표시 ─────────────────────────

        /// <summary>
        /// 노드 i의 현재 월드 위치.
        ///
        /// [2026-07-22 되돌림] 처음엔 대상이 있는 노드를 그 적을 따라 움직이게 만들었다 —
        /// "사용자가 예측과 다르게 움직이면 적도 다르게 움직이니 잔상을 좌표에 못 박아두면
        /// 도착해도 적이 없다"는 게 근거였고 그 자체는 지금도 맞다. 그런데 실제로 플레이해보니
        /// <b>목표가 움직이는 것 자체가 훨씬 큰 혼란</b>이었다("어디로 가야 하는지 못 찾겠다").
        /// 예측이 보여준 그림과 실행 중의 그림이 달라지면 애초에 예지를 보는 의미가 없다.
        /// 그래서 잔상은 예측이 그린 좌표에 고정한다.
        ///
        /// 대신 대상이 죽거나 멀어져 성립하지 않게 된 노드는 <see cref="IsReachable"/>에서
        /// 건너뛰므로, 좌표 고정이 진행을 막지는 않는다. 다시 추종으로 바꾸려면
        /// <see cref="PredictionConfig.FreerunNodesFollowTarget"/>만 true로 돌리면 된다.
        /// </summary>
        public Vector3 NodeWorldPosition(int index, in SimWorld w)
        {
            if (index < 0 || index >= nodes.Count) return Vector3.zero;
            Node n = nodes[index];
            if (!PredictionConfig.FreerunNodesFollowTarget || n.targetId < 0) return n.anchor;

            int ti = PlayerCombat.FindEnemyIndex(in w, n.targetId);
            if (ti < 0 || !w.enemies[ti].alive) return n.anchor;

            EnemySim e = w.enemies[ti];
            Vector3 toPlayer = w.player.pos - e.pos;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude < 1e-4f) toPlayer = Vector3.forward;
            return e.pos + toPlayer.normalized * PredictionConfig.FreerunTargetStandoff;
        }

        /// <summary>다음에 가야 할 노드의 인덱스. 없으면 -1.</summary>
        public int NextIndex => Cursor < nodes.Count ? Cursor : -1;

        /// <summary>다음 노드의 월드 위치·종류. 안내 표시(월드 기둥·화면 화살표)가 쓴다.</summary>
        public bool TryGetNext(in SimWorld w, out Vector3 position, out PredictedActionType type)
        {
            int i = NextIndex;
            if (i < 0) { position = Vector3.zero; type = PredictedActionType.Jump; return false; }
            position = NodeWorldPosition(i, in w);
            type = nodes[i].type;
            return true;
        }

        public FreerunNodeState StateOf(int index)
            => index >= 0 && index < nodes.Count ? nodes[index].state : FreerunNodeState.Gone;

        /// <summary>깨짐 연출 진행률 0~1. Shattering이 아니면 0.</summary>
        public float ShatterProgress(int index)
        {
            if (index < 0 || index >= nodes.Count || nodes[index].state != FreerunNodeState.Shattering)
                return 0f;
            return Mathf.Clamp01(
                (Time.unscaledTime - nodes[index].shatterStart) / PredictionConfig.FreerunShatterSeconds);
        }

        void Feedback(string text, float seconds)
        {
            feedback = text;
            feedbackUntil = Time.unscaledTime + seconds;
        }

        /// <summary>
        /// "다음에 어디로 가야 하는가"를 화면에 그린다. 실제 플레이에서 가장 큰 불만이었던 부분 —
        /// 잔상이 월드에 떠 있어도 1인칭에서는 어느 게 다음 것인지, 어느 방향인지 못 읽는다.
        ///   · 화면 안이면 목표 위에 마름모 + 거리 + 어떤 액션이 터질지
        ///   · 화면 밖/뒤면 화면 가장자리로 밀어낸 화살표
        ///   · 그리고 <b>지금 눌러야 할 이동 키</b>(카메라 기준 W/A/S/D)
        /// 액션 키는 안내하지 않는다 — 자유 주행에서 액션은 닿으면 자동으로 터지기 때문이고,
        /// 그 사실 자체를 배지에 적어 헷갈리지 않게 한다.
        /// </summary>
        void DrawNextNodeGuide(in SimWorld w, Camera cam)
        {
            if (cam == null) return;
            if (!TryGetNext(in w, out Vector3 target, out PredictedActionType type)) return;

            Vector3 aim = target + Vector3.up * PredictionConfig.FreerunGuideHeight;
            Vector3 sp = cam.WorldToScreenPoint(aim);
            bool behind = sp.z <= 0f;
            // 카메라 뒤는 스크린 좌표가 뒤집혀 나오므로 부호를 바로잡아 화면 밖으로 밀어낸다.
            Vector2 gui = behind
                ? new Vector2(Screen.width - sp.x, sp.y)
                : new Vector2(sp.x, Screen.height - sp.y);

            float margin = PredictionConfig.FreerunGuideEdgeMargin;
            bool offscreen = behind
                             || gui.x < margin || gui.x > Screen.width - margin
                             || gui.y < margin || gui.y > Screen.height - margin;

            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (offscreen)
            {
                Vector2 dir = (gui - center);
                if (dir.sqrMagnitude < 1e-4f) dir = Vector2.up;
                dir.Normalize();
                float radius = Mathf.Min(Screen.width, Screen.height) * 0.5f - margin;
                gui = center + dir * radius;
            }

            float pulse = 0.5f + 0.5f * Mathf.Sin(
                Time.unscaledTime * PredictionConfig.FreerunGuidePulseHz * Mathf.PI * 2f);
            Color old = GUI.color;
            GUI.color = Color.Lerp(PredictionConfig.FreerunGuideDim,
                                   PredictionConfig.FreerunGuideBright, pulse);

            // 마름모(회전한 사각형) — 잔상 캡슐과 겹쳐도 구분되는 모양.
            float size = offscreen
                ? PredictionConfig.FreerunGuideSize * 1.3f
                : PredictionConfig.FreerunGuideSize;
            Matrix4x4 matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, gui);
            GUI.DrawTexture(new Rect(gui.x - size * 0.5f, gui.y - size * 0.5f, size, size),
                Texture2D.whiteTexture);
            GUI.matrix = matrix;
            GUI.color = old;

            float distance = Vector3.Distance(w.player.pos, target);
            var label = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperCenter, fontSize = 15,
                fontStyle = FontStyle.Bold, richText = true,
            };
            GUI.Label(new Rect(gui.x - 130f, gui.y + size * 0.7f, 260f, 46f),
                $"<color=#DFFFFF>{distance:0}m</color>  " +
                $"<color=#7FFFD0>{LabelOf(type)}</color>\n" +
                $"<size=12><color=#8FB3AB>닿으면 자동</color></size>", label);

            DrawMoveKeys(in w, cam, target);
        }

        /// <summary>목표 방향을 카메라 기준 W/A/S/D로 환산해 화면 아래에 크게 보여준다.</summary>
        void DrawMoveKeys(in SimWorld w, Camera cam, Vector3 target)
        {
            Vector3 delta = target - w.player.pos;
            delta.y = 0f;
            if (delta.sqrMagnitude < 0.04f) return;

            // 카메라 요를 기준으로 회전시키면 z=앞뒤, x=좌우가 그대로 키 배치가 된다.
            Vector3 local = Quaternion.Euler(0f, -cam.transform.eulerAngles.y, 0f) * delta;
            float span = new Vector2(local.x, local.z).magnitude;
            float gate = span * PredictionConfig.FreerunMoveKeyGate;

            string keys = "";
            if (local.z > gate) keys += "W";
            else if (local.z < -gate) keys += "S";
            if (local.x > gate) keys += keys.Length > 0 ? " + D" : "D";
            else if (local.x < -gate) keys += keys.Length > 0 ? " + A" : "A";
            if (keys.Length == 0) return;

            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter, fontSize = 30,
                fontStyle = FontStyle.Bold, richText = true,
            };
            GUI.Label(new Rect(Screen.width * 0.5f - 250f, Screen.height * 0.78f, 500f, 44f),
                $"<color=#DFFFFF>{keys}</color>", style);
        }

        static string LabelOf(PredictedActionType type)
        {
            switch (type)
            {
                case PredictedActionType.Jump: return "JUMP";
                case PredictedActionType.Attack: return "SLASH";
                case PredictedActionType.Lunge: return "LUNGE";
                case PredictedActionType.DashForward: return "DASH ↑";
                case PredictedActionType.DashBackward: return "DASH ↓";
                case PredictedActionType.DashLeft: return "DASH ←";
                case PredictedActionType.DashRight: return "DASH →";
                default: return "DASH";
            }
        }

        public void DrawHud(in SimWorld w, Camera cam)
        {
            if (!Active) return;

            DrawNextNodeGuide(in w, cam);

            var counter = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperRight,
                fontSize = 22,
                fontStyle = FontStyle.Bold,
                richText = true,
            };
            float remain = Mathf.Max(0f, budgetSeconds - (Time.unscaledTime - startRealTime));
            GUI.Label(new Rect(Screen.width - 268f, 16f, 250f, 96f),
                $"<color=#DFFFFF>{FiredCount} / {nodes.Count}</color>\n" +
                $"<size=16><color=#7FFFD0>{KillCount} KILL</color>  " +
                $"<color=#8FB3AB>{remain:0.0}s</color></size>", counter);

            if (Time.unscaledTime >= feedbackUntil || string.IsNullOrEmpty(feedback)) return;
            // 피드백 문구는 안내 배지와 겹치지 않게 화면 위쪽에 띄운다.
            var flash = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 30,
                fontStyle = FontStyle.Bold,
                richText = true,
            };
            GUI.Label(new Rect(Screen.width * 0.5f - 250f, Screen.height * 0.5f - 150f, 500f, 40f),
                $"<color=#FFD86A>{feedback}</color>", flash);
        }
    }
    // <<< [자유 주행 끝]
}
