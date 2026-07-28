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
        [Tooltip("도약 높이 1m당 침하 깊이(m). ※업킥과 크기·지속이 비슷하면 서로 상쇄돼 아무것도 안 느껴진다.")]
        public float launchDipPerMeter = 0.05f;
        public float launchDipMax = 0.12f;
        public float launchDuration = 0.18f;
        public AnimationCurve launchCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 4f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0f, -1.5f, 0f));

        [Header("점프 발구름(업킥) — 위로 '탁' 튀었다 원복")]
        [Tooltip("높이와 무관하게 항상 붙는 킥(m). 기본 0 — 상하 흔들림은 멀미 유발이라 롤 킥을 먼저 쓴다.")]
        public float launchKickBase = 0f;
        [Tooltip("도약 높이 1m당 추가로 위로 튀는 양(m).")]
        public float launchKickPerMeter = 0f;
        public float launchKickMax = 0.26f;
        [Tooltip("킥은 침하보다 짧아야 '탁' 하고 튄다.")]
        public float launchKickDuration = 0.15f;
        public AnimationCurve launchKickCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 9f), new Keyframe(0.22f, 1f), new Keyframe(1f, 0f, -1.8f, 0f));

        [Header("착지 (강도 = 착지 순간 낙하 속도)")]
        [Tooltip("낙하 속도 1m/s당 침하 깊이(m).")]
        public float landDipPerSpeed = 0.012f;
        public float landDipMax = 0.22f;
        [Tooltip("이 속도(m/s) 미만의 착지는 연출 없음. ※접지 중 수직 속도가 -2로 고정되므로 " +
                 "2 이하로 두면 접지가 깜빡일 때마다 연출이 터져 화면이 계속 흔들린다.")]
        public float landMinSpeed = 4.5f;
        public float landDuration = 0.3f;
        public AnimationCurve landCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 5f), new Keyframe(0.25f, 1f), new Keyframe(0.6f, 0.12f), new Keyframe(1f, 0f));

        [Header("착지(업킥) — 충격 뒤 위로 '탁'")]
        [Tooltip("낙하 속도 1m/s당 위로 튀는 양(m). 기본 0 — 롤 킥을 먼저 쓴다.")]
        public float landKickPerSpeed = 0f;
        public float landKickMax = 0.26f;
        public float landKickDuration = 0.18f;
        public AnimationCurve landKickCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 9f), new Keyframe(0.22f, 1f), new Keyframe(1f, 0f, -1.8f, 0f));

        [Header("잡고 올라가기 안착 (높이 무관 고정)")]
        public float settleDip = 0.05f;
        public float settleDuration = 0.22f;
        public AnimationCurve settleCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 4f), new Keyframe(0.4f, 1f), new Keyframe(1f, 0f, -1f, 0f));

        [Header("롤 킥 — 도약·착지 때 좌우로 '파박'")]
        [Tooltip("도약 발구름 시 좌우 기울임 진폭(도). 등반 당김의 교차 기울임과 같은 계열.")]
        public float launchRollDeg = 3.5f;
        [Tooltip("지속(초). 짧을수록 '파박'.")]
        public float launchRollDuration = 0.26f;
        [Tooltip("그 사이 좌우로 오가는 횟수. 1.25면 한 번 크게 갔다 반대로 살짝.")]
        public float launchRollCycles = 1.25f;

        [Tooltip("착지 시 좌우 기울임 진폭(도).")]
        public float landRollDeg = 2.5f;
        public float landRollDuration = 0.3f;
        public float landRollCycles = 1.25f;

        [Header("VR 감쇠")]
        [Tooltip("VR에서 위치 오프셋에 곱하는 배율.")]
        [Range(0f, 1f)] public float vrPositionScale = 0.25f;
        [Tooltip("VR에서 롤에 곱하는 배율. 인위적 롤 = 멀미 1순위라 기본 0.")]
        [Range(0f, 1f)] public float vrRollScale = 0f;

        /// <summary>이번 프레임의 롤(도). FirstPersonPlayer가 시점 회전에 합성한다.</summary>
        public float CurrentRoll { get; private set; }

        struct Fx { public bool active; public float amp, dur, t; }
        Fx _launch, _land, _settle;       // 아래로(침하)
        Fx _launchKick, _landKick;        // 위로(킥)

        // 당김 스웨이 상태(AutoTraversal 구동)
        bool _swayActive;
        float _swayCycles, _swayAmp, _swaySign, _swayProgress;

        // 롤 킥 — 감쇠 진동 하나로 좌우를 훑는다. 방향은 번갈아 바뀐다(같은 쪽만 기울면 금방 티가 난다).
        Fx _rollKick;
        float _rollCycles, _rollSign = 1f;

        Vector3 _appliedPos;

        public void OnJumpLaunch(float rise)
        {
            float r = Mathf.Max(0f, rise);
            Fire(ref _launch, Mathf.Min(launchDipMax, launchDipPerMeter * r), launchDuration);
            Fire(ref _launchKick, Mathf.Min(launchKickMax, launchKickBase + launchKickPerMeter * r), launchKickDuration);
            FireRoll(launchRollDeg, launchRollDuration, launchRollCycles);
        }

        public void OnLand(float impactSpeed)
        {
            if (impactSpeed < landMinSpeed) return;
            Fire(ref _land, Mathf.Min(landDipMax, landDipPerSpeed * impactSpeed), landDuration);
            Fire(ref _landKick, Mathf.Min(landKickMax, landKickPerSpeed * impactSpeed), landKickDuration);
            FireRoll(landRollDeg, landRollDuration, landRollCycles);
        }

        void FireRoll(float deg, float dur, float cycles)
        {
            if (deg <= 0.01f || dur <= 0f) return;
            _rollSign = -_rollSign;                 // 매번 반대쪽부터
            _rollCycles = Mathf.Max(0.5f, cycles);
            Fire(ref _rollKick, deg, dur);
        }

        static void Fire(ref Fx fx, float amp, float dur)
        {
            if (amp <= 0.0001f || dur <= 0f) return;
            fx.active = true; fx.amp = amp; fx.dur = dur; fx.t = 0f;
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

            // 위로 튀는 성분 — 킥이 침하보다 짧아 '탁' 튀었다 원복하는 순서로 읽힌다.
            float kick = Tick(ref _launchKick, launchKickCurve, dt)
                       + Tick(ref _landKick, landKickCurve, dt);

            // 롤 킥 — 감쇠 진동. sin이 0에서 출발해 빠르게 최고점을 찍으므로 '파박' 하고 튄다.
            float roll = 0f;
            if (_rollKick.active)
            {
                _rollKick.t += dt;
                float u = _rollKick.dur > 0f ? _rollKick.t / _rollKick.dur : 1f;
                if (u >= 1f) _rollKick.active = false;
                else roll += _rollSign * _rollKick.amp
                           * Mathf.Sin(u * _rollCycles * 2f * Mathf.PI) * (1f - u);
            }

            if (_swayActive)
            {
                // sin(교차) × sin(진행 포락선) — 시작·끝에서 0으로 수렴해 뚝 끊기지 않는다.
                float envelope = Mathf.Sin(_swayProgress * Mathf.PI);
                roll += _swaySign * _swayAmp * envelope
                      * Mathf.Sin(_swayProgress * _swayCycles * 2f * Mathf.PI);
            }

            bool vr = VrMode.Enabled;
            float posScale = vr ? vrPositionScale : 1f;
            CurrentRoll = roll * (vr ? vrRollScale : 1f);

            _appliedPos = Vector3.up * ((kick - dip) * posScale);
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
