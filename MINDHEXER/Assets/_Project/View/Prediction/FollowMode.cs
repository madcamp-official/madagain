using UnityEngine;
using UnityEngine.InputSystem;
using Game.Sim;
using Game.Prediction;

namespace Game.View
{
    // >>> [추적 방식 추상화, 2026-07-22] "예측 경로를 어떻게 따라가게 할 것인가"의 후보를
    // 계속 새로 붙여보기 위한 레이어.
    //
    // 이전 구조의 문제: 모드 1~5(리듬)는 전부 같은 파이프라인(기록 입력 재생 + RhythmJudge)을
    // 쓰고 "박자를 어떻게 치나"만 달라서 RhythmModeRuntime 내부 switch로 잘 처리됐다. 그런데
    // 6번(자유 주행)은 파이프라인 자체를 우회해서, PredictionController 2157줄 곳곳에
    // `if (freerun.Active)`가 15군데 흩어졌다. 7번째 후보를 같은 식으로 넣으면 또 15군데가 는다.
    //
    // 그래서 추상화 지점을 "박자 치는 법"이 아니라 <b>추적 방식</b>으로 한 단계 올린다.
    // 인터페이스의 각 멤버는 컨트롤러가 이미 묻고 있던 질문 그대로다 — 새로 발명한 게 아니라
    // 흩어져 있던 분기를 이름 붙여 모은 것이다.
    //
    // 모드 추가 = <see cref="FollowModeRegistry"/>의 배열에 한 줄. 숫자키 매핑은 배열 인덱스라
    // 따로 손댈 곳이 없다. 기존 RhythmModeRuntime·PredictionFreerun의 내부 로직은 건드리지
    // 않고 래퍼만 씌웠으므로, 이 레이어를 통째로 걷어내도 그 둘은 그대로 남는다.

    /// <summary>이 추적 방식이 실제 플레이어 입력을 어떻게 다루는가 — 컨트롤러의 분기 축.</summary>
    public enum FollowInputOwnership
    {
        /// <summary>기록 입력을 그대로 재생하고 RhythmJudge가 박자를 판정한다(모드 1~5).</summary>
        RecordedReplay,
        /// <summary>기록 입력을 재생하되, 모드가 특정 틱에서 재생을 <b>붙잡아</b> 사용자 행동을
        /// 기다린다(모드 7 클릭 체인). 판정기는 쓰지 않는다.</summary>
        GatedReplay,
        /// <summary>재생하지 않는다 — 이동·시점이 사용자 것이고 모드는 액션만 얹는다(모드 6).</summary>
        LiveInput,
    }

    /// <summary>실행 중 카메라를 어떻게 둘 것인가.</summary>
    public enum FollowCameraMode
    {
        /// <summary>실제 플레이어의 눈으로 따라간다(기존 동작).</summary>
        FirstPerson,
        /// <summary>3인칭 궤도를 유지한 채 움직이는 플레이어를 뒤에서 따라간다 —
        /// 자기 캐릭터가 예측대로 움직이는 걸 <b>보게</b> 하는 방식(모드 9).</summary>
        ThirdPersonOrbit,
    }

    /// <summary>액션 잔상 하나의 표시 상태. 컨트롤러의 UpdateGhostMarks가 읽는다.</summary>
    public struct FollowNodeVisual
    {
        /// <summary>false면 이 잔상을 숨긴다(이미 소진됨).</summary>
        public bool visible;
        /// <summary>표시 위치(월드). 모드가 대상 추종/고정 중 무엇을 쓰는지는 모드가 정한다.</summary>
        public Vector3 position;
        /// <summary>깨짐 연출 진행률 0~1.</summary>
        public float shatter;
        /// <summary>true면 아래 <see cref="tint"/>로 색을 덮어쓴다(기본 잔상 색 규칙 무시).
        /// "다음에 칠 잔상"을 확실하게 구분시키려는 모드가 쓴다.</summary>
        public bool hasTint;
        public Color tint;
    }

    /// <summary>
    /// 예측 경로를 따라가는 한 가지 방식. PredictionController가 소유하지 않고
    /// <see cref="FollowModeRegistry"/>가 인스턴스를 들고 있으며, 컨트롤러는 Current만 본다.
    ///
    /// Time.timeScale은 여전히 컨트롤러가 소유한다 — 모드는 <see cref="TimeScale"/>로 값만
    /// 내주고 직접 쓰지 않는다.
    /// </summary>
    public interface IFollowMode
    {
        /// <summary>숫자키·PlayerPrefs 저장에 쓰는 식별자.</summary>
        PredictionRhythmMode Id { get; }
        /// <summary>화면 배지에 찍히는 이름(예: "7 CHAIN").</summary>
        string Name { get; }
        /// <summary>전환 시 로그·배지에 찍히는 한 줄 설명.</summary>
        string Hint { get; }

        /// <summary>Following 실행 중인가. Begin~End 사이에만 true.</summary>
        bool Active { get; }
        FollowInputOwnership Ownership { get; }

        // ── 생명주기 ──
        void Begin(PredictedRoute route, in SimWorld w);
        void End();
        /// <summary>컨트롤러가 Exit해야 하는가(완주·시간 초과 등).</summary>
        bool WantsExit { get; }

        /// <summary>매 프레임(실시간). 카메라는 화면 좌표 판정이 필요한 모드만 쓴다.</summary>
        void UpdateFrame(in SimWorld w, Camera cam);

        // ── 시간 ──
        /// <summary>true면 컨트롤러가 리듬 페이싱 대신 <see cref="TimeScale"/>을 그대로 쓴다.</summary>
        bool OwnsTimeScale { get; }
        float TimeScale { get; }

        // ── 매 sim 틱 ──
        /// <summary><see cref="FollowInputOwnership.LiveInput"/> 전용. 사용자 입력 위에 액션을 얹는다.</summary>
        bool TryInject(in SimWorld w, ref InputCmd cmd);
        /// <summary><see cref="FollowInputOwnership.GatedReplay"/> 전용. false면 이번 틱 재생을 멈춘다.
        /// 항상 true를 주면 "재생은 그대로 두고 판정만 따로 하는" 리듬형 모드가 된다(모드 10).</summary>
        bool TryAdvanceReplay(int tick, in SimWorld w);

        /// <summary>
        /// <see cref="TryAdvanceReplay"/>가 false를 준 <b>동안</b> 세계를 계속 굴릴 명령.
        ///
        /// 재생을 붙잡으면 Main.FixedUpdate가 그 틱을 통째로 건너뛰므로 <b>적·투사체까지
        /// 전부 얼어붙는다</b>. 여기서 true와 함께 중립 명령을 주면, 재생 인덱스는 그대로 둔 채
        /// 그 명령으로 sim을 한 틱 굴린다 — 플레이어는 기록 경로에서 멈춰 있지만 적은 계속
        /// 움직이고 공격하고 투사체도 날아간다.
        ///
        /// false면 예전처럼 완전 정지.
        /// </summary>
        bool TryGetHoldCommand(in SimWorld w, out InputCmd cmd);
        /// <summary>이 모드가 도는 동안 런지 히트스톱을 끌 것인가. 히트스톱은 sim 틱을
        /// 통째로 건너뛰므로 "적만 얼어붙은" 것처럼 보인다 — 액션이 잦은 모드에서는 끈다.</summary>
        bool SuppressesHitStop { get; }

        // ── 입력 ──
        /// <summary>
        /// 모드가 직접 받는 키/마우스. 기록 재생 + RhythmJudge 경로(모드 1~5)는 컨트롤러의
        /// CaptureRhythmInputs가 처리하므로 여기서 아무것도 안 해도 된다. A/B 난타처럼
        /// 판정기를 안 쓰는 모드가 자기 입력을 받는 자리다.
        /// </summary>
        void CaptureInput(Keyboard kb, Mouse mouse, in SimWorld w);

        // ── 카메라 ──
        FollowCameraMode CameraMode { get; }
        /// <summary>3인칭에서 플레이어 본체(아바타)를 세워 보여줄 것인가.</summary>
        bool ShowsPlayerBody { get; }
        /// <summary>시선 yaw를 모드가 지정한다(다음 노드 자동 조준 등). false면 기본 규칙.</summary>
        bool TryGetCameraYaw(in SimWorld w, out float yaw);
        /// <summary>
        /// 지금 시선 조작권이 사용자에게 있는가. 기록 재생 모드도 <b>일시적으로</b> true가 될 수
        /// 있다(슬로우 포켓 동안 자유 회전). true인 동안 Main이 마우스를 폴링하고 카메라가
        /// input.Yaw/Pitch를 따라간다 — 그래서 자동 추종 카메라와 동시에 켜지면 안 된다.
        /// <see cref="FollowInputOwnership.LiveInput"/>은 항상 true인 것과 같다.
        /// </summary>
        bool AllowsLiveLook { get; }

        // ── 표시 ──
        /// <summary>"지금 처리해야 할 잔상"의 인덱스. -1이면 모드가 정하지 않음
        /// (컨트롤러가 RhythmJudge의 대기 인덱스로 폴백한다).</summary>
        int HighlightIndex { get; }
        /// <summary>잔상 i의 표시를 모드가 덮어쓰는가. false면 컨트롤러 기본 표시를 쓴다.</summary>
        bool TryGetNodeVisual(int index, in SimWorld w, out FollowNodeVisual visual);
        /// <summary>월드 안내 표시(빛기둥)를 세울 위치. false면 안 세운다.</summary>
        bool TryGetWorldGuide(in SimWorld w, out Vector3 position);
        /// <summary>마우스 커서를 보여야 하는가(화면 클릭으로 조준하는 모드).</summary>
        bool WantsCursorVisible { get; }

        // ── HUD ──
        /// <summary>기본 박자 HUD 대신 모드 전용 HUD를 그리는가.</summary>
        bool ReplacesDefaultHud { get; }
        void DrawHud(in SimWorld w, Camera cam);
    }

    /// <summary>
    /// 모드 1~5 래퍼. 다섯 모드가 <see cref="RhythmModeRuntime"/> 인스턴스 <b>하나</b>를 공유하고
    /// 변종 값만 다르다 — 클래스를 다섯 개 만들 이유가 없다. 실제 판정·연출은 예전 그대로
    /// 컨트롤러의 RecordedReplay 경로가 처리하므로, 여기서는 생명주기만 중계한다.
    /// </summary>
    public sealed class RhythmFollowMode : IFollowMode
    {
        readonly PredictionRhythmMode id;
        readonly RhythmModeRuntime runtime;
        readonly FollowCameraMode camera;
        bool active;

        public RhythmFollowMode(
            PredictionRhythmMode id, RhythmModeRuntime runtime,
            FollowCameraMode camera = FollowCameraMode.FirstPerson)
        {
            this.id = id;
            this.runtime = runtime;
            this.camera = camera;
        }

        public PredictionRhythmMode Id => id;
        public string Name => RhythmModeRuntime.ModeName(id);
        public string Hint => RhythmModeRuntime.ModeHint(id);
        public bool Active => active;
        public FollowInputOwnership Ownership => FollowInputOwnership.RecordedReplay;

        public void Begin(PredictedRoute route, in SimWorld w)
        {
            active = true;
            runtime.BeginRoute();
        }

        public void End()
        {
            if (!active) return;
            active = false;
            runtime.EndRoute();
        }

        public bool WantsExit => false;   // 재생이 끝나면 컨트롤러가 알아서 Exit한다
        public void UpdateFrame(in SimWorld w, Camera cam) { }

        public bool OwnsTimeScale => false;   // 리듬 페이싱은 컨트롤러가 계속 소유
        public float TimeScale => 1f;

        public bool TryInject(in SimWorld w, ref InputCmd cmd) => false;
        public bool TryAdvanceReplay(int tick, in SimWorld w) => true;
        public void CaptureInput(Keyboard kb, Mouse mouse, in SimWorld w) { }
        public bool TryGetHoldCommand(in SimWorld w, out InputCmd cmd) { cmd = default; return false; }
        public bool SuppressesHitStop => false;

        // 카메라만 바꾼 변종이 곧 모드 9다 — 판정·재생은 Classic과 완전히 같고, 자기 캐릭터를
        // 3인칭으로 보면서 타이밍을 친다. 그래서 새 상태 기계를 만들 이유가 없다.
        public FollowCameraMode CameraMode => camera;
        public bool ShowsPlayerBody => camera == FollowCameraMode.ThirdPersonOrbit;
        public bool TryGetCameraYaw(in SimWorld w, out float yaw) { yaw = 0f; return false; }
        public bool AllowsLiveLook => false;

        public int HighlightIndex => -1;      // RhythmJudge의 대기 인덱스를 쓰라는 뜻
        public bool TryGetNodeVisual(int index, in SimWorld w, out FollowNodeVisual visual)
        { visual = default; return false; }
        public bool TryGetWorldGuide(in SimWorld w, out Vector3 position)
        { position = default; return false; }
        public bool WantsCursorVisible => false;

        // Highway의 레일 HUD는 예전처럼 컨트롤러의 리듬 경로 안에서 그린다(rhythmJudge가 필요).
        public bool ReplacesDefaultHud => false;
        public void DrawHud(in SimWorld w, Camera cam) { }
    }

    /// <summary>모드 6 래퍼. <see cref="PredictionFreerun"/>의 기존 API를 그대로 중계한다.</summary>
    public sealed class FreerunFollowMode : IFollowMode
    {
        readonly PredictionFreerun runtime;
        public FreerunFollowMode(PredictionFreerun runtime) { this.runtime = runtime; }

        public PredictionRhythmMode Id => PredictionRhythmMode.Freerun;
        public string Name => RhythmModeRuntime.ModeName(Id);
        public string Hint => RhythmModeRuntime.ModeHint(Id);
        public bool Active => runtime.Active;
        public FollowInputOwnership Ownership => FollowInputOwnership.LiveInput;

        public void Begin(PredictedRoute route, in SimWorld w) => runtime.Begin(route, in w);
        public void End() => runtime.End();
        public bool WantsExit => runtime.WantsExit;
        public void UpdateFrame(in SimWorld w, Camera cam) => runtime.UpdateFrame(in w);

        public bool OwnsTimeScale => true;
        public float TimeScale => runtime.TimeScale;

        public bool TryInject(in SimWorld w, ref InputCmd cmd) => runtime.TryInject(in w, ref cmd);
        public bool TryAdvanceReplay(int tick, in SimWorld w) => true;
        public void CaptureInput(Keyboard kb, Mouse mouse, in SimWorld w) { }
        public bool TryGetHoldCommand(in SimWorld w, out InputCmd cmd) { cmd = default; return false; }
        public bool SuppressesHitStop => false;

        public FollowCameraMode CameraMode => FollowCameraMode.FirstPerson;
        public bool ShowsPlayerBody => false;
        public bool TryGetCameraYaw(in SimWorld w, out float yaw) { yaw = 0f; return false; }
        public bool AllowsLiveLook => true;   // 자유 주행 — 이동·시점이 통째로 사용자 것

        public int HighlightIndex => runtime.NextIndex;

        public bool TryGetNodeVisual(int index, in SimWorld w, out FollowNodeVisual visual)
        {
            if (!runtime.Active) { visual = default; return false; }
            visual = new FollowNodeVisual
            {
                visible = runtime.StateOf(index) != FreerunNodeState.Gone,
                position = runtime.NodeWorldPosition(index, in w),
                shatter = runtime.ShatterProgress(index),
            };
            return true;
        }

        public bool TryGetWorldGuide(in SimWorld w, out Vector3 position)
            => runtime.TryGetNext(in w, out position, out _);

        public bool WantsCursorVisible => false;
        public bool ReplacesDefaultHud => true;
        public void DrawHud(in SimWorld w, Camera cam) => runtime.DrawHud(in w, cam);
    }

    /// <summary>
    /// 추적 방식 등록소. <b>모드를 추가하려면 아래 <c>modes</c> 배열에 한 줄만 넣으면 된다</b> —
    /// 숫자키는 배열 인덱스(1번 키 = modes[0])라 입력 처리를 따로 고칠 필요가 없다.
    /// </summary>
    public static class FollowModeRegistry
    {
        // 상태 기계 인스턴스는 여기가 소유한다(예전엔 PredictionController의 필드였다).
        // 컨트롤러가 여러 번 생성되더라도 모드 선택·진행이 흔들리지 않게 하기 위함.
        public static readonly RhythmModeRuntime Rhythm = new RhythmModeRuntime();
        public static readonly PredictionFreerun Freerun = new PredictionFreerun();
        public static readonly PredictionClickChain ClickChain = new PredictionClickChain();
        public static readonly PredictionMagnetRun MagnetRun = new PredictionMagnetRun();
        public static readonly PredictionDrumRhythm DrumRhythm = new PredictionDrumRhythm();
        public static readonly PredictionSlowAim SlowAim = new PredictionSlowAim();

        // [2026-07-22] 예측 추적 방식은 SLOW-AIM 하나로 확정 — F를 누르면 바로 이 모드로 실행된다.
        // 다른 후보 모드(Classic/Freerun/ClickChain/…)는 선택지에서 제거했다. 각 상태 기계 클래스와
        // 위의 static 인스턴스는 참조 안정성 때문에 남겨뒀지만, 이 배열에 없으면 게임에선 쓸 수 없다.
        // 다시 실험하려면 그 줄을 배열에 되돌려 넣기만 하면 된다.
        static readonly IFollowMode[] modes =
        {
            new SlowAimFollowMode(SlowAim),   // 11 SLOW-AIM — 유일 모드
        };

        const string PrefKey = "PredictionRhythmMode";

        // [기존 버그 회피 유지, 2026-07-22] PlayerPrefs를 정적 초기화자에서 읽으면 MonoBehaviour
        // 생성자 경로에서 예외가 나 Main의 필드 초기화가 통째로 중단된다(PredictionRhythmModes.cs
        // 의 주석 참고). 실제로 읽을 때까지 미룬다.
        static int index;
        static bool loaded;

        static int Index
        {
            get
            {
                if (!loaded)
                {
                    loaded = true;
                    index = Mathf.Clamp(PlayerPrefs.GetInt(PrefKey, 0), 0, modes.Length - 1);
                }
                return index;
            }
            set { loaded = true; index = Mathf.Clamp(value, 0, modes.Length - 1); }
        }

        public static IFollowMode Current => modes[Index];
        public static PredictionRhythmMode CurrentId => modes[Index].Id;
        public static int Count => modes.Length;

        public static void Select(int i)
        {
            if (i < 0 || i >= modes.Length || i == Index) return;
            Index = i;
            PlayerPrefs.SetInt(PrefKey, i);
            Debug.Log($"[예측 추적] 모드 전환 → {Current.Name}  ({Current.Hint})");
        }

        public static void Select(PredictionRhythmMode id)
        {
            for (int i = 0; i < modes.Length; i++)
                if (modes[i].Id == id) { Select(i); return; }
        }

        /// <summary>
        /// 숫자키 감시. Following 중엔 흐름이 깨지므로 호출하지 않는다.
        /// Key 열거형이 Digit1..Digit9, Digit0 순서로 연속이라 인덱스로 직접 훑는다 —
        /// 10번째 모드가 자연스럽게 0번 키에 걸린다. 11개째부터는 숫자키가 모자라므로
        /// 백쿼트(`)로 전체를 순환한다 — 숫자키로 못 가는 모드는 이걸로만 닿는다.
        /// </summary>
        public static bool PollSwitch(Keyboard kb)
        {
            if (kb == null) return false;

            if (kb.backquoteKey.wasPressedThisFrame)
            {
                Select((Index + 1) % modes.Length);
                return true;
            }

            int keys = Mathf.Min(modes.Length, 10);
            for (int i = 0; i < keys; i++)
            {
                if (!kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame) continue;
                Select(i);
                return true;
            }
            return false;
        }
    }
}
