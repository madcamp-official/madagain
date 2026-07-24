using UnityEngine;
using MindHexer.Shared.Protocol;
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

        [Tooltip("6DoF 트래커(ARCore 등)가 갱신하는 디바이스 포즈. 미할당 시 자이로 회전만 사용(3DoF 폴백).")]
        [SerializeField] private Transform poseSource;

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

            // 6DoF 포즈: 트래커가 있으면 위치+회전을, 없으면 자이로 회전 + 0 위치(3DoF 폴백).
            Vector3 position;
            Quaternion rotation;
            if (poseSource != null)
            {
                position = poseSource.localPosition;
                rotation = poseSource.localRotation;
            }
            else
            {
                position = Vector3.zero;
                rotation = SystemInfo.supportsGyroscope
                    ? UnityEngine.Input.gyro.attitude
                    : Quaternion.identity;
            }

            int touchCount = UnityEngine.Input.touchCount;
            if (touchCount == 0)
            {
                // 터치가 없어도 6DoF 포즈는 계속 흘려보낸다(동적 인식 유지).
                _sender.Send(TouchPhaseCode.None, -1, Vector2.zero, position, rotation, accel, ts);
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

                _sender.Send(phase, t.fingerId, norm, position, rotation, accel, ts);
            }
        }
    }
}
