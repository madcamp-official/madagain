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
    ///       + ArcorePoseSource + TouchGyroCapture + FloatingJoystickInput + PatternPadInput
    ///       + PairingFlow + ControllerHud
    /// </summary>
    public static class ControllerBootstrap
    {
        private const string RootName = "MindHexerController";

        /// <summary>
        /// 직결 모드 — 페어링(WebSocket) 없이 <see cref="DirectServerIp"/>로 UDP를 바로 쏜다.
        ///
        /// <para>기본 흐름은 디스커버리→WebSocket 페어링(PairAck)→스트리밍 개시인데,
        /// <see cref="Net.PairingFlow"/>가 PairAck 전까지 스트리밍을 막기 때문에 수신 측이
        /// WebSocket 서버까지 갖춰야 데이터가 흐른다. MINDHEXER 에디터에서 실측 튜닝만 할 때는
        /// 그 전부가 불필요하므로, 관련 컴포넌트를 <b>아예 붙이지 않고</b> UDP만 흘린다.
        /// (팀원 코드는 수정하지 않는다 — 인스턴스화만 생략한다.)</para>
        ///
        /// <para>실기 2폰 구성으로 되돌릴 땐 이 값을 false로만 바꾸면 된다.</para>
        /// </summary>
        private const bool DirectMode = true;

        /// <summary>직결 대상. Windows PC 모바일 핫스팟 호스트는 항상 192.168.137.1.</summary>
        private const string DirectServerIp = "192.168.137.1";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (GameObject.Find(RootName) != null) return; // 중복 방지

            // 컨트롤러는 가로(landscape) 고정: 왼쪽 조이스틱 + 오른쪽 패턴 스와이프 패드 레이아웃 기준.
            Screen.orientation = ScreenOrientation.LandscapeLeft;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;

            // 비활성 상태로 생성 → 모든 컴포넌트 추가 후 활성화 →
            // Awake가 전부 갖춰진 뒤 실행되어 GetComponent 자동 연결이 안전하다.
            var go = new GameObject(RootName);
            go.SetActive(false);

            var sender = go.AddComponent<UdpSender>();

            if (!DirectMode)
            {
                go.AddComponent<WsClient>();
                go.AddComponent<RttProbeBehaviour>();
                go.AddComponent<DiscoveryListenerBehaviour>();
            }
            else
            {
                // GameObject가 아직 비활성이라 OnEnable(=소켓 Connect)이 실행되기 전이다.
                // 여기서 대상을 박아두면 활성화되는 순간 그 IP로 바로 붙는다.
                sender.TargetIp = DirectServerIp;
            }

            // ARCore(6DoF)는 TouchGyroCapture보다 먼저 붙인다 — 포즈 Transform이 먼저 생기게.
            // 페어링 여부와 무관하게 계속 돌려 세션이 미리 수렴하게 둔다(해킹 성공 순간에 켜면 늦다).
            go.AddComponent<ArcorePoseSource>();

            // 직결 모드에선 PairingFlow가 없으므로 스트리밍을 끄는 주체도 없다 → 켜진 채로 시작한다.
            go.AddComponent<TouchGyroCapture>();
            go.AddComponent<FloatingJoystickInput>();
            go.AddComponent<PatternPadInput>();

            if (!DirectMode) go.AddComponent<PairingFlow>();

            go.AddComponent<ControllerHud>();

            Object.DontDestroyOnLoad(go);
            go.SetActive(true);

            Debug.Log("[Bootstrap] MindHexer controller assembled.");
        }
    }
}
