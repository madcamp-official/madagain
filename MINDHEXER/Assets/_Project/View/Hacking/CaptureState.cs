namespace Game.View
{
    /// <summary>
    /// 해킹 대상의 장악 상태 (이음새). 비주얼이 색으로 표현한다(§7): None/Hacking=초록계열, Captured=파랑.
    /// </summary>
    public enum CaptureState
    {
        None,      // 아직 안 먹음 (해킹 가능, 초록)
        Hacking,   // 점 패턴 그리는 중
        Captured,  // 장악됨 (파랑, §6.5 1회 장악)
    }
}
