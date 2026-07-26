using UnityEngine;
using Game.Sim;

namespace Game.Prediction
{
    /// <summary>
    /// 탐색 파라미터. `Mini`는 docs/shared/PREDICTION_INTEGRATION_PLAN.md 15장 G3
    /// 미니 탐색 기준(행동 4개 × 깊이 3 × Beam 4), `Full`은 12장 본탐색 기준
    /// (5초/300틱, 매크로 15틱→깊이 20, Beam 12)이다.
    /// </summary>
    public struct PredictionSettings
    {
        /// <summary>예측 게이지 메커닉이 지원하는 최소·최대 예측 시간(초). `ForDuration`이 이 범위로 자른다.</summary>
        public const float MinDurationSeconds = 1f;
        public const float MaxDurationSeconds = 5f;

        /// <summary>매크로 1스텝 = 15틱(0.25초, 60Hz). Full·Mini·ForDuration 공통 — 서브틱 콤보
        /// (LungeStrike/JumpStrike)의 좌클릭 타이밍이 이 길이를 기준으로 맞춰져 있으니 함께 본다.</summary>
        public const int MacroTicksPerStep = 15;

        /// <summary>매크로 행동 1개가 유지되는 틱 수.</summary>
        public int macroTicks;

        /// <summary>매크로 스텝(깊이) 수.</summary>
        public int macroDepth;

        /// <summary>각 깊이에서 유지할 상위 후보 수.</summary>
        public int beamWidth;

        /// <summary>노드 1개당 생성할 최대 행동 후보 수.</summary>
        public int maxActionsPerNode;

        /// <summary>이 설정의 실제 예측 시간(초). macroTicks×macroDepth 틱을 60Hz 기준으로 환산.</summary>
        public float DurationSeconds => (float)macroTicks * macroDepth / SimConfig.TickRate;

        public static PredictionSettings Mini => new PredictionSettings
        {
            macroTicks = 15,
            macroDepth = 3,
            beamWidth = 4,
            maxActionsPerNode = 4,
        };

        public static PredictionSettings Full => new PredictionSettings
        {
            macroTicks = MacroTicksPerStep,
            macroDepth = 20,
            beamWidth = 12,
            // 이동4 + Jump + 대시4 + Attack + 런지 최대 2 + Wait = 13. 여기에 공중 마무리 콤보
            // (LungeStrike는 런지 슬롯을 재사용하지만, JumpStrike는 얼어붙은 공중 슈터 상황에서만
            // 추가로 1개 생성)까지 잘려나가지 않도록 +1.
            maxActionsPerNode = 16,
        };

        /// <summary>
        /// 예측 게이지 메커닉용: 원하는 예측 시간(초, 1~5초로 잘림)에 맞춰 macroDepth만 조절한다.
        /// Beam 폭·행동 후보 수는 `Full`과 동일하게 유지한다 — 시간에 따른 품질 축소는 별도
        /// 관심사(OPTIMIZATION.md 12장 "시간 예산과 동적 축소")라 여기서 같이 하지 않는다.
        /// macroTicks(매크로 1스텝당 틱 수)는 Full/Mini와 같은 15틱(0.25초) 단위를 그대로 써서
        /// 매크로 스텝 경계가 어긋나지 않게 한다.
        /// </summary>
        public static PredictionSettings ForDuration(float seconds)
        {
            float clamped = Mathf.Clamp(seconds, MinDurationSeconds, MaxDurationSeconds);
            int totalTicks = Mathf.RoundToInt(clamped * SimConfig.TickRate);
            int depth = Mathf.Max(1, Mathf.RoundToInt((float)totalTicks / MacroTicksPerStep));

            return new PredictionSettings
            {
                macroTicks = MacroTicksPerStep,
                macroDepth = depth,
                beamWidth = 12,
                maxActionsPerNode = 16,   // Full과 동일(공중 추격/지형 도약 후보 여유 포함)
            };
        }

        /// <summary>
        /// docs/shared/OPTIMIZATION.md 12장 "시간 예산과 동적 축소"를 구현하되, 실제 걸린
        /// 시간(스톱워치)이 아니라 <paramref name="enemyCount"/>로만 판단한다 — 벽시계 시간을
        /// 기준으로 삼으면 같은 스냅샷이라도 컴퓨터 성능·부하에 따라 다른 설정(=다른 결과)이
        /// 나와서 이 프로젝트 전체가 지켜온 결정론이 깨진다. `docs/shared/ENEMY_SIMULATION_TIER_PROPOSAL.md`가
        /// 실측한 대로 적 수가 비용을 사실상 그대로 예측하므로, 대신 적 수 구간별로 문서의
        /// 축소 순서(Beam 12→8→6, 예측 길이 축소, 런지 후보 2→1)를 결정론적으로 적용한다.
        /// EnemySimulationTier가 없는 지금은 임시 완화책이며, 32마리 이상에서는 하드리밋(300ms)을
        /// 완전히 보장하지 못한다(ENEMY_SIMULATION_TIER_PROPOSAL.md 참고) — 그래도 아무것도
        /// 안 하는 것보다는 낫다.
        /// </summary>
        public static PredictionSettings Degrade(PredictionSettings baseline, int enemyCount)
        {
            PredictionSettings s = baseline;

            if (enemyCount > 16)
                s.beamWidth = Mathf.Min(s.beamWidth, 8);
            if (enemyCount >= 32)
            {
                s.beamWidth = Mathf.Min(s.beamWidth, 6);
                s.macroDepth = Mathf.Max(1, Mathf.RoundToInt(s.macroDepth * (2f / 3f)));
                // maxActionsPerNode는 일부러 안 건드린다: ActionGenerator.Priority 순서상
                // 이동4개+Jump+대시4개가 먼저 캡을 채우므로, 대시 충전이 남아있는 한
                // 낮은 cap에서는 Attack(10번째)·Lunge·Wait가 후보 생성 단계에서 사라진다.
                // 성능보다 "공격이 후보에서 사라짐"이 훨씬 심각한 문제라 여기선
                // beamWidth·macroDepth 축소만으로 절감한다.
            }
            if (enemyCount >= 50)
                s.macroDepth = Mathf.Max(1, Mathf.RoundToInt(baseline.macroDepth * 0.5f));

            return s;
        }
    }
}
