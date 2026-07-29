using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 회전 조종 기믹 — 중심축을 기준으로 <see cref="riderRoot"/>만 돌고, 그 위에 선 것을 함께 실어
    /// 돌린다. 구조는 <see cref="RailSet"/>(§6.2)과 비슷하지만(앵커 기준 범위, 크립+플릭) 코드는
    /// 별도다 — 이동이 아니라 회전이라 라이더 이송 계산이 다르다.
    ///
    /// <para><b>몸체는 돌지 않는다.</b> 이 컴포넌트가 붙은 트랜스폼(하우징)은 고정이고,
    /// <see cref="riderRoot"/>(터닝 플랫폼 비주얼 + 그 위 라이더)만 자기 원점을 축으로 돈다.
    /// 그래서 이 오브젝트가 레일 세트의 라이더로 얹혀도(§6.2 중첩) 하우징에 <see cref="RailPlatform"/>을
    /// 얹기만 하면 되고, 여기서 따로 처리할 게 없다 — RailSet이 계층으로 옮기고, RailPlatform이
    /// 위에 선 플레이어만 옮긴다.</para>
    ///
    /// <para><b>45도 격자는 플릭 전용.</b> 홀드(크립)는 격자에 안 묶이고 연속으로 돈다. 더블클릭
    /// 플릭만 현재 각도를 45도 격자에 반올림한 뒤 그 방향 한 칸으로 스냅한다.</para>
    ///
    /// <para><b>라이더 강제이동</b> — <see cref="RailPlatform"/>은 위치 델타만 다루므로 회전에는
    /// 못 쓴다. 매 프레임 riderRoot의 회전 델타로 각 라이더의 피벗 기준 새 위치를 계산해
    /// <c>CharacterController.Move</c>로 옮기고, <see cref="MotionFeel.OnCarried"/>를 그대로 호출해
    /// 지하철 연출(§롤·FOV)을 공유한다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class RotationPlatform : MonoBehaviour, IExternalControl, IRunResettable
    {
        [Header("구성")]
        [Tooltip("실제로 회전하는 부분(터닝 플랫폼 비주얼 + 라이더). 이 트랜스폼의 로컬 원점이 피벗이다.")]
        public Transform riderRoot;

        [Header("회전축 (이 컴포넌트가 붙은 하우징의 로컬 기준, 하우징 자체는 안 돈다)")]
        public Vector3 axis = Vector3.up;

        [Header("회전 범위 (부착 시 각도=0 기준, RailSet의 rangeMin/Max와 동일 개념)")]
        [Tooltip("0(부착 각도)에서 음(−) 방향 한계(도).")]
        public float rangeMinDeg = -90f;

        [Tooltip("0(부착 각도)에서 양(+) 방향 한계(도).")]
        public float rangeMaxDeg = 90f;

        [Header("조종 감각")]
        [Tooltip("홀드 시 크립 각속도(도/초).")]
        public float rotateSpeedDeg = 60f;

        [Tooltip("크립 가감속 램프(초). 0이면 즉시.")]
        public float accelTime = 0.08f;

        [Tooltip("플릭 격자 단위(도). 연속 회전은 이 격자에 안 묶이고 플릭 타겟 계산에만 쓴다.")]
        public float flickStepDeg = 45f;

        [Tooltip("플릭 1회에 걸리는 시간(초).")]
        public float flickTime = 0.28f;

        [Tooltip("플릭 끝의 오버슈트 세기 — '철컥' 안착감. 0이면 없음.")]
        [Range(0f, 3f)] public float overshoot = 1.6f;

        [Tooltip("홀드를 놓았을 때 가장 가까운 격자로 붙일지. 기본 끔(크립은 미세 조정용).")]
        public bool snapAnalog = false;

        [Header("라이더 감지")]
        [Tooltip("발 밑 이 거리 안에서 riderRoot가 잡히면 '올라타 있다'로 본다(m).")]
        public float probeDistance = 0.35f;

        /// <summary>0(부착 각도) 기준 현재 회전각(도).</summary>
        public float AngleOffset { get; private set; }

        /// <summary>플릭·크립이 모두 멈춘 상태.</summary>
        public bool AtRest => !_flicking && Mathf.Approximately(_vel, 0f);

        public Vector3 AxisLocal => axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.up;

        Quaternion _baseLocalRotation;
        Quaternion _prevWorldRotation;
        float _analog, _vel;
        bool _flicking;
        float _flickFrom, _flickTo, _flickT;

        static readonly List<CharacterController> Riders = new List<CharacterController>();
        static float _nextScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ResetStatics()
        {
            Riders.Clear();
            _nextScan = 0f;
        }

        // ── IExternalControl ──────────────────────────────────────────────
        public int AxisCount => 1;

        /// <summary>
        /// 조종 방향으로 <b>회전축이 아니라 수평 접선</b>을 낸다.
        ///
        /// <para>회전축(보통 월드 up)을 그대로 주면 <see cref="HackDriver.FreezeControlMapping"/>의
        /// 화면 투영이 이 부품을 "세로 축"으로 배정한다 → 손을 <b>위아래로</b> 움직여야 회전판이
        /// 도는 매핑이 나온다. 조종하는 사람에게 의미 있는 방향은 축이 아니라 라이더가 밀려가는
        /// 접선이다. 기즈모도 이 방향을 그리는 편이 맞다.</para>
        /// </summary>
        public Vector3 AxisWorld(int slot)
        {
            Vector3 n = transform.TransformDirection(AxisLocal).normalized;
            Vector3 t = Vector3.ProjectOnPlane(transform.right, n);
            if (t.sqrMagnitude < 1e-6f) t = Vector3.ProjectOnPlane(transform.forward, n);
            return t.sqrMagnitude < 1e-6f ? n : t.normalized;
        }

        /// <summary>부착 각도=0, 양 끝=±1. 범위가 비대칭이어도 각 방향을 따로 정규화한다.</summary>
        public float GetNormalized(int slot)
        {
            if (slot != 0) return 0f;
            if (AngleOffset >= 0f) return rangeMaxDeg > 1e-4f ? Mathf.Clamp01(AngleOffset / rangeMaxDeg) : 0f;
            return rangeMinDeg < -1e-4f ? -Mathf.Clamp01(AngleOffset / rangeMinDeg) : 0f;
        }

        public void Drive(int slot, float analog, int flick)
        {
            if (slot != 0) return;

            if (flick != 0) { StartFlick(NextGridTarget(flick)); return; }

            // 플릭 중에는 아날로그를 무시한다(§RailSet과 동일 이유 — 더블클릭 직후 버튼이 눌린 채라
            // 여기서 끊으면 플릭이 시작하자마자 죽는다).
            if (_flicking) return;

            _analog = Mathf.Clamp(analog, -1f, 1f);
        }
        // ──────────────────────────────────────────────────────────────────

        void Awake()
        {
            if (riderRoot == null) { enabled = false; Debug.LogWarning("[RotationPlatform] riderRoot가 비어 있습니다.", this); return; }
            _baseLocalRotation = riderRoot.localRotation;
            _prevWorldRotation = riderRoot.rotation;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) { _analog = 0f; return; }

            if (_flicking) StepFlick(dt);
            else StepCreep(dt);

            riderRoot.localRotation = _baseLocalRotation * Quaternion.AngleAxis(AngleOffset, AxisLocal);

            Quaternion worldRot = riderRoot.rotation;
            Quaternion deltaRot = worldRot * Quaternion.Inverse(_prevWorldRotation);
            _prevWorldRotation = worldRot;

            deltaRot.ToAngleAxis(out float deltaDeg, out _);
            if (!Mathf.Approximately(deltaDeg, 0f)) CarryRiders(deltaRot);

            _analog = 0f;   // 매 프레임 소비. HackDriver는 입력이 있을 때만 Drive를 부른다.
        }

        void StepFlick(float dt)
        {
            _flickT += dt;
            float x = flickTime > 1e-4f ? Mathf.Clamp01(_flickT / flickTime) : 1f;
            AngleOffset = Mathf.LerpUnclamped(_flickFrom, _flickTo, Mech(x));
            if (x >= 1f) { AngleOffset = _flickTo; _flicking = false; }
        }

        void StepCreep(float dt)
        {
            float target = _analog * rotateSpeedDeg;
            float rate = accelTime > 1e-4f ? rotateSpeedDeg / accelTime : float.MaxValue;
            _vel = Mathf.MoveTowards(_vel, target, rate * dt);

            if (!Mathf.Approximately(_vel, 0f))
            {
                float next = Mathf.Clamp(AngleOffset + _vel * dt, rangeMinDeg, rangeMaxDeg);
                if (Mathf.Approximately(next, AngleOffset)) _vel = 0f;   // 범위 끝에 닿으면 정지
                else AngleOffset = next;
            }

            if (snapAnalog && Mathf.Approximately(_analog, 0f) && Mathf.Approximately(_vel, 0f))
            {
                float n = NearestGrid(AngleOffset);
                if (Mathf.Abs(n - AngleOffset) > 1e-3f) StartFlick(n);
            }
        }

        void StartFlick(float target)
        {
            if (Mathf.Approximately(target, AngleOffset)) return;
            _flickFrom = AngleOffset;
            _flickTo = target;
            _flickT = 0f;
            _flicking = true;
            _vel = 0f;
        }

        /// <summary>현재 격자에서 dir 방향 한 칸. 범위를 넘으면 범위 끝에 붙인다.</summary>
        float NextGridTarget(int dir)
        {
            float unit = Mathf.Max(1e-4f, flickStepDeg);
            int cell = Mathf.RoundToInt(AngleOffset / unit);
            return Mathf.Clamp((cell + Mathf.Clamp(dir, -1, 1)) * unit, rangeMinDeg, rangeMaxDeg);
        }

        float NearestGrid(float deg)
        {
            float unit = Mathf.Max(1e-4f, flickStepDeg);
            return Mathf.Clamp(Mathf.RoundToInt(deg / unit) * unit, rangeMinDeg, rangeMaxDeg);
        }

        /// <summary>기계식 이동 곡선 — RailSet과 동일(부드럽게 가속·감속하다 살짝 지나쳐 철컥 안착).</summary>
        float Mech(float x)
        {
            x = Mathf.Clamp01(x);
            float s = x * x * x * (x * (x * 6f - 15f) + 10f);          // smootherstep
            float k = Mathf.Clamp01((x - 0.6f) / 0.4f);                // 끝 40% 구간
            return s + overshoot * 0.08f * Mathf.Sin(k * Mathf.PI);    // x=1에서 0으로 돌아옴
        }

        /// <summary>riderRoot 위에 선 것들을 회전 델타만큼 피벗(riderRoot 원점) 기준으로 옮긴다.</summary>
        void CarryRiders(Quaternion deltaRot)
        {
            RefreshRiders();
            Vector3 pivot = riderRoot.position;

            for (int i = 0; i < Riders.Count; i++)
            {
                var cc = Riders[i];
                if (cc == null || !cc.enabled || !cc.gameObject.activeInHierarchy) continue;
                if (!StandingOnMe(cc)) continue;

                Vector3 pos = cc.transform.position;
                Vector3 newPos = pivot + deltaRot * (pos - pivot);
                Vector3 delta = newPos - pos;
                cc.Move(delta);
                // 회전이든 직선이든 "내가 걸은 건지 밀린 건지" 구분은 여기 한 곳으로 모인다(RailPlatform 참조).
                if (cc.TryGetComponent(out MotionFeel feel)) feel.OnCarried(delta);
            }
        }

        bool StandingOnMe(CharacterController cc)
        {
            float r = Mathf.Max(0.01f, cc.radius * 0.9f);
            Vector3 bottomSphere = cc.transform.TransformPoint(cc.center)
                                 + Vector3.up * (-cc.height * 0.5f + cc.radius);

            if (!Physics.SphereCast(bottomSphere, r, Vector3.down, out RaycastHit hit,
                                    probeDistance, ~0, QueryTriggerInteraction.Ignore))
                return false;

            return hit.collider != null && hit.collider.transform.IsChildOf(riderRoot);
        }

        static void RefreshRiders()
        {
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 1f;

            Riders.Clear();
            Riders.AddRange(Object.FindObjectsByType<CharacterController>(FindObjectsSortMode.None));
        }

        // ── IRunResettable ────────────────────────────────────────────────
        /// <summary>아레나 리셋 — 연출 없이 부착 각도로 즉시 복귀.</summary>
        public void ResetForRestart()
        {
            _flicking = false;
            _vel = 0f;
            _analog = 0f;
            AngleOffset = 0f;
            if (riderRoot != null)
            {
                riderRoot.localRotation = _baseLocalRotation;
                _prevWorldRotation = riderRoot.rotation;
            }
        }
    }
}
