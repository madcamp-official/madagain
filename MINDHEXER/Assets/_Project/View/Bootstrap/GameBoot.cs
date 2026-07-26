using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// MINDHEXER 게임 씬 부트스트랩. 이식된 Precog <c>Main.cs</c>(결정론 예측 시뮬레이션 기반)를
    /// 대체하는 **우리 진입점**이다. 씬에 [GameBoot] 하나만 두면 1인칭 카메라 + 해킹 시스템이 구성된다.
    /// 예측·결정론 World/Snapshot에 의존하지 않는 **실시간 게임 부트**.
    ///
    /// VR 이식: <see cref="VrMode"/>.Enabled면 XR 리그(머리 회전=Cardboard 소유) + World-Space HUD를
    /// 구성한다 — Precog Main.cs의 VR 카메라 경로를 이 부트로 옮긴 것(예측/vcam 의존은 제거).
    /// Precog Main은 GameBoot 존재 시 자동부팅을 건너뛴다(Main.cs Boot 가드).
    /// (docs/KJH/decisions/0002-precog-purge.md)
    /// </summary>
    [DisallowMultipleComponent]
    public class GameBoot : MonoBehaviour
    {
        [Header("플레이어 시작")]
        [Tooltip("플레이어(카메라/리그) 시작 위치.")]
        public Vector3 startPosition = new Vector3(0f, 0f, -6f);
        [Tooltip("눈높이(카메라 Y 오프셋).")]
        public float eyeHeight = 1.6f;
        [Tooltip("PC 모드에서 시작 시 마우스 커서를 잠글지.")]
        public bool lockCursor = true;

        Camera _cam;

        void Awake()
        {
            _cam = EnsureCamera();
            if (VrMode.Enabled) SetupVrRig();
            else                SetupPcRig();
        }

        Camera EnsureCamera()
        {
            var c = Camera.main;
            if (c == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                c = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            c.nearClipPlane = 0.1f;
            var camData = c.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>()
                       ?? c.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            camData.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            return c;
        }

        /// <summary>PC: 카메라에 WASD+마우스 이동/시점 + 해킹 시스템.</summary>
        void SetupPcRig()
        {
            _cam.transform.SetParent(null, false);
            _cam.transform.position = startPosition + Vector3.up * eyeHeight;

            var go = _cam.gameObject;
            if (go.GetComponent<FreeLookController>() == null) go.AddComponent<FreeLookController>();
            // HackDriver는 [RequireComponent(HackContext)]라 HackContext도 함께 붙는다.
            if (go.GetComponent<HackDriver>() == null) go.AddComponent<HackDriver>();

            if (lockCursor) Cursor.lockState = CursorLockMode.Locked;
        }

        /// <summary>
        /// VR: XR 리그 루트(이동) + 카메라 자식(머리 트래킹이 회전 소유) + World-Space HUD.
        /// Main.cs VR 경로 이식 — CinemachineBrain을 붙이지 않는다(XR 헤드트래킹과 충돌).
        /// </summary>
        void SetupVrRig()
        {
            var rig = new GameObject("[XR Rig]").transform;
            rig.position = startPosition;

            _cam.transform.SetParent(rig, false);
            _cam.transform.localPosition = Vector3.up * eyeHeight;
            _cam.transform.localRotation = Quaternion.identity;   // 로컬 자세는 XR(머리)이 채운다

            // 리그 이동만(WASD=locomotion 자리표시자, 나중에 S10e). 시점 회전은 머리가 소유.
            var mover = rig.gameObject.AddComponent<FreeLookController>();
            mover.lookEnabled = false;

            // 해킹 시선 = 카메라(머리) 정면 → HackDriver는 카메라에.
            var hd = _cam.GetComponent<HackDriver>() ?? _cam.gameObject.AddComponent<HackDriver>();
            // VR 입력 = 네트워크(S10e) 소스, 지연 가리기 층 경유. SYB 네트워크가 NetworkHexInputSource.Active로 Push.
            hd.Source = new NetworkHexInputSource();

            // ScreenSpace HUD → 머리 앞 World-Space 패널(양안 렌더). (사용자 C 작업 이식)
            var hud = new GameObject("[VrHudSpace]").AddComponent<VrHudSpace>();
            hud.head = _cam.transform;
        }
    }
}
