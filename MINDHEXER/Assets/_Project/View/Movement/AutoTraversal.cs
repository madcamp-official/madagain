using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 자동 지형 통과 — <b>버튼 없이</b> 앞으로 걸어가기만 하면 처리된다.
    ///
    /// <para><b>① 난간 오르기(mantle)</b>: 마인크래프트 자동 점프처럼, 아슬아슬한 턱만이 아니라
    /// <b>키보다 조금 높은 높이(~2m)까지</b> 앞으로 밀면 손으로 잡고 올라간다. 매끄럽게 미끄러져
    /// 올라가는 게 아니라 침하→당김→올라서기 3단계로 사람이 힘을 쓰는 것처럼 움직인다.</para>
    ///
    /// <para><b>② 틈 건너뛰기(hop)</b>: 발판 끝에서 앞이 비어 있고 건너편에 착지할 곳이 있으면
    /// 자동으로 도약한다.</para>
    ///
    /// <para><b>VR 원칙</b>: 위치만 스크립트가 몰고 <b>회전은 절대 건드리지 않는다</b>(머리 트래킹 소유).
    /// 롤(기울기)도 넣지 않는다 — VR 멀미 최대 유발 요인이다. 등반 중에도 자유롭게 둘러볼 수 있다.</para>
    ///
    /// <para>낮은 턱(CharacterController.stepOffset 이하)은 엔진이 이미 처리하므로 여기선 그 위부터 본다.</para>
    /// </summary>
    [RequireComponent(typeof(FirstPersonPlayer))]
    [RequireComponent(typeof(CharacterController))]
    public class AutoTraversal : MonoBehaviour
    {
        enum Phase { None, Dip, Pull, Step }

        [Header("공통")]
        [Tooltip("지형 판정 레이어. 플레이어 자신은 빼는 게 안전하다.")]
        public LayerMask obstacleMask = ~0;

        [Tooltip("이 속도(m/s) 미만으로 움직이면 아무것도 발동하지 않는다.")]
        public float minSpeed = 0.6f;

        [Tooltip("동작 종료 후 재발동까지 쉬는 시간(초).")]
        public float cooldown = 0.15f;

        [Header("난간 오르기 — 판정")]
        [Tooltip("오를 수 있는 최소 높이(발 기준). CharacterController.stepOffset보다 커야 한다.")]
        public float minHeight = 0.35f;

        [Tooltip("오를 수 있는 최대 높이(발 기준). 눈높이 1.6 기준 2.0이면 키보다 조금 위.")]
        public float maxHeight = 2.0f;

        [Tooltip("전방 벽을 찾는 거리(m).")]
        public float wallProbeDistance = 0.8f;

        [Tooltip("벽 판정 허용 오차. 법선 y가 이보다 크면 경사면으로 보고 무시한다.")]
        [Range(0f, 0.6f)] public float wallNormalTolerance = 0.3f;

        [Tooltip("올라설 윗면의 최소 평탄도(법선 y). 1에 가까울수록 평평한 면만 허용.")]
        [Range(0.3f, 1f)] public float minFlatness = 0.7f;

        [Tooltip("벽 안쪽으로 이만큼 들어간 지점에서 윗면을 찾는다(모서리 오판 방지).")]
        public float surfaceInset = 0.25f;

        [Tooltip("착지 지점에서 앞으로 이만큼 더 들어가 선다.")]
        public float landingForward = 0.35f;

        [Header("난간 오르기 — 동작")]
        [Tooltip("당기기 직전 몸이 가라앉는 깊이(m). 힘을 싣는 예비 동작. 크면 멀미 유발.")]
        public float dipDepth = 0.08f;

        [Tooltip("침하 시간(초).")]
        public float dipDuration = 0.12f;

        [Tooltip("당김 시간 — 최소 높이일 때(초).")]
        public float pullDurationMin = 0.30f;

        [Tooltip("당김 시간 — 최대 높이일 때(초). 높을수록 힘겹게.")]
        public float pullDurationMax = 0.90f;

        [Tooltip("올라서기 시간(초).")]
        public float stepDuration = 0.18f;

        [Tooltip("당김 단계에서 미리 앞으로 붙는 비율(0~1). 나머지는 올라서기에서 처리.")]
        [Range(0f, 1f)] public float pullForwardFraction = 0.3f;

        [Tooltip("침하 곡선.")]
        public AnimationCurve dipCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("당김 곡선. 초반 빠르고 팔이 펴지며 감속하는 형태가 사실적이다.")]
        public AnimationCurve pullCurve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 2.2f), new Keyframe(1f, 1f, 0f, 0f));

        [Tooltip("올라서기 곡선.")]
        public AnimationCurve stepCurve = new AnimationCurve(new Keyframe(0f, 0f, 0f, 1.6f), new Keyframe(1f, 1f, 0f, 0f));

        [Header("틈 건너뛰기")]
        [Tooltip("끈다면 난간 오르기만 동작한다.")]
        public bool enableGapHop = true;

        [Tooltip("발 앞 이 거리에 땅이 없으면 '틈'으로 본다(m).")]
        public float gapCheckDistance = 0.7f;

        [Tooltip("틈으로 인정할 최소 낙차(m). 이보다 얕으면 그냥 걸어간다.")]
        public float gapMinDrop = 0.6f;

        [Tooltip("건너뛸 수 있는 최대 거리(m).")]
        public float maxHopDistance = 3.5f;

        [Tooltip("착지 지점이 발보다 이보다 많이 낮으면 건너뛰지 않는다(그냥 떨어짐).")]
        public float maxHopDrop = 1.5f;

        [Tooltip("도약 수직 속도(m/s). 포물선 높이를 정한다.")]
        public float hopVerticalSpeed = 4.5f;

        [Tooltip("계산된 수평 속도에 곱하는 여유분. 1보다 조금 크게 잡아야 걸치지 않는다.")]
        public float hopSpeedMargin = 1.12f;

        FirstPersonPlayer _fpp;
        CharacterController _cc;

        Phase _phase = Phase.None;
        float _t, _phaseLen, _pullLen, _cooldownLeft;
        Vector3 _start, _target, _dipped, _pullEnd;

        public bool IsMantling => _phase != Phase.None;

        void Awake()
        {
            _fpp = GetComponent<FirstPersonPlayer>();
            _cc = GetComponent<CharacterController>();
        }

        /// <summary>발밑(캡슐 바닥) 월드 좌표.</summary>
        Vector3 Feet => transform.position + _cc.center - Vector3.up * (_cc.height * 0.5f);

        /// <summary>발 위치 → transform.position 으로 되돌리는 Y 오프셋.</summary>
        float FeetToOrigin => -_cc.center.y + _cc.height * 0.5f;

        void Update()
        {
            float dt = Time.deltaTime;

            if (_phase != Phase.None) { TickMantle(dt); return; }

            if (_cooldownLeft > 0f) { _cooldownLeft -= dt; return; }
            if (!_cc.isGrounded) return;

            Vector2 v = _fpp.move.Velocity;
            if (v.magnitude < minSpeed) return;
            Vector3 dir = new Vector3(v.x, 0f, v.y).normalized;

            if (TryBeginMantle(dir)) return;
            if (enableGapHop) TryGapHop(dir);
        }

        // ── ① 난간 오르기 ─────────────────────────────────────────────────

        bool TryBeginMantle(Vector3 dir)
        {
            Vector3 feet = Feet;

            // 가슴 높이에서 전방 벽 찾기. 벽이 아니라 경사면이면 그냥 걸어 올라가면 되므로 제외.
            Vector3 probe = feet + Vector3.up * Mathf.Min(1.0f, maxHeight * 0.5f);
            if (!Physics.Raycast(probe, dir, out RaycastHit wall, wallProbeDistance,
                                 obstacleMask, QueryTriggerInteraction.Ignore)) return false;
            if (Mathf.Abs(wall.normal.y) > wallNormalTolerance) return false;

            // 벽 너머 위쪽에서 아래로 쏴 올라설 윗면 찾기.
            Vector3 over = wall.point + dir * surfaceInset + Vector3.up * (maxHeight + 0.3f);
            if (!Physics.Raycast(over, Vector3.down, out RaycastHit top, maxHeight + 0.6f,
                                 obstacleMask, QueryTriggerInteraction.Ignore)) return false;
            if (top.normal.y < minFlatness) return false;

            float height = top.point.y - feet.y;
            if (height < minHeight || height > maxHeight) return false;

            // 착지 지점에 몸이 들어갈 공간이 있는지(머리 공간 포함).
            Vector3 landFeet = top.point + dir * landingForward + Vector3.up * 0.03f;
            if (Blocked(landFeet)) return false;

            BeginMantle(landFeet + Vector3.up * FeetToOrigin, height);
            return true;
        }

        bool Blocked(Vector3 feetPos)
        {
            float r = _cc.radius * 0.95f;
            Vector3 p0 = feetPos + Vector3.up * r;
            Vector3 p1 = feetPos + Vector3.up * (_cc.height - r);
            return Physics.CheckCapsule(p0, p1, r, obstacleMask, QueryTriggerInteraction.Ignore);
        }

        void BeginMantle(Vector3 target, float height)
        {
            _start = transform.position;
            _target = target;
            _dipped = _start + Vector3.down * dipDepth;

            // 높을수록 오래 — 같은 속도로 오르면 높이에 상관없이 똑같이 느껴져 무게감이 죽는다.
            float k = Mathf.InverseLerp(minHeight, maxHeight, height);
            _phaseLen = dipDuration;
            _t = 0f;
            _phase = Phase.Dip;
            _pullLen = Mathf.Lerp(pullDurationMin, pullDurationMax, k);

            _fpp.ExternalMotion = true;
            _fpp.VerticalVelocity = 0f;
            _fpp.move.Reset();
            _cc.enabled = false;          // 위치를 직접 몬다(진입 전 공간 검증을 이미 마쳤다)
        }

        void TickMantle(float dt)
        {
            _t += dt;
            float u = _phaseLen > 0f ? Mathf.Clamp01(_t / _phaseLen) : 1f;

            switch (_phase)
            {
                case Phase.Dip:
                    transform.position = Vector3.Lerp(_start, _dipped, dipCurve.Evaluate(u));
                    if (u >= 1f) { _phase = Phase.Pull; _t = 0f; _phaseLen = _pullLen; }
                    break;

                case Phase.Pull:
                {
                    float c = pullCurve.Evaluate(u);
                    Vector3 flat = new Vector3(_target.x - _dipped.x, 0f, _target.z - _dipped.z) * pullForwardFraction;
                    Vector3 p = _dipped + flat * c;
                    p.y = Mathf.Lerp(_dipped.y, _target.y, c);
                    transform.position = p;
                    if (u >= 1f) { _pullEnd = p; _phase = Phase.Step; _t = 0f; _phaseLen = stepDuration; }
                    break;
                }

                case Phase.Step:
                    transform.position = Vector3.Lerp(_pullEnd, _target, stepCurve.Evaluate(u));
                    if (u >= 1f) EndMantle();
                    break;
            }
        }

        void EndMantle()
        {
            transform.position = _target;
            _cc.enabled = true;
            _fpp.ExternalMotion = false;
            _fpp.VerticalVelocity = 0f;
            _fpp.move.Reset();            // 올라선 뒤 튀어나가지 않게
            _phase = Phase.None;
            _cooldownLeft = cooldown;
        }

        // ── ② 틈 건너뛰기 ─────────────────────────────────────────────────

        void TryGapHop(Vector3 dir)
        {
            Vector3 feet = Feet;

            // 바로 앞에 땅이 있으면 틈이 아니다.
            Vector3 edge = feet + dir * gapCheckDistance + Vector3.up * 0.1f;
            if (Physics.Raycast(edge, Vector3.down, gapMinDrop + 0.1f, obstacleMask, QueryTriggerInteraction.Ignore))
                return;

            // 건너편 착지 지점 탐색.
            for (float d = gapCheckDistance + 0.25f; d <= maxHopDistance; d += 0.25f)
            {
                Vector3 p = feet + dir * d + Vector3.up * 0.5f;
                if (!Physics.Raycast(p, Vector3.down, out RaycastHit land, 0.5f + maxHopDrop,
                                     obstacleMask, QueryTriggerInteraction.Ignore)) continue;
                if (land.normal.y < minFlatness) continue;
                if (land.point.y < feet.y - maxHopDrop) continue;
                if (Blocked(land.point + Vector3.up * 0.03f)) continue;

                Hop(dir, d);
                return;
            }
        }

        void Hop(Vector3 dir, float distance)
        {
            // 같은 높이로 돌아오는 데 걸리는 시간 동안 distance를 덮을 수평 속도.
            float airTime = 2f * hopVerticalSpeed / Mathf.Max(0.01f, _fpp.gravity);
            float need = distance / Mathf.Max(0.01f, airTime) * hopSpeedMargin;

            Vector2 h = new Vector2(dir.x, dir.z) * Mathf.Max(need, _fpp.move.Velocity.magnitude);
            _fpp.Launch(h, hopVerticalSpeed);
            _cooldownLeft = cooldown;
        }
    }
}
