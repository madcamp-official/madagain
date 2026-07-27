using System;

namespace MindHexer.Shared.Net
{
    /// <summary>
    /// 한 WebSocket 연결에 대한 양방향 텍스트 채널 추상화.
    /// WebSocket 라이브러리(NativeWebSocket / WebSocketSharp)를 이 인터페이스 뒤로 숨겨,
    /// 페어링 로직(<see cref="PairingClient"/>/<see cref="PairingServer"/>)을 라이브러리 없이 검증 가능하게 한다.
    ///
    /// 구현체는 <see cref="Received"/>/<see cref="Closed"/>를 어느 스레드에서 올릴지 문서화해야 한다.
    /// (Unity 어댑터는 MainThreadDispatcher로 메인 스레드에 넘겨 올린다.)
    /// </summary>
    public interface IEventChannel
    {
        /// <summary>이 채널로 텍스트(플랫 JSON) 한 건 송신.</summary>
        void Send(string json);

        /// <summary>이 채널(연결)을 닫는다. 서버가 스테일 세션을 끊어 재페어링을 유도할 때 사용.</summary>
        void Close();

        /// <summary>텍스트 수신 시 발생. 인자는 원본 문자열.</summary>
        event Action<string> Received;

        /// <summary>채널 닫힘 시 발생.</summary>
        event Action Closed;
    }
}
