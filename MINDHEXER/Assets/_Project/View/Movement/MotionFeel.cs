using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 절차적 카메라 연출 레이어 — 동작 종류별로 이벤트가 갈라져 있다:
    ///  · <see cref="OnJumpLaunch"/> — 도약 발구름: 아래로 훅 가라앉았다 풀리는 다운킥. 도약 높이 비례.
    ///  · <see cref="OnLand"/> — 순수 점프/낙하 착지: <b>착지 순간 수직 속도</b> 비례 침하.
    ///    "얼마나 높이 올라갔나"가 아니라 "얼마나 빨리 떨어지고 있었나"라서, 높은 곳에
    ///    올라갔다고 큰 흔들림이 나오는 일이 없다.
    ///  · <see cref="OnMantleFinish"/> — 잡고 올라가기 완료: 높이 무관 고정 소량의 안착.
    ///  · 당김 롤 스웨이 — 한 팔씩 번갈아 당기는 좌우 교차 기울임. AutoTraversal이 구동.
    ///
    /// <para>적용 방식: 위치 오프셋은 LateUpdate에서 직전 프레임 적용분을 되돌리고 새로 더한다
    /// (CharacterController와 안 싸움). 롤은 이 컴포넌트가 회전을 건드리지 않고
    /// <see cref="CurrentRoll"/>만 계산 — FirstPersonPlayer가 시점 회전을 쓸 때 합성한다.</para>
    ///
    /// <para><b>VR</b>: 인위적 롤은 멀미 유발 1순위라 <see cref="vrRollScale"/> 기본 0.
    /// 위치 성분도 <see cref="vrPositionScale"/>로 축소. 실기에서 견딜 만하면 올린다.</para>
    /// </summary>
    [DefaultExecutionOrder(-100)]   // 오프셋 되돌리기가 모든 위치 구동자보다 먼저 돌아야 한다
    public class MotionFeel : MonoBehaviour
    {
        [Header("점프 발구름(다운킥)")]
        [Tooltip("도약 높이 1m당 침하 깊이(m).")]
        public float launchDipPerMeter = 0.05f;
        public float launchDipMax = 0.12f;
        public float launchDuration = 0.18f;
        public AnimationCurve launchCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 4f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0f, -1.5f, 0f));

        [Header("착지 (강도 = 착지 순간 낙하 속도)")]
        [Tooltip("낙하 속도 1m/s당 침하 깊이(m).")]
        public float landDipPerSpeed = 0.012f;
        public float landDipMax = 0.22f;
        [Tooltip("이 속도(m/s) 미만의 착지는 연출 없음(계단 내려오기 등).")]
        public float landMinSpeed = 3.5f;
        public float landDuration = 0.3f;
        public AnimationCurve landCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 5f), new Keyframe(0.25f, 1f), new Keyframe(0.6f, 0.12f), new Keyframe(1f, 0f));

        [Header("잡고 올라가기 안착 (높이 무관 고정)")]
        public float settleDip = 0.05f;
        public float settleDuration = 0.22f;
        public AnimationCurve settleCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 4f), new Keyframe(0.4f, 1f), new Keyframe(1f, 0f, -1f, 0f));

        [Header("VR 감쇠")]
        [Tooltip("VR에서 위치 오프셋에 곱하는 배율.")]
        [Range(0f, 1f)] public float vrPositionScale = 0.25f;
        [Tooltip("VR에서 롤에 곱하는 배율. 인위적 롤 = 멀미 1순위라 기본 0.")]
        [Range(0f, 1f)] public float vrRollScale = 0f;

        /// <summary>이번 프레임의 롤(도). FirstPersonPlayer가 시점 회전에 합성한다.</summary>
        public float CurrentRoll { get; private set; }

        struct Fx { public bool active; public float amp, dur, t; }
        Fx _launch, _land, _settle;

        // 당김 스웨이 상태(AutoTraversal 구동)
        bool _swayActive;
        float _swayCycles, _swayAmp, _swaySign, _swayProgress;

        Vector3 _appliedPos;

        public void OnJumpLaunch(float rise)
        {
            _launch.active = true;
            _launch.amp = Mathf.Min(launchDipMax, launchDipPerMeter * Mathf.Max(0f, rise));
            _launch.dur = launchDuration;
            _launch.t = 0f;
        }

        public void OnLand(float impactSpeed)
        {
            if (impactSpeed < landMinSpeed) return;
            _land.active = true;
            _land.amp = Mathf.Min(landDipMax, landDipPerSpeed * impactSpeed);
            _land.dur = landDuration;
            _land.t = 0f;
        }

        public void OnMantleFinish()
        {
            _settle.active = true;
            _settle.amp = settleDip;
            _settle.dur = settleDuration;
            _settle.t = 0f;
        }

        /// <summary>당김 시작. cycles=교차 횟수, sign=첫 기울임 방향(+1/-1).</summary>
        public void BeginPullSway(float cycles, float amplitudeDeg, float sign)
        {
            _swayActive = true;
            _swayCycles = Mathf.Max(0.5f, cycles);
            _swayAmp = amplitudeDeg;
            _swaySign = Mathf.Sign(sign);
            _swayProgress = 0f;
        }

        public void SetPullProgress(float p) { _swayProgress = Mathf.Clamp01(p); }

        public void EndPullSway() { _swayActive = false; }

        /// <summary>
        /// 직전 프레임 오프셋 되돌리기 — <b>모든 위치 구동자보다 먼저</b>(실행 순서 -100).
        /// LateUpdate에서 같이 처리하면, 그 사이 AutoTraversal이 위치를 절대값으로 덮어쓴 경우
        /// 이미 사라진 오프셋을 또 빼서 매 프레임 이중 차감 → 화면이 가라앉으며 떨린다.
        /// </summary>
        void Update()
        {
            transform.position -= _appliedPos;
            _appliedPos = Vector3.zero;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;

            float dip = Tick(ref _launch, launchCurve, dt)
                      + Tick(ref _land, landCurve, dt)
                      + Tick(ref _settle, settleCurve, dt);

            float roll = 0f;
            if (_swayActive)
            {
                // sin(교차) × sin(진행 포락선) — 시작·끝에서 0으로 수렴해 뚝 끊기지 않는다.
                float envelope = Mathf.Sin(_swayProgress * Mathf.PI);
                roll = _swaySign * _swayAmp * envelope
                     * Mathf.Sin(_swayProgress * _swayCycles * 2f * Mathf.PI);
            }

            bool vr = VrMode.Enabled;
            float posScale = vr ? vrPositionScale : 1f;
            CurrentRoll = roll * (vr ? vrRollScale : 1f);

            _appliedPos = Vector3.down * (dip * posScale);
            transform.position += _appliedPos;
        }

        static float Tick(ref Fx fx, AnimationCurve curve, float dt)
        {
            if (!fx.active) return 0f;
            fx.t += dt;
            float u = fx.dur > 0f ? fx.t / fx.dur : 1f;
            if (u >= 1f) { fx.active = false; return 0f; }
            return fx.amp * Mathf.Max(0f, curve.Evaluate(u));
        }
    }
}
