using UnityEngine;
using MindHexer.Headset.Net;
using MindHexer.Headset.Input;
using MindHexer.Headset.Gameplay;
using MindHexer.Headset.UI;

namespace MindHexer.Headset
{
    /// <summary>
    /// S24+ 네트워킹 스택을 런타임에 조립한다. (WebSocket 서버 + UDP 수신 + 디스커버리 + RTT 응답 + 입력 브릿지 + 패턴 판정)
    /// 외부 DLL 없이(<see cref="MindHexer.Shared.Net.TcpWebSocketServer"/>) 서버가 뜨므로 빈 씬에서도 동작한다.
    ///
    /// VR 렌더링(Cardboard XR)은 별개 — 이 부트스트랩은 네트워킹/입력만 담당한다.
    /// 각 컴포넌트는 Awake에서 형제를 GetComponent로 자동 연결한다.
    /// </summary>
    public static class HeadsetBootstrap
    {
        private const string RootName = "MindHexerHeadset";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (GameObject.Find(RootName) != null) return;

            var go = new GameObject(RootName);
            go.SetActive(false);

            go.AddComponent<MainThreadDispatcher>();         // WS 콜백 메인스레드 마샬
            go.AddComponent<UdpReceiver>();                  // S10e 6DoF/이동 스트림 수신
            go.AddComponent<WebSocketServerHost>();          // 내장 WebSocket 서버(페어링/이벤트)
            go.AddComponent<DiscoveryBroadcasterBehaviour>();// 자신의 IP 브로드캐스트
            go.AddComponent<RttResponderBehaviour>();        // RTT Ping 에코
            go.AddComponent<InputBridge>();                  // 수신 패킷 → 지터 버퍼 → 보간된 게임 입력
            go.AddComponent<HackGrid>();                     // 패턴 수신/판정(WS PatternSubmit)
            go.AddComponent<HeadsetHud>();                   // 화면 상태 표시(서버/수신/6DoF)

            Object.DontDestroyOnLoad(go);
            go.SetActive(true);

            Debug.Log("[Bootstrap] MindHexer headset networking assembled (no external WS DLL).");
        }
    }
}
