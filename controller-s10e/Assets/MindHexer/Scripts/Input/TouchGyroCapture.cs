using UnityEngine;
using MindHexer.Shared.Protocol;
using MindHexer.Shared.Input;
using MindHexer.Controller.Net;

namespace MindHexer.Controller.Input
{
    /// <summary>
    /// 멀티터치 + 6DoF 포즈(위치+회전) + 가속도를 캡처해 매 프레임 <see cref="UdpSender"/>로 스트리밍한다. (SPEC 4)
    /// 화면 좌표는 0..1로 정규화(해상도 독립).
    ///
    /// 6DoF 위치는 IMU 단독으로는 드리프트가 커서 얻을 수 없으므로 외부 트래커(ARCore VIO 등)가
    /// 갱신하는 <see cref="poseSource"/> Transform에서 읽는다. 미할당 시 회전만 자이로로 채우고
    /// 위치는 0(=3DoF 폴백)으로 보낸다.
    ///
    /// TODO(담당자 B):
    ///  - ARCore(AR Foundation) 세션을 붙여 poseSource를 카메라/디바이스 포즈로 구동.
    ///  - Input System EnhancedTouch로 멀티터치/스와이프 궤적 정밀 캡처, Down/Move/Up 분류.
    ///  - 타임스탬프 기준(단조 시계) 확정. 여기서는 Time.realtimeSinceStartupAsDouble 사용.
    /// </summary>
    public sealed class TouchGyroCapture : MonoBehaviour
    {
        [SerializeField] private UdpSender _sender;
        [SerializeField] private FloatingJoystickInput _joystick;

        [Tooltip("6DoF 트래커(ARCore 등)가 갱신하는 디바이스 포즈. 할당되면 정확한 위치/회전을 그대로 사용.")]
        [SerializeField] private Transform poseSource;

        [Header("IMU 위치 폴백 (poseSource 미할당 시)")]
        [Tooltip("ARCore 없이 IMU 선형가속도 적분으로 위치를 추정(드리프트 있음, 브링업용). 끄면 위치 0.")]
        [SerializeField] private bool useImuPositionFallback = true;
        [SerializeField] private float imuVelocityDamping = 3.0f;
        [SerializeField] private float imuAccelDeadZone = 0.30f;

        private const float G = 9.81f;
        private readonly ImuPositionEstimator _imu = new ImuPositionEstimator();

        private void Awake()
        {
            if (_sender == null) _sender = GetComponent<UdpSender>();
            if (_joystick == null) _joystick = GetComponent<FloatingJoystickInput>();
            _imu.VelocityDamping = imuVelocityDamping;
            _imu.AccelDeadZone = imuAccelDeadZone;
        }

        private void Start()
        {
            if (SystemInfo.supportsGyroscope)
                UnityEngine.Input.gyro.enabled = true;
        }

        private void Update()
        {
            if (_sender == null) return;

            long ts = (long)(Time.realtimeSinceStartupAsDouble * 1000.0);
            Vector3 accel = UnityEngine.Input.acceleration;
            Vector2 move = _joystick != null ? _joystick.MoveAxis : Vector2.zero; // 조이스틱 이동축

            // 6DoF 포즈: 트래커(poseSource)가 있으면 그대로. 없으면 자이로 회전 + IMU 적분 위치(폴백).
            Vector3 position;
            Quaternion rotation;
            if (poseSource != null)
            {
                position = poseSource.localPosition;
                rotation = poseSource.localRotation;
            }
            else
            {
                rotation = SystemInfo.supportsGyroscope
                    ? UnityEngine.Input.gyro.attitude
                    : Quaternion.identity;

                if (useImuPositionFallback && SystemInfo.supportsGyroscope)
                {
                    // userAcceleration: 중력 제거된 선형가속도(단위 g) → m/s²로 변환 후 이중 적분.
                    Vector3 ua = UnityEngine.Input.gyro.userAcceleration;
                    _imu.Integrate(new Vector3(ua.x * G, ua.y * G, ua.z * G), Time.deltaTime);
                    position = _imu.Position;
                }
                else
                {
                    position = Vector3.zero;
                }
            }

            int touchCount = UnityEngine.Input.touchCount;
            if (touchCount == 0)
            {
                // 터치가 없어도 6DoF 포즈 + 조이스틱 값은 계속 흘려보낸다(이동 유지).
                _sender.Send(TouchPhaseCode.None, -1, Vector2.zero, position, rotation, accel, move, ts);
                return;
            }

            for (int i = 0; i < touchCount; i++)
            {
                Touch t = UnityEngine.Input.GetTouch(i);
                Vector2 norm = new Vector2(
                    t.position.x / Screen.width,
                    t.position.y / Screen.height);

                var phase = t.phase switch
                {
                    TouchPhase.Began => TouchPhaseCode.Down,
                    TouchPhase.Moved => TouchPhaseCode.Move,
                    TouchPhase.Stationary => TouchPhaseCode.Move,
                    TouchPhase.Ended => TouchPhaseCode.Up,
                    TouchPhase.Canceled => TouchPhaseCode.Up,
                    _ => TouchPhaseCode.None
                };

                _sender.Send(phase, t.fingerId, norm, position, rotation, accel, move, ts);
            }
        }
    }
}
