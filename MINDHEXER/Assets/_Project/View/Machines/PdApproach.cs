namespace Game.View
{
    /// <summary>
    /// 스프링-댐퍼 1축 접근기 — 목표값(<see cref="Target"/>)으로 현재값(<see cref="Value"/>)이
    /// 부드럽게 다가간다. 기초_설계안 §6.2 "구동 = PD 제어"의 실체.
    ///
    /// <para>어떤 dt·omega 조합에도 안정적인 <b>암시적(semi-implicit) 스프링-댐퍼</b> 공식을 쓴다
    /// (Rory Driscoll, "Damped Springs" — 게임에서 널리 쓰는 표준 적분식). Euler 적분과 달리
    /// omega가 크거나 프레임이 길어도 발산하지 않는다.</para>
    ///
    /// <para><see cref="damping"/>=1(임계감쇠)이면 오버슈트 없이 묵직하게 도달 — 유압프레스.
    /// &lt;1이면 도달 후 살짝 튕기고 멈춤 — 피스톤 플릭의 "철컥" 느낌에 쓸 수 있다.
    /// &gt;1이면 느리고 둔하게 도달.</para>
    /// </summary>
    [System.Serializable]
    public class PdApproach
    {
        /// <summary>고유 각진동수(rad/s). 클수록 빨리 반응. "묵직함"은 이걸 낮게, damping을 1로.</summary>
        public float frequency = 6f;

        /// <summary>감쇠비. 1=임계감쇠(오버슈트 없음), &lt;1=오버슈트 후 정착, &gt;1=둔함.</summary>
        public float damping = 1f;

        public float Value { get; private set; }
        public float Velocity { get; private set; }
        public float Target { get; set; }

        public void SnapTo(float v) { Value = v; Target = v; Velocity = 0f; }

        public void Step(float dt)
        {
            if (dt <= 0f) return;
            float omega = frequency;
            float zeta = damping;

            float f = 1f + 2f * dt * zeta * omega;
            float oo = omega * omega;
            float hoo = dt * oo;
            float hhoo = dt * hoo;
            float detInv = 1f / (f + hhoo);

            float detX = f * Value + dt * Velocity + hhoo * Target;
            float detV = Velocity + hoo * (Target - Value);

            Value = detX * detInv;
            Velocity = detV * detInv;
        }
    }
}
