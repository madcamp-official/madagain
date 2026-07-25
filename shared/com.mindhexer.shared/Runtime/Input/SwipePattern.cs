using System;
using System.Collections.Generic;

namespace MindHexer.Shared.Input
{
    /// <summary>
    /// 안드로이드 잠금패턴식 3x3 스와이프 패턴 빌더(Unity 비의존).
    /// 손가락이 지나가는 셀을 순서대로 잇되, 각 셀은 한 번만 사용한다. 두 셀 사이에 일직선상의
    /// 방문 안 한 중간 셀이 있으면(예: 0→2 사이의 1, 0→8 사이의 4) 자동으로 먼저 포함한다.
    ///
    /// 셀 인덱스는 <see cref="MindHexer.Shared.Protocol.HackGridMath"/>와 동일(0..8, row*3+col, 0이 좌하단).
    /// 순수 로직이라 콘솔/EditMode로 결정론 검증 가능.
    /// </summary>
    public sealed class SwipePattern
    {
        private const int N = 9;
        private readonly List<int> _path = new List<int>(N);
        private readonly bool[] _visited = new bool[N];

        /// <summary>지금까지 이어진 셀 순서.</summary>
        public IReadOnlyList<int> Path => _path;

        public int Count => _path.Count;

        public bool Contains(int cell) => cell >= 0 && cell < N && _visited[cell];

        /// <summary>새 스와이프 시작(경로 초기화).</summary>
        public void Begin()
        {
            _path.Clear();
            Array.Clear(_visited, 0, N);
        }

        /// <summary>
        /// 손가락이 올라온 셀을 경로에 추가. 이미 방문했으면 무시.
        /// 직전 셀과 일직선 중간 셀이 미방문이면 그것부터 추가(안드로이드식). 실제로 추가됐으면 true.
        /// </summary>
        public bool AddCell(int cell)
        {
            if (cell < 0 || cell >= N) return false;
            if (_visited[cell]) return false;

            if (_path.Count > 0)
            {
                int last = _path[_path.Count - 1];
                int mid = Midpoint(last, cell);
                if (mid >= 0 && !_visited[mid])
                {
                    _visited[mid] = true;
                    _path.Add(mid);
                }
            }

            _visited[cell] = true;
            _path.Add(cell);
            return true;
        }

        /// <summary>완성된 경로 스냅샷.</summary>
        public int[] Snapshot() => _path.ToArray();

        /// <summary>목표 패턴과 정확히(순서 포함) 일치하는지.</summary>
        public bool Matches(IReadOnlyList<int> target)
        {
            if (target == null || target.Count != _path.Count) return false;
            for (int i = 0; i < _path.Count; i++)
                if (_path[i] != target[i]) return false;
            return true;
        }

        // a와 b가 일직선상 두 칸 간격이면 그 사이 셀 인덱스, 아니면 -1.
        private static int Midpoint(int a, int b)
        {
            int ar = a / 3, ac = a % 3;
            int br = b / 3, bc = b % 3;
            int rs = ar + br, cs = ac + bc;
            if ((rs & 1) == 0 && (cs & 1) == 0)
            {
                int mid = (rs / 2) * 3 + (cs / 2);
                if (mid != a && mid != b) return mid;
            }
            return -1;
        }
    }
}
