using UnityEngine;
using MindHexer.Shared.Protocol;
using MindHexer.Controller.Net;

namespace MindHexer.Controller.Input
{
    /// <summary>
    /// 멀티터치 + 자이로/가속도를 캡처해 매 프레임 <see cref="UdpSender"/>로 스트리밍한다. (SPEC 4)
    /// 화면 좌표는 0..1로 정규화해서 보낸다(해상도 독립).
    ///
    /// TODO(담당자 B, 2일차):
    ///  - Input System EnhancedTouch로 멀티터치/스와이프 궤적 정밀 캡처
    ///  - Down/Move/Up phase 정확히 분류, 터치별 touchId 유지
    ///  - 타임스탬프 기준(단조 시계) 확정. 여기서는 Time.realtimeSinceStartup 사용.
    /// </summary>
    public sealed class TouchGyroCapture : MonoBehaviour
    {
        [SerializeField] private UdpSender _sender;

        private void Start()
        {
            if (SystemInfo.supportsGyroscope)
                UnityEngine.Input.gyro.enabled = true;
        }

        private void Update()
        {
            if (_sender == null) return;

            long ts = (long)(Time.realtimeSinceStartupAsDouble * 1000.0);
            Quaternion gyro = SystemInfo.supportsGyroscope
                ? UnityEngine.Input.gyro.attitude
                : Quaternion.identity;
            Vector3 accel = UnityEngine.Input.acceleration;

            int touchCount = UnityEngine.Input.touchCount;
            if (touchCount == 0)
            {
                // 터치 없음: 자이로/가속도만 흘려보내려면 아래 주석 해제.
                // _sender.Send(TouchPhaseCode.None, -1, Vector2.zero, gyro, accel, ts);
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

                _sender.Send(phase, t.fingerId, norm, gyro, accel, ts);
            }
        }
    }
}
