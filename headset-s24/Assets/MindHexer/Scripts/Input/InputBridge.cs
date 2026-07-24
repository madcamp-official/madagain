using UnityEngine;
using MindHexer.Shared.Protocol;
using MindHexer.Headset.Net;

namespace MindHexer.Headset.Input
{
    /// <summary>
    /// 수신된 <see cref="InputPacket"/>을 게임 입력으로 변환한다. (SPEC 3.3)
    ///  - 터치 정규화 좌표: Lerp 보간 → 3x3 해킹 그리드 좌표계 매핑
    ///  - 자이로 회전값: Slerp 보간 → 해킹 보조 연출 (헤드트래킹 아님 — SPEC 5.5)
    ///
    /// TODO(담당자 A, 4일차): 그리드 매핑/패턴 입력 이벤트 연결, 지터 버퍼 튜닝.
    /// </summary>
    public sealed class InputBridge : MonoBehaviour
    {
        [SerializeField] private UdpReceiver _receiver;

        [Tooltip("좌표 Lerp 보간 계수(프레임당). 값이 클수록 반응 빠르고 끊김 큼.")]
        [Range(0.01f, 1f)] public float PositionLerp = 0.35f;

        [Tooltip("자이로 Slerp 보간 계수(프레임당).")]
        [Range(0.01f, 1f)] public float RotationSlerp = 0.35f;

        private Vector2 _smoothedPos;
        private Quaternion _smoothedRot = Quaternion.identity;

        /// <summary>보간된 정규화 좌표(0..1). 그리드 매핑 입력.</summary>
        public Vector2 SmoothedNormalizedPos => _smoothedPos;

        /// <summary>보간된 보조 자이로 회전.</summary>
        public Quaternion SmoothedGyro => _smoothedRot;

        private void Update()
        {
            if (_receiver == null) return;

            if (_receiver.IsTimedOut)
            {
                // TODO(SPEC 5.1): UI 경고 표시 + WebSocket 재연결 시도 트리거.
                return;
            }

            if (!_receiver.TryGetLatest(out var p)) return;

            _smoothedPos = Vector2.Lerp(_smoothedPos, p.NormalizedPos, PositionLerp);
            _smoothedRot = Quaternion.Slerp(_smoothedRot, p.GyroRotation, RotationSlerp);

            // TODO: _smoothedPos → 3x3 그리드 셀 매핑 → HackGrid에 입력 이벤트 발행.
        }
    }
}
