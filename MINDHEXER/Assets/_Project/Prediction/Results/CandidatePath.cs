using Game.Sim;

namespace Game.Prediction
{
    /// <summary>
    /// Beam Search 결과 1개. docs/shared/PREDICTION_CONTRACT.md 11장 "후보 결과 계약"의
    /// 필드를 담는다. `controls`·`predictedFrames`·`actionEvents`는 Beam 확장 중에는 비워둔다
    /// (계약: "Beam 확장 중에는 전체 프레임 궤적을 저장하지 않는다") — 최종 후보로 뽑힌
    /// 다음 `CandidateReplayer.Replay`가 최초 스냅샷에서 60Hz로 다시 실행해서 채운다.
    /// </summary>
    public sealed class CandidatePath
    {
        public int candidateId;
        public MacroAction[] actions;

        /// <summary>매 틱 실제로 넣은 입력. CandidateReplayer.Replay 이후에만 채워진다.</summary>
        public InputCmd[] controls;

        /// <summary>최초 스냅샷에서 60Hz로 다시 실행해 얻은 매 틱 프레임. 리듬 판정·경로선·잔상은
        /// 여기서만 샘플링한다(계약 3.1.1절). CandidateReplayer.Replay 이후에만 채워진다.</summary>
        public PredictedFrame[] predictedFrames;

        /// <summary>실제 Sim 상태머신에서 행동이 시작된 정확한 틱들. CandidateReplayer.Replay
        /// 이후에만 채워진다.</summary>
        public PredictedActionEvent[] actionEvents;

        /// <summary>최종 정밀 재생에서 처음 확정된 적 처치 이벤트. View 처치 마커가 소비한다.</summary>
        public PredictedDefeatEvent[] defeatEvents;

        public int killCount;
        public int damageDealt;
        public int dashCount;
        public int lungeCount;
        public int expectedHits;
        public int durationTicks;

        public float safetyScore;
        public float killScore;
        public float difficultyScore;
        public float rawExecutionDifficulty;
        public float rawSalientTargetProgress;
        public float rawTerminalPositionQuality;
        public float TotalScore => safetyScore + killScore + difficultyScore;

        public ulong initialSnapshotHash;
        public int mapVersion;

        /// <summary>탐색이 생존 경로를 하나도 찾지 못했을 때(전부 사망)의 폴백 여부. 계약엔 없는 내부용.</summary>
        public bool isDeadFallback;

        /// <summary>이 후보를 만든 평가 프로필 이름(안전형/기회형/공격형, 또는 레거시 절충형).
        /// BeamSearch.Run은 항상 어떤 프로필로든 돌기 때문에 이 필드는 항상 채워진다 — 기존
        /// 3-인자 Run/Plan() 경로는 내부적으로 ScoreProfile.Balanced("절충형")로 위임하므로
        /// 그 값이 들어간다(PlanByProfile은 더 이상 절충형을 쓰지 않는다).</summary>
        public string profileLabel;
    }
}
