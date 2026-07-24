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
    /// 고정 길이 72바이트로 직렬화된다 → <see cref="PacketSerializer"/> 참조.
    ///
    /// v2(6DoF): 컨트롤러가 회전(<see cref="Rotation"/>)뿐 아니라 위치(<see cref="Position"/>)까지
    /// 전달해 3DoF → 6DoF 포즈로 확장. 위치 산출원(ARCore VIO 등)은 컨트롤러 앱이 담당하고,
    /// 여기서는 그 결과 포즈만 실어 나른다. (헤드트래킹은 여전히 S24+ 자체 센서 전담 — SPEC 5.5)
    /// </summary>
    public struct InputPacket
    {
        public uint Sequence;              // 단조 증가. 역전/중복 폐기 기준.
        public long TimestampMs;           // 송신측 기준 상대 시각(ms).
        public int TouchId;                // 멀티터치 식별자.
        public TouchPhaseCode Phase;       // Down/Move/Up.
        public Vector2 NormalizedPos;      // 터치 정규화 좌표 (0..1).
        public Vector3 Position;           // 6DoF 위치(컨트롤러 로컬 원점 기준, meter).
        public Quaternion Rotation;        // 6DoF 회전(디바이스 자세). 해킹 조작/조준 등 동적 입력에 사용.
        public Vector3 Acceleration;       // 선형 가속도. 데드레커닝/예측 보정용.

        public override string ToString()
        {
            return $"InputPacket(seq={Sequence}, t={TimestampMs}ms, touch#{TouchId} {Phase}, " +
                   $"uv={NormalizedPos}, pos={Position}, rot={Rotation.eulerAngles}, acc={Acceleration})";
        }
    }
}
