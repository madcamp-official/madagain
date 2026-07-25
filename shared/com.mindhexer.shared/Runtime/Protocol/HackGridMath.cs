using UnityEngine;

namespace MindHexer.Shared.Protocol
{
    /// <summary>
    /// 3x3 해킹 그리드 좌표 매핑(공유). S24+(판정)와 S10e(오버레이/하이라이트)가 동일 매핑을 쓰도록 shared에 둔다.
    ///
    /// 레이아웃(가로 화면): 왼쪽=조이스틱, **오른쪽=패턴 패드**. 패턴 입력은 화면 전체가 아니라
    /// 오른쪽의 축소된 패드에서만 인식하며, 안드로이드 잠금패턴처럼 스와이프로 조작한다(<see cref="MindHexer.Shared.Input.SwipePattern"/>).
    /// 패드 영역(<see cref="PadX"/>..)은 정규화 스크린 좌표(원점 좌하단)의 고정 상수라
    /// 컨트롤러(그리기)와 헤드셋(판정)이 같은 값을 쓴다.
    ///
    /// UnityEngine.Mathf/Rect에 의존하지 않도록 float/Vector2만 사용(콘솔 하니스·pc-receiver 호환).
    /// </summary>
    public static class HackGridMath
    {
        public const int Size = 3;
        public const int CellCount = Size * Size;

        // ---- 패턴 패드 영역(정규화 스크린, 원점 좌하단). 가로 화면 오른쪽, 세로 중앙(오른손 엄지). ----
        // 가로(약 19.5:9)에서 셀이 대략 정사각형으로 보이도록 W:H 비율을 잡음.
        public static float PadX = 0.67f;
        public static float PadY = 0.19f;
        public static float PadW = 0.30f;
        public static float PadH = 0.62f;

        /// <summary>패드 로컬 uv(0..1)를 3x3 셀 인덱스(0..8, row*3+col, row/col은 0이 좌하단)로 변환.</summary>
        public static int ToCellIndex(Vector2 localUv)
        {
            int col = Clamp((int)(localUv.x * Size), 0, Size - 1);
            int row = Clamp((int)(localUv.y * Size), 0, Size - 1);
            return row * Size + col;
        }

        /// <summary>
        /// 화면 정규화 좌표(0..1)가 하단 패드 영역 안이면 셀 인덱스를 채우고 true, 밖이면 false.
        /// 헤드셋은 수신한 NormalizedPos로, 컨트롤러는 터치 좌표로 이걸 호출해 동일 판정을 얻는다.
        /// </summary>
        public static bool TryToCellIndex(float screenNormX, float screenNormY, out int cell)
        {
            float lx = (screenNormX - PadX) / PadW;
            float ly = (screenNormY - PadY) / PadH;
            if (lx < 0f || lx > 1f || ly < 0f || ly > 1f) { cell = -1; return false; }
            cell = ToCellIndex(new Vector2(lx, ly));
            return true;
        }

        /// <summary>셀 인덱스의 패드-로컬 중심 정규화 좌표(0..1).</summary>
        public static Vector2 CellCenter(int index)
        {
            index = Clamp(index, 0, CellCount - 1);
            int row = index / Size;
            int col = index % Size;
            return new Vector2((col + 0.5f) / Size, (row + 0.5f) / Size);
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
