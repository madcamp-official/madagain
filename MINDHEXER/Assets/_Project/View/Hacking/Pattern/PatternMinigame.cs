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

        [Tooltip("패턴 화면 UI. 비우면 로직만(로그).")]
        public PatternUI ui;

        public PatternState State { get; private set; } = PatternState.Idle;
        public DotPattern Target { get; private set; }
        public PatternInput Input { get; private set; } = new PatternInput();

        System.Random _rng;
        bool _onTrack;

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
            if (ui != null) ui.Show(Target, Input, NextTargetDot());
        }

        /// <summary>취소 — 진행 초기화 + UI 숨김. Space 재탭 등으로 호출.</summary>
        public PatternState Cancel()
        {
            if (State != PatternState.InProgress) return State;
            State = PatternState.Cancelled;
            if (ui != null) ui.Hide();
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

                int i = Input.StrokeCount;
                bool matchesHere = i < Target.dots.Length && hit == Target.dots[i];
                if (!matchesHere) _onTrack = false;   // 정답 경로 이탈(완주해도 성공 불가)

                if (_onTrack && i >= Target.LineCount)
                {
                    State = PatternState.Succeeded;
                    if (ui != null) StartCoroutine(HideAfter(successHideDelay));
                }
            }

            if (State == PatternState.InProgress && ui != null) ui.Refresh(Input, NextTargetDot());
            return State;
        }

        IEnumerator HideAfter(float t)
        {
            yield return new WaitForSeconds(t);
            if (ui != null) ui.Hide();
        }
    }
}
