using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 펫 거미 리그 — 손목 위 상시 동반자이자 <b>해킹의 물리적 실체</b>.
    /// 설계: `docs/KJH/design/플레이어_신체표현_설계.md` §6 / `기초_설계안.md` §2.2·§2.6
    ///
    /// <para><b>본에 붙이지 않는다</b>(§6.1). 거미는 장식이 아니라 빙의 시 대상으로 날아가고
    /// 릴레이로 옮겨 다니며 줄이 그 엉덩이에서 나간다. 본 자식으로 묶으면 <b>떨어져 나갈 수 없다.</b>
    /// 대신 <b>소프트 추종 제약</b>으로 간다 — 위치는 앵커를 스프링으로 따라가되 언제든 이탈 가능하다.
    /// 부모 재설정이 없으므로 전환에 팝이 없고, 스프링 지연이 곧 생명감이다.</para>
    ///
    /// <para><b>회전 안정화 — 독수리 원리</b>(§6.3). 매잡이의 팔이 기울어도 매는 수평을 유지한다.
    /// 그냥 추종하면 손목이 꺾일 때 거미가 같이 뒤집힌다(HandIK는 손목을 ±85°까지 비튼다).
    /// 그래서 <b>yaw만 따라가고 roll/pitch는 감쇠</b>한다.</para>
    ///
    /// <para><b>HackDriver는 읽기만 한다</b> — 그쪽 파일은 건드리지 않는다.</para>
    ///
    /// <para><b>다리 움직임은 이번 범위 밖</b>(§6.5) — 모델에 뼈가 0개다. 리깅이 오면
    /// <see cref="legIkRoot"/> 아래에 다리 IK를 얹는다.</para>
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public class SpiderRig : MonoBehaviour
    {
        public enum State { Perched, Launching, Attached, Returning }

        [Header("부착")]
        [Tooltip("손목 위 자리. 비우면 PlayerBodyParts의 오른손 뼈 아래에 자동 생성한다.")]
        public Transform perchAnchor;

        [Tooltip("실이 나오는 지점(방적돌기). 비우면 거미 뒤쪽으로 자동 생성한다.")]
        public Transform spinneretAnchor;

        [Tooltip("비우면 씬에서 찾는다.")]
        public PlayerBodyParts body;
        public HackDriver hack;

        [Header("추종 (소프트 제약 — 부모 자식이 아니다)")]
        [Tooltip("위치 추종 반응 속도. 클수록 딱 붙고, 작을수록 늘어져 따라온다.")]
        public float followSpeed = 14f;

        [Tooltip("회전 추종 반응 속도.")]
        public float rotateSpeed = 10f;

        [Header("자세 안정화 (독수리)")]
        [Tooltip("켜면 손목이 꺾여도 거미가 수평을 유지한다. 끄면 손목을 그대로 따라간다.")]
        public bool stabilize = true;

        [Tooltip("기울기(roll/pitch)를 얼마나 남길지. 0=완전 수평, 1=손목 그대로.")]
        [Range(0f, 1f)] public float tiltFollow = 0.15f;

        [Header("비행")]
        [Tooltip("대상으로 날아가는 시간(초).")]
        public float launchTime = 0.22f;
        [Tooltip("손목으로 돌아오는 시간(초).")]
        public float returnTime = 0.28f;
        [Tooltip("비행 궤적이 위로 부푸는 정도(m). 0이면 직선.")]
        public float flightArc = 0.35f;

        [Header("몸통 들썩임 (다리는 팔에 고정된 채 몸만 움직인다)")]
        [Tooltip("숨 쉬듯 위아래로 움직이는 폭(m). 다리 IK가 이걸 따라 굽었다 펴진다.")]
        public float bobAmount = 0.004f;
        [Tooltip("들썩임 주기(Hz).")]
        public float bobRate = 0.6f;
        [Tooltip("좌우로 무게중심을 옮기는 폭(m). 주기는 위아래와 어긋난다.")]
        public float shiftAmount = 0.002f;
        [Tooltip("몸통이 앞뒤로 까딱이는 각도(도).")]
        public float bobPitch = 1.5f;

        [Header("줄 발사 자세 (엉덩이를 들어 줄과 각도를 맞춘다)")]
        [Tooltip("엉덩이(방적돌기가 달린 마디) 뼈. 비우면 몸통 전체를 기울인다.")]
        public Transform abdomen;
        [Tooltip("실이 나갈 때 엉덩이가 대상 쪽으로 도는 정도. 1이면 정확히 겨눔.")]
        [Range(0f, 1f)] public float aimStrength = 0.8f;
        [Tooltip("조준 자세로 들어가고 나오는 속도.")]
        public float aimSpeed = 9f;
        [Tooltip("엉덩이의 어느 축이 실 방향인가. 보통 -Z(뒤쪽) 또는 +Y.")]
        public Vector3 spinneretAxis = new Vector3(0f, 0f, -1f);

        [Header("다리 (리깅 후 연결)")]
        [Tooltip("비우면 자식에서 찾는다. 비행 중에는 자동으로 가중치를 0으로 내린다.")]
        public SpiderLegs legs;

        [Header("디버그")]
        public bool drawGizmos = true;
        public bool logState;

        // ── 상태 ──
        State _state = State.Perched;
        float _t;
        Vector3 _flightFrom;
        Transform _flightTarget;
        ControlTether _tether;
        float _bobTime;
        float _aim;                       // 조준 자세 강도 0~1
        Quaternion _abdomenRest;
        bool _abdomenCaptured;

        public State Current => _state;

        void Awake()
        {
            if (body == null) body = FindFirstObjectByType<PlayerBodyParts>();
            if (hack == null) hack = FindFirstObjectByType<HackDriver>();
            if (hack != null) _tether = hack.tether;
            if (legs == null) legs = GetComponentInChildren<SpiderLegs>(true);
            if (abdomen != null) { _abdomenRest = abdomen.localRotation; _abdomenCaptured = true; }
        }

        void Start()
        {
            EnsureAnchors();
            SnapToPerch();
        }

        // ── 앵커 준비 ────────────────────────────────────────────────────

        void EnsureAnchors()
        {
            if (perchAnchor == null && body != null)
            {
                body.AutoFindBones();
                if (body.rightHand != null)
                {
                    var go = new GameObject("[SpiderPerch]");
                    go.transform.SetParent(body.rightHand, false);
                    // 손등 위쪽. 실제 값은 실기에서 맞춘다(§11).
                    go.transform.localPosition = new Vector3(0f, 0.04f, 0.02f);
                    perchAnchor = go.transform;
                }
            }

            if (spinneretAnchor == null)
            {
                var go = new GameObject("[Spinneret]");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, 0f, -0.04f);   // 거미 뒤쪽(엉덩이)
                spinneretAnchor = go.transform;
            }
        }

        void SnapToPerch()
        {
            if (perchAnchor == null) return;
            transform.SetPositionAndRotation(perchAnchor.position, PerchRotation());
        }

        // ── 상태 판정 ────────────────────────────────────────────────────

        void Update()
        {
            UpdateState();
            UpdateTetherOrigin();
            UpdateAim();
            UpdateLegWeight();
            _bobTime += Time.deltaTime;
        }

        /// <summary>실이 나가는 중이면 조준 자세로. 아니면 평상 자세로.</summary>
        void UpdateAim()
        {
            bool firing = _tether != null && _tether.Active && _state != State.Attached;
            float want = firing ? 1f : 0f;
            _aim = Mathf.Lerp(_aim, want, 1f - Mathf.Exp(-aimSpeed * Time.deltaTime));
        }

        /// <summary>날아가는 동안에는 다리를 팔에서 뗀다 — 앵커가 뒤에 남아 다리가 찢어진다.</summary>
        void UpdateLegWeight()
        {
            if (legs == null) return;
            bool onArm = _state == State.Perched;
            legs.TargetWeight = onArm ? 1f : 0f;
        }

        /// <summary>
        /// 빙의 중이면 대상으로, 아니면 손목에.
        /// <para>외부 조종에서는 <b>손목에 남는다</b> — 거미가 대상으로 가버리면 실 길이가 0이 된다.
        /// §2.6의 "실 문 채 자세 잡음(손↔대상 연결 유지)"와 일치한다.</para>
        /// </summary>
        void UpdateState()
        {
            bool possessing = hack != null && hack.viewEntry != null && hack.viewEntry.Active;
            Transform target = possessing && hack.Controlled != null ? hack.Controlled.transform : null;

            switch (_state)
            {
                case State.Perched:
                    if (possessing && target != null) BeginLaunch(target);
                    break;

                case State.Launching:
                    _t += Time.deltaTime;
                    if (!possessing) { BeginReturn(); break; }
                    if (_t >= launchTime) { _state = State.Attached; _flightTarget = target; Log(); }
                    break;

                case State.Attached:
                    if (!possessing) BeginReturn();
                    else _flightTarget = target;   // 릴레이로 대상이 바뀌면 따라간다
                    break;

                case State.Returning:
                    _t += Time.deltaTime;
                    if (possessing && target != null) { BeginLaunch(target); break; }
                    if (_t >= returnTime) { _state = State.Perched; Log(); }
                    break;
            }
        }

        void BeginLaunch(Transform target)
        {
            _flightFrom = transform.position;
            _flightTarget = target;
            _state = State.Launching;
            _t = 0f;
            Log();
        }

        void BeginReturn()
        {
            _flightFrom = transform.position;
            _state = State.Returning;
            _t = 0f;
            Log();
        }

        void Log() { if (logState) Debug.Log($"[SpiderRig] → {_state}"); }

        /// <summary>
        /// 줄이 어디서 나오는가. 거미가 <b>플레이어 쪽에 있을 때만</b> 엉덩이를 시작점으로 넘긴다.
        /// 대상에 붙어 있을 땐 비워야 한다 — 안 그러면 실이 대상→대상이 되어 길이가 0이다.
        /// </summary>
        void UpdateTetherOrigin()
        {
            if (_tether == null && hack != null) _tether = hack.tether;
            if (_tether == null) return;

            bool nearPlayer = _state != State.Attached;
            _tether.originOverride = nearPlayer ? spinneretAnchor : null;
        }

        // ── 적용 ─────────────────────────────────────────────────────────

        void LateUpdate()
        {
            Vector3 wantPos;
            Quaternion wantRot;
            bool snap = false;

            switch (_state)
            {
                case State.Perched:
                {
                    if (perchAnchor == null) return;
                    // 다리 끝은 팔에 고정돼 있으므로, 여기서 몸통을 흔들면 다리가 IK로 굽었다 펴진다.
                    float p = _bobTime * Mathf.PI * 2f;
                    float up   = Mathf.Sin(p * bobRate) * bobAmount;
                    float side = Mathf.Sin(p * bobRate * 0.63f + 1.1f) * shiftAmount;   // 위아래와 어긋내야 기계적이지 않다
                    float pitch = Mathf.Sin(p * bobRate + 0.8f) * bobPitch;

                    wantRot = PerchRotation() * Quaternion.Euler(pitch, 0f, 0f);
                    wantPos = perchAnchor.position + perchAnchor.up * up + perchAnchor.right * side;
                    break;
                }

                case State.Launching:
                {
                    if (_flightTarget == null) return;
                    float u = Mathf.Clamp01(launchTime <= 0f ? 1f : _t / launchTime);
                    wantPos = Arc(_flightFrom, _flightTarget.position, u);
                    wantRot = LookAlong(_flightTarget.position - transform.position);
                    snap = true;   // 비행은 스프링 없이 궤적 그대로
                    break;
                }

                case State.Attached:
                    if (_flightTarget == null) return;
                    wantPos = _flightTarget.position;
                    wantRot = _flightTarget.rotation;
                    break;

                default:   // Returning
                {
                    if (perchAnchor == null) return;
                    float u = Mathf.Clamp01(returnTime <= 0f ? 1f : _t / returnTime);
                    wantPos = Arc(_flightFrom, perchAnchor.position, u);
                    wantRot = LookAlong(perchAnchor.position - transform.position);
                    snap = true;
                    break;
                }
            }

            if (snap)
            {
                transform.SetPositionAndRotation(wantPos, wantRot);
            }
            else
            {
                // 소프트 추종 — 프레임률 무관 지수 수렴
                float dt = Time.deltaTime;
                transform.position = Vector3.Lerp(transform.position, wantPos, 1f - Mathf.Exp(-followSpeed * dt));
                transform.rotation = Quaternion.Slerp(transform.rotation, wantRot, 1f - Mathf.Exp(-rotateSpeed * dt));
            }

            ApplyAim();
        }

        /// <summary>
        /// 실이 나갈 때 엉덩이를 들어 줄 방향과 각도를 맞춘다(§2.6 "방적돌기에서 실 발사").
        /// 몸통 자체는 다리에 붙들려 있으므로 <b>엉덩이 마디만</b> 돌린다.
        /// </summary>
        void ApplyAim()
        {
            if (abdomen == null || _aim <= 0.001f || _tether == null) return;
            if (!_abdomenCaptured) { _abdomenRest = abdomen.localRotation; _abdomenCaptured = true; }

            Vector3 toTarget = _tether.EndPoint - abdomen.position;
            if (toTarget.sqrMagnitude < 1e-6f) { abdomen.localRotation = _abdomenRest; return; }

            // 방적돌기 축이 대상을 향하도록 돌린다.
            Vector3 axis = spinneretAxis.sqrMagnitude < 1e-6f ? Vector3.back : spinneretAxis.normalized;
            Vector3 curDir = abdomen.TransformDirection(axis);
            Quaternion delta = Quaternion.FromToRotation(curDir, toTarget.normalized);
            Quaternion aimed = delta * abdomen.rotation;

            // rest ↔ 조준 사이를 강도만큼 섞는다.
            abdomen.localRotation = Quaternion.Slerp(_abdomenRest, ToLocal(abdomen, aimed), _aim * aimStrength);
        }

        static Quaternion ToLocal(Transform t, Quaternion world) =>
            t.parent != null ? Quaternion.Inverse(t.parent.rotation) * world : world;

        /// <summary>
        /// 손목 위에서의 목표 회전. 안정화가 켜져 있으면 <b>yaw만</b> 따라가고 기울기는 감쇠한다 —
        /// 손목이 뒤집혀도 거미는 거의 수평을 유지한다(§6.3).
        /// </summary>
        Quaternion PerchRotation()
        {
            if (perchAnchor == null) return transform.rotation;
            if (!stabilize) return perchAnchor.rotation;

            // 손목의 정면을 수평면에 투영해 yaw만 뽑는다.
            Vector3 fwd = perchAnchor.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f)
            {
                fwd = perchAnchor.up;   // 손목이 수직이면 up이 수평에 가깝다
                fwd.y = 0f;
            }
            if (fwd.sqrMagnitude < 1e-6f) return transform.rotation;

            Quaternion level = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            // tiltFollow만큼만 실제 손목 기울기를 섞는다.
            return Quaternion.Slerp(level, perchAnchor.rotation, tiltFollow);
        }

        static Quaternion LookAlong(Vector3 dir) =>
            dir.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(dir.normalized, Vector3.up) : Quaternion.identity;

        /// <summary>위로 부푼 비행 궤적. 직선으로 쏘면 벌레가 아니라 총알로 보인다.</summary>
        Vector3 Arc(Vector3 a, Vector3 b, float u)
        {
            Vector3 p = Vector3.Lerp(a, b, u);
            p.y += Mathf.Sin(u * Mathf.PI) * flightArc;
            return p;
        }

        void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;
            if (perchAnchor != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(perchAnchor.position, 0.02f);
                Gizmos.DrawLine(perchAnchor.position, transform.position);
            }
            if (spinneretAnchor != null)
            {
                Gizmos.color = new Color(0.4f, 1f, 0.3f);
                Gizmos.DrawWireSphere(spinneretAnchor.position, 0.012f);
            }
        }
    }
}
