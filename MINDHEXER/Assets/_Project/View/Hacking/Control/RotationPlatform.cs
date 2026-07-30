using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 회전 조종 — 중심축을 기준으로 <see cref="riderRoot"/>만 돌린다. 구조는 <see cref="RailSet"/>(§6.2)과
    /// 비슷하지만(앵커 기준 범위, 크립+플릭) 코드는 별도다 — 이동이 아니라 회전이라 라이더 이송 계산이 다르다.
    ///
    /// <para><b>회전판과 터렛이 이 하나를 같이 쓴다</b>(기초_설계안 §6.2). 둘의 차이는
    /// <see cref="carryRiders"/> 하나뿐이다 — 회전판은 위에 선 것을 실어 나르고, 터렛은 아무도 안 탄다.
    /// 조작 문법(크립·플릭·격자·범위)이 같은데 클래스를 나누면 값 튜닝을 두 번 하게 되므로 합쳐 둔다.
    /// 나중에 정말 갈라지면 그때 뽑는다.</para>
    ///
    /// <para><b>각도 제한은 기본으로 풀려 있다</b>(±360). 터렛처럼 한 바퀴 돌아야 하는 것이 기본이고,
    /// 제한은 문·다리처럼 물리적으로 못 도는 것에만 좁혀서 준다. 완전 무한 회전(랩어라운드)은
    /// 지원하지 않는다 — ±360이면 퍼즐에선 사실상 무제한이고, 랩을 넣으면
    /// <see cref="GetNormalized"/>(VR 위치 제어의 기준)가 정의되지 않는다.</para>
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
        [Tooltip("0(부착 각도)에서 음(−) 방향 한계(도). 기본은 −360 = 사실상 제한 없음.\n" +
                 "제한을 두려면 값을 좁힐 것(예: 문·다리처럼 물리적으로 못 도는 것).")]
        public float rangeMinDeg = -360f;

        [Tooltip("0(부착 각도)에서 양(+) 방향 한계(도). 기본은 +360 = 사실상 제한 없음.")]
        public float rangeMaxDeg = 360f;

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

        [Header("마일스톤 (홀드 전용 — 6DoF 떨림·튐 차단, §6.2)")]
        [Tooltip("홀드 회전을 딸깍 단위로 양자화한다. 스텝 ÷ 간격 = 최대 각속도이므로 " +
                 "rotateSpeedDeg(60)와 간격(0.0833)에서 유도하면 정확히 5도가 나온다.")]
        public MilestoneStepper creepStep = new MilestoneStepper(5f);

        [Header("라이더 감지")]
        [Tooltip("회전면 위에 선 것을 같이 돌릴지. ★ 터렛처럼 <b>아무도 올라타지 않는</b> 회전체는 꺼야 한다 — " +
                 "켜 두면 매 프레임 씬의 CharacterController를 훑고 SphereCast를 쏘는 헛일을 한다.\n" +
                 "발판·회전판은 켠 채로 둘 것.")]
        public bool carryRiders = true;

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

        /// <summary>회전판은 좌/우가 대칭이라 "보이는 대로" 돌아야 한다 → 화면 기준 부호 보정을 쓴다.</summary>
        public bool ScreenRelativeSign => true;

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
            // Awake에서 한 번 확인해도 충분하지 않다 — riderRoot는 Play 도중에 파괴될 수 있고
            // (파괴 연출·씬 정리 등), 그러면 매 프레임 MissingReferenceException이 터진다.
            // ★ `== null`로 비교해야 한다. 유니티가 오버로드한 비교라 <b>파괴된 객체도</b> 잡는다.
            if (riderRoot == null) { enabled = false; return; }

            float dt = Time.deltaTime;
            if (dt <= 0f) { _analog = 0f; return; }

            if (_flicking) StepFlick(dt);
            else StepCreep(dt);

            riderRoot.localRotation = _baseLocalRotation * Quaternion.AngleAxis(AngleOffset, AxisLocal);

            Quaternion worldRot = riderRoot.rotation;
            Quaternion deltaRot = worldRot * Quaternion.Inverse(_prevWorldRotation);
            _prevWorldRotation = worldRot;

            if (carryRiders)
            {
                deltaRot.ToAngleAxis(out float deltaDeg, out _);
                if (!Mathf.Approximately(deltaDeg, 0f)) CarryRiders(deltaRot);
            }

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

            // 요구량을 마일스톤으로 양자화 — 0 아니면 정확히 ±스텝(§MilestoneStepper).
            // _vel이 0이어도 불러야 쿨다운·잔량이 정리된다(RailSet과 같은 이유).
            float move = creepStep.Advance(_vel * dt, dt);

            if (!Mathf.Approximately(move, 0f))
            {
                float next = Mathf.Clamp(AngleOffset + move, rangeMinDeg, rangeMaxDeg);
                if (Mathf.Approximately(next, AngleOffset)) { _vel = 0f; creepStep.Reset(); }   // 범위 끝
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
                // MotionFeel은 카메라 리그에 있으므로 자식까지 뒤져야 한다(RailPlatform 주석 참조).
                var feel = cc.GetComponentInChildren<MotionFeel>();
                if (feel != null) feel.OnCarried(delta);
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
            creepStep.Reset();   // 잔량이 남으면 재시작 첫 프레임에 튄다
            AngleOffset = 0f;
            if (riderRoot != null)
            {
                riderRoot.localRotation = _baseLocalRotation;
                _prevWorldRotation = riderRoot.rotation;
            }
        }
    }
}
