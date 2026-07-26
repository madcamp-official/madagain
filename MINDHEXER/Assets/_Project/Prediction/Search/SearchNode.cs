namespace Game.Prediction
{
    /// <summary>
    /// Beam Search 트리의 노드 1개. 행동 시퀀스는 매번 배열로 복사하지 않고
    /// parentIndex를 따라가며 재구성한다(고정 배열 인덱스 체인).
    /// </summary>
    public struct SearchNode
    {
        public int worldBufferIndex;
        public int parentIndex;   // 루트는 -1
        public MacroAction actionTaken;
        public float score;       // safetyScore+killScore+difficultyScore 합(Beam 정렬용)
        public int depth;

        public int killCountNormal;
        public int killCountMid;
        public int damageDealt;
        public int hitsTaken;     // 계약의 expectedHits
        public int dashCount;
        public int lungeCount;
        public int waitCount;
        public int consecutiveWaitCount;
        public int ticksSurvived;
        public float executionDifficulty;
        public float salientTargetProgress;
        public float terminalPositionQuality;

        public float safetyScore;
        public float killScore;
        public float difficultyScore;
        public float profile1Score;
        public float profile1SafetyScore;
        public float profile1KillScore;
        public float profile1DifficultyScore;
        public float profile2Score;
        public float profile2SafetyScore;
        public float profile2KillScore;
        public float profile2DifficultyScore;

        public bool alive;
        public ulong stateKey;
    }
}
