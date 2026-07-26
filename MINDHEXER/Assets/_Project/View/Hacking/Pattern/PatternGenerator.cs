using System.Collections.Generic;

namespace Game.View
{
    /// <summary>
    /// K4 위 좌상단 시작 제약 랜덤워크로 점 패턴을 즉석 생성. 길이 = lineCount, 변별 사용 ≤3회.
    /// (기초_설계안 §2.4 — 오일러 경로 아님, 되돌아가기 허용.)
    /// </summary>
    public static class PatternGenerator
    {
        public static DotPattern Generate(int lineCount, System.Random rng)
        {
            int cur = PatternGraph.StartDot;
            int[] edgeUse = new int[PatternGraph.EdgeCount];
            var dots = new List<int>(lineCount + 1) { cur };
            var cand = new List<int>(3);

            for (int i = 0; i < lineCount; i++)
            {
                cand.Clear();
                for (int nb = 0; nb < PatternGraph.DotCount; nb++)
                {
                    if (nb == cur) continue;
                    int e = PatternGraph.EdgeBetween(cur, nb);
                    if (e >= 0 && edgeUse[e] < 3) cand.Add(nb);
                }

                // 용량(9) > 최대 N(7)이라 이론상 항상 후보 존재. 안전망만.
                int pick = cand.Count > 0
                    ? cand[rng.Next(cand.Count)]
                    : (cur + 1) % PatternGraph.DotCount;

                int edge = PatternGraph.EdgeBetween(cur, pick);
                if (edge >= 0) edgeUse[edge]++;
                dots.Add(pick);
                cur = pick;
            }

            return new DotPattern(dots.ToArray());
        }
    }
}
