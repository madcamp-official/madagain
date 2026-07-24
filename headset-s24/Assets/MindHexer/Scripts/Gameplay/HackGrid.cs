using System.Collections.Generic;
using UnityEngine;
using MindHexer.Shared.Events;
using MindHexer.Headset.Net;

namespace MindHexer.Headset.Gameplay
{
    /// <summary>
    /// 3x3 해킹 그리드 + 패턴 판정. (SPEC 3.3 / 6, 담당자 A 3일차)
    /// 정규화 좌표가 매핑된 셀 시퀀스를 목표 패턴과 비교하고,
    /// 결과를 WebSocket 확정 이벤트(PatternResult)로 S10e에 통보한다.
    ///
    /// TODO: 실제 패턴 정의/입력 타이밍/시각 피드백 구현. 여기서는 판정 골격만.
    /// </summary>
    public sealed class HackGrid : MonoBehaviour
    {
        [SerializeField] private WebSocketServerHost _wsServer;

        /// <summary>목표 패턴 (셀 인덱스 0..8 시퀀스).</summary>
        public List<int> TargetPattern = new List<int> { 0, 1, 2, 5, 8 };

        private readonly List<int> _input = new List<int>();

        /// <summary>정규화 좌표(0..1)를 3x3 셀 인덱스(0..8)로 변환.</summary>
        public static int ToCellIndex(Vector2 normalized)
        {
            int col = Mathf.Clamp((int)(normalized.x * 3f), 0, 2);
            int row = Mathf.Clamp((int)(normalized.y * 3f), 0, 2);
            return row * 3 + col;
        }

        /// <summary>한 셀 입력을 기록. 패턴이 완성되면 판정 후 결과를 통보.</summary>
        public void OnCellInput(int cellIndex)
        {
            if (_input.Count == 0 || _input[_input.Count - 1] != cellIndex)
                _input.Add(cellIndex);

            if (_input.Count >= TargetPattern.Count)
                Evaluate();
        }

        private void Evaluate()
        {
            bool success = _input.Count == TargetPattern.Count;
            if (success)
                for (int i = 0; i < TargetPattern.Count; i++)
                    if (_input[i] != TargetPattern[i]) { success = false; break; }

            // 페어링된 모든 클라이언트(S10e)에 판정 결과 통보.
            _wsServer?.Broadcast(EventMessage.PatternResult(success, patternId: 0));

            _input.Clear();
        }
    }
}
