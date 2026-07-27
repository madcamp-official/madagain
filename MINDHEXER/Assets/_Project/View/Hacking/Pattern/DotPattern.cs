namespace Game.View
{
    /// <summary>
    /// 생성된 점 패턴 = 방문 점 시퀀스. dots[0]=시작(좌상단 0), 길이 = lineCount+1.
    /// 시작 고정이라 점 시퀀스가 곧 변(방향) 시퀀스와 동치 → 판정도 인덱스 순 비교로 단순(§2.4).
    /// </summary>
    public class DotPattern
    {
        public readonly int[] dots;

        public DotPattern(int[] dots) { this.dots = dots; }

        /// <summary>획(선) 개수 = 점 수 - 1.</summary>
        public int LineCount => dots.Length - 1;
    }
}
