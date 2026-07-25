using UnityEngine;
using MindHexer.Shared.Protocol;
using MindHexer.Headset.Net;

namespace MindHexer.Headset.Input
{
    /// <summary>
    /// 수신된 6DoF <see cref="InputPacket"/>을 게임 입력으로 변환한다. (SPEC 3.3)
    ///  - 터치 정규화 좌표: Lerp 보간 → 3x3 해킹 그리드 좌표계 매핑
    ///  - 6DoF 위치: Lerp 보간 (Vector3)
    ///  - 6DoF 회전: Slerp 보간 (Quaternion) → 조준/동적 해킹 조작
    /// 위치·회전 함께 보간해 패킷 유실 시에도 부드러운 6DoF 포즈를 유지한다.
    /// 헤드트래킹(시점)은 S24+ 자체 센서 전담이며, 이 포즈는 컨트롤러 입력용(SPEC 5.5).
    ///
    /// TODO(담당자 A, 4일차): 그리드 매핑/조준 레이 연결, 지터 버퍼 튜닝.
    /// </summary>
    public sealed class InputBridge : MonoBehaviour
    {
        [SerializeField] private UdpReceiver _receiver;

        [Tooltip("좌표/위치 Lerp 보간 계수(프레임당). 값이 클수록 반응 빠르고 끊김 큼.")]
        [Range(0.01f, 1f)] public float PositionLerp = 0.35f;

        [Tooltip("회전 Slerp 보간 계수(프레임당).")]
        [Range(0.01f, 1f)] public float RotationSlerp = 0.35f;

        [Tooltip("이동축(조이스틱) Lerp 보간 계수(프레임당).")]
        [Range(0.05f, 1f)] public float MoveLerp = 0.5f;

        private Vector2 _smoothedUv;
        private Vector3 _smoothedPos;
        private Quaternion _smoothedRot = Quaternion.identity;
        private Vector2 _smoothedMove;

        /// <summary>보간된 정규화 좌표(0..1). 그리드 매핑 입력.</summary>
        public Vector2 SmoothedNormalizedPos => _smoothedUv;

        /// <summary>보간된 6DoF 위치.</summary>
        public Vector3 SmoothedPosition => _smoothedPos;

        /// <summary>보간된 6DoF 회전.</summary>
        public Quaternion SmoothedRotation => _smoothedRot;

        /// <summary>보간된 조이스틱 이동축(-1..1 디스크). 캐릭터 이동 입력.</summary>
        public Vector2 MoveAxis => _smoothedMove;

        private void Awake()
        {
            if (_receiver == null) _receiver = GetComponent<UdpReceiver>();
        }

        private void Update()
        {
            if (_receiver == null) return;

            if (_receiver.IsTimedOut)
            {
                // TODO(SPEC 5.1): UI 경고 표시 + WebSocket 재연결 시도 트리거.
                return;
            }

            if (!_receiver.TryGetLatest(out var p)) return;

            _smoothedUv = Vector2.Lerp(_smoothedUv, p.NormalizedPos, PositionLerp);
            _smoothedPos = Vector3.Lerp(_smoothedPos, p.Position, PositionLerp);
            _smoothedRot = Quaternion.Slerp(_smoothedRot, p.Rotation, RotationSlerp);
            _smoothedMove = Vector2.Lerp(_smoothedMove, p.MoveAxis, MoveLerp);

            // TODO: _smoothedMove → 캐릭터 이동, _smoothedPos/_smoothedRot → 조준 레이 발행.
        }
    }
}
