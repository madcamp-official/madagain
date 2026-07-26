using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Game.Sim;
using Game.Prediction;

namespace Game.View
{
    // >>> [자석 주행(Magnet), 2026-07-22] 8번 추적 방식. 지금까지 나온 것들을 섞은 안.
    //
    // 자유 주행(6번)이 기반이다 — 이동은 사용자가 직접 하고 잔상에 닿으면 액션이 터진다.
    // 거기서 실제로 걸리던 세 가지를 고친다:
    //
    //   1. <b>자석</b> — 자유 주행의 판정 반경은 "정확히 그 자리에 서야" 하는 크기라, 대시로
    //      지나치거나 살짝 빗나가면 노드가 안 잡혔다. 여기서는 반경을 크게 잡고, 그 안에
    //      들어오면 이동 입력을 노드 쪽으로 섞어준다(cmd.move를 덮어쓰지 않고 blend) —
    //      "빨려 들어가는" 느낌이지 조작을 뺏는 게 아니다.
    //
    //   2. <b>공유 게이지</b> — 노드마다 제한 시간을 두면 매번 시계를 보게 되고, 한 번 늦으면
    //      바로 실패라 압박이 처벌로 읽힌다. 대신 전체가 나눠 쓰는 게이지 하나가 잔상과 잔상
    //      <b>사이를 이동하는 동안에만</b> 닳고, 노드에 닿으면 일부를 돌려받는다. 빠르게 이으면
    //      게이지가 유지되고 길게 헤매면 바닥난다 — 실패 판정이 아니라 자원 관리다.
    //
    //   3. <b>자동 회전</b> — 원형 포위 경로는 다음 대상이 등 뒤인 구간이 반드시 생긴다(경로
    //      진단에서 확인). 노드에 닿는 순간 시선을 다음 노드 쪽으로 부드럽게 돌려줘서, 회전이
    //      "찾는 노동"이 아니라 "따라가는 흐름"이 되게 한다. 도는 동안에도 조작권은 사용자에게
    //      있고(마우스를 움직이면 그쪽이 이긴다), Main.SetLookYaw로 실제 시선 상태를 같이
    //      갱신해서 회전이 끝난 뒤 마우스를 건드려도 원래 각도로 튕겨 돌아가지 않는다.
    //
    // Time.timeScale은 컨트롤러가 소유하므로 값만 내준다.
    // <<< [자석 주행 끝]

    /// <summary>자석 주행 노드 하나의 표시 상태.</summary>
    public enum MagnetNodeState : byte
    {
        Pending,
        Shattering,
        Gone,
    }

    /// <summary>자석 주행 상태 기계. <see cref="MagnetRunFollowMode"/>가 감싼다.</summary>
    public sealed class PredictionMagnetRun
    {
        struct Node
        {
            public int tick;
            public PredictedActionType type;
            public int targetId;
            public Vector3 anchor;
            public MagnetNodeState state;
            public float shatterStart;
            /// <summary>한 번이라도 반경 밖에 있었는가 — "닿음"은 겹침이 아니라 진입이다.
            /// (자유 주행과 같은 이유: 시작 발밑 노드가 가만히 서 있는데 터지는 걸 막는다.)</summary>
            public bool armed;
        }

        readonly List<Node> nodes = new List<Node>();
        readonly HashSet<int> defeatedSeen = new HashSet<int>();

        public bool Active { get; private set; }
        public int Cursor { get; private set; }
        public int NodeCount => nodes.Count;
        public int FiredCount { get; private set; }
        public int KillCount { get; private set; }

        public float TimeScale { get; private set; } = 1f;
        public bool WantsExit { get; private set; }

        /// <summary>전체가 공유하는 게이지 0~1. HUD가 이 값을 그린다.</summary>
        public float Gauge { get; private set; } = 1f;

        int cooldownTicks;
        string feedback = "";
        float feedbackUntil;
        float slowUntil;
        float slowScale = 1f;
        float slowSeconds;

        // 자동 회전 상태 — 노드에 닿는 순간 시작해서 짧게 돈다.
        bool turning;
        float turnFromYaw, turnToYaw, turnStart;

        Texture2D bar;

        // ───────────────────────── 생명주기 ─────────────────────────

        public void Begin(PredictedRoute route, in SimWorld w)
        {
            nodes.Clear();
            defeatedSeen.Clear();
            Cursor = 0;
            FiredCount = 0;
            KillCount = 0;
            TimeScale = 1f;
            WantsExit = false;
            Gauge = 1f;
            cooldownTicks = 0;
            feedback = "";
            feedbackUntil = 0f;
            slowUntil = 0f;
            slowScale = 1f;
            slowSeconds = 0f;
            turning = false;

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
                        state = MagnetNodeState.Pending,
                    });
                }
            }

            for (int i = 0; i < w.enemyCount; i++)
                if (!w.enemies[i].alive || w.enemies[i].combat.gloryStage > 0)
                    defeatedSeen.Add(w.enemies[i].id);

            Active = nodes.Count > 0;
            if (!Active) Debug.LogWarning("[자석 주행] 액션 노드가 없는 경로 — 건너뜁니다.");
        }

        public void End()
        {
            if (Active)
                Debug.Log($"[자석 주행] 종료 — 노드 {FiredCount}/{nodes.Count}, 처치 {KillCount}, " +
                          $"남은 게이지 {Gauge * 100f:0}%");
            Active = false;
            nodes.Clear();
            TimeScale = 1f;
            WantsExit = false;
            turning = false;
        }

        // ───────────────────────── 매 프레임 ─────────────────────────

        public void UpdateFrame(in SimWorld w, Camera cam)
        {
            if (!Active) return;

            DetectDefeats(in w);
            AdvanceShatters();
            ArmNodes(in w);
            DrainGauge();
            AdvanceTurn();

            float now = Time.unscaledTime;
            if (now < slowUntil && slowSeconds > 0f)
            {
                float u = Mathf.InverseLerp(slowUntil, slowUntil - slowSeconds, now);
                TimeScale = Mathf.Lerp(1f, slowScale, Mathf.SmoothStep(0f, 1f, u));
            }
            else { TimeScale = 1f; slowScale = 1f; slowSeconds = 0f; }

            if (Cursor >= nodes.Count)
            {
                if (!WantsExit) Debug.Log("[자석 주행] 완주 — 직접 조작으로 돌아갑니다.");
                WantsExit = true;
            }
        }

        /// <summary>
        /// 게이지는 <b>다음 노드로 이동하는 동안에만</b> 닳는다. 노드에 닿는 순간 일부를
        /// 돌려받으므로, 빠르게 이으면 유지되고 헤매면 바닥난다.
        /// </summary>
        void DrainGauge()
        {
            if (Cursor >= nodes.Count) return;
            Gauge -= PredictionConfig.MagnetGaugeDrainPerSecond * Time.unscaledDeltaTime;
            if (Gauge > 0f) return;

            Gauge = 0f;
            if (!WantsExit) Debug.Log("[자석 주행] 게이지 소진 — 예지가 풀립니다.");
            WantsExit = true;
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
                // 처치는 게이지를 돌려준다 — "잘 이으면 더 오래 본다"는 보상 구조.
                Gauge = Mathf.Min(1f, Gauge + PredictionConfig.MagnetGaugeKillRefund);
                Slow(PredictionConfig.FreerunKillSlowSeconds, PredictionConfig.FreerunKillSlowScale);
                Feedback("KILL", 0.6f);
            }
        }

        void ArmNodes(in SimWorld w)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].armed || nodes[i].state != MagnetNodeState.Pending) continue;
                if (WithinCapture(i, in w)) continue;
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
                if (nodes[i].state != MagnetNodeState.Shattering) continue;
                if (now - nodes[i].shatterStart < PredictionConfig.FreerunShatterSeconds) continue;
                Node n = nodes[i];
                n.state = MagnetNodeState.Gone;
                nodes[i] = n;
            }
        }

        // ───────────────────────── 자동 회전 ─────────────────────────

        void BeginTurn(in SimWorld w)
        {
            int next = NextIndex;
            if (next < 0) { turning = false; return; }

            Vector3 to = NodeWorldPosition(next, in w) - w.player.pos;
            to.y = 0f;
            if (to.sqrMagnitude < 1e-4f) { turning = false; return; }

            turnFromYaw = Main.Instance != null ? Main.Instance.LookYaw : w.player.yaw;
            turnToYaw = Quaternion.LookRotation(to.normalized).eulerAngles.y;
            // 이미 그쪽을 보고 있으면 굳이 돌리지 않는다 — 미세하게 튀는 것보다 가만한 게 낫다.
            if (Mathf.Abs(Mathf.DeltaAngle(turnFromYaw, turnToYaw)) < PredictionConfig.MagnetTurnMinDegrees)
            { turning = false; return; }

            turnStart = Time.unscaledTime;
            turning = true;
        }

        void AdvanceTurn()
        {
            if (!turning) return;
            float u = Mathf.Clamp01(
                (Time.unscaledTime - turnStart) / Mathf.Max(0.01f, PredictionConfig.MagnetTurnSeconds));
            float eased = 0.5f - 0.5f * Mathf.Cos(Mathf.PI * u);
            float yaw = Mathf.LerpAngle(turnFromYaw, turnToYaw, eased);

            // 실제 시선 상태를 같이 갱신한다 — 안 그러면 회전이 끝난 뒤 마우스를 조금만
            // 움직여도 원래 각도로 튕겨 돌아간다(입력 쪽이 자기 누적값을 계속 들고 있으므로).
            if (Main.Instance != null) Main.Instance.SetLookYaw(yaw);
            if (u >= 1f) turning = false;
        }

        /// <summary>회전 중이면 카메라 yaw를 모드가 지정한다.</summary>
        public bool TryGetCameraYaw(in SimWorld w, out float yaw)
        {
            if (!turning) { yaw = 0f; return false; }
            float u = Mathf.Clamp01(
                (Time.unscaledTime - turnStart) / Mathf.Max(0.01f, PredictionConfig.MagnetTurnSeconds));
            float eased = 0.5f - 0.5f * Mathf.Cos(Mathf.PI * u);
            yaw = Mathf.LerpAngle(turnFromYaw, turnToYaw, eased);
            return true;
        }

        // ───────────────────────── 매 sim 틱 ─────────────────────────

        /// <summary>사용자 입력 위에 자석 유도와 노드 발동을 얹는다.</summary>
        public bool TryInject(in SimWorld w, ref InputCmd cmd)
        {
            if (!Active) return false;

            ApplyMagnetSteering(in w, ref cmd);

            if (cooldownTicks > 0) { cooldownTicks--; return false; }
            if (!CanAcceptAction(in w)) return false;

            int last = Mathf.Min(Cursor + PredictionConfig.FreerunLookaheadNodes, nodes.Count - 1);
            for (int i = Cursor; i <= last; i++)
            {
                if (i < 0 || nodes[i].state != MagnetNodeState.Pending) continue;
                if (!IsReachable(i, in w)) continue;
                Fire(i, in w, ref cmd);
                return true;
            }
            return false;
        }

        /// <summary>
        /// 자석 유도. 노드가 유인 반경 안에 있고 <b>사용자가 실제로 이동 중일 때만</b> 걸린다 —
        /// 가만히 서 있는데 끌려가면 조작을 뺏긴 느낌이 되고, 그건 자동 재생과 다를 게 없다.
        /// 덮어쓰지 않고 섞기 때문에 사용자가 반대로 밀면 그쪽이 이긴다.
        /// </summary>
        void ApplyMagnetSteering(in SimWorld w, ref InputCmd cmd)
        {
            int i = NextIndex;
            if (i < 0) return;
            if (cmd.move.sqrMagnitude < PredictionConfig.MagnetSteerMinInput) return;

            Vector3 to = NodeWorldPosition(i, in w) - w.player.pos;
            to.y = 0f;
            float distance = to.magnitude;
            if (distance < 0.01f || distance > PredictionConfig.MagnetSteerRadius) return;

            // 가까울수록 세게 — 멀리서부터 끌면 경로 전체가 레일처럼 느껴진다.
            float pull = PredictionConfig.MagnetSteerStrength
                         * (1f - Mathf.Clamp01(distance / PredictionConfig.MagnetSteerRadius));

            // 월드 방향을 카메라(yaw) 기준 로컬로 바꿔야 cmd.move와 같은 축이 된다.
            Vector3 local = Quaternion.Euler(0f, -cmd.yaw, 0f) * to.normalized;
            var want = new Vector2(local.x, local.z);
            Vector2 blended = Vector2.Lerp(cmd.move, want * cmd.move.magnitude, pull);
            if (blended.sqrMagnitude > 1f) blended.Normalize();
            cmd.move = blended;
        }

        static bool CanAcceptAction(in SimWorld w)
        {
            PlayerCombatState c = w.player.combat;
            if (c.gloryPhase != CombatConfig.GlNone) return false;
            if (c.lungePhase != CombatConfig.LgNone) return false;
            if (c.attackPhase == CombatConfig.PhWindup
                || c.attackPhase == CombatConfig.PhActive) return false;
            return true;
        }

        bool IsReachable(int index, in SimWorld w)
        {
            Node n = nodes[index];
            if (!n.armed) return false;

            if (n.targetId >= 0)
            {
                int ti = PlayerCombat.FindEnemyIndex(in w, n.targetId);
                if (ti < 0 || !w.enemies[ti].alive || w.enemies[ti].combat.gloryStage > 0)
                { Skip(index); return false; }
            }
            return WithinCapture(index, in w);
        }

        /// <summary>자유 주행보다 훨씬 넉넉한 포획 반경 — 이게 "자석"의 본체다.</summary>
        bool WithinCapture(int index, in SimWorld w)
        {
            Vector3 nodePos = NodeWorldPosition(index, in w);
            Vector3 p = w.player.pos;
            float horizontal = new Vector2(p.x - nodePos.x, p.z - nodePos.z).magnitude;
            float vertical = Mathf.Abs(p.y - nodePos.y);
            return horizontal <= PredictionConfig.MagnetCaptureRadius
                   && vertical <= PredictionConfig.MagnetCaptureVerticalRadius;
        }

        void Skip(int index)
        {
            Node n = nodes[index];
            if (n.state != MagnetNodeState.Pending) return;
            n.state = MagnetNodeState.Gone;
            nodes[index] = n;
            if (index == Cursor) AdvanceCursor();
        }

        void Fire(int index, in SimWorld w, ref InputCmd cmd)
        {
            Node n = nodes[index];

            for (int i = Cursor; i < index; i++)
            {
                if (nodes[i].state != MagnetNodeState.Pending) continue;
                Node s = nodes[i];
                s.state = MagnetNodeState.Gone;
                nodes[i] = s;
            }

            n.state = MagnetNodeState.Shattering;
            n.shatterStart = Time.unscaledTime;
            nodes[index] = n;
            Cursor = index + 1;
            AdvanceCursor();
            FiredCount++;
            cooldownTicks = PredictionConfig.FreerunNodeCooldownTicks;

            Gauge = Mathf.Min(1f, Gauge + PredictionConfig.MagnetGaugeNodeRefund);

            ApplyAction(n, in w, ref cmd);
            CombatAudio.Hit();
            Feedback(LabelOf(n.type), 0.4f);
            Slow(PredictionConfig.MagnetNodeSlowSeconds, PredictionConfig.MagnetNodeSlowScale);
            BeginTurn(in w);   // 다음 노드 쪽으로 시선을 돌려준다
        }

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
            while (Cursor < nodes.Count && nodes[Cursor].state != MagnetNodeState.Pending) Cursor++;
        }

        void ApplyAction(Node n, in SimWorld w, ref InputCmd cmd)
        {
            switch (n.type)
            {
                case PredictedActionType.Jump: cmd.jump = true; break;
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

        static void AimAt(int targetId, in SimWorld w, ref InputCmd cmd)
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

        static int NearestStrikeTarget(in SimWorld w)
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

        void Feedback(string text, float seconds)
        {
            feedback = text;
            feedbackUntil = Time.unscaledTime + seconds;
        }

        // ───────────────────────── 표시 ─────────────────────────

        public Vector3 NodeWorldPosition(int index, in SimWorld w)
            => index >= 0 && index < nodes.Count ? nodes[index].anchor : Vector3.zero;

        public MagnetNodeState StateOf(int index)
            => index >= 0 && index < nodes.Count ? nodes[index].state : MagnetNodeState.Gone;

        public float ShatterProgress(int index)
        {
            if (index < 0 || index >= nodes.Count
                || nodes[index].state != MagnetNodeState.Shattering) return 0f;
            return Mathf.Clamp01(
                (Time.unscaledTime - nodes[index].shatterStart)
                / Mathf.Max(0.01f, PredictionConfig.FreerunShatterSeconds));
        }

        public int NextIndex => Cursor < nodes.Count ? Cursor : -1;

        public bool TryGetNext(in SimWorld w, out Vector3 position)
        {
            int i = NextIndex;
            if (i < 0) { position = Vector3.zero; return false; }
            position = NodeWorldPosition(i, in w);
            return true;
        }

        // ───────────────────────── HUD ─────────────────────────

        public void DrawHud(in SimWorld w, Camera cam)
        {
            EnsureBar();
            DrawGauge();
            DrawNextArrow(in w, cam);
            DrawFeedback();
        }

        /// <summary>공유 게이지 — 이 모드의 유일한 압박이므로 크고 분명하게 그린다.</summary>
        void DrawGauge()
        {
            float width = Mathf.Clamp(Screen.width * 0.32f, 260f, 560f);
            float height = Mathf.Clamp(Screen.height * 0.017f, 10f, 20f);
            float x = (Screen.width - width) * 0.5f;
            float y = Screen.height * 0.845f;

            Color old = GUI.color;
            GUI.color = PredictionConfig.MagnetGaugeBack;
            GUI.DrawTexture(new Rect(x, y, width, height), bar);

            // 낮아질수록 붉어진다 — 숫자를 안 봐도 위험이 읽히게.
            GUI.color = Color.Lerp(
                PredictionConfig.MagnetGaugeLow, PredictionConfig.MagnetGaugeHigh,
                Mathf.SmoothStep(0f, 1f, Gauge));
            GUI.DrawTexture(new Rect(x, y, width * Mathf.Clamp01(Gauge), height), bar);
            GUI.color = old;

            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperCenter,
                fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.019f, 13f, 20f)),
                richText = true,
            };
            int done = Mathf.Min(Cursor, nodes.Count);
            GUI.Label(new Rect(x - 60f, y + height + 4f, width + 120f, 26f),
                      $"<color=#9FE6D2>FORESIGHT {Gauge * 100f:0}%   ·   {done} / {nodes.Count}</color>",
                      style);
        }

        /// <summary>다음 노드가 화면 밖이면 어느 쪽인지만 알려준다(자동 회전이 대부분 해결한다).</summary>
        void DrawNextArrow(in SimWorld w, Camera cam)
        {
            int i = NextIndex;
            if (i < 0 || cam == null) return;

            Vector3 world = NodeWorldPosition(i, in w) + Vector3.up * PredictionConfig.FreerunGuideHeight;
            Vector3 sp = cam.WorldToScreenPoint(world);
            bool onScreen = sp.z > 0f && sp.x >= 0f && sp.x <= Screen.width
                            && sp.y >= 0f && sp.y <= Screen.height;
            if (onScreen) return;

            Vector2 dir = new Vector2(sp.x, sp.y) - new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (sp.z <= 0f) dir = -dir;
            if (dir.sqrMagnitude < 1e-4f) return;
            dir.Normalize();

            float margin = PredictionConfig.FreerunGuideEdgeMargin;
            float gx = Screen.width * 0.5f + dir.x * (Screen.width * 0.5f - margin);
            float gy = Screen.height - (Screen.height * 0.5f + dir.y * (Screen.height * 0.5f - margin));

            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(PredictionConfig.FreerunGuideSize),
                fontStyle = FontStyle.Bold,
                richText = true,
            };
            GUI.Label(new Rect(gx - 40f, gy - 20f, 80f, 40f), "<color=#7CFFD0>◆</color>", style);
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
            GUI.Label(new Rect(Screen.width * 0.5f - 200f, Screen.height * 0.63f, 400f, 46f),
                      $"<color=#7CFFD0>{feedback}</color>", style);
        }

        void EnsureBar()
        {
            if (bar != null) return;
            bar = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "PredictionMagnetBar" };
            bar.SetPixel(0, 0, Color.white);
            bar.Apply();
        }

        static string LabelOf(PredictedActionType t)
        {
            switch (t)
            {
                case PredictedActionType.Jump: return "JUMP";
                case PredictedActionType.Attack: return "STRIKE";
                case PredictedActionType.Lunge: return "LUNGE";
                default: return "DASH";
            }
        }
    }

    /// <summary>모드 8 래퍼.</summary>
    public sealed class MagnetRunFollowMode : IFollowMode
    {
        readonly PredictionMagnetRun runtime;
        public MagnetRunFollowMode(PredictionMagnetRun runtime) { this.runtime = runtime; }

        public PredictionRhythmMode Id => PredictionRhythmMode.MagnetRun;
        public string Name => RhythmModeRuntime.ModeName(Id);
        public string Hint => RhythmModeRuntime.ModeHint(Id);
        public bool Active => runtime.Active;
        public FollowInputOwnership Ownership => FollowInputOwnership.LiveInput;

        public void Begin(PredictedRoute route, in SimWorld w) => runtime.Begin(route, in w);
        public void End() => runtime.End();
        public bool WantsExit => runtime.WantsExit;
        public void UpdateFrame(in SimWorld w, Camera cam) => runtime.UpdateFrame(in w, cam);

        public bool OwnsTimeScale => true;
        public float TimeScale => runtime.TimeScale;

        public bool TryInject(in SimWorld w, ref InputCmd cmd) => runtime.TryInject(in w, ref cmd);
        public bool TryAdvanceReplay(int tick, in SimWorld w) => true;
        public void CaptureInput(Keyboard kb, Mouse mouse, in SimWorld w) { }
        public bool TryGetHoldCommand(in SimWorld w, out InputCmd cmd) { cmd = default; return false; }
        public bool SuppressesHitStop => false;

        public FollowCameraMode CameraMode => FollowCameraMode.FirstPerson;
        public bool ShowsPlayerBody => false;
        public bool TryGetCameraYaw(in SimWorld w, out float yaw)
            => runtime.TryGetCameraYaw(in w, out yaw);
        public bool AllowsLiveLook => true;

        public int HighlightIndex => runtime.NextIndex;

        public bool TryGetNodeVisual(int index, in SimWorld w, out FollowNodeVisual visual)
        {
            if (!runtime.Active) { visual = default; return false; }
            visual = new FollowNodeVisual
            {
                visible = runtime.StateOf(index) != MagnetNodeState.Gone,
                position = runtime.NodeWorldPosition(index, in w),
                shatter = runtime.ShatterProgress(index),
            };
            return true;
        }

        public bool TryGetWorldGuide(in SimWorld w, out Vector3 position)
            => runtime.TryGetNext(in w, out position);

        public bool WantsCursorVisible => false;
        public bool ReplacesDefaultHud => true;
        public void DrawHud(in SimWorld w, Camera cam) => runtime.DrawHud(in w, cam);
    }
}
