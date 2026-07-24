using UnityEngine;

namespace MindHexer.Shared.Protocol
{
    /// <summary>터치 상태. UDP 와이어에서는 uint8로 인코딩.</summary>
    public enum TouchPhaseCode : byte
    {
        None = 0,
        Down = 1,
        Move = 2,
        Up = 3
    }

    /// <summary>
    /// S10e → S24+ 로 초당 수십~수백 회 스트리밍되는 입력 상태값. (SPEC 4.2)
    /// 고정 길이 60바이트로 직렬화된다 → <see cref="PacketSerializer"/> 참조.
    /// </summary>
    public struct InputPacket
    {
        public uint Sequence;              // 단조 증가. 역전/중복 폐기 기준.
        public long TimestampMs;           // 송신측 기준 상대 시각(ms).
        public int TouchId;                // 멀티터치 식별자.
        public TouchPhaseCode Phase;       // Down/Move/Up.
        public Vector2 NormalizedPos;      // 정규화 좌표 (0..1).
        public Quaternion GyroRotation;    // 자이로 회전(해킹 보조용, 헤드트래킹 아님 — SPEC 5.5).
        public Vector3 Acceleration;       // 가속도 벡터.

        public override string ToString()
        {
            return $"InputPacket(seq={Sequence}, t={TimestampMs}ms, touch#{TouchId} {Phase}, " +
                   $"pos={NormalizedPos}, gyro={GyroRotation.eulerAngles}, acc={Acceleration})";
        }
    }
}
