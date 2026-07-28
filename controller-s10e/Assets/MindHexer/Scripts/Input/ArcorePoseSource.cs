using System.Collections;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;

namespace MindHexer.Controller.Input
{
    /// <summary>
    /// ARCore VIO로 디바이스 <b>6DoF 포즈</b>를 추적해 자식 Transform(<see cref="Pose"/>)에 반영한다.
    /// <see cref="TouchGyroCapture"/>가 이 Transform을 읽어 InputPacket의 위치·회전으로 전송한다.
    /// (와이어 포맷은 이미 v2 6DoF라 프로토콜 변경 없음 — SPEC 4.2)
    ///
    /// <para><b>왜 TrackedPoseDriver가 아니라 레거시 XR API(<see cref="InputDevices"/>)인가</b> —
    /// TrackedPoseDriver는 Input System 패키지 소속인데, 이 프로젝트는
    /// <b>Active Input Handling = Input Manager (Old)</b> 고정이다(docs/SETUP.md 0.1).
    /// 조이스틱·패턴패드가 <c>Input.touch</c>/<c>Input.gyro</c> 레거시 입력을 쓰기 때문이다.
    /// InputDevices는 XR Plug-in Management + ARCore 로더만으로 동작해 입력 핸들링을 안 건드린다.</para>
    ///
    /// <para><b>리센터는 여기서 하지 않는다.</b> ARCore 세션 좌표 원본을 그대로 보내고, 헤드셋 쪽이
    /// 해킹 성공 순간의 포즈를 원점으로 잡는다. 조종 구간이 그 이산 이벤트로 시작하므로 두 기기의
    /// 좌표계를 맞출 필요가 없다.</para>
    ///
    /// <para>⚠️ 미검증: ARCore 로더가 <see cref="XRNode.CenterEye"/>로 포즈를 노출하는지는 실기 첫
    /// 확인 대상이다. 안 나오면 <see cref="State"/>가 <see cref="PoseState.NoPoseDevice"/>가 되고
    /// HUD에 표시된다 — 그 경우 AR 카메라 + 포즈 드라이버 방식으로 우회해야 한다.</para>
    /// </summary>
    public sealed class ArcorePoseSource : MonoBehaviour
    {
        public enum PoseState
        {
            Idle,
            CheckingSupport,
            Unsupported,        // 기기가 ARCore 미지원
            NeedsInstall,       // Google Play Services for AR 설치 중/필요
            PermissionDenied,   // 카메라 권한 거부
            Starting,           // 세션 기동 중
            NoPoseDevice,       // 세션은 떴는데 XR 입력이 포즈를 안 준다(위 ⚠️)
            TrackingLost,       // 특징점 부족 등으로 추적 상실 — 값이 멈추거나 튄다
            Tracking,
        }

        [Tooltip("XR 포즈 디바이스를 못 잡았을 때 재탐색 간격(초).")]
        public float deviceScanInterval = 0.5f;

        /// <summary>현재 상태. HUD 표시·진단용.</summary>
        public PoseState State { get; private set; } = PoseState.Idle;

        /// <summary>디바이스 포즈가 반영되는 Transform. TouchGyroCapture.poseSource가 이걸 읽는다.</summary>
        public Transform Pose => _pose;

        /// <summary>위치가 실제로 살아 있는가(=6DoF가 유효한가). false면 3DoF 폴백 상태다.</summary>
        public bool HasPosition => State == PoseState.Tracking;

        /// <summary>사람이 읽는 상태 문자열.</summary>
        public string StatusText => State switch
        {
            PoseState.Idle => "-",
            PoseState.CheckingSupport => "AR 확인 중…",
            PoseState.Unsupported => "AR 미지원 기기",
            PoseState.NeedsInstall => "AR 서비스 설치 필요",
            PoseState.PermissionDenied => "카메라 권한 거부됨",
            PoseState.Starting => "AR 기동 중…",
            PoseState.NoPoseDevice => "포즈 없음(XR 입력 미노출)",
            PoseState.TrackingLost => "추적 상실(3DoF)",
            PoseState.Tracking => "6DoF 추적 중",
            _ => "-"
        };

        private Transform _pose;
        private ARSession _session;
        private Camera _arCamera;
        private InputDevice _device;
        private float _nextDeviceScan;

        private void Awake()
        {
            // 포즈 Transform은 Awake에서 만들어 둔다 — 다른 컴포넌트가 Awake 순서와 무관하게 참조할 수 있게.
            var go = new GameObject("[ArcorePose]");
            go.transform.SetParent(transform, false);
            _pose = go.transform;
        }

        private IEnumerator Start()
        {
            State = PoseState.CheckingSupport;

            // 1) 카메라 권한 — VIO는 카메라가 있어야 동작한다.
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                Permission.RequestUserPermission(Permission.Camera);

                // 권한 다이얼로그는 앱을 일시정지시키므로 unscaled 시간으로 기다린다.
                float deadline = Time.realtimeSinceStartup + 60f;
                while (!Permission.HasUserAuthorizedPermission(Permission.Camera)
                       && Time.realtimeSinceStartup < deadline)
                    yield return null;

                if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
                {
                    State = PoseState.PermissionDenied;
                    Debug.LogWarning("[ARCore] 카메라 권한 거부 — 6DoF 없이 3DoF로 동작합니다.");
                    yield break;
                }
            }

            // 2) ARCore 가용성 + Google Play Services for AR 설치 유도.
            yield return ARSession.CheckAvailability();

            if (ARSession.state == ARSessionState.NeedsInstall)
            {
                State = PoseState.NeedsInstall;
                yield return ARSession.Install();
            }

            if (ARSession.state == ARSessionState.Unsupported)
            {
                State = PoseState.Unsupported;
                Debug.LogWarning("[ARCore] 이 기기는 ARCore 미지원 — 3DoF로 동작합니다.");
                yield break;
            }

            // 3) AR 리그를 코드로 조립한다 — 이 프로젝트는 씬 에셋을 쓰지 않는다(ControllerBootstrap 방침).
            BuildRig();
            State = PoseState.Starting;
            Debug.Log("[ARCore] session rig built.");
        }

        /// <summary>
        /// 최소 AR 리그: ARSession + 카메라(ARCameraManager).
        /// 카메라는 <b>아무것도 그리지 않는다</b>(cullingMask=0) — ARCore 세션이 카메라를 잡게만 하고
        /// 화면은 기존 IMGUI(조이스틱·패턴패드·HUD)가 그대로 쓴다. IMGUI는 카메라와 무관하게 그려진다.
        /// 카메라 피드를 화면에 깔지 않으므로 ARCameraBackground는 붙이지 않는다.
        /// </summary>
        private void BuildRig()
        {
            var sessionGo = new GameObject("[ARSession]");
            sessionGo.transform.SetParent(transform, false);
            _session = sessionGo.AddComponent<ARSession>();

            var camGo = new GameObject("[ARCamera]");
            camGo.transform.SetParent(transform, false);
            _arCamera = camGo.AddComponent<Camera>();
            _arCamera.clearFlags = CameraClearFlags.SolidColor;
            _arCamera.backgroundColor = Color.black;
            _arCamera.cullingMask = 0;      // 아무 레이어도 안 그림
            _arCamera.depth = -100;         // 혹시 다른 카메라가 있으면 그 뒤에
            _arCamera.nearClipPlane = 0.1f;
            camGo.AddComponent<ARCameraManager>();
        }

        private void Update()
        {
            if (_session == null) return;   // 아직 기동 전이거나 미지원

            if (!_device.isValid)
            {
                if (Time.unscaledTime < _nextDeviceScan) return;
                _nextDeviceScan = Time.unscaledTime + Mathf.Max(0.1f, deviceScanInterval);

                _device = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
                if (!_device.isValid)
                {
                    // 세션은 떴는데 XR 입력이 포즈를 안 준다 — 설계 주석의 ⚠️ 경우.
                    if (State != PoseState.Starting) State = PoseState.NoPoseDevice;
                    return;
                }
                Debug.Log("[ARCore] XR pose device acquired.");
            }

            bool gotPos = _device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 p);
            bool gotRot = _device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion r);

            if (!gotPos && !gotRot)
            {
                State = PoseState.NoPoseDevice;
                return;
            }

            if (gotPos) _pose.localPosition = p;
            if (gotRot) _pose.localRotation = r;

            // 추적 상실은 값이 멈추거나 튀는 것으로 나타난다. 패킷에 실을 자리가 없어(72바이트 고정)
            // 전송하지는 않고 여기 상태로만 노출한다 — 필요해지면 프로토콜 v3에서 추가.
            State = ARSession.state == ARSessionState.SessionTracking
                ? PoseState.Tracking
                : PoseState.TrackingLost;
        }

        private void OnDestroy()
        {
            if (_session != null) Destroy(_session.gameObject);
            if (_arCamera != null) Destroy(_arCamera.gameObject);
        }
    }
}
