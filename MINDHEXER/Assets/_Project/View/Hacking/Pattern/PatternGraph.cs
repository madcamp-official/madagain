using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 점 패턴의 K4 그래프 (2×2 점 4개, 6변, 대각 포함). 시작점 = 좌상단(0) 고정. (기초_설계안 §2.4)
    /// 점 인덱스: 0=TL, 1=TR, 2=BL, 3=BR.
    /// </summary>
    public static class PatternGraph
    {
        public const int DotCount = 4;
        public const int EdgeCount = 6;
        public const int StartDot = 0;   // 좌상단 고정

        /// <summary>정규화 좌표 (x,y 0..1, y↑). TL=(0,1) TR=(1,1) BL=(0,0) BR=(1,0).</summary>
        public static readonly Vector2[] Pos =
        {
            new Vector2(0f, 1f), // 0 TL
            new Vector2(1f, 1f), // 1 TR
            new Vector2(0f, 0f), // 2 BL
            new Vector2(1f, 0f), // 3 BR
        };

        /// <summary>변 6개 (두 점 인덱스). 상·좌·우·하·대각2.</summary>
        public static readonly Vector2Int[] Edges =
        {
            new Vector2Int(0, 1), // 상
            new Vector2Int(0, 2), // 좌
            new Vector2Int(1, 3), // 우
            new Vector2Int(2, 3), // 하
            new Vector2Int(0, 3), // 대각 TL-BR
            new Vector2Int(1, 2), // 대각 TR-BL
        };

        /// <summary>두 점 사이 변 인덱스 (K4라 서로 다른 점이면 항상 존재).</summary>
        public static int EdgeBetween(int a, int b)
        {
            for (int e = 0; e < EdgeCount; e++)
            {
                Vector2Int ed = Edges[e];
                if ((ed.x == a && ed.y == b) || (ed.x == b && ed.y == a)) return e;
            }
            return -1;
        }

        /// <summary>
        /// 현재 점에서 입력 방향(dir)에 가장 잘 맞는 이웃 점. dir이 너무 작거나 정렬이 약하면 -1.
        /// </summary>
        public static int DirectionToNeighbor(int dot, Vector2 dir, float minMag = 0.01f, float minAlign = 0.3f)
        {
            if (dir.sqrMagnitude < minMag * minMag) return -1;
            Vector2 d = dir.normalized;
            int best = -1;
            float bestDot = -2f;
            for (int nb = 0; nb < DotCount; nb++)
            {
                if (nb == dot) continue;
                Vector2 toNb = (Pos[nb] - Pos[dot]).normalized;
                float dp = Vector2.Dot(d, toNb);
                if (dp > bestDot) { bestDot = dp; best = nb; }
            }
            return bestDot >= minAlign ? best : -1;
        }
    }
}
