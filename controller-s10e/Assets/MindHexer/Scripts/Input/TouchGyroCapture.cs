using UnityEngine;
using MindHexer.Shared.Protocol;
using MindHexer.Controller.Net;

namespace MindHexer.Controller.Input
{
    /// <summary>
    /// 멀티터치 + 6DoF 포즈 + 가속도 + 화면 기하를 캡처해 <b>매 프레임 패킷 하나</b>로 보낸다. (SPEC 4)
    ///
    /// <para><b>프레임당 1패킷인 이유</b> — v2는 터치 하나당 패킷 하나를 보냈다. 그런데 송신 측이
    /// 매 Send마다 시퀀스를 올리고 수신 측은 최고 시퀀스만 수용하므로, 한 프레임에 손가락이 둘이면
    /// 먼저 처리된 쪽이 매 프레임 조용히 버려졌다. 이제 터치를 배열에 담아 한 번에 보낸다.</para>
    ///
    /// <para><b>여기서 해석하지 않는다.</b> 조이스틱·패턴·플릭 판정은 전부 수신 측(MINDHEXER) 몫이다.
    /// 플레이 중엔 헤드셋을 쓰고 있어 컨트롤러 화면이 보이지 않으므로, 이 기기의 시각 피드백은
    /// 원리적으로 무의미하고 조작 표시는 VR 화면에 있어야 한다. 컨트롤러는 눈먼 터치패드다.</para>
    ///
    /// <para><b>화면 기하를 함께 싣는 이유</b> — 정규화 좌표만으로는 수신 측이 물리 거리(엄지 도달
    /// 범위는 mm 단위다)와 시스템이 가로채는 영역을 알 수 없어, 조작 반경을 물리적으로 맞추거나
    /// 가장자리에서 시작한 드래그에 공간이 모자란지 판단할 수 없다.</para>
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

            ResolvePoseSource();

            var p = new InputPacket
            {
                TimestampMs = (long)(Time.realtimeSinceStartupAsDouble * 1000.0),
                Acceleration = UnityEngine.Input.acceleration,
                ScreenWidth = Screen.width,
                ScreenHeight = Screen.height,
                Dpi = Screen.dpi,
                SafeArea = Screen.safeArea,
                Touch0 = TouchSample.Empty,
                Touch1 = TouchSample.Empty,
            };

            FillPose(ref p);
            FillTouches(ref p);

            _sender.Send(p);
        }

        /// <summary>
        /// 트래커가 <b>실제로 추적 중일 때만</b> 그 포즈를 쓰고, 아니면 자이로 회전 + 0 위치로 떨어진다.
        /// 기동 중이거나 추적을 잃은 상태에서 poseSource를 그대로 읽으면 회전이 identity로 굳어
        /// 자이로보다 못한 값이 나가기 때문이다. 어느 쪽인지는 <see cref="InputPacket.Tracking"/>으로
        /// 알려, 수신 측이 위치 0을 "진짜 원점"으로 오해하지 않게 한다.
        /// </summary>
        private void FillPose(ref InputPacket p)
        {
            bool poseLive = poseSource != null && (_arcore == null || _arcore.HasPosition);
            if (poseLive)
            {
                p.Position = poseSource.localPosition;
                p.Rotation = poseSource.localRotation;
                p.Tracking = TrackingStateCode.Tracking6Dof;
                return;
            }

            p.Position = Vector3.zero;
            if (SystemInfo.supportsGyroscope)
            {
                p.Rotation = UnityEngine.Input.gyro.attitude;
                p.Tracking = TrackingStateCode.GyroOnly;
            }
            else
            {
                p.Rotation = Quaternion.identity;
                p.Tracking = TrackingStateCode.None;
            }
        }

        /// <summary>
        /// 활성 터치를 슬롯에 담는다. 손가락이 <see cref="NetworkConstants.MaxTouches"/>보다 많으면
        /// 앞의 것부터 채우고 나머지는 버린다(양손 엄지가 전제라 실사용에서 넘칠 일이 없다).
        /// </summary>
        private void FillTouches(ref InputPacket p)
        {
            int touchCount = UnityEngine.Input.touchCount;
            int slot = 0;

            for (int i = 0; i < touchCount && slot < NetworkConstants.MaxTouches; i++)
            {
                Touch t = UnityEngine.Input.GetTouch(i);

                TouchPhaseCode phase;
                switch (t.phase)
                {
                    case TouchPhase.Began: phase = TouchPhaseCode.Down; break;
                    case TouchPhase.Moved:
                    case TouchPhase.Stationary: phase = TouchPhaseCode.Move; break;
                    case TouchPhase.Ended:
                    case TouchPhase.Canceled: phase = TouchPhaseCode.Up; break;
                    default: phase = TouchPhaseCode.None; break;
                }
                if (phase == TouchPhaseCode.None) continue;

                p.SetTouch(slot, new TouchSample
                {
                    Id = t.fingerId,
                    Phase = phase,
                    // 정규화(0..1). 원점 좌하단 — Unity 터치 좌표계 그대로.
                    Normalized = new Vector2(t.position.x / Screen.width, t.position.y / Screen.height),
                });
                slot++;
            }

            p.TouchCount = slot;
        }
    }
}
