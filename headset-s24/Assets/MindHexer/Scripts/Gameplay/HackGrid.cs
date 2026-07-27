using System.Collections.Generic;
using UnityEngine;
using MindHexer.Shared.Events;
using MindHexer.Headset.Net;

namespace MindHexer.Headset.Gameplay
{
    /// <summary>
    /// 2x2 해킹 패턴 판정. (SPEC 3.3 / 6, 담당자 A)
    /// 컨트롤러(S10e)가 플로팅 2x2 스와이프로 완성한 노드 시퀀스(0..3)를 받아 목표 패턴과 비교하고,
    /// 결과를 WebSocket 확정 이벤트(PatternResult)로 통보한다.
    ///
    /// 노드 인덱스는 컨트롤러의 <see cref="MindHexer.Shared.Input.SwipePattern"/>과 동일(0=좌상단,1=우상단,2=좌하단,3=우하단).
    /// 입력 전달(완성 패턴 → 헤드셋)은 아직 배선 전(TODO): WsClient→PairingServer 이벤트 또는 InputPacket 확장.
    /// </summary>
    public sealed class HackGrid : MonoBehaviour
    {
        [SerializeField] private WebSocketServerHost _wsServer;

        /// <summary>목표 패턴 (2x2 노드 인덱스 0..3 시퀀스). 예: 0→1→3→2 (ㄷ자).</summary>
        public List<int> TargetPattern = new List<int> { 0, 1, 3, 2 };

        /// <summary>마지막으로 수신한 패턴/판정(HUD 표시용).</summary>
        public string LastPattern { get; private set; } = "-";
        public string LastResult { get; private set; } = "-";

        private void Awake()
        {
            if (_wsServer == null) _wsServer = GetComponent<WebSocketServerHost>();
            if (_wsServer != null) _wsServer.PatternSubmitted += OnPatternSubmitted;
        }

        private void OnDestroy()
        {
            if (_wsServer != null) _wsServer.PatternSubmitted -= OnPatternSubmitted;
        }

        // 컨트롤러가 스와이프로 완성한 패턴 수신 → 판정.
        private void OnPatternSubmitted(int[] nodes) => SubmitPattern(nodes);

        /// <summary>완성된 스와이프 패턴을 판정하고 결과를 페어링된 클라이언트에 통보.</summary>
        public bool SubmitPattern(IReadOnlyList<int> nodes)
        {
            bool success = nodes != null && nodes.Count == TargetPattern.Count;
            if (success)
                for (int i = 0; i < TargetPattern.Count; i++)
                    if (nodes[i] != TargetPattern[i]) { success = false; break; }

            LastPattern = nodes != null ? string.Join(",", nodes) : "-";
            LastResult = success ? "성공" : "실패";

            _wsServer?.Broadcast(EventMessage.PatternResult(success, patternId: 0));
            return success;
        }
    }
}
