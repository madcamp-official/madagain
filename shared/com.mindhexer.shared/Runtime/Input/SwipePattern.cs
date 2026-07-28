using System;
using System.Collections.Generic;

namespace MindHexer.Shared.Input
{
    /// <summary>
    /// 안드로이드 잠금패턴식 N×N 스와이프 패턴 빌더(Unity 비의존).
    /// 손가락이 지나가는 노드를 순서대로 잇되, 각 노드는 한 번만 사용한다. 두 노드 사이에 일직선상의
    /// 방문 안 한 중간 노드가 있으면(3×3 이상에서만 발생) 자동으로 먼저 포함한다(안드로이드 방식).
    ///
    /// 노드 인덱스는 0..N²-1 (row = i/size, col = i%size). 인덱스↔화면 위치 매핑은 호출측(컨트롤러)이
    /// 담당하므로 이 코어는 화면/방향에 독립적이다. 순수 로직이라 EditMode/콘솔로 결정론 검증 가능.
    /// </summary>
    public sealed class SwipePattern
    {
        private readonly int _size;
        private readonly int _n;
        private readonly List<int> _path;
        private readonly bool[] _visited;

        /// <param name="size">한 변의 노드 수(예: 2 → 2×2 = 4노드, 3 → 3×3 = 9노드).</param>
        public SwipePattern(int size = 3)
        {
            if (size < 1) size = 1;
            _size = size;
            _n = size * size;
            _path = new List<int>(_n);
            _visited = new bool[_n];
        }

        public int Size => _size;
        public int NodeCount => _n;

        /// <summary>
        /// 이미 지난 노드를 **다시** 지날 수 있게 허용한다(예: 1→2→4→2→3→2→1).
        /// 단, <b>직전 노드를 연속으로 두 번</b> 지나는 것은 항상 금지. 기본값 false(안드로이드 잠금패턴식 1회 방문).
        /// </summary>
        public bool AllowRevisit { get; set; }

        /// <summary>지금까지 이어진 노드 순서.</summary>
        public IReadOnlyList<int> Path => _path;

        public int Count => _path.Count;

        /// <summary>경로의 마지막(현재) 노드. 비어 있으면 -1.</summary>
        public int Last => _path.Count > 0 ? _path[_path.Count - 1] : -1;

        public bool Contains(int node) => node >= 0 && node < _n && _visited[node];

        /// <summary>새 스와이프 시작(경로 초기화).</summary>
        public void Begin()
        {
            _path.Clear();
            Array.Clear(_visited, 0, _n);
        }

        /// <summary>
        /// 손가락이 올라온 노드를 경로에 추가. **직전 노드와 같으면**(연속 반복) 무시.
        /// <see cref="AllowRevisit"/> off면 이미 방문한 노드도 무시(1회 방문).
        /// 직전 노드와 일직선 중간 노드가 있으면 먼저 추가(안드로이드식). 실제로 추가됐으면 true.
        /// </summary>
        public bool AddCell(int node)
        {
            if (node < 0 || node >= _n) return false;

            int last = _path.Count > 0 ? _path[_path.Count - 1] : -1;
            if (node == last) return false;                       // 자기 자신 연속 두 번 금지(항상)
            if (_visited[node] && !AllowRevisit) return false;    // 재방문 금지 모드: 1회만

            if (last >= 0)
            {
                int mid = Midpoint(last, node);
                // 직선 중간 노드 자동 삽입: 재방문 금지 모드에선 미방문일 때만(기존 동작),
                // 재방문 허용 모드에선 항상 삽입(직선 통과 표현 유지). Midpoint가 mid≠last, mid≠node 보장.
                if (mid >= 0 && (AllowRevisit || !_visited[mid]))
                {
                    _visited[mid] = true;
                    _path.Add(mid);
                }
            }

            _visited[node] = true;
            _path.Add(node);
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

        // a와 b가 일직선상 두 칸 간격이면 그 사이 노드 인덱스, 아니면 -1. (2×2에서는 항상 -1)
        private int Midpoint(int a, int b)
        {
            int ar = a / _size, ac = a % _size;
            int br = b / _size, bc = b % _size;
            int rs = ar + br, cs = ac + bc;
            if ((rs & 1) == 0 && (cs & 1) == 0)
            {
                int mid = (rs / 2) * _size + (cs / 2);
                if (mid != a && mid != b) return mid;
            }
            return -1;
        }
    }
}
