using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.View
{
    /// <summary>
    /// 간이 1인칭 플레이어 — CharacterController 기반 WASD 이동 + 중력 + 마우스 시점.
    /// FreeLook(나는 테스트 카메라)을 대체하는 PC 본체. VR 이식 시엔 입력만 네트워크로 교체.
    ///
    /// <para>수평 이동은 <see cref="MoveIntegrator"/>가 담당한다 — 즉시 속도가 아니라 가속/감속 램프.
    /// 그래야 네트워크 입력이 늦게 와도 <b>속도를 앞당기는 방식</b>으로 지연을 가릴 수 있다(위치 순간이동 없이).
    /// PC는 <see cref="InputAge"/>=0이라 보상이 0이고, VR 배선 시 <see cref="LatencyEstimator"/>가 낸
    /// age를 여기에 넣어주면 같은 코드가 그대로 보상까지 한다.</para>
    ///
    /// 점프는 이 스크립트 소관이 아니다(자동 점프로 별도 처리 예정) — 여기엔 중력·접지만 있다.
    ///
    /// 해킹 중엔 <see cref="LookFrozen"/>=true로 <b>시점만</b> 멈춘다(마우스가 패턴을 그리므로).
    /// WASD 이동은 해킹 중에도 계속된다 — 그려가며 도망칠 수 있어야 하기 때문.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonPlayer : MonoBehaviour
    {
        [Header("시점")]
        public float lookSens = 0.1f;

        [Header("이동 (가속/감속 — 값으로 아이작식 관성 조절)")]
        public MoveIntegrator move = new MoveIntegrator();

        [Header("수직")]
        public float gravity = 20f;

        /// <summary>해킹 중 시점만 멈출 때 true(이동은 계속). HackDriver가 세팅.</summary>
        public bool LookFrozen;

        /// <summary>
        /// 지금 반영 중인 이동 입력이 몇 초 묵었는지(초). PC는 0.
        /// VR 네트워크 배선 시 <see cref="LatencyEstimator.Observe"/> 결과를 매 샘플 여기에 넣는다.
        /// </summary>
        public float InputAge;

        /// <summary>
        /// true면 이동·중력을 이 스크립트가 처리하지 않는다 — <see cref="AutoTraversal"/>(자동 등반)이
        /// 위치를 직접 몰 때 켜진다. <b>시점은 계속 동작한다</b>(VR에선 머리를 막을 수 없고, 막아서도 안 된다).
        /// </summary>
        public bool ExternalMotion;

        /// <summary>지금 향하고 있는 수평 방향(피치 제외). 자동 등반의 전방 탐지 기준.</summary>
        public Vector3 FlatForward
        {
            get { float r = _yaw * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r)); }
        }

        public CharacterController Controller => _cc;

        /// <summary>수직 속도(m/s). 자동 도약이 읽고 쓴다.</summary>
        public float VerticalVelocity { get => _vy; set => _vy = value; }

        /// <summary>도약 — 수평 속도와 수직 속도를 한 번에 지정(틈 건너뛰기).</summary>
        public void Launch(Vector2 horizontal, float vertical)
        {
            move.SetVelocity(horizontal);
            _vy = vertical;
        }

        CharacterController _cc;
        float _yaw, _pitch, _vy;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _cc.height = 1.8f;
            _cc.radius = 0.3f;
            _cc.center = new Vector3(0f, -0.7f, 0f);   // 카메라(눈)=1.6 위 → 발이 지면에

            Vector3 e = transform.eulerAngles;
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
            if (kb == null) return;

            float dt = Time.deltaTime;

            // 시점 — 해킹 중엔 마우스가 패턴을 그리므로 잠근다(이동은 아래에서 계속 처리).
            if (!LookFrozen && mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 d = mouse.delta.ReadValue();
                _yaw += d.x * lookSens;
                _pitch = Mathf.Clamp(_pitch - d.y * lookSens, -85f, 85f);
                transform.localRotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }

            if (kb.escapeKey.wasPressedThisFrame)
                Cursor.lockState = Cursor.lockState == CursorLockMode.Locked
                    ? CursorLockMode.None : CursorLockMode.Locked;

            // 자동 등반이 위치를 몰고 있으면 이동·중력은 넘긴다(시점은 위에서 이미 처리됨).
            if (ExternalMotion) return;

            // 이동 입력(yaw 기준 수평) → 가속 적분기
            float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            float z = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            Vector2 local = new Vector2(x, z);
            if (local.sqrMagnitude > 1f) local.Normalize();

            float yawRad = _yaw * Mathf.Deg2Rad;
            float sin = Mathf.Sin(yawRad), cos = Mathf.Cos(yawRad);
            Vector2 wish = new Vector2(local.x * cos + local.y * sin,      // 월드 X
                                       local.y * cos - local.x * sin);    // 월드 Z

            move.Step(wish, dt, _cc.isGrounded, InputAge);
            ApplyMove(dt);
        }

        void ApplyMove(float dt)
        {
            if (_cc.isGrounded && _vy < 0f) _vy = -2f;
            else _vy -= gravity * dt;

            Vector2 h = move.Velocity;
            _cc.Move(new Vector3(h.x, _vy, h.y) * dt);
        }
    }
}
