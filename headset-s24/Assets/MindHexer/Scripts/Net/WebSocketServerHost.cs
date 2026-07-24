using UnityEngine;
using MindHexer.Shared.Events;

namespace MindHexer.Headset.Net
{
    /// <summary>
    /// S24+ 내장 WebSocket 서버(확정 이벤트 채널). WebSocketSharp로 구현 예정. (SPEC 2.2)
    ///
    /// TODO(담당자 A, 3일차):
    ///  - WebSocketSharp.WebSocketServer 기동 (포트 NetworkConstants.WebSocketPort, 경로 WebSocketPath)
    ///  - OnMessage 콜백은 별도 스레드 → MainThreadDispatcher.Enqueue 경유로 게임 로직에 전달
    ///  - PatternResult / BatteryWarning 등 서버→클라 이벤트 Send 구현
    ///  - 페어링(PairRequest/PairAck) 시 ProtocolVersion 일치 확인
    ///
    /// WebSocketSharp.dll을 Assets/Plugins/ 에 배치해야 컴파일된다(docs/SETUP.md 3절).
    /// </summary>
    public sealed class WebSocketServerHost : MonoBehaviour
    {
        /// <summary>확정 이벤트를 접속된 클라이언트(S10e)로 송신.</summary>
        public void SendEvent(EventMessage message)
        {
            // TODO: JsonUtility.ToJson(message) → WebSocketSharp broadcast
            var json = JsonUtility.ToJson(message);
            Debug.Log($"[WS→] {json}"); // 스텁: 실제 전송으로 교체
        }
    }
}
