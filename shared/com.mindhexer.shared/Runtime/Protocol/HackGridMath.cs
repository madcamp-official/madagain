using UnityEngine;

namespace MindHexer.Shared.Protocol
{
    /// <summary>
    /// 3x3 해킹 그리드 좌표 매핑(공유). S24+(판정)와 S10e(오버레이/하이라이트)가 동일 매핑을 쓰도록 shared에 둔다.
    /// 정규화 좌표(0..1) ↔ 셀 인덱스(0..8, row*3+col, row/col은 0이 좌하단).
    /// UnityEngine.Mathf에 의존하지 않도록 정수 클램프를 자체 구현(하니스 검증 편의).
    /// </summary>
    public static class HackGridMath
    {
        public const int Size = 3;
        public const int CellCount = Size * Size;

        public static int ToCellIndex(Vector2 normalized)
        {
            int col = Clamp((int)(normalized.x * Size), 0, Size - 1);
            int row = Clamp((int)(normalized.y * Size), 0, Size - 1);
            return row * Size + col;
        }

        /// <summary>셀 인덱스의 중심 정규화 좌표.</summary>
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
