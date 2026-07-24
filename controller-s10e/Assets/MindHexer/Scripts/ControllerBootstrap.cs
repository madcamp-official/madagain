using UnityEngine;
using MindHexer.Controller.Net;
using MindHexer.Controller.Input;
using MindHexer.Controller.UI;

namespace MindHexer.Controller
{
    /// <summary>
    /// S10e 컨트롤러 전체를 런타임에 조립한다. (SPEC 4.2 — 씬 1개, 리소스 최소)
    /// 모든 컴포넌트를 하나의 GameObject에 붙이므로, 씬을 손으로 구성할 필요 없이
    /// 빈 씬에서도 자동 부팅된다(각 컴포넌트는 Awake에서 형제 컴포넌트를 GetComponent로 자동 연결).
    ///
    /// 구성: UdpSender + WsClient + RttProbeBehaviour + DiscoveryListenerBehaviour
    ///       + TouchGyroCapture + PairingFlow + ControllerHud
    /// </summary>
    public static class ControllerBootstrap
    {
        private const string RootName = "MindHexerController";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (GameObject.Find(RootName) != null) return; // 중복 방지

            // 비활성 상태로 생성 → 모든 컴포넌트 추가 후 활성화 →
            // Awake가 전부 갖춰진 뒤 실행되어 GetComponent 자동 연결이 안전하다.
            var go = new GameObject(RootName);
            go.SetActive(false);

            go.AddComponent<UdpSender>();
            go.AddComponent<WsClient>();
            go.AddComponent<RttProbeBehaviour>();
            go.AddComponent<DiscoveryListenerBehaviour>();
            go.AddComponent<TouchGyroCapture>();
            go.AddComponent<PairingFlow>();
            go.AddComponent<ControllerHud>();

            Object.DontDestroyOnLoad(go);
            go.SetActive(true);

            Debug.Log("[Bootstrap] MindHexer controller assembled.");
        }
    }
}
