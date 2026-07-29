using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 피스톤·유압프레스의 <b>조종 스킴</b>. <see cref="TelescopingActuator"/>가 "어떻게 늘어나는가"를
    /// 담당하고, 이 컴포넌트는 "무엇이 그걸 얼마나 늘리는가"만 담당한다(연출/조종 분리).
    ///
    /// <para><b>축은 하나뿐이고 두 끝이 대칭이 아니다.</b> 레일·갠트리는 좌우가 대등해서 "보이는 대로"
    /// 움직여야 하지만, 액추에이터는 <b>뻗는다 / 집어넣는다</b>가 의미의 전부다. 그래서
    /// <see cref="ScreenRelativeSign"/>을 false로 돌려 <see cref="HackDriver"/>의 화면 기준 부호
    /// 보정을 받지 않는다 — 어느 방향에서 보든 <b>좌클릭 = 신장</b>으로 고정된다.
    /// (보정을 받으면 물체 뒤로 돌아가는 순간 좌/우가 뒤집혀 조작이 예측 불가능해진다.)</para>
    ///
    /// <para><b>홀드 = 살살, 플릭 = 끝에서 끝.</b> 기초_설계안 §6.2의 공통 조작 문법 그대로다.
    /// 피스톤과 프레스의 차이는 스킴이 아니라 <see cref="TelescopingActuator"/> 쪽 구동 값
    /// (속도·감쇠)이므로, 여기서 종류를 나누지 않는다 — 부품이 늘어도 이 파일은 그대로다.</para>
    ///
    /// <para>VR(휴대폰2 위치 제어)은 <see cref="AxisWorld"/>로 신장 방향을 받아 손 변위를 투영하고,
    /// <see cref="GetNormalized"/>와의 오차를 analog로 넣는 서보로 동작한다 — 여기 코드는 그대로다.</para>
    /// </summary>
    /// <para><b>액추에이터는 자식에서 찾는다.</b> <see cref="TelescopingActuator"/>는 파츠 참조 때문에
    /// 모델 프리팹(<c>Model_Piston</c>·<c>Model_Presser</c>) 안에 들어 있어야 하고, 이 컴포넌트는
    /// <see cref="Hackable"/>과 <b>같은 오브젝트</b>에 있어야 한다(<see cref="HackDriver"/>가
    /// <c>GetComponent</c>로 찾는다). 둘이 다른 계층이라 <c>RequireComponent</c>를 걸지 않는다.</para>
    [DisallowMultipleComponent]
    public class ActuatorControl : MonoBehaviour, IExternalControl, IRunResettable
    {
        [Header("홀드(살살 조절)")]
        [Tooltip("홀드 1초당 움직이는 스트로크 비율. 1이면 1초에 완전 수축↔완전 신장.")]
        public float holdSpeed = 0.6f;

        [Header("플릭(더블클릭 = 끝에서 끝)")]
        [Tooltip("끄면 플릭을 무시하고 홀드만 받는다. 압사 위험이 있는 프레스를 느리게만 쓰고 싶을 때.")]
        public bool allowFlick = true;

        [Header("대상")]
        [Tooltip("비워두면 자기 자신·자식에서 자동으로 찾는다(모델 프리팹 안에 있는 게 정상).")]
        public TelescopingActuator actuator;

        TelescopingActuator _act;

        void Awake()
        {
            _act = actuator != null ? actuator : GetComponentInChildren<TelescopingActuator>(true);
            if (_act == null)
            {
                Debug.LogError($"[액추에이터] {name}: TelescopingActuator를 찾지 못해 조종할 수 없습니다.", this);
                enabled = false;
                return;
            }

            // 조종이 붙은 이상 Space 미리보기는 입력을 덮어쓰기만 한다. 모델 프리팹에 켜진 채 남아 있어도
            // 조용히 조작이 안 먹는 사고가 나지 않도록 여기서 확실히 끈다.
            _act.debugPreview = false;
        }

        // 시작 상태는 TelescopingActuator.startExtension 하나가 소유한다 — 그 값이 에디터 씬 뷰에도
        // 그대로 보이므로 "보이는 대로 시작"이 보장된다. 여기에 같은 값을 또 두면 둘이 어긋난다.

        // ── IExternalControl ──────────────────────────────────────────────

        /// <summary>신축 1축뿐. 슬롯0(좌/우클릭)에만 배정된다.</summary>
        public int AxisCount => 1;

        /// <summary>신장 방향의 월드 벡터. 표시·VR 손 변위 투영용.</summary>
        public Vector3 AxisWorld(int slot) => _act != null ? _act.ExtendWorld : Vector3.right;

        /// <summary>완전 수축=−1, 완전 신장=+1. (VR 위치 제어의 오차 계산용)</summary>
        public float GetNormalized(int slot) => _act != null ? _act.Current * 2f - 1f : 0f;

        public void Drive(int slot, float analog, int flick)
        {
            if (_act == null) return;

            // 좌클릭은 axisH = −1이다(HexInputReader). 좌클릭 = 신장이므로 부호를 뒤집어 받는다.
            // 화면 보정을 받지 않기로 했으므로(ScreenRelativeSign=false) 이 뒤집기가 유일한 부호 처리다.
            if (allowFlick && flick != 0)
            {
                _act.Target = flick < 0 ? 1f : 0f;
                return;
            }

            if (!Mathf.Approximately(analog, 0f))
                _act.Target = Mathf.Clamp01(_act.Target + (-analog) * holdSpeed * Time.deltaTime);
        }

        /// <summary>피스톤·프레스는 축이 비대칭이라 화면 기준 보정을 쓰지 않는다(클래스 주석).</summary>
        public bool ScreenRelativeSign => false;

        // ── IRunResettable ────────────────────────────────────────────────

        public void ResetForRestart()
        {
            if (_act != null) _act.SnapTo(_act.startExtension);
        }
    }
}
