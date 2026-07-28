using UnityEngine;
using MindHexer.Shared.Protocol;
using MindHexer.Shared.Net;
using MindHexer.Headset.Net;

namespace MindHexer.Headset.Input
{
    /// <summary>
    /// 수신된 6DoF <see cref="InputPacket"/> 스트림을 **지터 버퍼**(<see cref="JitterBuffer"/>)로 흘려
    /// 재생 지연만큼 뒤처진 시점에서 시간 보간(위치 Lerp / 회전 Slerp)한 부드러운 포즈를 게임 입력으로 낸다. (SPEC 2.1/5)
    ///
    /// 이전엔 매 프레임 최신값을 고정계수로 Lerp만 했으나, 그건 지터/유실에 취약했다. 이제:
    ///  - <see cref="UdpReceiver.Drain"/>로 프레임 사이 도착한 **모든** 패킷을 버퍼에 넣고
    ///  - 버퍼가 (newest - 적응지연) 시점에서 두 샘플을 시간 보간한다.
    /// 헤드트래킹(시점)은 S24+ 자체 센서 전담이며, 이 포즈는 컨트롤러 입력용(SPEC 5.5).
    /// </summary>
    public sealed class InputBridge : MonoBehaviour
    {
        [SerializeField] private UdpReceiver _receiver;

        [Header("지터 버퍼 튜닝")]
        [Tooltip("기본 재생 지연(ms). 클수록 부드럽지만 지연↑. 적응 시 하한으로 작동.")]
        [Range(0f, 250f)] public float TargetDelayMs = 60f;
        [Tooltip("관측 간격·지터에 맞춰 지연 자동 조정.")]
        public bool Adaptive = true;
        [Tooltip("재생 커서가 목표로 수렴하는 속도(1/초).")]
        [Range(1f, 20f)] public float CatchupRate = 8f;
        [Range(0f, 200f)] public float MinDelayMs = 30f;
        [Range(50f, 500f)] public float MaxDelayMs = 250f;

        [Header("지연 보정(예측 외삽)")]
        [Tooltip("컨트롤러 송신 시각으로 패킷 나이를 추정해 최신 포즈를 속도 외삽 → 전송 지연 우회.")]
        public bool LatencyCompensation = true;
        [Tooltip("편도 전송 지연만큼 추가로 앞서 예측(ms). 대략 RTT/2. 0이면 시계 지터만 상쇄.")]
        [Range(0f, 120f)] public float PredictAheadMs = 40f;
        [Tooltip("최신 샘플 이후 외삽 최대 시간(ms). 오버슛/노이즈 증폭 방지 상한.")]
        [Range(0f, 200f)] public float MaxExtrapolationMs = 120f;

        private readonly JitterBuffer _buffer = new JitterBuffer();
        private bool _wasTimedOut;

        private Vector2 _smoothedUv;
        private Vector3 _smoothedPos;
        private Quaternion _smoothedRot = Quaternion.identity;
        private Vector2 _smoothedMove;

        /// <summary>보간된 정규화 좌표(0..1).</summary>
        public Vector2 SmoothedNormalizedPos => _smoothedUv;
        /// <summary>보간된 6DoF 위치.</summary>
        public Vector3 SmoothedPosition => _smoothedPos;
        /// <summary>보간된 6DoF 회전.</summary>
        public Quaternion SmoothedRotation => _smoothedRot;
        /// <summary>보간된 조이스틱 이동축(-1..1).</summary>
        public Vector2 MoveAxis => _smoothedMove;

        // 디버그/HUD용
        public float BufferDelayMs => _buffer.CurrentDelayMs;
        public double JitterMs => _buffer.JitterMs;
        public double IntervalMs => _buffer.IntervalMs;
        public int BufferedSamples => _buffer.Count;
        /// <summary>지연 보정으로 최신 샘플 대비 앞서 예측 중인 양(ms).</summary>
        public double PredictLeadMs => _buffer.LastLeadMs;
        /// <summary>시계 오프셋 추정 확보 여부(보정 활성 조건).</summary>
        public bool ClockLocked => _buffer.HasClock;

        private void Awake()
        {
            if (_receiver == null) _receiver = GetComponent<UdpReceiver>();
        }

        private void Update()
        {
            if (_receiver == null) return;

            // 인스펙터 튜닝을 버퍼에 반영.
            _buffer.TargetDelayMs = TargetDelayMs;
            _buffer.Adaptive = Adaptive;
            _buffer.CatchupRate = CatchupRate;
            _buffer.MinDelayMs = MinDelayMs;
            _buffer.MaxDelayMs = MaxDelayMs;
            _buffer.LatencyCompensation = LatencyCompensation;
            _buffer.PredictAheadMs = PredictAheadMs;
            _buffer.MaxExtrapolationMs = MaxExtrapolationMs;

            if (_receiver.IsTimedOut)
            {
                // 연결 끊김: 큰 시간 간극을 넘어 보간하지 않도록 1회 리셋하고 마지막 포즈 유지.
                if (!_wasTimedOut) { _buffer.Reset(); _wasTimedOut = true; }
                return;
            }
            _wasTimedOut = false;

            long nowMs = (long)(Time.realtimeSinceStartupAsDouble * 1000.0);
            _receiver.Drain(p => _buffer.Push(p, nowMs)); // 프레임 사이 도착분 전부 적재

            _buffer.Advance(Time.deltaTime * 1000.0); // 폴백(_playbackTs) 유지용

            // 지연 보정: 컨트롤러 송신 시각으로 예측 외삽. 시계 미확보 시 내부에서 지연 재생으로 폴백.
            if (_buffer.SampleCompensated(nowMs, out var s))
            {
                _smoothedUv = s.NormalizedPos;
                _smoothedPos = s.Position;
                _smoothedRot = s.Rotation;
                _smoothedMove = s.MoveAxis;
                // TODO: _smoothedMove → 캐릭터 이동, _smoothedPos/_smoothedRot → 조준 레이.
            }
        }
    }
}
