using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 수평 이동 속도 적분기 — 아이작식 가속/감속 + 지연 따라잡기.
    ///
    /// <para><b>왜 가속 램프인가</b>: 즉시 속도 방식이면 늦게 도착한 입력을 보상하려고 <b>위치를
    /// 순간이동</b>시켜야 한다(눈에 확 띈다). 램프가 있으면 같은 보상이 <b>속도를 살짝 앞당기는 것</b>으로
    /// 나타나 보이지 않는다. 즉 램프는 연출이 아니라 지연 보상을 성립시키는 전제다.</para>
    ///
    /// <para><b>따라잡기</b>: 입력이 <i>바뀐</i> 프레임에만 <c>age</c>만큼 적분을 더 돌린다.
    /// 입력이 그대로인 프레임에는 더하지 않는다 — 매 프레임 더하면 중복 보상으로 폭주한다.</para>
    ///
    /// <para><b>남는 오차</b>(정직하게): age가 들쭉날쭉하면 "출발 때 40ms 보상 / 정지 때 10ms 보상"처럼
    /// 비대칭이 생겨 위치가 조금 어긋난다. 크기는 <c>maxSpeed × 지터</c> 수준이고(예: 6m/s × 30ms ≒ 18cm),
    /// 지터가 zero-mean이라 계속 쌓이지는 않는다. <see cref="maxCatchUp"/>이 상한을 건다.</para>
    ///
    /// <para>순수 로직(MonoBehaviour 아님) → PC/VR이 같은 코드를 쓴다. PC는 age=0으로 넣으면 된다.</para>
    /// </summary>
    [System.Serializable]
    public class MoveIntegrator
    {
        [Header("속도")]
        [Tooltip("최고 수평 속도(m/s). 6 → 8.4 (1.4배).\n" +
                 "⚠️ 이 값을 바꾸면 가속·감속 시간이 같이 변한다(시간 = maxSpeed / acceleration).\n" +
                 "  손맛을 유지하려면 acceleration·deceleration도 같은 배율로 올릴 것.")]
        public float maxSpeed = 8.4f;

        [Tooltip("가속도(m/s²). 0→최고속 걸리는 시간 = maxSpeed / acceleration. 56이면 약 0.15초.")]
        public float acceleration = 56f;

        [Tooltip("감속도(m/s²). 입력을 놓았을 때. 84면 최고속→0이 약 0.1초.")]
        public float deceleration = 84f;

        [Tooltip("공중에서 가속·감속에 곱하는 배율(1=지상과 동일, 0=공중 조작 불가).")]
        [Range(0f, 1f)] public float airControl = 0.35f;

        [Header("지연 보상")]
        [Tooltip("따라잡기로 한 번에 더 돌릴 수 있는 최대 시간(초). 지터 상한보다 살짝 크게.")]
        public float maxCatchUp = 0.15f;

        [Tooltip("입력이 '바뀌었다'고 볼 최소 변화량. 아날로그 스틱 미세 떨림으로 매 프레임 보상되는 걸 막는다.")]
        public float changeThreshold = 0.1f;

        [Tooltip("기저 편도 지연 보정(초). LatencyEstimator가 알 수 없는 상수 지연 — 손감각으로 맞춘다.")]
        public float baselineCompensation = 0f;

        /// <summary>적분된 수평 속도(월드 XZ). 임펄스는 포함하지 않는다 — 실제 이동엔 <see cref="Output"/>을 쓸 것.</summary>
        public Vector2 Velocity { get; private set; }

        /// <summary>
        /// 실제 이동에 쓸 속도 = 적분 속도 + 감쇠 중인 임펄스.
        /// Precog 이단점프와 같은 모델 — 임펄스를 일반 이동에 <b>덧셈</b>해서 "힘을 받는" 느낌을 낸다.
        /// </summary>
        public Vector2 Output => Velocity + _boostDir * (_boostSpeed * BoostFactor);

        float BoostFactor => _boostDuration > 0f ? Mathf.Clamp01(_boostLeft / _boostDuration) : 0f;

        Vector2 _lastWish, _boostDir;
        float _boostSpeed, _boostLeft, _boostDuration;

        public void Reset()
        {
            Velocity = Vector2.zero;
            _lastWish = Vector2.zero;
            ClearBoost();
        }

        /// <summary>속도를 강제로 지정(자동 등반 종료·도약 등 외부 동작이 제어권을 넘길 때).</summary>
        public void SetVelocity(Vector2 v) => Velocity = v;

        /// <summary>
        /// 감쇠 임펄스 부여 — 등반 종료·도약처럼 "밀려나가는" 힘. 선형으로 0까지 줄어든다.
        /// (Precog PlayerMovement의 AirJumpBoost와 같은 모델)
        /// </summary>
        public void AddBoost(Vector2 dir, float speed, float duration)
        {
            if (dir.sqrMagnitude < 1e-6f || duration <= 0f) return;
            _boostDir = dir.normalized;
            _boostSpeed = speed;
            _boostDuration = duration;
            _boostLeft = duration;
        }

        public void ClearBoost() { _boostDir = Vector2.zero; _boostSpeed = 0f; _boostLeft = 0f; _boostDuration = 0f; }

        /// <summary>
        /// 한 프레임 전진. <paramref name="wish"/>=원하는 이동 방향(월드 XZ, 크기 0~1),
        /// <paramref name="age"/>=이 입력이 몇 초 묵었는지(PC는 0), <paramref name="grounded"/>=접지 여부.
        /// </summary>
        public void Step(Vector2 wish, float dt, bool grounded, float age = 0f)
        {
            if (_boostLeft > 0f) _boostLeft -= dt;

            float extra = 0f;
            if ((wish - _lastWish).sqrMagnitude > changeThreshold * changeThreshold)
                extra = Mathf.Clamp(age + baselineCompensation, 0f, maxCatchUp);   // 입력이 바뀐 순간에만 따라잡기
            _lastWish = wish;

            Integrate(wish, dt + extra, grounded);
        }

        void Integrate(Vector2 wish, float dt, bool grounded)
        {
            Vector2 target = wish * maxSpeed;
            float rate = wish.sqrMagnitude > 1e-6f ? acceleration : deceleration;
            if (!grounded) rate *= airControl;
            Velocity = Vector2.MoveTowards(Velocity, target, rate * dt);
        }
    }
}
