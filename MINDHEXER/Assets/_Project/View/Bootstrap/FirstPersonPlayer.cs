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

        /// <summary>
        /// 이번 프레임 이동 <b>입력</b>(월드 XZ, 크기 0~1). 자동 도약이 "의도"를 읽는 창구다.
        /// 결과 속도로 판정하면 가장자리 정지가 속도를 깎는 순간 "안 움직인다"로 뒤집혀 스스로 풀린다.
        /// </summary>
        public Vector2 Wish { get; private set; }

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
        MotionFeel _feel;
        float _yaw, _pitch, _vy;
        bool _wasGrounded;
        float _suppressLandUntil;

        /// <summary>이 시각까지 일반 착지 연출을 억제 — 잡고 올라가기 완료가 낙하 착지로 오인되지 않게.</summary>
        public void SuppressLand(float duration) => _suppressLandUntil = Time.time + duration;

        [Header("의도치 않은 밀림 감지 (레일 오브젝트 충돌 등 — 소스 불문)")]
        [Tooltip("실제 이동량이 의도한 이동량과 이만큼(m) 넘게 차이 나면 '외부가 밀었다'로 보고 " +
                 "MotionFeel.OnCarried()를 부른다. 너무 작으면 벽 슬라이딩 같은 정상 보정에도 반응한다.")]
        public float unexpectedPushThreshold = 0.02f;

        /// <summary>
        /// 이번 프레임 이 방향(월드 XZ, 정규화)으로 나가는 속도 성분을 막는다 — 가장자리 낙하 방지.
        /// <b>적분 직후·이동 직전</b>에 적용해야 한다. 적분 전에 깎으면 같은 프레임에 다시 가속해
        /// 프레임당 가속분(≈accel×dt)만큼 계속 새어 나간다(= 가장자리에서 조금씩 밀리는 버그).
        /// </summary>
        public void BlockDirection(Vector2 dir) { _blockDir = dir; _blockFrame = Time.frameCount; }

        /// <summary>직전 이동에서 차단이 <b>실제로 적용</b>됐는지(속도를 깎았는지). 낙하 추적용.</summary>
        public bool BlockedThisFrame { get; private set; }

        Vector2 _blockDir;
        int _blockFrame = -1;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _feel = GetComponent<MotionFeel>();
            _cc.height = 1.8f;
            _cc.radius = 0.3f;
            _cc.center = new Vector3(0f, -0.7f, 0f);   // 카메라(눈)=1.6 위 → 발이 지면에
            // 유니티 기본값을 유지한다. 크게 잡으면 컨트롤러가 턱 면을 타고 떠서 Below 접지가 풀리고,
            // 그러면 AutoTraversal이 Grounded=false로 보고 등반 판정을 아예 안 돌려 낀 채로 멈춘다.
            // 이 값보다 높은 단차는 AutoTraversal이 맡는다(minHeight를 이 값 이하로 두어 사각지대 없음).
            _cc.stepOffset = 0.3f;

            Vector3 e = transform.eulerAngles;
            _yaw = e.y;
            _pitch = e.x;
        }

        void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
        }

        /// <summary>
        /// 외부 입력(S10e 컨트롤러)의 이동 지령(월드 XZ, 크기 0~1). WASD와 <b>합산</b>된다.
        /// 연속값이라 매 프레임 소스가 덮어쓴다(0도 명시적으로 써야 한다).
        /// </summary>
        public Vector2 ExternalWish;

        /// <summary>
        /// 외부 입력의 시점 변화량(마우스 delta와 같은 단위·의미). <b>델타라서 매 프레임 소비</b>된다.
        /// </summary>
        public Vector2 ExternalLook;

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;

            // ⚠️ 예전엔 여기서 kb == null이면 통째로 return했다. 안드로이드엔 키보드가 없어서
            // 이동뿐 아니라 시점·중력·ApplyMove까지 전부 죽어 플레이어가 공중에 얼어붙었다.
            // 키보드·마우스는 이제 <b>선택</b>이다 — 없으면 외부 입력만으로 동작한다.

            float dt = Time.deltaTime;

            // 시점 — 해킹 중엔 마우스가 패턴을 그리므로 "회전 입력"만 잠근다(이동은 아래에서 계속 처리).
            // LookFrozen은 yaw/pitch 갱신만 막는다 — 회전 적용 자체(아래 대입)는 항상 실행돼야
            // MotionFeel의 절차적 롤(지하철 스웨이·착지 킥 등)이 해킹 중에도 계속 보인다. 예전엔
            // 이 대입이 같은 if 블록 안에 있어서 해킹 중엔 롤 연출까지 통째로 안 먹혔다(버그, 수정됨) —
            // "회전 불가능"과 "연출 없음"은 다른 요구사항이다.
            if (!LookFrozen)
            {
                Vector2 d = ExternalLook;   // 컨트롤러 자이로(에디터) — 마우스와 같은 단위
                if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
                    d += mouse.delta.ReadValue();

                _yaw += d.x * lookSens;
                _pitch = Mathf.Clamp(_pitch - d.y * lookSens, -85f, 85f);
            }
            ExternalLook = Vector2.zero;   // 델타는 매 프레임 소비(LookFrozen이어도 쌓이면 안 된다)
            transform.localRotation = Quaternion.Euler(_pitch, _yaw,
                _feel != null ? _feel.CurrentRoll : 0f);

            if (kb != null && kb.escapeKey.wasPressedThisFrame)
                Cursor.lockState = Cursor.lockState == CursorLockMode.Locked
                    ? CursorLockMode.None : CursorLockMode.Locked;

            // 자동 등반이 위치를 몰고 있으면 이동·중력은 넘긴다(시점은 위에서 이미 처리됨).
            if (ExternalMotion) return;

            // 이동 입력(yaw 기준 수평) → 가속 적분기.
            // WASD와 외부(컨트롤러 조이스틱)를 합산한다 — 에디터에서 둘 다 쓸 수 있어야 튜닝이 쉽다.
            Vector2 local = ExternalWish;
            if (kb != null)
            {
                local.x += (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
                local.y += (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            }
            if (local.sqrMagnitude > 1f) local.Normalize();

            float yawRad = _yaw * Mathf.Deg2Rad;
            float sin = Mathf.Sin(yawRad), cos = Mathf.Cos(yawRad);
            Vector2 wish = new Vector2(local.x * cos + local.y * sin,      // 월드 X
                                       local.y * cos - local.x * sin);    // 월드 Z
            Wish = wish;

            move.Step(wish, dt, _groundedBelow, InputAge);
            ApplyMove(dt);
        }

        void ApplyMove(float dt)
        {
            float prevVy = _vy;   // 착지 순간의 낙하 속도(연출 강도 기준)

            // 접지는 isGrounded 단독이 아니라 '실제 아래 접촉'(CollisionFlags.Below)으로 본다.
            // 모서리에 옆으로 스치는 프레임까지 접지로 치면 낙하 속도가 -2로 계속 리셋돼
            // 뚝뚝 끊기며 떨어진다.
            if (_groundedBelow && _vy < 0f) _vy = -2f;
            else _vy -= gravity * dt;

            Vector2 h = move.Output;   // 적분 속도 + 감쇠 임펄스

            // 가장자리 낙하 방지 — 적분이 끝난 뒤, 이동 직전에 바깥 성분만 깎는다.
            BlockedThisFrame = false;
            if (_blockFrame == Time.frameCount && _blockDir.sqrMagnitude > 1e-4f)
            {
                Vector2 d = _blockDir.normalized;
                float outward = Vector2.Dot(h, d);
                if (outward > 0f)
                {
                    h -= d * outward;
                    move.SetVelocity(h);   // 적분기 상태도 맞춰야 다음 프레임에 되살아나지 않는다
                    move.ClearBoost();
                    BlockedThisFrame = true;
                }
            }

            Vector3 posBefore = transform.position;
            Vector3 intendedDelta = new Vector3(h.x, _vy, h.y) * dt;
            CollisionFlags flags = _cc.Move(intendedDelta);
            bool below = (flags & CollisionFlags.Below) != 0;

            // 의도치 않은 밀림 감지 — CharacterController.Move()는 겹친 오브젝트를 자동으로
            // 밀어내는데(depenetration), 그 보정량은 내가 의도한 이동과 무관하다. "실제로 움직인 양"에서
            // "내가 의도한 양"을 빼면 남는 게 순수하게 외부(레일에 붙은 충돌 오브젝트 등, 소스 불문)가
            // 밀어낸 양이다. 이걸 MotionFeel에 넘기면 지하철 스웨이가 소스 상관없이 자동으로 붙고,
            // MotionFeel 쪽의 짧은 카메라 흡수 지연이 뚝뚝 끊기는 걸 완화한다.
            //
            // ※ 수평(XZ)만 본다 — 접지 중엔 _vy=-2f로 매 프레임 살짝 눌러붙이는데 바닥이 그걸
            // 막아버려서 intendedDelta.y와 actualDelta.y가 항상 약간 어긋난다(정상 동작). 그걸 수직까지
            // 잔차로 잡으면 가만히 서 있기만 해도 매 프레임 오탐지가 나 미약하게 계속 떨린다.
            Vector3 actualDelta = transform.position - posBefore;
            Vector3 extra = actualDelta - intendedDelta;
            extra.y = 0f;
            if (extra.sqrMagnitude > unexpectedPushThreshold * unexpectedPushThreshold && _feel != null)
                _feel.OnCarried(extra);

            // 착지 감지 — 강도는 "얼마나 빨리 떨어지고 있었나"(|vy|). 등반 완료 직후엔 억제됨.
            if (below && !_groundedBelow && prevVy < 0f
                && Time.time >= _suppressLandUntil && _feel != null)
                _feel.OnLand(Mathf.Abs(prevVy));

            _groundedBelow = below;
            _wasGrounded = below;
        }

        /// <summary>실제 아래 접촉 기준 접지(모서리 스침 제외). 자동 도약 판정도 이걸 쓴다.</summary>
        public bool Grounded => _groundedBelow;

        bool _groundedBelow;
    }
}
