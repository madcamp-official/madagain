using System.Collections;
using UnityEngine;

namespace Game.View
{
    public enum PatternState { Idle, InProgress, Succeeded, Cancelled }

    /// <summary>
    /// 점 패턴 미니게임 오케스트레이터. HackDriver가 Begin/Tick으로 구동. (기초_설계안 §2.4)
    ///
    /// 규칙(사용자 확정):
    ///  - 판정은 휴대폰 패턴 락과 동일 — 절대 위치 기반(<see cref="PatternInput"/>), 방향 판정 없음.
    ///  - 패턴은 대상 인스턴스별 고정(Hackable.pattern에 1회 생성 캐시).
    ///  - 오답이어도 획은 계속 진행(즉시 실패 없음). 정답 경로를 벗어나면 그 시도는 완성 불가.
    ///  - 성공 = 그린 점 시퀀스가 타겟과 정확히 일치(완주).
    ///  - 취소는 <see cref="Cancel"/> 호출(Space 재탭) — 홀드 유지 개념 없음.
    ///  - 성공 후 0.2초 뒤 UI 하이드.
    /// </summary>
    public class PatternMinigame : MonoBehaviour
    {
        [Header("입력 튜닝")]
        [Tooltip("마우스 감도. 마우스 1px가 정규화 좌표로 얼마나 움직이는지. 격자 한 변 = 1.0.")]
        public float sensitivity = 1f / 300f;

        [Tooltip("점 히트 반경(격자 한 변 = 1.0 기준). 커서가 이 반경 안에 들어와야 연결된다.")]
        public float hitRadius = 0.16f;

        [Header("연출")]
        [Tooltip("성공 후 UI를 감출 때까지 지연(초).")]
        public float successHideDelay = 0.2f;

        [Tooltip("구형 화면 UI(ScreenSpaceOverlay). ★ VR에서 못 쓴다 — 양안에서 어긋나고 후처리를 " +
                 "안 타 보스전 흑빨에서 UI만 흰색으로 남는다. panel이 있으면 자동으로 꺼진다.")]
        public PatternUI ui;

        [Tooltip("월드스페이스 해킹 패널. 비어 있으면 씬에서 찾는다.")]
        public HackPanel panel;

        [Header("소리 — 점 연결마다 번갈아 재생")]
        [Tooltip("점 하나 연결될 때마다 이 둘을 번갈아 재생한다(같은 소리 연타 대신 딸깍이는 느낌).")]
        public AudioClip tickA;
        public AudioClip tickB;

        [Tooltip("틱음을 낼 AudioSource. 비우면 자기 자신에서 찾거나 새로 붙인다.")]
        public AudioSource tickAudio;

        [Range(0f, 1f)] public float tickVolume = 0.8f;

        bool _nextTickIsA = true;

        /// <summary>
        /// 쓸 패널을 확정한다. <b>구형 UI와 겹쳐 그려지는 것을 막는 것이 핵심</b> —
        /// <c>HackDriver</c>가 <see cref="ui"/>를 자동으로 붙이므로, 패널이 있으면 여기서 꺼야 한다.
        /// (<c>HackDriver</c>는 다른 작업이 진행 중인 파일이라 건드리지 않는다.)
        /// </summary>
        void ResolveView()
        {
            if (panel == null) panel = FindFirstObjectByType<HackPanel>();
            if (panel != null && ui != null && ui.enabled)
            {
                ui.Hide();
                ui.enabled = false;
            }
        }

        public PatternState State { get; private set; } = PatternState.Idle;
        public DotPattern Target { get; private set; }
        public PatternInput Input { get; private set; } = new PatternInput();

        System.Random _rng;
        bool _onTrack;

        void Awake()
        {
            if (tickAudio == null)
            {
                tickAudio = GetComponent<AudioSource>();
                if (tickAudio == null) tickAudio = gameObject.AddComponent<AudioSource>();
            }
            tickAudio.playOnAwake = false;
            tickAudio.spatialBlend = 0f;   // 2D — 손끝이 아니라 UI 소리다

            // ★ 이 컴포넌트는 런타임에만 생성돼(HackDriver.Awake) 씬에 없으므로 인스펙터로 못 물린다.
            //   Resources 폴더 관례로 자동 로드한다 — 인스펙터에 직접 물리면 그 값이 우선한다.
            if (tickA == null) tickA = Resources.Load<AudioClip>("Sfx/PatternTick/puzzle_tick1");
            if (tickB == null) tickB = Resources.Load<AudioClip>("Sfx/PatternTick/puzzle_tick2");
        }

        /// <summary>점 하나가 연결된 순간(성공·오답 무관) — tickA/B를 번갈아 낸다.</summary>
        void PlayTick()
        {
            AudioClip clip = _nextTickIsA ? tickA : tickB;
            _nextTickIsA = !_nextTickIsA;
            if (clip != null && tickAudio != null) tickAudio.PlayOneShot(clip, tickVolume);
        }

        /// <summary>아직 정답 경로 위에 있는지. 벗어나면 이번 시도는 완주해도 성공 불가.</summary>
        public bool OnTrack => _onTrack;

        /// <summary>
        /// 지금 가야 할 다음 목표 점(UI 강조용). 경로를 벗어났거나 완주했으면 -1.
        /// 순서를 이것으로만 전달한다 — 유령선만으로는 획 순서를 알 수 없기 때문(§2.4).
        /// </summary>
        public int NextTargetDot()
        {
            if (!_onTrack || Target == null) return -1;
            int i = Input.StrokeCount + 1;
            return i < Target.dots.Length ? Target.dots[i] : -1;
        }

        /// <summary>해킹 시작 — 대상의 고정 패턴을 쓴다(없으면 1회 생성해 캐시).</summary>
        public void Begin(Hackable target)
        {
            _rng ??= new System.Random(System.Environment.TickCount);
            if (target.pattern == null)
                target.pattern = PatternGenerator.Generate(target.PatternLineCount, _rng);

            Target = target.pattern;
            Input.sensitivity = sensitivity;   // 인스펙터 튜닝값 주입
            Input.hitRadius = hitRadius;
            Input.Reset();
            _onTrack = true;
            State = PatternState.InProgress;
            ResolveView();
            ViewShow();
        }

        // ── 뷰 호출 한 곳으로 모음 ────────────────────────────────────
        // 패널이 있으면 패널만, 없으면 구형 UI. 둘을 동시에 그리는 경우는 없다.

        void ViewShow()
        {
            if (panel != null) panel.Show(Target, Input, NextTargetDot());
            else if (ui != null) ui.Show(Target, Input, NextTargetDot());
        }

        void ViewRefresh()
        {
            if (panel != null) panel.Refresh(Input, NextTargetDot());
            else if (ui != null) ui.Refresh(Input, NextTargetDot());
        }

        void ViewHide()
        {
            if (panel != null) panel.Hide();
            else if (ui != null) ui.Hide();
        }

        bool HasView { get { return panel != null || ui != null; } }

        /// <summary>취소 — 진행 초기화 + UI 숨김. Space 재탭 등으로 호출.</summary>
        public PatternState Cancel()
        {
            if (State != PatternState.InProgress) return State;
            State = PatternState.Cancelled;
            ViewHide();
            return State;
        }

        /// <summary>매 프레임. mouseDelta=마우스 이동량(px). 상태 반환.</summary>
        public PatternState Tick(Vector2 mouseDelta)
        {
            if (State != PatternState.InProgress) return State;

            int hit = Input.Move(mouseDelta);
            if (hit >= 0)
            {
                Input.Advance(hit);   // 오답이어도 진행(획 거부 없음)
                PlayTick();

                int i = Input.StrokeCount;
                bool matchesHere = i < Target.dots.Length && hit == Target.dots[i];
                if (!matchesHere) _onTrack = false;   // 정답 경로 이탈(완주해도 성공 불가)

                if (_onTrack && i >= Target.LineCount)
                {
                    State = PatternState.Succeeded;
                    if (HasView) StartCoroutine(HideAfter(successHideDelay));
                }
            }

            if (State == PatternState.InProgress) ViewRefresh();
            return State;
        }

        IEnumerator HideAfter(float t)
        {
            yield return new WaitForSeconds(t);
            ViewHide();
        }
    }
}
