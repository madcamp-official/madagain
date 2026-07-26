namespace Game.View
{
    /// <summary>
    /// 해킹 성공 후 결과 종류. 하이라이트 색·거미 연출·입력 컨텍스트 분기의 기준.
    /// (기초_설계안 §6 대상 분류 / §7 색 언어)
    /// </summary>
    public enum ControlType
    {
        /// <summary>외부 조종 (연두). 내 시점 유지, 밖에서 3축 조종.</summary>
        ExternalControl,
        /// <summary>시점 진입 (청록). 대상 시점 전환·릴레이.</summary>
        ViewEntry,
        /// <summary>스턴 (보스 전용). 시점 전환·조종 없음.</summary>
        Stun,
    }
}
