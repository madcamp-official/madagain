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

        /// <summary>채택된 포즈 기기 이름. 진단용(어느 경로로 잡혔는지 HUD에서 확인).</summary>
        public string DeviceName { get; private set; }

        // 관례적으로 포즈가 걸리는 노드들. 앞에서부터 시도한다.
        static readonly XRNode[] ProbeNodes =
        {
            XRNode.CenterEye,
            XRNode.Head,
            XRNode.HardwareTracker,
        };

        private readonly System.Collections.Generic.List<InputDevice> _scanBuf =
            new System.Collections.Generic.List<InputDevice>();

        private Transform _pose;
        private ARSession _session;
        private Camera _arCamera;
        private InputDevice _device;
        private float _nextDeviceScan;
        private bool _poseMoved;        // 드라이버가 실제로 값을 쓰기 시작했는가
        private bool _loggedFirstPose;

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

            // 포즈는 AR Foundation 공식 드라이버가 채운다. 직접 InputDevices를 폴링했더니
            // 세션이 "Position and Rotation 지원"을 보고하는데도 기기 목록이 0이었다(실기 확인).
            // ARPoseDriver는 기기 연결 이벤트를 구독하므로 초기화 순서를 스스로 감당한다.
            // 자기 transform을 구동하므로 _pose에 붙인다.
            _pose.gameObject.AddComponent<ARPoseDriver>();

            LogXrDiagnostics();
        }

        /// <summary>
        /// XR 로더·서브시스템 상태를 한 번 남긴다. <b>포즈가 안 나올 때 원인을 가르는 유일한 단서다.</b>
        /// 입력 서브시스템이 멈춰 있으면 여기서 켠다 — 그게 원인이면 이 한 줄로 해결된다.
        /// </summary>
        private void LogXrDiagnostics()
        {
            var sb = new System.Text.StringBuilder("[ARCore] XR 진단");

            var gs = UnityEngine.XR.Management.XRGeneralSettings.Instance;
            var mgr = gs != null ? gs.Manager : null;
            sb.Append("\n  activeLoader=").Append(mgr != null && mgr.activeLoader != null
                ? mgr.activeLoader.GetType().Name : "없음");
            sb.Append(" initComplete=").Append(mgr != null && mgr.isInitializationComplete);

            var inputSubsystems = new System.Collections.Generic.List<UnityEngine.XR.XRInputSubsystem>();
            SubsystemManager.GetSubsystems(inputSubsystems);
            sb.Append("\n  XRInputSubsystem=").Append(inputSubsystems.Count);
            for (int i = 0; i < inputSubsystems.Count; i++)
            {
                var s = inputSubsystems[i];
                sb.Append("\n    running=").Append(s.running);
                if (!s.running)
                {
                    s.Start();
                    sb.Append(" -> Start() 호출, 이제 running=").Append(s.running);
                }
            }

            _scanBuf.Clear();
            InputDevices.GetDevices(_scanBuf);
            sb.Append("\n  InputDevices=").Append(_scanBuf.Count);
            for (int i = 0; i < _scanBuf.Count; i++)
                sb.Append("\n    '").Append(_scanBuf[i].name).Append("' chars=").Append(_scanBuf[i].characteristics);

            Debug.Log(sb.ToString());
        }

        private void Update()
        {
            if (_session == null) return;   // 아직 기동 전이거나 미지원

            // 포즈 값 자체는 ARPoseDriver가 _pose에 써 넣는다. 여기서는 상태만 판정한다.
            if (ARSession.state != ARSessionState.SessionTracking)
            {
                State = PoseState.TrackingLost;
                return;
            }

            // 세션이 추적 중이라고 해도 드라이버가 실제로 값을 넣고 있는지는 별개다.
            // 포즈가 원점에 붙박여 있으면 구동이 안 되는 것으로 보고 구분해 표시한다 —
            // "추적은 되는데 값이 안 온다"와 "추적이 끊겼다"는 원인이 전혀 다르다.
            if (!_poseMoved && _pose.localPosition.sqrMagnitude > 1e-8f) _poseMoved = true;
            State = _poseMoved ? PoseState.Tracking : PoseState.NoPoseDevice;

            if (_poseMoved && !_loggedFirstPose)
            {
                _loggedFirstPose = true;
                Debug.Log("[ARCore] 포즈 구동 확인 — ARPoseDriver 동작. pos=" + _pose.localPosition.ToString("F3"));
            }
        }

        /// <summary>
        /// 포즈를 주는 XR 기기를 찾는다. 관례적 노드를 먼저 훑고, 실패하면 <b>전수 조사</b>한다.
        ///
        /// <para>노드를 하나씩 찍어 맞히는 건 도박이다 — 실기에서 <see cref="XRNode.CenterEye"/>가
        /// 비었는데도 세션은 "Position and Rotation 지원"을 보고했다. 그래서 실패해도 어떤 기기가
        /// 있었는지 <b>전부 로그로 남겨</b>, 다음 수를 로그 보고 정하게 한다.</para>
        /// </summary>
        private bool TryAcquireDevice()
        {
            for (int i = 0; i < ProbeNodes.Length; i++)
            {
                InputDevice d = InputDevices.GetDeviceAtXRNode(ProbeNodes[i]);
                if (d.isValid && HasPose(d))
                {
                    _device = d;
                    DeviceName = d.name;
                    Debug.Log($"[ARCore] pose device acquired at node {ProbeNodes[i]}: '{d.name}'");
                    return true;
                }
            }

            _scanBuf.Clear();
            InputDevices.GetDevices(_scanBuf);

            var sb = new System.Text.StringBuilder();
            sb.Append("[ARCore] node probe failed. devices=").Append(_scanBuf.Count);
            for (int i = 0; i < _scanBuf.Count; i++)
            {
                InputDevice d = _scanBuf[i];
                sb.Append("\n  '").Append(d.name).Append("' valid=").Append(d.isValid)
                  .Append(" chars=").Append(d.characteristics)
                  .Append(" pose=").Append(HasPose(d));
            }
            Debug.Log(sb.ToString());

            for (int i = 0; i < _scanBuf.Count; i++)
            {
                if (!HasPose(_scanBuf[i])) continue;
                _device = _scanBuf[i];
                DeviceName = _device.name;
                Debug.Log($"[ARCore] pose device acquired by scan: '{_device.name}'");
                return true;
            }
            return false;
        }

        /// <summary>이 기기가 실제로 포즈 값을 내놓는가. isValid만으로는 부족하다.</summary>
        private static bool HasPose(InputDevice d)
        {
            return d.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 _)
                || d.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 _)
                || d.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion _);
        }

        private void OnDestroy()
        {
            if (_session != null) Destroy(_session.gameObject);
            if (_arCamera != null) Destroy(_arCamera.gameObject);
        }
    }
}
