namespace Game.View
{
    /// <summary>
    /// PC 입력 소스 — 기존 <see cref="HexInputReader"/>(키보드/마우스)를 <see cref="IHexInputSource"/>에
    /// 맞춘 얇은 어댑터. PC/에디터 경로의 기본 소스다.
    /// </summary>
    public sealed class PcHexInputSource : IHexInputSource
    {
        public readonly HexInputReader Reader = new HexInputReader();

        public HexInput Poll(ControlContext context)
        {
            Reader.Context = context;
            return Reader.Poll();
        }
    }
}
