using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.View
{
    /// <summary>
    /// PC 시점 — <c>[Head]</c>에 붙는 <b>시점 회전의 유일한 소유자</b>. 마우스 + 외부(자이로) 델타를
    /// yaw/pitch로 누적해 적용한다. 롤은 아래층(카메라의 <see cref="MotionFeel"/>)이 담당한다 —
    /// 롤이 시선 축을 돌려면 yaw/pitch보다 <b>아래</b>에 있어야 하기 때문(GameBoot 주석 ★).
    /// <see cref="FirstPersonPlayer"/>(몸)에서 뜯어낸 코드다 — 몸은 이제 회전하지 않는다.
    ///
    /// <para>VR에선 이 컴포넌트를 붙이지 않는다 — TrackedPoseDriver(머리)가 같은 자리를 소유한다.
    /// 같은 카메라에 둘 다 붙으면 서로 덮어쓰며 싸운다.</para>
    ///
    /// <para>해킹 중(<see cref="FirstPersonPlayer.LookFrozen"/>)엔 <b>입력 누적만</b> 멈춘다.
    /// 회전 적용 자체는 항상 실행돼야 MotionFeel의 절차적 롤(지하철 스웨이 등)이 해킹 중에도
    /// 계속 보인다 — "회전 불가능"과 "연출 없음"은 다른 요구사항이다(과거 실제 버그).</para>
    /// </summary>
    public sealed class MouseLook : MonoBehaviour
    {
        [Tooltip("마우스 감도(픽셀당 도).")]
        public float lookSens = 0.1f;

        /// <summary>
        /// 외부 입력(컨트롤러 자이로)의 시점 변화량. 마우스 delta와 같은 단위.
        /// <b>델타라서 매 프레임 소비</b>된다.
        /// </summary>
        public Vector2 ExternalLook;

        /// <summary>
        /// 감도 배율. 연출이 "아직 정신이 안 든" 여운을 줄 때 1 미만으로 낮췄다 되돌린다.
        /// <see cref="lookSens"/>를 직접 건드리지 않는 이유: 그건 사용자 설정이라, 연출이 중간에
        /// 끊기면 낮아진 값이 그대로 굳는다.
        /// </summary>
        public float SensScale = 1f;

        /// <summary>지금 보고 있는 방향(도). 기상 연출이 시작점을 읽고 목표로 되돌릴 때 쓴다.</summary>
        public float Yaw => _yaw;

        /// <summary>지금 위아래 각(도). −는 위, +는 아래(Euler.x 규약 그대로).</summary>
        public float Pitch => _pitch;

        /// <summary>
        /// 시점을 직접 지정한다. <b>연출이 시점을 움직이려면 반드시 이걸 써야 한다.</b>
        ///
        /// <para>이 컴포넌트는 매 프레임 <c>transform.localRotation</c>을 자기 yaw/pitch로
        /// 덮어쓴다. 그래서 다른 컴포넌트가 <c>localRotation</c>을 직접 쓰면 <b>다음 프레임에
        /// 사라진다</b> — 회전의 소유자를 하나로 유지하기 위해 진입점을 여기 둔다.</para>
        ///
        /// <para>입력 누적을 같이 막으려면 <see cref="FirstPersonPlayer.LookFrozen"/>을 켤 것.
        /// 안 그러면 이 값이 마우스 입력에 곧바로 밀린다.</para>
        /// </summary>
        public void SetLook(float yaw, float pitch)
        {
            _yaw = yaw;
            _pitch = Mathf.Clamp(pitch, -85f, 85f);
        }

        FirstPersonPlayer _body;
        MotionFeel _feel;
        float _yaw, _pitch;

        void Awake()
        {
            _body = GetComponentInParent<FirstPersonPlayer>();
            // [Head] 구조에서는 자식(카메라)에 있다. 손으로 조립한 씬에서는 부모(몸)일 수 있다.
            _feel = GetComponentInChildren<MotionFeel>();
            if (_feel == null) _feel = GetComponentInParent<MotionFeel>();

            Vector3 e = transform.localEulerAngles;
            _yaw = e.y;
            _pitch = e.x;
        }

        void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;

            if (kb != null && kb.escapeKey.wasPressedThisFrame)
                Cursor.lockState = Cursor.lockState == CursorLockMode.Locked
                    ? CursorLockMode.None : CursorLockMode.Locked;

            bool frozen = _body != null && _body.LookFrozen;
            if (!frozen)
            {
                Vector2 d = ExternalLook;
                if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
                    d += mouse.delta.ReadValue();

                float s = lookSens * Mathf.Max(0f, SensScale);
                _yaw += d.x * s;
                _pitch = Mathf.Clamp(_pitch - d.y * s, -85f, 85f);
            }
            ExternalLook = Vector2.zero;   // 델타는 매 프레임 소비(frozen이어도 쌓이면 안 된다)

            // 롤은 [CamRig](MotionFeel)이 소유하면 거기서 이미 걸린다 — 여기서 또 더하면 두 배가 된다.
            // 구형 배치(MotionFeel이 카메라에 직접)에서만 여기서 합성한다.
            float roll = (_feel != null && !_feel.OwnsRotation) ? _feel.CurrentRoll : 0f;
            transform.localRotation = Quaternion.Euler(_pitch, _yaw, roll);
        }
    }
}
