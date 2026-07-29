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

        [Header("프레임")]
        [Tooltip("목표 프레임레이트. ⚠️ Unity는 Android에서 이 값을 지정하지 않으면 배터리 절약을 위해 " +
                 "30으로 묶는다 — 실기에서 정확히 30.0fps에 고정되는 것으로 확인했다. VR에서 30fps는 " +
                 "멀미를 유발하므로 반드시 올려야 한다. 0 이하면 건드리지 않는다.")]
        public int targetFrameRate = 60;

        Camera _cam;

        void Awake()
        {
            ApplyFrameRate();
            _cam = EnsureCamera();
            SetupRig();
        }

        /// <summary>
        /// 통합 리그 — PC와 VR이 <b>같은 몸</b>을 쓴다. 차이는 카메라에 붙는 시점 드라이버 하나뿐.
        ///
        /// <code>
        /// [PlayerBody]  ← CC + 이동·중력·밀림감지 + AutoTraversal + MantleRig. 회전하지 않는다.
        ///    └ Main Camera ← 시점 소유(PC=MouseLook / VR=TrackedPoseDriver RotationOnly)
        ///                    + MotionFeel(연출) + HackDriver(조준=정면)
        /// </code>
        ///
        /// <para>몸 원점 = 눈높이. 기존 이동·등반 코드 전부가 "트랜스폼 위치 = 눈"을 전제하므로
        /// 그 규약을 유지한다(발 위치는 CC의 center −0.7이 만든다). 카메라 로컬은 0 — VR에서
        /// TrackedPoseDriver가 RotationOnly라 위치를 안 건드려 눈높이가 꺼지지 않는다.</para>
        ///
        /// <para>이동 기준 = 카메라 수평 forward("조이스틱 앞 = 보는 방향" — VR 표준 로코모션이자
        /// PC의 원래 동작). 예전 VR 전용 [XR Rig]+FreeLookController 경로는 삭제 — 몸이 없어서
        /// 충돌·중력 없이 벽을 뚫고 다녔고 눈높이도 TPD가 덮어써 땅이 꺼져 보였다.</para>
        /// </summary>
        void SetupRig()
        {
            var body = new GameObject("[PlayerBody]");
            body.transform.position = startPosition + Vector3.up * eyeHeight;

            // 카메라 = 시점 전용 자식. 연출(MotionFeel)은 여기 — 몸 위치를 건드리면 CC와 싸운다.
            _cam.transform.SetParent(body.transform, false);
            _cam.transform.localPosition = Vector3.zero;
            _cam.transform.localRotation = Quaternion.identity;
            if (_cam.GetComponent<MotionFeel>() == null) _cam.gameObject.AddComponent<MotionFeel>();

            // 몸 — MotionFeel(자식)이 먼저 있어야 Awake 캐시가 잡힌다.
            if (body.GetComponent<MantleRig>() == null) body.AddComponent<MantleRig>();
            var fpp = body.GetComponent<FirstPersonPlayer>() ?? body.AddComponent<FirstPersonPlayer>();
            fpp.view = _cam.transform;
            if (body.GetComponent<AutoTraversal>() == null) body.AddComponent<AutoTraversal>();

            // 해킹 — 조준 = 카메라 정면. [RequireComponent(HackContext)]라 HackContext도 함께 붙는다.
            if (_cam.GetComponent<HackDriver>() == null) _cam.gameObject.AddComponent<HackDriver>();

            if (VrMode.Enabled)
            {
                AttachHeadPoseDriver(_cam);   // RotationOnly — 위치는 몸이 소유

                // ScreenSpace HUD → 머리 앞 World-Space 패널(양안 렌더).
                var hud = new GameObject("[VrHudSpace]").AddComponent<VrHudSpace>();
                hud.head = _cam.transform;

                // VR 튜닝 — 눈높이·HUD·렌더스케일 JSON 저장/로드.
                var tuning = gameObject.AddComponent<VrTuning>();
                tuning.head = _cam.transform;
                tuning.hud = hud;
                tuning.eyeBase = eyeHeight;   // 몸 원점이 이미 눈높이 — 카메라 로컬은 차이만 받는다
            }
            else
            {
                _cam.gameObject.AddComponent<MouseLook>();
                if (body.GetComponent<MoveTuningPanel>() == null) body.AddComponent<MoveTuningPanel>();
                if (lockCursor) Cursor.lockState = CursorLockMode.Locked;
            }
        }

        /// <summary>
        /// 머리 포즈를 카메라에 적용한다.
        ///
        /// <para><b>왜 필요한가</b> — 구형 VR은 유니티가 메인 카메라에 머리 포즈를 자동 적용했지만,
        /// 신형 XR SDK는 그러지 않는다. 그래서 <b>스플래시 화면은 회전하는데 게임은 안 도는</b>
        /// 증상이 나온다(실기 확인) — 트래킹은 살아 있고 카메라가 그 값을 안 받는 것이다.</para>
        ///
        /// <para>바인딩 경로는 Cardboard 공식 샘플(HelloCardboard)의 카메라 설정에서 그대로 가져왔다.
        /// 추측하면 틀린다.</para>
        /// </summary>
        void AttachHeadPoseDriver(Camera cam)
        {
            if (cam == null) return;
            if (cam.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>() != null) return;

            var tpd = cam.gameObject.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            // RotationOnly — 위치까지 받으면 Cardboard(3DoF)의 거의-0 위치가 카메라 로컬을 덮어써
            // 눈높이가 지워지고 땅이 꺼져 보인다(실기 확인). 위치는 [PlayerBody]가 소유한다.
            tpd.trackingType = UnityEngine.InputSystem.XR.TrackedPoseDriver.TrackingType.RotationOnly;
            // Update + BeforeRender — 렌더 직전에 한 번 더 갱신해야 머리 지연이 최소가 된다.
            tpd.updateType = UnityEngine.InputSystem.XR.TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;

            var rot = new UnityEngine.InputSystem.InputAction(
                "HMD Rotation", UnityEngine.InputSystem.InputActionType.Value, "<XRHMD>/centerEyeRotation");
            rot.Enable();
            tpd.rotationInput = new UnityEngine.InputSystem.InputActionProperty(rot);

            Debug.Log("[GameBoot] 머리 포즈 드라이버 부착 (<XRHMD>/centerEyeRotation, RotationOnly)");
        }

        /// <summary>
        /// 프레임 상한 해제. vSync가 켜져 있으면 <c>targetFrameRate</c>가 무시되므로 함께 끈다.
        /// (Android 기본 품질 레벨은 이미 <c>vSyncCount 0</c>이지만, 레벨이 바뀌어도 안 흔들리게 명시한다.)
        /// </summary>
        void ApplyFrameRate()
        {
            if (targetFrameRate <= 0) return;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = targetFrameRate;
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

    }
}
