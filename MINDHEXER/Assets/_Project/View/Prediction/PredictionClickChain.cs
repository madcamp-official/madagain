using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Game.Sim;
using Game.Prediction;

namespace Game.View
{
    // >>> [클릭 체인(Chain), 2026-07-22] 7번 추적 방식.
    //
    // 근거: 예측 경로의 뼈대는 런지 연쇄이고(경로 진단 결과 LungeStrike#N이 시퀀스 대부분),
    // 런지는 원래 게임에서 <b>조준하고 클릭하는</b> 액션이다(CombatConfig.LungeAimRadius).
    // 즉 "잔상을 클릭한다"는 건 예지 전용 미니게임이 아니라 평소 조작 그대로다 — 리듬 모드
    // 1~5가 게임의 동사와 무관한 새 규칙을 얹어서 밋밋했던 것과 정반대 방향이다.
    //
    // 규칙은 한 줄이다: <b>다음 잔상을 찍어라. 나머지는 알아서 간다.</b>
    //   · 잔상 사이 이동은 기록 입력 재생(예측이 계산한 그 경로 그대로) — 예지 화면과
    //     실제 움직임이 어긋나지 않는다.
    //   · 액션 잔상의 틱에 도달하면 재생을 <b>붙잡고</b> 포켓(슬로모)을 연다.
    //   · 사용자가 그 잔상을 클릭하면 재생이 풀리고 액션이 나간다.
    //   · 시간이 다 되면 실패가 아니라 그냥 자동 발동한다 — 처벌이 아니라 판독 시간이다.
    //
    // 포켓의 리듬은 발명한 게 아니다. 런지 명중마다 이미 히트스톱이 걸리고 있었고
    // (CombatConfig.LungeHitStopTicks = 7), 여기서는 그 자리를 조준 창으로 늘렸을 뿐이다.
    //
    // 조준은 커서로 한다(카메라를 돌리지 않는다). 기록 재생이 시선(yaw)을 소유하는데 그 위에
    // 마우스 회전을 얹으면 서로 싸우고, 원형 포위 경로는 다음 대상이 등 뒤인 구간이 반드시
    // 생겨서 "찾는 노동"이 된다. 커서 방식이면 그 문제가 통째로 사라진다. 조준 쾌감을 되찾고
    // 싶으면 PredictionConfig.ClickChainCursorAim을 false로 두고 화면 중앙 조준으로 바꾸면 된다.
    // <<< [클릭 체인 끝]

    /// <summary>클릭 체인 노드 하나의 표시 상태.</summary>
    public enum ClickChainNodeState : byte
    {
        /// <summary>아직 안 찍음.</summary>
        Pending,
        /// <summary>찍혀서 발동 — 깨지는 연출 중.</summary>
        Shattering,
        /// <summary>연출까지 끝나 사라짐.</summary>
        Gone,
    }

    /// <summary>
    /// 클릭 체인 상태 기계. <see cref="ClickChainFollowMode"/>가 감싸고, 컨트롤러는
    /// IFollowMode 훅으로만 호출한다. Time.timeScale은 컨트롤러가 소유하므로 값만 내준다.
    /// </summary>
    public sealed class PredictionClickChain
    {
        struct Node
        {
            public int tick;
            public PredictedActionType type;
            public int targetId;
            /// <summary>예측이 이 액션을 시작한 위치. 클릭 체인은 기록 경로를 그대로 재생하므로
            /// 이 좌표가 실제로 플레이어가 도달할 자리다 — 추종시킬 이유가 없다.</summary>
            public Vector3 anchor;
            public ClickChainNodeState state;
            public float shatterStart;
            public bool autoFired;   // 사용자가 찍은 게 아니라 시간 초과로 나간 것
        }

        readonly List<Node> nodes = new List<Node>();

        public bool Active { get; private set; }
        /// <summary>다음에 찍어야 할 노드 인덱스. 잔상 강조가 이 값을 쓴다.</summary>
        public int Cursor { get; private set; }
        public int NodeCount => nodes.Count;
        public int ClickedCount { get; private set; }
        public int AutoFiredCount { get; private set; }

        /// <summary>컨트롤러가 매 프레임 Time.timeScale에 반영할 값.</summary>
        public float TimeScale { get; private set; } = 1f;

        /// <summary>지금 조준 포켓이 열려 있는가(재생을 붙잡고 클릭을 기다리는 중).</summary>
        public bool PocketOpen { get; private set; }
        float pocketOpenedAt;

        string feedback = "";
        float feedbackUntil;
        Texture2D reticle;

        // ───────────────────────── 생명주기 ─────────────────────────

        public void Begin(PredictedRoute route, in SimWorld w)
        {
            nodes.Clear();
            Cursor = 0;
            ClickedCount = 0;
            AutoFiredCount = 0;
            TimeScale = 1f;
            PocketOpen = false;
            pocketOpenedAt = 0f;
            feedback = "";
            feedbackUntil = 0f;

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
                        state = ClickChainNodeState.Pending,
                    });
                }
            }

            Active = nodes.Count > 0;
            if (!Active)
                Debug.LogWarning("[클릭 체인] 액션 노드가 없는 경로 — 클릭 체인을 건너뜁니다.");
        }

        public void End()
        {
            if (Active)
                Debug.Log($"[클릭 체인] 종료 — 직접 클릭 {ClickedCount} / 자동 {AutoFiredCount} " +
                          $"(노드 {nodes.Count}개)");
            Active = false;
            nodes.Clear();
            PocketOpen = false;
            TimeScale = 1f;
        }

        /// <summary>재생이 끝나면 컨트롤러가 알아서 Exit하므로 여기서 종료를 요구하지 않는다.</summary>
        public bool WantsExit => false;

        // ───────────────────────── 매 sim 틱: 재생 게이트 ─────────────────────────

        /// <summary>
        /// 기록 입력 재생을 이번 틱에 진행시켜도 되는가. 다음 노드의 틱에 도달하면 false를
        /// 돌려 재생을 붙잡고, 사용자가 그 잔상을 클릭하면 다시 true가 된다.
        /// </summary>
        public bool TryAdvanceReplay(int tick, in SimWorld w)
        {
            if (!Active) return true;
            if (Cursor >= nodes.Count) { ClosePocket(); return true; }

            // 아직 노드 틱 전 — 자유롭게 재생(이 구간이 곧 자동 주행 = 질주 구간).
            if (tick < nodes[Cursor].tick) { ClosePocket(); return true; }

            // 노드 틱 도달 — 포켓을 열고 클릭을 기다린다.
            if (!PocketOpen)
            {
                PocketOpen = true;
                pocketOpenedAt = Time.unscaledTime;
            }
            return false;
        }

        void ClosePocket()
        {
            PocketOpen = false;
        }

        // ───────────────────────── 매 프레임 ─────────────────────────

        public void UpdateFrame(in SimWorld w, Camera cam)
        {
            if (!Active) return;

            AdvanceShatters();

            // 포켓이 열려 있으면 느려지고, 아니면 주행 속도로 돌아온다. 스냅이 아니라
            // 감속/가속이라 "질주 → 정지 → 질주"의 케이던스가 연출로 읽힌다.
            float target = PocketOpen
                ? PredictionConfig.ClickChainPocketTimeScale
                : PredictionConfig.ClickChainRunTimeScale;
            TimeScale = Mathf.MoveTowards(
                TimeScale, target,
                PredictionConfig.ClickChainTimeScaleRate * Time.unscaledDeltaTime);

            if (!PocketOpen) return;

            if (TryClick(in w, cam)) return;

            // 시간 초과 — 실패가 아니라 그냥 예측대로 나간다.
            if (Time.unscaledTime - pocketOpenedAt >= PredictionConfig.ClickChainPocketSeconds)
                Fire(Cursor, auto: true);
        }

        void AdvanceShatters()
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].state != ClickChainNodeState.Shattering) continue;
                if (now - nodes[i].shatterStart < PredictionConfig.FreerunShatterSeconds) continue;
                Node n = nodes[i];
                n.state = ClickChainNodeState.Gone;
                nodes[i] = n;
            }
        }

        /// <summary>이번 프레임의 좌클릭이 강조된 잔상을 맞췄는가.</summary>
        bool TryClick(in SimWorld w, Camera cam)
        {
            var mouse = Mouse.current;
            if (mouse == null || cam == null) return false;
            if (!mouse.leftButton.wasPressedThisFrame) return false;
            if (!TryGetScreenPoint(Cursor, in w, cam, out Vector2 target)) return false;

            Vector2 aim = PredictionConfig.ClickChainCursorAim
                ? mouse.position.ReadValue()
                : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            if (Vector2.Distance(aim, target) > HitRadius()) return false;

            Fire(Cursor, auto: false);
            return true;
        }

        static float HitRadius()
            => Mathf.Max(PredictionConfig.ClickChainHitRadiusMinPx,
                         Screen.height * PredictionConfig.ClickChainHitRadiusScreen);

        void Fire(int index, bool auto)
        {
            if (index < 0 || index >= nodes.Count) return;

            Node n = nodes[index];
            n.state = ClickChainNodeState.Shattering;
            n.shatterStart = Time.unscaledTime;
            n.autoFired = auto;
            nodes[index] = n;

            Cursor = index + 1;
            AdvanceCursor();
            ClosePocket();

            if (auto)
            {
                AutoFiredCount++;
                Feedback("AUTO", 0.35f);
            }
            else
            {
                ClickedCount++;
                CombatAudio.Hit();
                Feedback(LabelOf(n.type), 0.4f);
            }
        }

        void AdvanceCursor()
        {
            while (Cursor < nodes.Count && nodes[Cursor].state != ClickChainNodeState.Pending) Cursor++;
        }

        void Feedback(string text, float seconds)
        {
            feedback = text;
            feedbackUntil = Time.unscaledTime + seconds;
        }

        // ───────────────────────── 표시 ─────────────────────────

        /// <summary>노드 i의 월드 위치. 기록 경로를 그대로 재생하므로 예측 좌표에 고정한다.</summary>
        public Vector3 NodeWorldPosition(int index, in SimWorld w)
            => index >= 0 && index < nodes.Count ? nodes[index].anchor : Vector3.zero;

        public ClickChainNodeState StateOf(int index)
            => index >= 0 && index < nodes.Count ? nodes[index].state : ClickChainNodeState.Gone;

        public float ShatterProgress(int index)
        {
            if (index < 0 || index >= nodes.Count
                || nodes[index].state != ClickChainNodeState.Shattering) return 0f;
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

        bool TryGetScreenPoint(int index, in SimWorld w, Camera cam, out Vector2 point)
        {
            point = default;
            if (index < 0 || index >= nodes.Count || cam == null) return false;
            Vector3 world = NodeWorldPosition(index, in w)
                            + Vector3.up * PredictionConfig.ClickChainAimHeight;
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) return false;   // 카메라 뒤 — 클릭 불가(시간 초과로 자동 발동한다)
            point = new Vector2(sp.x, sp.y);
            return true;
        }

        // ───────────────────────── HUD ─────────────────────────

        public void DrawHud(in SimWorld w, Camera cam)
        {
            EnsureReticle();

            if (PocketOpen && TryGetScreenPoint(Cursor, in w, cam, out Vector2 sp))
            {
                // GUI는 좌상단 원점이라 y를 뒤집는다.
                float gx = sp.x;
                float gy = Screen.height - sp.y;
                float radius = HitRadius();

                float remain = Mathf.Clamp01(
                    1f - (Time.unscaledTime - pocketOpenedAt) / PredictionConfig.ClickChainPocketSeconds);

                Color old = GUI.color;
                // 바깥 링이 조여들면서 남은 시간을 보여준다 — 숫자보다 빠르게 읽힌다.
                GUI.color = PredictionConfig.ClickChainReticleDim;
                float outer = Mathf.Lerp(radius * 1.15f, radius * 2.6f, remain);
                GUI.DrawTexture(Centered(gx, gy, outer * 2f), reticle);

                float pulse = 0.5f + 0.5f * Mathf.Sin(
                    Time.unscaledTime * PredictionConfig.ClickChainReticlePulseHz * Mathf.PI * 2f);
                GUI.color = Color.Lerp(
                    PredictionConfig.ClickChainReticleDim,
                    PredictionConfig.ClickChainReticleBright, pulse);
                GUI.DrawTexture(Centered(gx, gy, radius * 2f), reticle);
                GUI.color = old;

                var label = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.024f, 16f, 26f)),
                    fontStyle = FontStyle.Bold,
                    richText = true,
                };
                GUI.Label(new Rect(gx - 160f, gy + radius + 6f, 320f, 30f),
                          $"<color=#7CFFD0>{LabelOf(nodes[Cursor].type)}</color>", label);
            }

            DrawProgress();
            DrawFeedback();
        }

        void DrawProgress()
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperCenter,
                fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.021f, 14f, 22f)),
                richText = true,
            };
            int done = Mathf.Min(Cursor, nodes.Count);
            GUI.Label(new Rect(Screen.width * 0.5f - 200f, Screen.height * 0.13f, 400f, 30f),
                      $"<color=#9FE6D2>{done} / {nodes.Count}</color>", style);

            if (!PocketOpen) return;
            var hint = new GUIStyle(style) { fontSize = Mathf.RoundToInt(style.fontSize * 0.85f) };
            GUI.Label(new Rect(Screen.width * 0.5f - 260f, Screen.height * 0.13f + 26f, 520f, 30f),
                      "<color=#6FBFAE>다음 잔상을 클릭</color>", hint);
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
            GUI.Label(new Rect(Screen.width * 0.5f - 200f, Screen.height * 0.62f, 400f, 46f),
                      $"<color=#7CFFD0>{feedback}</color>", style);
        }

        static Rect Centered(float x, float y, float size)
            => new Rect(x - size * 0.5f, y - size * 0.5f, size, size);

        void EnsureReticle()
        {
            if (reticle != null) return;
            const int size = 128;
            const float ringOuter = 0.5f;
            const float ringInner = 0.5f - 0.055f;
            reticle = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "PredictionClickChainReticle",
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
                    float a = Mathf.Clamp01((ringOuter - d) / 0.012f)
                              * Mathf.Clamp01((d - ringInner) / 0.012f);
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            reticle.SetPixels(px);
            reticle.Apply();
        }

        static string LabelOf(PredictedActionType t)
        {
            switch (t)
            {
                case PredictedActionType.Jump: return "JUMP";
                case PredictedActionType.Attack: return "STRIKE";
                case PredictedActionType.Lunge: return "LUNGE";
                case PredictedActionType.DashForward: return "DASH ↑";
                case PredictedActionType.DashBackward: return "DASH ↓";
                case PredictedActionType.DashLeft: return "DASH ←";
                case PredictedActionType.DashRight: return "DASH →";
                default: return t.ToString().ToUpperInvariant();
            }
        }
    }

    /// <summary>모드 7 래퍼.</summary>
    public sealed class ClickChainFollowMode : IFollowMode
    {
        readonly PredictionClickChain runtime;
        public ClickChainFollowMode(PredictionClickChain runtime) { this.runtime = runtime; }

        public PredictionRhythmMode Id => PredictionRhythmMode.ClickChain;
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
        public void CaptureInput(Keyboard kb, Mouse mouse, in SimWorld w) { }   // 클릭은 UpdateFrame이 본다
        public bool TryGetHoldCommand(in SimWorld w, out InputCmd cmd) { cmd = default; return false; }
        public bool SuppressesHitStop => false;

        public FollowCameraMode CameraMode => FollowCameraMode.FirstPerson;
        public bool ShowsPlayerBody => false;
        public bool TryGetCameraYaw(in SimWorld w, out float yaw) { yaw = 0f; return false; }
        public bool AllowsLiveLook => false;   // 커서로 조준하므로 시선은 재생이 소유

        public int HighlightIndex => runtime.NextIndex;

        public bool TryGetNodeVisual(int index, in SimWorld w, out FollowNodeVisual visual)
        {
            if (!runtime.Active) { visual = default; return false; }
            visual = new FollowNodeVisual
            {
                visible = runtime.StateOf(index) != ClickChainNodeState.Gone,
                position = runtime.NodeWorldPosition(index, in w),
                shatter = runtime.ShatterProgress(index),
            };
            return true;
        }

        public bool TryGetWorldGuide(in SimWorld w, out Vector3 position)
            => runtime.TryGetNext(in w, out position);

        /// <summary>커서로 조준하므로 포켓이 열린 동안만 커서를 보여준다.</summary>
        public bool WantsCursorVisible => PredictionConfig.ClickChainCursorAim && runtime.PocketOpen;

        public bool ReplacesDefaultHud => true;
        public void DrawHud(in SimWorld w, Camera cam) => runtime.DrawHud(in w, cam);
    }
}
