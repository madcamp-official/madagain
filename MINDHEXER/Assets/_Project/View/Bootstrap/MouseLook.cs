using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.View
{
    /// <summary>
    /// PC 시점 — 카메라에 붙는 <b>시점 회전의 유일한 소유자</b>. 마우스 + 외부(자이로) 델타를
    /// yaw/pitch로 누적하고 <see cref="MotionFeel.CurrentRoll"/>을 합성해 적용한다.
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

        FirstPersonPlayer _body;
        MotionFeel _feel;
        float _yaw, _pitch;

        void Awake()
        {
            _body = GetComponentInParent<FirstPersonPlayer>();
            _feel = GetComponent<MotionFeel>();

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

                _yaw += d.x * lookSens;
                _pitch = Mathf.Clamp(_pitch - d.y * lookSens, -85f, 85f);
            }
            ExternalLook = Vector2.zero;   // 델타는 매 프레임 소비(frozen이어도 쌓이면 안 된다)

            transform.localRotation = Quaternion.Euler(_pitch, _yaw,
                _feel != null ? _feel.CurrentRoll : 0f);
        }
    }
}
