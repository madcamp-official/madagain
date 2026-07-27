using UnityEngine;
using MindHexer.Headset.Net;
using MindHexer.Headset.Input;
using MindHexer.Headset.Gameplay;
using MindHexer.Headset.UI;

namespace MindHexer.Headset
{
    /// <summary>
    /// S24+ 네트워킹 스택 + 상태 HUD를 런타임에 조립한다. (WebSocket 서버 + UDP 수신 + 디스커버리 + RTT + 입력 브릿지 + 패턴 판정 + HUD)
    /// 외부 DLL 없이(<see cref="MindHexer.Shared.Net.TcpWebSocketServer"/>) 동작.
    ///
    /// **BeforeSceneLoad** 로 등록: AfterSceneLoad는 씬이 실제 로드돼야 발동하므로, 헤드셋 빌드에 씬이 없으면
    /// 콜백이 안 불려 아무것도 안 떴다. BeforeSceneLoad는 엔진 시작 시 씬 유무와 무관하게 발동한다.
    /// 씬/카메라가 없을 수 있으므로 카메라도 직접 추가해 화면(배경+HUD)이 보장되게 한다.
    ///
    /// VR 렌더링(Cardboard XR)은 별개.
    /// </summary>
    public static class HeadsetBootstrap
    {
        private const string RootName = "MindHexerHeadset";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            if (GameObject.Find(RootName) != null) return;
            Debug.Log("[Bootstrap] MindHexer headset boot start…");

            var go = new GameObject(RootName);
            go.SetActive(false);

            // 카메라 보장: 헤드셋 씬이 비어 있어도 배경이 렌더되고 HUD(OnGUI)가 확실히 보이게.
            if (Camera.main == null)
            {
                var cam = go.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.05f, 0.06f, 0.09f);
            }

            go.AddComponent<MainThreadDispatcher>();          // WS 콜백 메인스레드 마샬
            go.AddComponent<UdpReceiver>();                   // S10e 6DoF/이동 스트림 수신
            go.AddComponent<WebSocketServerHost>();           // 내장 WebSocket 서버(페어링/이벤트)
            go.AddComponent<DiscoveryBroadcasterBehaviour>(); // 자신의 IP 브로드캐스트(지향 브로드캐스트 포함)
            go.AddComponent<RttResponderBehaviour>();         // RTT Ping 에코
            go.AddComponent<InputBridge>();                   // 수신 → 지터 버퍼 → 보간 입력
            go.AddComponent<HackGrid>();                      // 패턴 수신/판정(WS PatternSubmit)
            go.AddComponent<HeadsetHud>();                    // 화면 상태 표시(서버/수신/6DoF)

            Object.DontDestroyOnLoad(go);
            go.SetActive(true);

            Debug.Log("[Bootstrap] MindHexer headset assembled (server + HUD up).");
        }
    }
}
