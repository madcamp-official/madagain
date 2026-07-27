using System.Collections.Generic;

namespace Game.View
{
    /// <summary>
    /// 대상 인스턴스의 고정 점 패턴을 1회 생성한다(§2.4). 좌상단(0)에서 시작하는 제약 랜덤 워크 —
    /// 같은 변은 최대 3회까지 재사용 가능(대각선 포함 6변뿐이라 재사용 없인 긴 패턴을 못 만듦).
    /// </summary>
    public static class PatternGenerator
    {
        const int MaxEdgeReuse = 3;

        public static DotPattern Generate(int lineCount, System.Random rng)
        {
            var dots = new List<int> { PatternGraph.StartDot };
            var edgeUse = new int[PatternGraph.EdgeCount];
            int current = PatternGraph.StartDot;

            for (int i = 0; i < lineCount; i++)
            {
                var candidates = new List<int>();
                for (int nb = 0; nb < PatternGraph.DotCount; nb++)
                {
                    if (nb == current) continue;
                    int e = PatternGraph.EdgeBetween(current, nb);
                    if (edgeUse[e] < MaxEdgeReuse) candidates.Add(nb);
                }

                // 이론상 K4 + 재사용 3회면 항상 후보가 있다(막다른 길 없음).
                int next = candidates[rng.Next(candidates.Count)];
                edgeUse[PatternGraph.EdgeBetween(current, next)]++;
                dots.Add(next);
                current = next;
            }

            return new DotPattern(dots.ToArray());
        }
    }
}
