namespace Game.Prediction
{
    /// <summary>
    /// 후보 평가 가중치. docs/shared/PREDICTION_CONTRACT.md 12장 값을 그대로 넣어봤다가
    /// 실제 재검증에서 회귀(적 3·6마리처럼 접근이 필요한 상황에서 아예 안 다가감)가
    /// 나서, 이전에 검증됐던 값으로 되돌렸다 — ThreatEvaluator.cs 주석 참고.
    /// </summary>
    public static class PredictionScoreConfig
    {
        public const float HpWeight = 10f;
        /// <summary>
        /// 개발용 무한 체력이 위치·피격 평가를 압도하지 않도록 예측 점수에서만 원래 체력 칸으로 포화한다.
        /// </summary>
        public const int ScoredPlayerHpCap = 3;
        public const float KillWeight = 30f;
        public const float DamageWeight = 8f;
        public const float SafeDistanceWeight = 1f;
        public const float SafeDistanceCap = 6f;
        public const float SurroundedRadius = 5f;
        public const int SurroundedTolerance = 4;
        public const float SurroundedWeight = 2f;

        // 안전형 종료 위치 관측. 마지막 깊이에서만 적용되므로 중간 자세를 반복 보상하지 않는다.
        public const float TerminalNearestDistanceCap = 8f;
        public const float TerminalNearbyPenalty = 7f;
        public const float TerminalHeightWeight = 3f;
        public const float TerminalOpenSectorRadius = 7f;
        public const float TerminalOpenSectorWeight = 1.5f;
        public const float TerminalReadyToActBonus = 8f;
        public const float TerminalGroundedBonus = 3f;
        public const float TerminalDashReserveBonus = 2f;
        public const float TerminalImminentProjectilePenalty = 12f;
        public const float TerminalImminentChargePenalty = 16f;

        /// <summary>원거리 솔저 투사체가 명중 궤도일 때의 감점(임박할수록 커짐). 회귀 이력 때문에
        /// 기존 가중치는 안 건드리고 새 항목으로만 추가한다 — ThreatEvaluator.cs 참고.</summary>
        public const float ProjectileImpactWeight = 3f;
        public const int ProjectileImpactHorizonTicks = 45;
        public const float ChargeImpactWeight = 4f;
        public const int ChargeImpactHorizonTicks = 60;
        /// <summary>공중 적 감점. 예전엔 "반경 내 아무 공중 적"에 붙여 접근 자체를 막아(=타겟팅 불가)
        /// 있었으나, 이제 실제로 조준/발사 중(aimingFlyingEnemyCount)인 적에만 적용한다 —
        /// 단순 부유는 회피 대상이 아니라 처치 대상.</summary>
        public const float FlyingThreatWeight = 2f;
        public const float FlyingThreatRadius = 10f;
        /// <summary>런지 사거리 안에 든 공중 적을 "마무리 기회"로 보는 가점(형태 유도).
        /// 실제 피해(DamageWeight)보다 작게 둬서 "자세만 잡고 안 침"으로 정체되지 않게 한다 —
        /// 접근/점프 중간 스텝(아직 피해 0)이 Beam에서 살아남아 실제 처치까지 이어지도록만 돕는다.</summary>
        public const float AerialOpportunityWeight = 4f;

        // ── [대공 등반, 2026-07-22] 사거리 밖 공중 적을 향한 고도 확보 유도 ──
        // 기존 가중치는 건드리지 않고 격리된 항만 추가한다(회귀 이력 존중 — 이 파일의 다른 항들과 같은 원칙).
        /// <summary>등반 진행도(0~1)에 곱하는 가점. 실제 처치·피해보다 반드시 작아야 한다 —
        /// 이건 "닿는 위치까지 가는 중간 스텝이 Beam에서 살아남게" 하는 형태 유도일 뿐이다.</summary>
        public const float AerialAscentProgressWeight = 6f;
        /// <summary>이만큼 더 올라가야 하면 진행도 0으로 본다(높이차 − LungeHeightTolerance 기준).</summary>
        public const float AerialAscentReferenceGap = 8f;
        /// <summary>이 수평 거리 밖의 닿지 않는 공중 적은 등반 유도 대상으로 보지 않는다.</summary>
        public const float AerialAscentRadius = 25f;
        public const float TraversalCommitmentWeight = 0.5f;

        /// <summary>사망 후보도 서로 순위를 매길 수 있도록 유한값 유지(무한대면 전멸 폴백 시
        /// "가장 덜 나쁜" 후보를 고를 수 없다) — 이건 계약 반영과 무관하게 유지하는 개선.</summary>
        public const float PlayerDeath = -10000f;

        // ── 기회형(Opportunistic) 전용 — OpportunityObservation을 가중치로 변환.
        // 우선순위(다음 처치 가능성 > 유리한 위치 > 자원 보존 > 현재 피해)를 그대로 값 크기에
        // 반영했다 — 전부 첫 튜닝값, 실사용하며 조정 대상이다. ──
        public const float OpportunityLungeableWeight = 25f;          // 런지 가능해 보이는 적 1명당
        public const float OpportunityExecutionReadyWeight = 40f;     // Large 처형 임박 1명당(최우선 신호)
        public const float OpportunityConeEnemyWeight = 8f;           // 부채꼴 안 적 1명당(다수면 누적)
        public const float OpportunityFlankWeight = 15f;              // 측면·후방 잡은 원거리 적 1명당
        public const float OpportunityFlankDotThreshold = 0.5f;       // 적 정면 기준 이 dot 미만이면 "측후방"(전방 약 120도 제외)
        public const float OpportunityHeightAdvantageBonus = 10f;
        public const float OpportunityHeightAdvantageMin = 1f;        // 이만큼 더 높아야 "고지대"로 인정
        public const float OpportunityHeightCheckRadius = 10f;        // 이 반경 안의 적 기준으로만 고지대 판단
        public const float OpportunityResourcePreserveWeight = 6f;    // 대시·런지 자원 보존 1종당(과거 회귀 이력 때문에 낮게 유지)
        public const float OpportunityReadyToActBonus = 10f;
        public const float OpportunitySurroundedRadius = 5f;          // SurroundedRadius와 같은 값(별도 상수로 둔 건 독립 튜닝 여지용)
        public const float OpportunitySurroundedCenterPenalty = 20f;
        public const float OpportunityAllResourcesSpentPenalty = 15f;
        public const float OpportunityLockedInDangerPenalty = 25f;
    }
}
