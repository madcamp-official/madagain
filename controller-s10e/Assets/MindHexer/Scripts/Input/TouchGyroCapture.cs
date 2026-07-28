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

        // 형제로 붙은 ARCore 소스. poseSource가 비어 있으면 여기서 가져온다.
        private ArcorePoseSource _arcore;

        private void Awake()
        {
            if (_sender == null) _sender = GetComponent<UdpSender>();
            if (poseSource == null) _arcore = GetComponent<ArcorePoseSource>();
        }

        /// <summary>
        /// poseSource를 늦게 채운다. ArcorePoseSource가 포즈 Transform을 Awake에서 만들긴 하지만,
        /// 컴포넌트 추가 순서에 의존하지 않도록 Update에서 한 번 더 확인한다.
        /// </summary>
        private void ResolvePoseSource()
        {
            if (poseSource != null || _arcore == null) return;
            poseSource = _arcore.Pose;
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

            // 6DoF 포즈: 트래커가 <b>실제로 추적 중일 때만</b> 쓰고, 아니면 자이로 회전 + 0 위치(3DoF 폴백).
            // ARCore가 아직 기동 중이거나 추적을 잃은 상태에서 poseSource를 그대로 읽으면 회전이
            // identity로 굳어 자이로보다 못한 값이 나간다 — 그래서 살아 있는지를 먼저 본다.
            ResolvePoseSource();

            Vector3 position;
            Quaternion rotation;
            bool poseLive = poseSource != null && (_arcore == null || _arcore.HasPosition);
            if (poseLive)
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
