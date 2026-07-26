namespace Game.View
{
    /// <summary>
    /// 해킹/조종/빙의 입력 스냅샷의 <b>출처 추상화</b>. 게임플레이는 이 인터페이스만 읽어
    /// 입력이 PC(키보드/마우스)에서 오는지 VR(네트워크 컨트롤러)에서 오는지 <b>몰라도 된다</b>.
    ///
    /// <para>"PC→VR 딸깍" 하네스의 핵심 이음새 — <see cref="HexInputReader"/> 주석의
    /// "VR 이식 시 이 클래스만 UDP 수신기로 교체하면 된다"를 인터페이스로 형식화한 것.</para>
    /// </summary>
    public interface IHexInputSource
    {
        /// <summary>주어진 컨텍스트 기준 이번 프레임 입력을 낸다.</summary>
        HexInput Poll(ControlContext context);
    }
}
