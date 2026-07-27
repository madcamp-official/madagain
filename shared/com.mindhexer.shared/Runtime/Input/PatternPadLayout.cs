namespace MindHexer.Shared.Input
{
    /// <summary>
    /// 플로팅 2x2 패턴 패드의 크기 계산(Unity 비의존).
    /// 첫 터치(=좌상단 노드0)에서 격자가 **오른쪽(+x)/아래(-y, 화면 y-up)** 로 펼쳐지므로,
    /// 시작점이 오른쪽·아래 프레임에 가까울수록 간격을 줄여 화면 밖으로 넘어가지 않게 한다.
    /// 상단 프레임에 붙고 가로로 여유가 있는 지점에서 시작하면 최대(=maxSpacing)가 된다.
    /// </summary>
    public static class PatternPadLayout
    {
        /// <summary>
        /// 화면에 들어가는 최대 노드 간격(px)을 구한다.
        /// </summary>
        /// <param name="pressX">시작 터치 x(px, 좌=0).</param>
        /// <param name="pressY">시작 터치 y(px, 하=0 · 화면 y-up).</param>
        /// <param name="screenW">화면 폭(px).</param>
        /// <param name="maxSpacing">간격 상한(px).</param>
        /// <param name="edgeMargin">프레임에서 남길 여백(px).</param>
        /// <param name="floorPx">퇴화 방지 최소 간격(px). 극단적 모서리 시작 시에만 걸린다.</param>
        public static float FitSpacing(float pressX, float pressY, float screenW,
                                       float maxSpacing, float edgeMargin, float floorPx = 8f)
        {
            float downRoom = pressY - edgeMargin;               // 아래 프레임(y=0)까지
            float rightRoom = screenW - pressX - edgeMargin;    // 오른쪽 프레임까지
            float room = downRoom < rightRoom ? downRoom : rightRoom;

            float s = room < maxSpacing ? room : maxSpacing;    // 방보다 크지 않게(= 화면 밖 금지)
            if (s < floorPx) s = floorPx;                       // 퇴화 방지(무시할 만한 오버플로)
            return s;
        }
    }
}
