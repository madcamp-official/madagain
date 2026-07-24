using UnityEngine;
using MindHexer.Shared.Events;

namespace MindHexer.Controller.Net
{
    /// <summary>
    /// S24+ 내장 WebSocket 서버에 접속하는 클라이언트(확정 이벤트 채널). NativeWebSocket 사용. (SPEC 2.2)
    ///
    /// TODO(담당자 B, 3일차):
    ///  - NativeWebSocket.WebSocket 연결 (ws://{serverIp}:{WebSocketPort}{WebSocketPath})
    ///  - OnMessage → JsonUtility.FromJson&lt;EventMessage&gt; → 이벤트 타입별 처리
    ///    (PatternResult 시각 피드백, BatteryWarning 표시 등)
    ///  - PairRequest 송신, 재연결(지수 백오프) 로직 (SPEC 5.1)
    ///  - NativeWebSocket은 WebGL 외 플랫폼에서 Update()에서 DispatchMessageQueue() 호출 필요.
    /// </summary>
    public sealed class WsClient : MonoBehaviour
    {
        public string ServerIp = "192.168.0.2";

        /// <summary>확정 이벤트를 서버(S24+)로 송신.</summary>
        public void SendEvent(EventMessage message)
        {
            var json = JsonUtility.ToJson(message);
            Debug.Log($"[WS→server] {json}"); // 스텁: 실제 전송으로 교체
        }
    }
}
