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
    /// 포즈의 신뢰도. 수신 측이 <see cref="InputPacket.Position"/>을 믿어도 되는지 판단하는 근거다.
    /// 이게 없으면 추적 실패로 0이 온 것을 "진짜 원점"으로 오해한다.
    /// </summary>
    public enum TrackingStateCode : byte
    {
        None = 0,          // 포즈 없음(AR 미지원·권한 거부 등). 위치·회전 모두 무의미.
        GyroOnly = 1,      // 3DoF 폴백 — 회전만 유효, 위치는 항상 0.
        Tracking6Dof = 2,  // 위치·회전 모두 유효.
    }

    /// <summary>터치 한 점. 좌표는 화면 정규화(0..1), 원점 좌하단.</summary>
    public struct TouchSample
    {
        public int Id;                 // 멀티터치 식별자. 없으면 -1.
        public TouchPhaseCode Phase;
        public Vector2 Normalized;

        public bool IsActive { get { return Phase != TouchPhaseCode.None; } }

        public static TouchSample Empty
        {
            get { return new TouchSample { Id = -1, Phase = TouchPhaseCode.None }; }
        }
    }

    /// <summary>
    /// S10e → S24+ 로 <b>프레임당 한 번</b> 스트리밍되는 입력 상태. (SPEC 4.2)
    /// 고정 길이 <see cref="NetworkConstants.InputPacketSize"/>바이트 → <see cref="PacketSerializer"/> 참조.
    ///
    /// <para><b>v3에서 바뀐 것</b>
    /// <list type="bullet">
    /// <item>터치를 <b>배열</b>로 싣는다. v2는 터치 하나당 패킷 하나를 보냈는데, 송신 측이 매 Send마다
    /// 시퀀스를 올리고 수신 측은 최고 시퀀스만 수용하므로 <b>한 프레임에 손가락이 둘이면 먼저 처리된
    /// 쪽이 조용히 버려졌다</b>. 이제 시퀀스는 프레임당 1씩 오른다.</item>
    /// <item><see cref="SessionId"/> — 앱을 재시작하면 <see cref="TimestampMs"/>가 0으로 돌아가
    /// 수신 측 지연 추정이 음수로 튄다. 이 값이 바뀌면 추정을 리셋하면 된다.</item>
    /// <item><see cref="Tracking"/> — 위 <see cref="TrackingStateCode"/> 참조.</item>
    /// <item>화면 기하(<see cref="ScreenWidth"/>·<see cref="Dpi"/>·<see cref="SafeArea"/>) — 정규화
    /// 좌표만으로는 <b>물리 거리</b>(엄지 도달 범위는 픽셀이 아니라 mm 단위다)와 <b>시스템이 가로채는
    /// 영역</b>을 알 수 없다. 수신 측이 조작 반경을 물리적으로 맞추고, 가장자리에서 시작한 드래그에
    /// 공간이 모자란지 판단하려면 필요하다.</item>
    /// </list></para>
    ///
    /// <para>헤드트래킹(시점)은 S24+ 자체 센서 전담이며, 이 포즈는 컨트롤러 입력용이다(SPEC 5.5).</para>
    /// </summary>
    public struct InputPacket
    {
        // ── 식별 ──────────────────────────────────────────────────────────
        public uint SessionId;             // 앱 부팅마다 새로 뽑는다. 재시작 감지용.
        public uint Sequence;              // 프레임당 1 증가. 역전/중복 폐기 기준.
        public long TimestampMs;           // 송신측 단조 시계(ms). 시계 동기화는 필요 없다.

        // ── 포즈 ──────────────────────────────────────────────────────────
        public TrackingStateCode Tracking;
        public Vector3 Position;           // 6DoF 위치(ARCore 세션 좌표, meter). 리센터는 수신 측이 한다.
        public Quaternion Rotation;        // 디바이스 자세.
        public Vector3 Acceleration;       // 선형 가속도. 데드레커닝/예측 보정용.

        // ── 화면 기하 (기기마다 고정이지만 매 패킷에 싣는다) ──────────────
        // 별도 핸드셰이크 패킷으로 빼면 그게 유실됐을 때 수신 측이 영영 모르는 상태가 된다.
        // 십수 바이트라 60Hz로 보내도 무시할 만하다.
        public int ScreenWidth;
        public int ScreenHeight;
        public float Dpi;
        public Rect SafeArea;              // 픽셀 단위. 노치·제스처바를 뺀 실제 사용 가능 영역.

        // ── 터치 ──────────────────────────────────────────────────────────
        public int TouchCount;             // 유효 슬롯 수(0..NetworkConstants.MaxTouches).
        public TouchSample Touch0;
        public TouchSample Touch1;

        /// <summary>슬롯 접근. 범위를 벗어나면 <see cref="TouchSample.Empty"/>.</summary>
        public TouchSample GetTouch(int index)
        {
            if (index == 0) return Touch0;
            if (index == 1) return Touch1;
            return TouchSample.Empty;
        }

        public void SetTouch(int index, TouchSample t)
        {
            if (index == 0) Touch0 = t;
            else if (index == 1) Touch1 = t;
        }

        // ── v2 호환 접근자 ────────────────────────────────────────────────
        // headset-s24의 기존 코드(InputBridge 등)가 단일 터치를 전제로 쓴다.
        public int TouchId { get { return Touch0.Id; } }
        public TouchPhaseCode Phase { get { return Touch0.Phase; } }
        public Vector2 NormalizedPos { get { return Touch0.Normalized; } }

        /// <summary>위치를 신뢰해도 되는가.</summary>
        public bool HasPosition { get { return Tracking == TrackingStateCode.Tracking6Dof; } }

        public override string ToString()
        {
            return string.Format(
                "InputPacket(sess={0}, seq={1}, t={2}ms, track={3}, pos={4}, rot={5}, touches={6})",
                SessionId, Sequence, TimestampMs, Tracking, Position, Rotation.eulerAngles, TouchCount);
        }
    }
}
