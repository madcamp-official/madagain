namespace MindHexer.Shared.Input
{
    /// <summary>2x2 패드 배치 결과: 노드 간격 + (필요 시 화면 안으로 보정된) 좌상단 앵커.</summary>
    public struct PadFit
    {
        public float Spacing;  // 노드 간 간격(px). 2x2 박스 한 변.
        public float AnchorX;  // 좌상단 노드(0) x(px)
        public float AnchorY;  // 좌상단 노드(0) y(px, 하=0)
    }

    /// <summary>
    /// 플로팅 2x2 패턴 패드의 크기/앵커 계산(Unity 비의존).
    /// 첫 터치(=좌상단 노드0)에서 격자가 **오른쪽(+x)/아래(-y, 화면 y-up)** 로 펼쳐진다.
    ///  - 여유가 충분하면 간격은 <c>maxSpacing</c> 까지(그 이상 커지지 않아 프레임에 닿지 않음).
    ///  - 시작점이 오른쪽·아래 프레임에 가까우면 간격을 줄여 화면 밖으로 안 나가게 한다.
    ///  - 단, <c>minSpacing</c> 아래로는 줄지 않는다(최소 크기 보장). 이때 최소 크기 박스가 화면에
    ///    들어가도록 앵커를 안쪽으로 밀어 **여전히 프레임 밖으로 나가지 않게** 한다.
    /// </summary>
    public static class PatternPadLayout
    {
        /// <summary>
        /// 최소 크기 보장 + 화면 이탈 방지까지 포함한 배치. (권장)
        /// </summary>
        public static PadFit Fit(float pressX, float pressY, float screenW, float screenH,
                                 float maxSpacing, float minSpacing, float edgeMargin)
        {
            if (minSpacing > maxSpacing) minSpacing = maxSpacing;

            float rightRoom = screenW - pressX - edgeMargin; // 오른쪽 프레임까지
            float downRoom = pressY - edgeMargin;            // 아래 프레임(y=0)까지
            float room = rightRoom < downRoom ? rightRoom : downRoom;

            float spacing = room < maxSpacing ? room : maxSpacing;

            float ax = pressX, ay = pressY;
            if (spacing < minSpacing)
            {
                // 최소 크기로 고정하고, 그 박스가 화면 안에 들어오도록 좌상단 앵커를 이동.
                spacing = minSpacing;
                // node1.x = ax + spacing ≤ screenW - edgeMargin  → ax ≤ screenW - edgeMargin - spacing
                ax = Clamp(pressX, edgeMargin, screenW - edgeMargin - spacing);
                // node2.y = ay - spacing ≥ edgeMargin, node0.y = ay ≤ screenH - edgeMargin
                ay = Clamp(pressY, edgeMargin + spacing, screenH - edgeMargin);
            }

            return new PadFit { Spacing = spacing, AnchorX = ax, AnchorY = ay };
        }

        /// <summary>
        /// 간격만 필요할 때(앵커=시작점 고정, 최소 크기 없이 화면에 맞춤). 하위호환용.
        /// </summary>
        public static float FitSpacing(float pressX, float pressY, float screenW,
                                       float maxSpacing, float edgeMargin, float floorPx = 8f)
        {
            float downRoom = pressY - edgeMargin;
            float rightRoom = screenW - pressX - edgeMargin;
            float room = downRoom < rightRoom ? downRoom : rightRoom;
            float s = room < maxSpacing ? room : maxSpacing;
            if (s < floorPx) s = floorPx;
            return s;
        }

        private static float Clamp(float v, float lo, float hi)
        {
            if (hi < lo) return lo;            // 화면이 극단적으로 작을 때의 방어
            return v < lo ? lo : (v > hi ? hi : v);
        }
    }
}
