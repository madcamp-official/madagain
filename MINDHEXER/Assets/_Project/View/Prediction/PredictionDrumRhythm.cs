using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Game.Sim;
using Game.Prediction;

namespace Game.View
{
    // >>> [난타(Drum), 2026-07-22] 10번 추적 방식.
    //
    // 지금까지의 리듬 모드(1~5)는 <b>액션 하나당 입력 하나</b>였다. 예측 경로의 액션은 3초에
    // 6~10개 정도라 입력이 띄엄띄엄하고, 그 사이엔 할 게 없어서 "밋밋하다"가 됐다.
    //
    // 여기서는 액션 사이의 <b>연결 구간에도 노트를 채운다</b>:
    //   · B 노트 = 실제 액션 잔상(런지·평타·대시·점프). 예측이 정한 틱에 고정.
    //   · A 노트 = 그 사이를 잇는 연결 노트. 구간 길이와 밀도로 자동 생성된다.
    //
    // 밀도는 구간마다 다르다 — 뒤로 갈수록 촘촘해지고(경로 후반이 클라이맥스), 구간 인덱스
    // 해시로 ±변주를 준다. 랜덤이 아니라 결정론적이라 같은 경로면 항상 같은 채보가 나온다
    // (이 프로젝트의 결정론 원칙을 채보에도 그대로 적용).
    //
    // 중요한 차이: <b>노트를 놓쳐도 캐릭터는 계속 간다.</b> 이동·전투는 기록 입력 재생이
    // 그대로 굴리고(TryAdvanceReplay가 항상 true), 노트는 순수하게 점수·콤보만 건드린다.
    // 곡이 끊기지 않는 리듬게임의 규칙이다. 그래서 판정기(RhythmJudge)도 안 쓰고 자체 판정을
    // 돈다 — 판정기는 "이 입력으로 액션을 실행할지"를 결정하는 물건이라 역할이 다르다.
    //
    // 연출은 노트가 화면 중심으로 <b>다가오는</b> 형태다. 실제로는 내 캐릭터가 그 지점을 향해
    // 달려가는 것이지만, 1인칭에서는 잔상이 나에게 밀려오는 것처럼 읽힌다.
    // <<< [난타 끝]

    /// <summary>노트 종류. A는 연결, B는 실제 액션.</summary>
    public enum DrumNoteKind : byte { Link, Action }

    /// <summary>난타 상태 기계. <see cref="DrumRhythmFollowMode"/>가 감싼다.</summary>
    public sealed class PredictionDrumRhythm
    {
        struct Note
        {
            public int tick;
            public DrumNoteKind kind;
            public PredictedActionType type;   // Action 노트만 의미 있음
            public float angle;                // 화면에서 날아오는 방향(라디안)
            public bool judged;
            public bool hit;
        }

        readonly List<Note> notes = new List<Note>();

        public bool Active { get; private set; }
        public bool WantsExit => false;   // 재생이 끝나면 컨트롤러가 Exit한다
        public float TimeScale => 1f;     // 곡은 일정 속도로 간다 — 슬로모 없음

        public int Score { get; private set; }
        public int Combo { get; private set; }
        public int MaxCombo { get; private set; }
        public int HitCount { get; private set; }
        public int MissCount { get; private set; }
        public int NoteCount => notes.Count;

        // 재생 틱 추적 — 프레임(입력)과 틱(재생)의 해상도가 달라 소수점 틱을 추정한다.
        int currentTick;
        float currentTickAt;

        string feedback = "";
        float feedbackUntil;
        float hitFlashUntil;
        Texture2D ring, dot;

        // ───────────────────────── 생명주기 ─────────────────────────

        public void Begin(PredictedRoute route, in SimWorld w)
        {
            notes.Clear();
            Score = 0;
            Combo = 0;
            MaxCombo = 0;
            HitCount = 0;
            MissCount = 0;
            currentTick = 0;
            currentTickAt = Time.unscaledTime;
            feedback = "";
            feedbackUntil = 0f;
            hitFlashUntil = 0f;

            if (route != null) BuildChart(route);

            Active = notes.Count > 0;
            if (!Active) Debug.LogWarning("[난타] 채보를 만들지 못했습니다 — 건너뜁니다.");
            else Debug.Log($"[난타] 채보 생성 — 노트 {notes.Count}개 " +
                           $"(액션 {CountOf(DrumNoteKind.Action)} · 연결 {CountOf(DrumNoteKind.Link)})");
        }

        int CountOf(DrumNoteKind kind)
        {
            int n = 0;
            for (int i = 0; i < notes.Count; i++) if (notes[i].kind == kind) n++;
            return n;
        }

        /// <summary>
        /// 채보 생성. 액션 마커를 B로 깔고, 인접한 두 B 사이를 A로 채운다.
        /// 구간 밀도는 (1) 경로 후반일수록 촘촘 (2) 구간 인덱스 해시로 ±변주 — 둘 다
        /// 결정론적이라 같은 경로면 항상 같은 채보가 나온다.
        /// </summary>
        void BuildChart(PredictedRoute route)
        {
            int markerCount = route.actionMarkers.Count;
            if (markerCount == 0) return;

            int lastTick = route.actionMarkers[markerCount - 1].tick;

            for (int i = 0; i < markerCount; i++)
            {
                ActionMarker m = route.actionMarkers[i];
                Add(m.tick, DrumNoteKind.Action, m.type);

                if (i + 1 >= markerCount) continue;
                int nextTick = route.actionMarkers[i + 1].tick;
                int interval = LinkInterval(i, m.tick, lastTick);

                // [2026-07-22 실측 후 수정] 처음엔 "액션이 띄엄띄엄하니 사이를 채운다"는 전제로
                // 간격을 크게(22→11틱) 잡았는데, 실제 경로를 넣어보니 액션 마커 자체가 5~20틱
                // 간격이었다(3초에 13개 = 초당 4.7액션). 그래서 조건이 한 번도 성립 안 해
                // 연결 노트가 0개였다. 실측에 맞춰 간격을 줄이고, 양 끝에서 최소 간격만
                // 지키면 넣도록 바꾼다 — 액션 노트 바로 옆에 붙어 어느 키인지 구분 못 하는
                // 것만 막으면 된다.
                int guard = PredictionConfig.DrumMinSeparationTicks;
                for (int t = m.tick + interval; t <= nextTick - guard; t += interval)
                {
                    if (t - m.tick < guard) continue;
                    Add(t, DrumNoteKind.Link, PredictedActionType.Attack);
                }
            }

            notes.Sort((a, b) => a.tick.CompareTo(b.tick));
        }

        /// <summary>연결 노트 간격(틱). 작을수록 촘촘 = 어렵다.</summary>
        static int LinkInterval(int segmentIndex, int segmentTick, int lastTick)
        {
            // 후반부일수록 촘촘하게 — 경로가 끝나갈수록 몰아친다.
            float progress = lastTick > 0 ? Mathf.Clamp01(segmentTick / (float)lastTick) : 0f;
            float baseInterval = Mathf.Lerp(
                PredictionConfig.DrumLinkIntervalStart,
                PredictionConfig.DrumLinkIntervalEnd, progress);

            // 구간마다 ±변주(결정론적 해시) — 모든 구간이 같은 간격이면 기계적으로 들린다.
            int h = (segmentIndex + 1) * 7919;
            h = (h ^ (h >> 7)) * 31 + 17;
            float jitter = ((h >> 3) & 0x3) * 0.5f - 0.75f;   // -0.75 ~ +0.75 스텝
            return Mathf.Max(PredictionConfig.DrumLinkIntervalMin,
                             Mathf.RoundToInt(baseInterval + jitter * PredictionConfig.DrumLinkJitter));
        }

        void Add(int tick, DrumNoteKind kind, PredictedActionType type)
        {
            // 각도는 인덱스 기반 결정론 — 황금각으로 돌려 인접 노트가 겹치지 않게 흩는다.
            float angle = notes.Count * 2.39996f;
            notes.Add(new Note { tick = tick, kind = kind, type = type, angle = angle });
        }

        public void End()
        {
            if (Active)
                Debug.Log($"[난타] 종료 — 점수 {Score}, 최대 콤보 {MaxCombo}, " +
                          $"{HitCount}/{notes.Count} 성공 (Miss {MissCount})");
            Active = false;
            notes.Clear();
        }

        // ───────────────────────── 매 sim 틱 ─────────────────────────

        /// <summary>재생은 절대 막지 않는다 — 여기서는 현재 틱만 받아 적는다.</summary>
        public bool TryAdvanceReplay(int tick, in SimWorld w)
        {
            currentTick = tick;
            currentTickAt = Time.unscaledTime;
            return true;
        }

        /// <summary>프레임 시점의 소수점 틱. 입력 판정이 틱 해상도(60Hz)에 갇히지 않게 한다.</summary>
        float NowTick()
        {
            float since = Time.unscaledTime - currentTickAt;
            return currentTick + Mathf.Clamp01(since * SimConfig.TickRate);
        }

        // ───────────────────────── 매 프레임 ─────────────────────────

        public void UpdateFrame(in SimWorld w, Camera cam)
        {
            if (!Active) return;

            // 판정 창을 지난 노트는 Miss로 넘긴다 — 곡은 계속 간다.
            float now = NowTick();
            for (int i = 0; i < notes.Count; i++)
            {
                if (notes[i].judged || now <= notes[i].tick + PredictionConfig.DrumGoodWindowTicks) continue;
                Note n = notes[i];
                n.judged = true;
                n.hit = false;
                notes[i] = n;
                MissCount++;
                Combo = 0;
                Feedback("MISS", 0.3f);
            }
        }

        /// <summary>A = 연결 노트, B = 액션 노트. 난타라 폴링이 아니라 눌린 프레임만 본다.</summary>
        public void CaptureInput(Keyboard kb, Mouse mouse, in SimWorld w)
        {
            if (!Active || kb == null) return;
            if (kb.aKey.wasPressedThisFrame) Judge(DrumNoteKind.Link);
            if (kb.bKey.wasPressedThisFrame) Judge(DrumNoteKind.Action);
        }

        void Judge(DrumNoteKind kind)
        {
            float now = NowTick();
            int best = -1;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < notes.Count; i++)
            {
                if (notes[i].judged || notes[i].kind != kind) continue;
                float d = Mathf.Abs(notes[i].tick - now);
                if (d > PredictionConfig.DrumGoodWindowTicks || d >= bestDistance) continue;
                bestDistance = d;
                best = i;
            }

            // 헛침에 벌점은 주지 않는다 — 난타 모드에서 오타를 처벌하면 손을 아끼게 되고,
            // 그러면 "난타"라는 감각 자체가 사라진다.
            if (best < 0) return;

            Note n = notes[best];
            n.judged = true;
            n.hit = true;
            notes[best] = n;

            bool perfect = bestDistance <= PredictionConfig.DrumPerfectWindowTicks;
            HitCount++;
            Combo++;
            if (Combo > MaxCombo) MaxCombo = Combo;
            Score += (perfect ? PredictionConfig.DrumScorePerfect : PredictionConfig.DrumScoreGood)
                     + Combo * PredictionConfig.DrumScoreComboStep;

            hitFlashUntil = Time.unscaledTime + 0.12f;
            Feedback(perfect ? "PERFECT" : "GOOD", 0.25f);
            CombatAudio.Hit();
        }

        void Feedback(string text, float seconds)
        {
            feedback = text;
            feedbackUntil = Time.unscaledTime + seconds;
        }

        // ───────────────────────── HUD ─────────────────────────

        public void DrawHud(in SimWorld w, Camera cam)
        {
            EnsureTextures();

            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            float hitRadius = Mathf.Clamp(Screen.height * 0.075f, 52f, 110f);
            float spawnRadius = Mathf.Min(Screen.width, Screen.height) * 0.62f;
            float now = NowTick();
            float lead = PredictionConfig.DrumLookaheadTicks;

            Color old = GUI.color;

            // 판정 원 — 여기까지 좁혀졌을 때 치면 된다.
            GUI.color = Time.unscaledTime < hitFlashUntil
                ? Color.white
                : PredictionConfig.DrumHitRingColor;
            GUI.DrawTexture(Centered(cx, cy, hitRadius * 2f), ring);

            // 노트: 남은 시간이 곧 중심까지의 거리 — 다가오는 것처럼 읽힌다.
            for (int i = 0; i < notes.Count; i++)
            {
                Note n = notes[i];
                if (n.judged) continue;
                float remain = n.tick - now;
                if (remain > lead || remain < -PredictionConfig.DrumGoodWindowTicks) continue;

                float u = Mathf.Clamp01(remain / lead);                 // 1=먼 미래, 0=지금
                float radius = Mathf.Lerp(hitRadius, spawnRadius, u);
                float nx = cx + Mathf.Cos(n.angle) * radius;
                float ny = cy + Mathf.Sin(n.angle) * radius;

                bool action = n.kind == DrumNoteKind.Action;
                float size = (action ? PredictionConfig.DrumActionNoteSize
                                     : PredictionConfig.DrumLinkNoteSize)
                             * Mathf.Lerp(1f, 0.45f, u);                 // 멀수록 작게 = 원근
                Color c = action ? PredictionConfig.DrumActionNoteColor
                                 : PredictionConfig.DrumLinkNoteColor;
                c.a *= Mathf.Lerp(1f, 0.25f, u);
                GUI.color = c;
                GUI.DrawTexture(Centered(nx, ny, size * 2f), action ? ring : dot);
            }
            GUI.color = old;

            DrawKeyGuide(cx, cy, hitRadius);
            DrawScore();
            DrawFeedback(cx, cy, hitRadius);
        }

        static void DrawKeyGuide(float cx, float cy, float hitRadius)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.02f, 14f, 22f)),
                fontStyle = FontStyle.Bold,
                richText = true,
            };
            GUI.Label(new Rect(cx - 200f, cy + hitRadius + 10f, 400f, 26f),
                      $"<color=#7CFFD0>A</color> <color=#8FB3AB>연결</color>    " +
                      $"<color=#FFC46B>B</color> <color=#8FB3AB>액션</color>", style);
        }

        void DrawScore()
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperCenter,
                fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.024f, 16f, 26f)),
                fontStyle = FontStyle.Bold,
                richText = true,
            };
            string comboText = Combo >= 2
                ? $"   <color=#FFC46B>{Combo} COMBO</color>"
                : "";
            GUI.Label(new Rect(Screen.width * 0.5f - 300f, Screen.height * 0.12f, 600f, 32f),
                      $"<color=#9FE6D2>{Score}</color>{comboText}", style);
        }

        void DrawFeedback(float cx, float cy, float hitRadius)
        {
            if (Time.unscaledTime >= feedbackUntil || string.IsNullOrEmpty(feedback)) return;
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.03f, 20f, 34f)),
                fontStyle = FontStyle.Bold,
                richText = true,
            };
            string color = feedback == "MISS" ? "#FF7A7A" : "#7CFFD0";
            GUI.Label(new Rect(cx - 200f, cy - hitRadius - 46f, 400f, 36f),
                      $"<color={color}>{feedback}</color>", style);
        }

        static Rect Centered(float x, float y, float size)
            => new Rect(x - size * 0.5f, y - size * 0.5f, size, size);

        void EnsureTextures()
        {
            if (ring == null) ring = MakeCircle("PredictionDrumRing", true);
            if (dot == null) dot = MakeCircle("PredictionDrumDot", false);
        }

        static Texture2D MakeCircle(string name, bool hollow)
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name,
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
                    float a = hollow
                        ? Mathf.Clamp01((0.5f - d) / 0.02f) * Mathf.Clamp01((d - 0.5f + 0.09f) / 0.02f)
                        : Mathf.Clamp01((0.46f - d) / 0.03f);
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }
    }

    /// <summary>모드 10 래퍼.</summary>
    public sealed class DrumRhythmFollowMode : IFollowMode
    {
        readonly PredictionDrumRhythm runtime;
        public DrumRhythmFollowMode(PredictionDrumRhythm runtime) { this.runtime = runtime; }

        public PredictionRhythmMode Id => PredictionRhythmMode.DrumRhythm;
        public string Name => RhythmModeRuntime.ModeName(Id);
        public string Hint => RhythmModeRuntime.ModeHint(Id);
        public bool Active => runtime.Active;
        /// <summary>재생을 붙잡지는 않지만(항상 통과), 판정기를 안 쓰므로 GatedReplay로 둔다 —
        /// 그래야 컨트롤러가 RhythmJudge 경로를 안 타고 CaptureInput을 이쪽으로 넘긴다.</summary>
        public FollowInputOwnership Ownership => FollowInputOwnership.GatedReplay;

        public void Begin(PredictedRoute route, in SimWorld w) => runtime.Begin(route, in w);
        public void End() => runtime.End();
        public bool WantsExit => runtime.WantsExit;
        public void UpdateFrame(in SimWorld w, Camera cam) => runtime.UpdateFrame(in w, cam);

        public bool OwnsTimeScale => true;   // 곡은 일정 속도 — 리듬 페이싱을 끈다
        public float TimeScale => runtime.TimeScale;

        public bool TryInject(in SimWorld w, ref InputCmd cmd) => false;
        public bool TryAdvanceReplay(int tick, in SimWorld w) => runtime.TryAdvanceReplay(tick, in w);
        public void CaptureInput(Keyboard kb, Mouse mouse, in SimWorld w)
            => runtime.CaptureInput(kb, mouse, in w);
        public bool TryGetHoldCommand(in SimWorld w, out InputCmd cmd) { cmd = default; return false; }
        public bool SuppressesHitStop => false;

        public FollowCameraMode CameraMode => FollowCameraMode.FirstPerson;
        public bool ShowsPlayerBody => false;
        public bool TryGetCameraYaw(in SimWorld w, out float yaw) { yaw = 0f; return false; }
        public bool AllowsLiveLook => false;

        // 잔상 강조·표시는 기존 규칙 그대로 둔다 — 여기서 보는 건 화면의 노트이지 월드 잔상이 아니다.
        public int HighlightIndex => -1;
        public bool TryGetNodeVisual(int index, in SimWorld w, out FollowNodeVisual visual)
        { visual = default; return false; }
        public bool TryGetWorldGuide(in SimWorld w, out Vector3 position)
        { position = default; return false; }

        public bool WantsCursorVisible => false;
        public bool ReplacesDefaultHud => true;
        public void DrawHud(in SimWorld w, Camera cam) => runtime.DrawHud(in w, cam);
    }
}
