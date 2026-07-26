using UnityEngine;

namespace Game.View
{
    public enum PatternState { Idle, InProgress, Succeeded, Failed, Cancelled }

    /// <summary>
    /// 점 패턴 미니게임 오케스트레이터. HackDriver가 Begin/Tick으로 구동한다.
    /// 판정(방향 시퀀스 일치)은 여기서 인라인 — 시작 고정이라 target.dots와 순서 비교로 단순(§2.4).
    /// UI는 PatternUI(있으면)가 그린다. 없으면 로직만(로그로 테스트 가능).
    /// </summary>
    public class PatternMinigame : MonoBehaviour
    {
        [Tooltip("오답 처리: true=틀리면 이 해킹 실패(재시도) / false=정답 방향만 스냅(틀린 커밋 무시). §2.4")]
        public bool failOnWrong = true;

        [Tooltip("커밋 임계(마우스 방향 누적 px).")]
        public float commitThreshold = 40f;

        [Tooltip("패턴 화면 UI. 비우면 로직만(로그).")]
        public PatternUI ui;

        [Tooltip("생성 시드 고정(0 이하=매번 랜덤).")]
        public int fixedSeed = 0;

        public PatternState State { get; private set; } = PatternState.Idle;
        public DotPattern Target { get; private set; }
        public PatternInput Input { get; private set; } = new PatternInput();

        System.Random _rng;

        /// <summary>해킹 시작 — 난이도(lineCount)로 패턴 생성.</summary>
        public void Begin(int lineCount)
        {
            _rng ??= new System.Random(fixedSeed > 0 ? fixedSeed : System.Environment.TickCount);
            Target = PatternGenerator.Generate(lineCount, _rng);
            Input.Reset();
            Input.CommitThreshold = commitThreshold;
            State = PatternState.InProgress;
            if (ui != null) ui.Show(Target, Input);
        }

        /// <summary>매 프레임. strokeDelta=마우스 방향, held=Space 유지. 상태 반환.</summary>
        public PatternState Tick(Vector2 strokeDelta, bool held)
        {
            if (State != PatternState.InProgress) return State;

            if (!held) { Finish(PatternState.Cancelled); return State; }

            int cand = Input.Detect(strokeDelta);
            if (cand >= 0)
            {
                int expected = Target.TargetAfter(Input.StrokeCount);
                if (cand == expected)
                {
                    Input.Advance(cand);
                    if (Input.StrokeCount >= Target.LineCount) Finish(PatternState.Succeeded);
                }
                else if (failOnWrong)
                {
                    Finish(PatternState.Failed);
                }
                // else 스냅 모드: 틀린 커밋은 무시(전진 안 함, 누적만 리셋됨)
            }

            if (State == PatternState.InProgress && ui != null) ui.Refresh(Input);
            return State;
        }

        void Finish(PatternState s)
        {
            State = s;
            if (ui != null) ui.Hide();
        }
    }
}
