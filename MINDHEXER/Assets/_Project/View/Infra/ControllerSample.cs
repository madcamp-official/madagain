using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 컨트롤러(S10e)에서 오는 <b>연속 입력</b>의 한 시점 스냅샷. 네트워크로 전달되는 원시 상태다.
    ///
    /// <para>머리 시점(S24+ 자이로)은 <b>로컬</b>이라 이 경로를 타지 않는다 — 지연 가리기는
    /// <b>컨트롤러 입력에만</b> 적용된다(네트워크를 타는 건 이쪽뿐).</para>
    ///
    /// <para>이산(discrete) 값인 <see cref="touchPhase"/>는 보간하지 않는다 — 구간을 지배하는
    /// 쪽(더 이른 샘플)의 값을 그대로 취한다. 연속 값(위치·회전·터치좌표)만 Lerp/Slerp 한다.</para>
    /// </summary>
    public struct ControllerSample
    {
        public Vector3 position;     // 6DoF 위치(ARCore VIO). 미지원/미전송이면 항상 zero.
        public Quaternion rotation;  // 자이로 회전.
        public Vector2 touchNorm;    // 정규화 터치 좌표(0..1).
        public int touchPhase;       // 0=None 1=Down 2=Move 3=Up.

        public static ControllerSample Identity
        {
            get { return new ControllerSample { rotation = Quaternion.identity }; }
        }

        /// <summary>두 샘플 사이 보간. 위치·터치=Lerp, 회전=Slerp. touchPhase는 구간 지배값(a).</summary>
        public static ControllerSample Lerp(in ControllerSample a, in ControllerSample b, float t)
        {
            return new ControllerSample
            {
                position   = Vector3.Lerp(a.position, b.position, t),
                rotation   = Quaternion.Slerp(a.rotation, b.rotation, t),
                touchNorm  = Vector2.Lerp(a.touchNorm, b.touchNorm, t),
                touchPhase = a.touchPhase,   // 이산값은 renderTime을 지배하는 이른 샘플 값을 유지
            };
        }

        /// <summary>
        /// 최신 샘플 <paramref name="b"/> 이후로 (b-a) 추세를 <paramref name="k"/>배 연장(dead-reckoning).
        /// 버퍼가 말라 미래를 그려야 할 때 유실 구간을 메운다.
        /// </summary>
        public static ControllerSample Extrapolate(in ControllerSample a, in ControllerSample b, float k)
        {
            Quaternion delta = b.rotation * Quaternion.Inverse(a.rotation);   // a→b 각속도
            return new ControllerSample
            {
                position   = b.position + (b.position - a.position) * k,
                rotation   = ScaleRotation(delta, k) * b.rotation,
                touchNorm  = b.touchNorm + (b.touchNorm - a.touchNorm) * k,
                touchPhase = b.touchPhase,
            };
        }

        /// <summary>회전 델타를 k배(구면상 선형 연장). q^k = 같은 축으로 각도만 k배.</summary>
        static Quaternion ScaleRotation(Quaternion delta, float k)
        {
            delta.ToAngleAxis(out float ang, out Vector3 axis);
            if (float.IsInfinity(axis.x) || float.IsNaN(axis.x)) return Quaternion.identity;
            if (ang > 180f) ang -= 360f;   // 최단 경로
            return Quaternion.AngleAxis(ang * k, axis);
        }
    }
}
