using Game.Sim;

namespace Game.Prediction
{
    /// <summary>예측 시스템의 유일한 진입점. 내부 구성 요소(BeamSearch 등)를 엮는다.</summary>
    public static class PredictionPlanner
    {
        /// <summary>점수 내림차순 후보 1~3개(계약 10장 "반환 후보 최대 3"). [0]이 최상위 후보.</summary>
        public static CandidatePath[] Plan(in SimWorld snapshot, in SimServices services, in PredictionSettings settings)
            => BeamSearch.Run(in snapshot, in services, in settings);

        /// <summary>
        /// 하나의 점수 기준으로 top-3를 뽑는 대신, 서로 다른 목적(안전형/기회형/공격형)을
        /// 같은 월드 확장 결과에서 함께 채점해 각각의 최상위 후보 하나씩을 반환한다. 항상 3개
        /// (안전형/기회형/공격형 순서)를 반환한다 — BeamSearch.Run은 확장 후보가 0이어도
        /// 최소 루트 스냅샷 후보 하나는 반환하도록 보장돼 있다. 가운데 슬롯은 원래 절충형
        /// (ScoreProfile.Balanced)이었으나, "다음 공격 준비" 목적의 기회형(ScoreProfile.
        /// Opportunistic)으로 교체했다 — Balanced 자체는 3-인자 Run/Plan()의 하위 호환 기준으로
        /// 남아있으니 값을 건드리지 않는다.
        ///
        /// 각 depth의 Beam 슬롯은 세 프로필에 라운드로빈으로 배분한다. 월드 복사와 SimStep은
        /// 한 번만 수행하고 프로필별 점수 계산만 추가하므로 기존 3회 독립 탐색보다 저렴하다.
        /// </summary>
        public static CandidatePath[] PlanByProfile(in SimWorld snapshot, in SimServices services, in PredictionSettings settings)
        {
            return BeamSearch.RunSharedProfiles(
                in snapshot, in services, in settings,
                ScoreProfile.Safe, ScoreProfile.Opportunistic, ScoreProfile.Aggressive);
        }
    }
}
