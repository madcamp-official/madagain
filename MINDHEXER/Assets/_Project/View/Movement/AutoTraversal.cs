using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 자동 지형 통과 — <b>버튼 없이</b> 전부 자동. 판정은 3중 게이트로 엄격하게(후한 판정은 오히려 불편).
    ///
    /// <para><b>자동 도약</b>: 실제 낙차가 있는 가장자리에서, <b>이동 방향</b> 원뿔 안의
    /// <see cref="ClimbLedge"/> 중 <b>가장 가까운</b> 도달 가능 목표로 자동 도약한다(원뿔은 필터로만 쓴다 —
    /// "가는 방향에 있는 것 중 제일 가까운 데로 뛴다"는 규칙 하나라 예측이 쉽다).
    /// 시선은 선정에 관여하지 않는다 — 시선이 승인하면 뒤·옆 낙하 보호가 뚫린다(decisions/0004).
    ///  · 목표가 낮거나 조금 위 → 한 번의 도약으로 착지
    ///  · 키보다 높음(≤maxMantleUp) → 도약으로 모서리를 잡고(손 월드 고정) 당겨 올라감
    ///  · 도달 가능한 목표가 없음 → 낙차가 <see cref="maxSafeFall"/> 이하면 <b>그냥 떨어지고</b>,
    ///    그보다 깊거나 바닥이 아예 없으면 <b>가장자리 정지</b>(바깥 속도를 깎아 안 떨어진다).</para>
    ///
    /// <para><b>걸어서 오르기</b>: 가장자리가 아니어도, 눈앞의 ClimbLedge를 향해 밀면 자동으로 오른다.</para>
    ///
    /// <para><b>이어진 발판</b>: 가장자리 판정이 콜라이더 경계가 아니라 <b>낙차</b>(safeDrop 이상) 기준이라
    /// 붙어 있는 바닥 이음매에서는 아무것도 발동하지 않는다.</para>
    ///
    /// <para><b>궤적</b>: 수평 = 진행 방향으로 치우친 베지어(몸이 관성을 이기고 휘어 들어가는 대각선),
    /// 수직 = 정점 보장 탄도(모서리를 긁지 않음). <b>이징은 쓰지 않는다</b> — 탄도에 가속·감속이 이미
    /// 물리로 들어 있어서, 시간 워핑을 얹으면 정점에서 감속이 이중으로 걸려 멈칫하고 중력감이 죽는다.
    /// 비행 시간은 실제 이동량으로 정하고 중력은 거기서 역산되므로, 세기는 <see cref="airSpeedCap"/>과
    /// 비행 시간 상·하한으로 조절한다.</para>
    ///
    /// <para><b>비행 중에는 CharacterController를 끄고</b> 위치를 직접 몬다(켠 채 몰면 벽에 밀려 도착 오차가
    /// 나 중단·재발동을 반복했다). 대신 출발 전에 착지·매달림 지점뿐 아니라 <b>궤적 중간까지 캡슐로 검사</b>해
    /// 지형을 뚫고 지나가 퍼즐을 건너뛰는 일이 없게 한다(<see cref="pathSamples"/>).</para>
    ///
    /// <para><b>연출 분리</b>: 발구름=OnJumpLaunch(높이), 순수 착지=OnLand(착지 속도), 잡고 올라가기
    /// 완료=OnMantleFinish(고정 소량) — 높이 올라갔다고 큰 착지 흔들림이 나오지 않는다.
    /// 당김 중에는 좌우 교차 롤 스웨이. VR에선 MotionFeel이 알아서 감쇠.</para>
    ///
    /// <para><b>VR</b>: 위치만 몰고 회전은 절대 건드리지 않는다(머리 트래킹 소유).</para>
    /// </summary>
    [DefaultExecutionOrder(-5)]   // FirstPersonPlayer보다 먼저 — 가장자리 정지가 같은 프레임 이동 전에 걸리게
    [RequireComponent(typeof(FirstPersonPlayer))]
    [RequireComponent(typeof(CharacterController))]
    public class AutoTraversal : MonoBehaviour
    {
        enum State { Idle, Flight, Pull, Over }

        [Header("공통")]
        [Tooltip("지형 판정 레이어.")]
        public LayerMask obstacleMask = ~0;

        [Tooltip("착지 후 관성으로 이어받을 최소 속도(m/s). 발동 여부 자체는 '입력'으로 판정한다.")]
        public float minSpeed = 0.6f;

        [Tooltip("전진이 끊겨도 이 시간(초) 안이면 발동 허용(선입력 손맛).")]
        public float inputBuffer = 0.2f;

        [Tooltip("동작 종료 후 재발동까지 쉬는 시간(초).")]
        public float cooldown = 0.15f;

        [Header("가장자리(낙차 기준 — 이어진 발판 이음매에선 발동 안 함)")]
        [Tooltip("발 앞 이 거리(m)에서 낙차를 검사한다.")]
        public float edgeProbeAhead = 0.45f;

        [Tooltip("이보다 얕은 낙차는 그냥 걷는다(이어진 바닥·stepOffset급 턱).")]
        public float safeDrop = 0.6f;

        [Tooltip("도약 목표가 없을 때 이 낙차까지는 그냥 떨어지게 둔다. 넘거나 바닥이 없으면 가장자리에서 멈춘다.")]
        public float maxSafeFall = 2.5f;

        [Header("시선 도약")]
        [Tooltip("도약 목표 검색 반경(m).")]
        public float jumpSearchRadius = 8f;

        [Tooltip("도약 원뿔 반각(도) — 축은 <b>이동 방향</b>(수평). 시선은 관여하지 않는다(decisions/0004).")]
        public float coneAngle = 25f;

        [Tooltip("수평 속도 상한(m/s). 비행 시간 = max(탄도 시간, 거리/이 값) — 먼 도약이 총알처럼 빠르지 않게.")]
        public float airSpeedCap = 7f;

        [Tooltip("비행 시간 하한(초). 짧은 도약이 순간이동처럼 보이지 않게.")]
        public float minFlightTime = 0.18f;

        [Tooltip("비행 시간 상한(초). 넘으면 중력을 키워 시간을 줄인다 — 정점·착지점은 그대로.")]
        public float maxFlightTime = 0.9f;

        [Tooltip("이 높이(m)까지는 한 번의 도약으로 바로 올라선다.")]
        public float maxDirectUp = 1.1f;

        [Tooltip("이 높이(m)까지는 도약+잡고 올라가기로 처리. 이상이면 도달 불가.")]
        public float maxMantleUp = 2.0f;

        [Tooltip("목표가 발보다 이보다 많이 낮으면 후보에서 제외(그건 추락). 오르기(2m)와 비대칭인 게 정상이지만 과하면 아래가 너무 헐거워진다.")]
        public float maxDropTarget = 4f;

        [Tooltip("모서리 위 여유 높이(m).")]
        public float clearance = 0.35f;

        [Tooltip("궤적 중간 충돌 검사 샘플 수. 0이면 검사 안 함(지형 관통 허용 — 퍼즐 스킵 위험).")]
        [Range(0, 16)] public int pathSamples = 8;

        [Tooltip("수평 경로가 진행 방향으로 치우치는 정도(0=직선, 대각선 휘어짐).")]
        [Range(0f, 0.8f)] public float curveBias = 0.35f;

        [Header("걸어서 오르기")]
        [Tooltip("눈앞 ClimbLedge 탐지 반경(m).")]
        public float detectRadius = 1.3f;

        [Tooltip("오를 최소 높이. CharacterController.stepOffset(0.3) <b>이하</b>로 둔다 — " +
                 "더 높게 두면 그 사이 단차를 엔진도 못 넘고 등반도 거부해 아예 못 올라간다(사각지대).")]
        public float minHeight = 0.3f;

        [Tooltip("걸어서 오르기 전방 판정 반각(도). 등 뒤로 끌려가는 것만 막는 정도로 넉넉하게.")]
        public float walkUpConeAngle = 90f;

        [Tooltip("이 높이(m) 이하의 낮은 단차는 방향 판정을 건너뛴다 — 쳐다보지 않아도, 옆·뒤로 움직여도 넘어간다.")]
        public float lowStepHeight = 0.7f;

        [Header("잡고 올라가기(맨틀)")]
        [Tooltip("팔 길이(m) — 매달렸을 때 눈이 모서리에서 이만큼 아래.")]
        public float armLength = 0.55f;

        [Tooltip("모서리가 이 거리(m) 안이면 도약을 건너뛰고 선 자리에서 바로 잡는다. 0이면 항상 도약으로 붙는다.")]
        public float directLatchRange = 1f;

        public float pullDurationMin = 0.25f;
        public float pullDurationMax = 0.45f;
        [Tooltip("모서리를 넘어 올라서는 시간(초). 이 구간에서 발이 모서리 아래에서 윗면까지 한 번에 올라오므로 너무 짧으면 확 튄다.")]
        public float overDuration = 0.28f;

        [Tooltip("당김 중 좌우 교차 기울임 빈도(Hz) — 교차 횟수 = 당김 시간 × 이 값.")]
        public float swayFrequency = 3f;

        [Tooltip("교차 기울임 진폭(도).")]
        public float swayAmplitude = 2.5f;

        [Tooltip("동작 종료 시 전방 감쇠 임펄스(m/s) — 올라서자마자 달려나가게.")]
        public float exitBoost = 4.5f;

        [Tooltip("종료 임펄스 지속(초). 짧게 '탁' 밀고 즉시 일반 조작으로 넘어간다.")]
        public float exitBoostDuration = 0.08f;

        [Tooltip("이 거리(m) 미만의 짧은 도약은 화면 연출을 넣지 않는다 — 낮은 턱마다 화면이 흔들리지 않게.")]
        public float feelMinTravel = 1.2f;

        [Header("디버그")]
        public bool logDecisions;
        public bool drawGizmos = true;

        [Tooltip("접지가 풀리는 순간 직전 프레임들을 한꺼번에 콘솔에 덤프한다. 낙하 원인 추적용.")]
        public bool traceFall = true;

        [Tooltip("덤프할 프레임 수.")]
        [Range(5, 150)] public int traceLines = 45;

        FirstPersonPlayer _fpp;
        CharacterController _cc;
        MotionFeel _feel;
        MantleRig _rig;
        readonly Collider[] _overlap = new Collider[12];
        // BlockedAt 전용 버퍼 — _overlap을 순회하는 도중에 호출되므로 같은 배열을 쓰면 반복이 깨진다.
        readonly Collider[] _blockBuf = new Collider[8];
        // DropDepth 프로브 전용 — 위와 같은 이유로 따로 둔다.
        readonly Collider[] _probeBuf = new Collider[8];

        /// <summary>도약 후보 하나. 원뿔을 통과한 것들을 모아 거리순으로 훑는다.</summary>
        struct Cand
        {
            public ClimbLedge.GrabInfo grab;
            public Vector3 target;
            public float dist;      // 수평 거리 — 원뿔이 yaw 기준이라 높이는 따로 걸렀다
            public bool mantle;
            public Collider volume; // 오르려는 대상 자신 — 궤적 검사에서 제외
        }

        readonly List<Cand> _cands = new List<Cand>(16);
        static readonly System.Comparison<Cand> ByDistance = (x, y) => x.dist.CompareTo(y.dist);

        State _state = State.Idle;
        float _t, _cooldownLeft, _lastMoveTime, _entrySpeed;
        Vector3 _lastMoveDir;
        JumpArc _arc;
        bool _arcThenMantle;
        ClimbLedge.GrabInfo _grab;
        float _pullDur;
        Vector3 _pullStart, _pullEnd, _overEnd;

        public bool Busy => _state != State.Idle;

        void Awake()
        {
            _fpp = GetComponent<FirstPersonPlayer>();
            _cc = GetComponent<CharacterController>();
            _feel = GetComponent<MotionFeel>();
            _rig = GetComponent<MantleRig>();
        }

        Vector3 Feet => transform.position + _cc.center - Vector3.up * (_cc.height * 0.5f);
        float FeetToOrigin => -_cc.center.y + _cc.height * 0.5f;

        void Update()
        {
            float dt = Time.deltaTime;

            switch (_state)
            {
                case State.Flight: TickFlight(dt); return;
                case State.Pull:
                case State.Over: TickMantle(dt); return;
            }

            // 쿨다운은 도약·등반 '개시'만 막는다. 가장자리 정지까지 막으면 착지 직후 0.15초 동안
            // 낙하 방지가 비어 그대로 떨어진다.
            if (_cooldownLeft > 0f) _cooldownLeft -= dt;
            bool ready = _cooldownLeft <= 0f;

            // ★ 방향·발동은 <b>입력 의도</b>로 판정한다. 결과 속도로 판정하면 가장자리 정지가 자기를 끈다:
            //   속도를 깎음 → "안 움직인다"로 뒤집힘 → 정지 해제 → 재가속 → 조금씩 밀려 나가 끼임.
            //   (그 상태에선 moving=false라 도약 판정도 같이 죽는다)
            Vector2 w = _fpp.Wish;
            if (w.sqrMagnitude > 0.04f)
            {
                _lastMoveTime = Time.time;
                _lastMoveDir = new Vector3(w.x, 0f, w.y).normalized;
            }

            // 진입 속도(착지 후 관성)는 실제 속도에서 딴다.
            Vector2 v = _fpp.move.Output;
            if (v.magnitude >= minSpeed) _entrySpeed = v.magnitude;

            bool grounded = _fpp.Grounded;
            bool moving = Time.time - _lastMoveTime <= inputBuffer && _lastMoveDir.sqrMagnitude > 0.5f;

            // 접지가 풀리는 순간(= 떨어지기 시작) 직전 기록을 통째로 덤프한다.
            // 매 프레임 찍으면 콘솔이 넘쳐 정작 그 구간을 못 보므로, 링버퍼에 모아뒀다 한 번에 낸다.
            if (_prevGrounded && !grounded) DumpTrace();
            _prevGrounded = grounded;

            if (!moving || !grounded)
            {
                Trace(grounded, moving, w, v, "-", moving ? "스킵:공중" : "스킵:moving=false");
                return;
            }

            Vector3 dir = _lastMoveDir;

            float drop = DropDepth(dir, out bool bottomless);
            string d = (bottomless ? "INF" : drop.ToString("F2")) + "[" + _dropReason + "]";

            if (bottomless || drop > safeDrop)
            {
                if (ready && TryGazeJump()) { Trace(grounded, moving, w, v, d, "도약 성립"); return; }

                // 목표가 없다 — 감당 가능한 낙차면 그냥 떨어지게 둔다(아래층으로 내려가는 유일한 길).
                // 무저갱이거나 너무 깊을 때만 막는다.
                if (bottomless || drop > maxSafeFall)
                {
                    EdgeStop(dir);
                    Trace(grounded, moving, w, v, d, "EdgeStop 발동");
                }
                else
                {
                    Trace(grounded, moving, w, v, d,
                          $"의도적 낙하 허용(≤maxSafeFall {maxSafeFall:F1})" + (ready ? "" : " ·쿨다운"));
                }
                return;
            }

            Trace(grounded, moving, w, v, d, "평지");
            if (ready) TryWalkUp(dir);
        }

        string _dropReason = "";

        // ── 낙하 추적 링버퍼 ─────────────────────────────────────────────
        // "EdgeStop은 발동했는데 왜 떨어졌나"를 가르려면 그 전후 프레임의 실제 값이 필요하다.
        // 매 프레임 로그로 흘리면 스팸에 묻히므로 모아뒀다가 낙하 순간에만 덤프한다.

        readonly string[] _trace = new string[150];
        int _traceHead, _traceCount;
        bool _prevGrounded = true;

        void Trace(bool grounded, bool moving, Vector2 wish, Vector2 vel, string drop, string act)
        {
            if (!traceFall) return;
            Vector3 p = transform.position;
            _trace[_traceHead] = string.Format(
                "f{0} pos=({1:F2},{2:F2},{3:F2}) wish=({4:F2},{5:F2}) dir=({6:F2},{7:F2}) " +
                "mv={8} gnd={9} v=({10:F2},{11:F2}) vy={12:F2} blk={13} drop={14} -> {15}",
                Time.frameCount, p.x, p.y, p.z, wish.x, wish.y, _lastMoveDir.x, _lastMoveDir.z,
                moving ? "T" : "F", grounded ? "T" : "F", vel.x, vel.y, _fpp.VerticalVelocity,
                _fpp.BlockedThisFrame ? "T" : "F", drop, act);

            _traceHead = (_traceHead + 1) % _trace.Length;
            if (_traceCount < _trace.Length) _traceCount++;
        }

        void DumpTrace()
        {
            if (!traceFall || _traceCount == 0) return;

            int n = Mathf.Min(_traceCount, Mathf.Clamp(traceLines, 5, _trace.Length));
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[Fall] 접지 해제 — 직전 {n}프레임 (오래된 것부터):");
            for (int i = n; i >= 1; i--)
            {
                int idx = ((_traceHead - i) % _trace.Length + _trace.Length) % _trace.Length;
                if (_trace[idx] != null) sb.AppendLine(_trace[idx]);
            }
            Debug.Log(sb.ToString());
            _traceCount = 0;   // 같은 낙하로 연속 덤프되지 않게
        }

        // ── 가장자리: 낙차 기준 ───────────────────────────────────────────

        /// <summary>
        /// 발 앞의 낙차(m). 콜라이더 경계가 아니라 <b>실제 낙차</b>라 이어진 발판 이음매에선 0에 가깝다.
        /// 사거리 안에 바닥이 없으면 <paramref name="bottomless"/>=true(무저갱).
        /// </summary>
        float DropDepth(Vector3 dir, out bool bottomless)
        {
            Vector3 feet = Feet;

            // 발 높이에서 쏘면 코앞의 턱 <b>안에서</b> 출발하게 된다. 유니티는 시작점이 내부인 콜라이더를
            // 잡지 않으므로 아래 바닥을 못 찾고 무저갱으로 오판 → 가장자리 정지가 걸려 끼어버린다.
            //
            // 예전엔 "지형 안이면 앞이 벽이니 낭떠러지가 아니다"로 <b>단정</b>하고 낙차 0을 반환했다.
            // 그게 낭떠러지에서 오발동하면 그 프레임의 보호가 통째로 사라져, 판정이 깜빡일 때마다
            // 조금씩 밀려 결국 떨어졌다(실측). 이제는 포기하지 않고 <b>빈 공간이 나올 때까지 프로브를
            // 올려서 다시 검사</b>한다 — 앞이 벽이면 그 벽 윗면을 바닥으로 잡아 결과가 같고,
            // 낭떠러지면 정상적으로 무저갱이 나온다.
            const float BaseUp = 0.6f;
            const float StepUp = 0.4f;
            const int MaxLift = 6;         // 최대 0.6 + 0.4×6 = 3.0m까지 올려본다

            Vector2 flat = new Vector2(dir.x, dir.z);
            Vector3 ahead = feet + new Vector3(flat.x, 0f, flat.y).normalized * edgeProbeAhead;

            float up = BaseUp;
            string blocker = null;
            for (int i = 0; i <= MaxLift; i++)
            {
                Vector3 p = ahead + Vector3.up * up;
                Collider inside = OverlapAt(p);
                if (inside == null) break;              // 빈 공간 확보
                blocker = inside.name;
                up += StepUp;
                if (i == MaxLift)
                {
                    // 3m를 올려도 계속 지형 안 = 앞이 통짜 벽. 낭떠러지가 아니다.
                    bottomless = false;
                    _dropReason = $"3m까지 전부 지형 안(통짜 벽) @{blocker}";
                    return 0f;
                }
            }

            Vector3 origin = ahead + Vector3.up * up;
            float len = up + Mathf.Max(safeDrop, maxSafeFall) + 0.6f;

            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, len,
                                obstacleMask, QueryTriggerInteraction.Ignore))
            {
                bottomless = false;
                _dropReason = blocker == null
                    ? $"레이 적중 @{hit.collider.name}"
                    : $"레이 적중 @{hit.collider.name} (프로브 {up:F1}m로 올림 — 막던 것: {blocker})";
                return Mathf.Max(0f, feet.y - hit.point.y);
            }

            bottomless = true;
            _dropReason = blocker == null
                ? $"레이 {len:F1}m 안에 바닥 없음"
                : $"레이 {len:F1}m 안에 바닥 없음 (프로브 {up:F1}m로 올림 — 막던 것: {blocker})";
            return float.PositiveInfinity;
        }

        /// <summary>
        /// 그 지점이 지형 안인가. 막고 있는 콜라이더를 <b>이름까지</b> 돌려준다 —
        /// bool만 받던 <c>CheckSphere</c>로는 "무엇이 막았는지"를 알 수 없어 원인 추적이 막혔다.
        /// <b>플레이어 자신은 제외</b>한다(BlockedAt과 같은 규칙 — DropDepth만 빠져 있었다).
        /// </summary>
        Collider OverlapAt(Vector3 p)
        {
            int n = Physics.OverlapSphereNonAlloc(p, 0.06f, _probeBuf, obstacleMask,
                                                  QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = _probeBuf[i];
                if (c == null) continue;
                if (c.transform == transform || c.transform.IsChildOf(transform)) continue;
                return c;
            }
            return null;
        }

        // 클램프는 FPP가 적분을 끝낸 뒤에 적용한다 — 여기서 직접 깎으면 같은 프레임에 재가속돼 샌다.
        void EdgeStop(Vector3 dir) => _fpp.BlockDirection(new Vector2(dir.x, dir.z));

        // ── 시선 도약 ─────────────────────────────────────────────────────

        /// <summary>
        /// 원뿔 <b>필터</b>를 통과한 후보 중 <b>가장 가까운</b> 것부터 훑어, 궤적이 막히지 않는 첫 후보로 뛴다.
        /// (정렬도 가중 점수를 쓰면 "왜 저기로 뛰었지"가 생긴다 — 규칙은 하나여야 예측이 된다.)
        /// </summary>
        bool TryGazeJump()
        {
            Vector3 feet = Feet;

            // 원뿔 축은 <b>이동 방향 하나뿐</b>이다. 시선은 쓰지 않는다.
            //
            // 원칙: <b>발판을 떠나는 방향은 이동 방향이다. 그러니 목표도 그 방향에 있어야 한다.</b>
            // 시선을 OR로 같이 인정하면, 앞에 후보를 둔 채 뒤·옆으로 나갈 때 그 앞 후보가 시선으로
            // 통과해 TryGazeJump가 true를 반환하고, 정작 <b>나가는 방향은 아무 판단도 없이</b>
            // EdgeStop만 건너뛴 채 그대로 떨어진다. (자세한 경위: decisions/0004)
            //
            // 수평(yaw)만 본다 — 고개를 들든 숙이든 선정이 흔들리지 않는다. 높이는 아래 dh가 따로 거른다.
            Vector3 run = _lastMoveDir; run.y = 0f;
            if (run.sqrMagnitude < 1e-4f) return false;   // 이동 방향이 없으면 떠날 방향도 없다
            run.Normalize();

            float cosCone = Mathf.Cos(coneAngle * Mathf.Deg2Rad);
            float r2 = jumpSearchRadius * jumpSearchRadius;

            _cands.Clear();
            for (int i = 0; i < ClimbLedge.All.Count; i++)
            {
                ClimbLedge ledge = ClimbLedge.All[i];
                if (ledge == null) continue;

                // 싼 검사부터 — 먼 것에까지 TryResolve를 돌리지 않는다(씬 전수 순회라 효과가 크다).
                if (ledge.WorldBounds.SqrDistance(feet) > r2) continue;

                Vector3 approach = ledge.transform.position - feet; approach.y = 0f;
                if (approach.sqrMagnitude < 1e-4f) continue;
                if (!ledge.TryResolve(feet, approach.normalized, out ClimbLedge.GrabInfo g)) continue;
                if (!SettleLanding(ref g)) { Rej(ledge, "착지 바닥 없음"); continue; }

                // 거리도 수평으로 잰다 — 원뿔이 yaw 기준이라 기준을 맞춘다.
                Vector3 flatTo = g.edgeCenter - feet; flatTo.y = 0f;
                float dist = flatTo.magnitude;

                // ★ 하한을 크게 잡으면 <b>바로 앞 낮은 단차가 후보에서 빠져</b> 멀리 있는 발판으로 뛰어버린다.
                //   같은 자리(0에 가까운 것)만 걸러낼 정도로만 둔다. 높이는 아래에서 따로 거른다.
                if (dist > jumpSearchRadius || dist < 0.2f) { Rej(ledge, $"거리 {dist:F2}m"); continue; }

                Vector3 toDir = flatTo / dist;
                if (Vector3.Dot(run, toDir) < cosCone) { Rej(ledge, "원뿔 밖(이동 방향)"); continue; }

                float dh = g.landingFeet.y - feet.y;
                if (dh > maxMantleUp || dh < -maxDropTarget) { Rej(ledge, $"높이차 {dh:F2}m"); continue; }

                // ★ 지금 서 있는 자리와 <b>사실상 같은 곳</b>은 후보가 아니다 —
                //   가깝고(0.6m 미만) 높이차도 없으면, 발판 위에서 자기 모서리로 '뛰었다 되돌아오는' 왕복이 된다.
                //   올라갈 턱(minHeight↑)·내려갈 낙차(safeDrop↓)·건널 거리(0.6m↑) 중 하나는 있어야 한다.
                if (dh < minHeight && dh > -safeDrop && dist < 0.6f)
                { Rej(ledge, $"제자리 (거리 {dist:F2}m · 높이차 {dh:F2}m)"); continue; }

                bool needMantle = dh > maxDirectUp;

                Collider vol = ledge.Volume;
                if (BlockedAt(g.landingFeet + Vector3.up * 0.03f, vol)) { Rej(ledge, "착지 막힘"); continue; }
                if (needMantle && !HangSpaceOk(g, feet, vol)) { Rej(ledge, "매달림 막힘"); continue; }

                Vector3 t = (needMantle ? HangFeet(g) : g.landingFeet) + Vector3.up * FeetToOrigin;
                _cands.Add(new Cand { grab = g, target = t, dist = dist, mantle = needMantle, volume = vol });
            }

            if (_cands.Count == 0) return false;
            _cands.Sort(ByDistance);

            for (int i = 0; i < _cands.Count; i++)
            {
                Cand c = _cands[i];
                JumpArc arc = BuildArc(transform.position, c.target, _lastMoveDir);
                if (!PathClear(arc, c.volume))
                {
                    if (logDecisions) Debug.Log($"[Jump] 경로 막힘 — 다음 후보로 (dist={c.dist:F1}m)");
                    continue;
                }

                StartFlight(arc, c.mantle, c.grab);
                if (logDecisions) Debug.Log($"[Jump] 시선 도약 → {(c.mantle ? "잡고 오르기" : "직행")} dist={c.dist:F1}m");
                return true;
            }
            return false;
        }

        /// <summary>도약 후보 탈락 사유 로그 — "왜 가까운 걸 두고 멀리 뛰었나"를 가른다.</summary>
        void Rej(ClimbLedge ledge, string why)
        {
            if (logDecisions) Debug.Log($"[Jump] 후보 탈락 — {why} @{ledge.name}");
        }

        // ── 걸어서 오르기 ─────────────────────────────────────────────────

        void TryWalkUp(Vector3 dir)
        {
            Vector3 feet = Feet;
            int n = Physics.OverlapSphereNonAlloc(feet + Vector3.up * 0.9f, detectRadius,
                                                  _overlap, obstacleMask, QueryTriggerInteraction.Ignore);
            float bestDist = float.MaxValue;
            ClimbLedge.GrabInfo bestGrab = default;
            Collider bestVolume = null;
            bool found = false;

            float cosFwd = Mathf.Cos(walkUpConeAngle * Mathf.Deg2Rad);
            int seen = 0, rejFace = 0, rejDir = 0, rejHeight = 0, rejBlocked = 0;

            for (int i = 0; i < n; i++)
            {
                var ledge = _overlap[i] != null ? _overlap[i].GetComponentInParent<ClimbLedge>() : null;
                if (ledge == null) continue;
                seen++;
                if (!ledge.TryResolve(feet, dir, out ClimbLedge.GrabInfo g)) { rejFace++; continue; }
                if (!SettleLanding(ref g)) { rejBlocked++; continue; }   // 착지점이 허공

                // 진행 방향 앞의 모서리만. TryResolve는 "이동 방향과 가장 반대인 면"을 고르므로,
                // 이 검사가 없으면 옆이나 등 뒤(멀어지는 중인) 상자 위로 끌려 올라간다.
                Vector3 flatTo = g.edgeCenter - feet; flatTo.y = 0f;
                float d = flatTo.magnitude;
                if (d > detectRadius) { rejDir++; continue; }

                float h = g.landingFeet.y - feet.y;
                if (h < minHeight || h > maxMantleUp)
                {
                    rejHeight++;
                    if (logDecisions) Debug.Log($"[Climb] 거부 — 오를 높이 {h:F2}m (허용 {minHeight:F2}~{maxMantleUp:F2}) @{ledge.name}");
                    continue;
                }

                // 낮은 단차는 방향 판정을 건너뛴다 — 걷다 걸리는 턱까지 "정면으로 봐야" 넘어가면 답답하다.
                // 높은 것에만 전방 판정을 걸어 등 뒤 모서리로 끌려가는 것을 막는다.
                if (h > lowStepHeight && d > 1e-3f && Vector3.Dot(flatTo / d, dir) < cosFwd)
                { rejDir++; continue; }
                if (BlockedAt(g.landingFeet + Vector3.up * 0.03f, ledge.Volume)) { rejBlocked++; continue; }

                if (d < bestDist) { bestDist = d; bestGrab = g; bestVolume = ledge.Volume; found = true; }
            }

            if (!found)
            {
                // "감지 자체가 안 되는 건지"를 한 줄로 가른다 — seen=0이면 반경 안에 ClimbLedge가 없는 것.
                if (logDecisions && (seen > 0 || n > 0))
                    Debug.Log($"[Climb] 대상 없음 — 콜라이더 {n}개 중 ClimbLedge {seen}개 " +
                              $"(면 {rejFace} · 방향/거리 {rejDir} · 높이 {rejHeight} · 막힘 {rejBlocked})");
                return;
            }

            float dh = bestGrab.landingFeet.y - feet.y;
            bool needMantle = dh > maxDirectUp;
            if (needMantle && !HangSpaceOk(bestGrab, feet, bestVolume))
            {
                if (logDecisions) Debug.Log($"[Climb] 거부 — 매달릴 공간 막힘 (h={dh:F2}m)");
                return;
            }

            // 팔이 닿는 거리면 비행을 건너뛰고 선 자리에서 바로 잡는다.
            // (모든 등반이 도약을 거치면 벽 앞에서 늘 살짝 뛰어 붙어 어색하다 — 진입점만 하나 더 둔다.)
            if (needMantle && bestDist <= directLatchRange)
            {
                _grab = bestGrab;
                _arcThenMantle = false;
                _fpp.ExternalMotion = true;
                _fpp.VerticalVelocity = 0f;
                _cc.enabled = false;      // 당김 구간은 위치를 직접 몬다(비행과 동일)
                BeginPull();
                if (logDecisions) Debug.Log($"[Climb] 바로 잡기 — 거리 {bestDist:F2}m · h={dh:F2}m");
                return;
            }

            Vector3 target = (needMantle ? HangFeet(bestGrab) : bestGrab.landingFeet)
                             + Vector3.up * FeetToOrigin;

            JumpArc arc = BuildArc(transform.position, target, dir);
            if (!PathClear(arc, bestVolume))
            {
                if (logDecisions) Debug.Log("[Climb] 경로 막힘 — 취소");
                return;
            }

            StartFlight(arc, needMantle, bestGrab);
            if (logDecisions) Debug.Log($"[Climb] 걸어서 오르기 → {(needMantle ? "잡고 오르기" : "직행")} h={dh:F2}m");
        }

        /// <summary>매달림 발 위치 — 몸은 벽면 밖, 눈은 모서리 아래 팔길이.</summary>
        Vector3 HangFeet(in ClimbLedge.GrabInfo g)
        {
            Vector3 xz = g.edgeCenter + g.faceNormal * (_cc.radius + 0.08f);
            float eyeY = g.topY - armLength;                       // transform 원점 = 눈(카메라)
            return new Vector3(xz.x, eyeY - FeetToOrigin, xz.z);
        }

        /// <summary>
        /// 이 발 위치에 몸이 들어갈 수 있나. <b>자기 콜라이더는 제외</b>한다 —
        /// 경로 검사는 현재 위치 바로 앞부터 훑기 때문에, 제외하지 않으면 자기 자신에 막혀 영영 못 뛴다.
        /// </summary>
        /// <summary>
        /// 착지 지점에 <b>실제로 바닥이 있는지</b> 확인하고 높이를 그 바닥에 맞춘다.
        /// 모서리 끝(코너)으로 접근하면 착지점이 옆면 밖 허공으로 잡히는데, 그대로 두면
        /// 올라서자마자 떨어진다. 바닥이 없으면 후보에서 뺀다.
        /// </summary>
        bool SettleLanding(ref ClimbLedge.GrabInfo g)
        {
            // landingInset(0.45)은 모서리에서 겨우 그만큼이라, 반지름 0.3인 캡슐이 테두리에 걸친다.
            // 반지름만큼 더 안쪽을 먼저 노리고, 거기 바닥이 없을 때만 원래 지점으로 물러선다.
            Vector3 inward = -g.faceNormal;
            Vector3 deeper = g.landingFeet + inward * _cc.radius;

            float y;
            if (GroundY(deeper, out y)) { g.landingFeet = new Vector3(deeper.x, y, deeper.z); return true; }
            if (GroundY(g.landingFeet, out y)) { g.landingFeet = new Vector3(g.landingFeet.x, y, g.landingFeet.z); return true; }
            return false;
        }

        /// <summary>그 지점 발밑에 바닥이 있나(있으면 정확한 높이).</summary>
        bool GroundY(Vector3 p, out float y)
        {
            RaycastHit hit;
            if (Physics.Raycast(p + Vector3.up * 0.6f, Vector3.down, out hit, 1.4f,
                                obstacleMask, QueryTriggerInteraction.Ignore))
            { y = hit.point.y; return true; }
            y = 0f;
            return false;
        }

        /// <summary>
        /// 매달릴 자리에 몸이 들어가는가.
        /// <para><b>그 자리가 지금 서 있는 높이보다 아래면 검사하지 않는다.</b> 매달림 지점은 계산상
        /// <c>모서리 − 팔길이 − 키</c>라, 지상에서 오르는 경우 늘 발밑(바닥 속)으로 잡힌다.
        /// 그걸 그대로 막으면 <b>평지에서의 매달림 등반이 전부 거부</b>된다(실제로 그러고 있었다).
        /// 검사가 의미 있는 건 공중·상단에서 매달릴 때뿐이다.</para>
        /// </summary>
        bool HangSpaceOk(in ClimbLedge.GrabInfo g, Vector3 feet, Collider ignore)
        {
            Vector3 hang = HangFeet(g);
            if (hang.y <= feet.y + 0.05f) return true;
            return !BlockedAt(hang, ignore);
        }

        bool BlockedAt(Vector3 feetPos, Collider ignore = null)
        {
            float r = _cc.radius * 0.95f;
            Vector3 p0 = feetPos + Vector3.up * (r + 0.02f);
            Vector3 p1 = feetPos + Vector3.up * (_cc.height - r);

            int n = Physics.OverlapCapsuleNonAlloc(p0, p1, r, _blockBuf, obstacleMask,
                                                   QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = _blockBuf[i];
                if (c == null) continue;
                if (c == ignore) continue;
                if (c.transform == transform || c.transform.IsChildOf(transform)) continue;
                return true;
            }
            return false;
        }

        /// <summary>
        /// 궤적 중간이 지형에 막히지 않는가 — 출발·도착은 이미 검증했으니 사이만 본다.
        /// <para><b>오르려는 대상 자신은 제외</b>한다. 벽을 타고 오르는 궤적은 그 벽면을 스치는 게 정상이라,
        /// 제외하지 않으면 정작 등반이 전부 거부된다(막으려는 건 '사이에 낀 다른 지형'이다).</para>
        /// </summary>
        bool PathClear(in JumpArc a, Collider ignore)
        {
            if (pathSamples <= 0) return true;

            // 출발 지점 근처는 검사하지 않는다 — 이미 서 있던 공간이고, 발밑 발판·경사면이
            // 캡슐에 걸려 정상 도약까지 막아버린다(경사 바닥에서 못 뛰던 원인).
            float near = _cc.radius * 2f + 0.3f;
            float nearSq = near * near;

            for (int i = 1; i < pathSamples; i++)
            {
                Vector3 p = ArcPointRaw(a, a.total * i / pathSamples);
                float dx = p.x - a.start.x, dz = p.z - a.start.z;
                if (dx * dx + dz * dz < nearSq) continue;
                if (BlockedAt(p - Vector3.up * FeetToOrigin, ignore)) return false;
            }
            return true;
        }

        // ── 비행(도약) ────────────────────────────────────────────────────

        void StartFlight(in JumpArc arc, bool thenMantle, in ClimbLedge.GrabInfo grab)
        {
            _arc = arc;
            _arcThenMantle = thenMantle;
            _grab = grab;
            _t = 0f;
            _state = State.Flight;

            _fpp.ExternalMotion = true;
            _fpp.VerticalVelocity = 0f;
            _cc.enabled = false;   // 위치 직접 구동 — 출발·도착·궤적 중간까지 검증한 뒤다

            // 작은 턱을 넘는 것까지 발구름 연출을 넣으면 걸을 때마다 화면이 흔들린다.
            float travel = Vector3.Distance(transform.position, _arc.end);
            float rise = Mathf.Max(_arc.apexY - transform.position.y, 0.2f);
            if (_feel != null && travel >= feelMinTravel) _feel.OnJumpLaunch(rise);
        }

        void TickFlight(float dt)
        {
            _t += dt;
            transform.position = ArcPos(_arc, Mathf.Min(_t, _arc.total));
            if (_t < _arc.total) return;

            transform.position = _arc.end;

            if (_arcThenMantle) { BeginPull(); return; }   // CC는 계속 꺼둔 채 당김으로

            // 직행 착지 — 연출은 탄도의 실제 착지 수직 속도로.
            _cc.enabled = true;
            Physics.SyncTransforms();   // CC 내부 위치가 이전 값으로 남아 첫 Move에서 튀는 것 방지
            if (_feel != null) _feel.OnLand(Mathf.Abs(_arc.LandingVy()));
            _fpp.SuppressLand(0.2f);
            FinishToControl(_arc.EndTangent());   // 방향 = 궤적 끝 접선(벽 법선이 아니라)
        }

        // ── 잡고 올라가기 ────────────────────────────────────────────────

        void BeginPull()
        {
            _state = State.Pull;   // CC는 비행부터 꺼져 있다
            _t = 0f;

            float dh = Mathf.InverseLerp(maxDirectUp, maxMantleUp, _grab.topY - Feet.y);
            _pullDur = Mathf.Lerp(pullDurationMin, pullDurationMax, Mathf.Clamp01(dh));

            // 끝점의 수평 위치는 <b>매달림 지점</b> 기준이다. 비행으로 들어온 경우엔 이미 거기 있으니
            // 결과가 같고, 지상에서 바로 잡은 경우엔 벽 쪽으로 당겨지며 올라간다(팔을 뻗어 잡는 모양).
            Vector3 hang = HangFeet(_grab) + Vector3.up * FeetToOrigin;
            _pullStart = transform.position;
            _pullEnd = new Vector3(hang.x, _grab.topY + 0.35f, hang.z);   // 눈이 모서리 위로
            _overEnd = _grab.landingFeet + Vector3.up * FeetToOrigin;

            // 손 앵커 — 모서리 선 위, 어깨폭 간격(모서리 끝이면 몰림).
            // edgeDir의 부호는 접근한 면에 따라 뒤집히므로 플레이어 기준 오른쪽으로 정규화한다.
            // 안 하면 왼손/오른손이 서로 건너가 X자로 꼬인다.
            Vector3 bodyRight = Vector3.Cross(Vector3.up, -_grab.faceNormal);
            Vector3 edgeRight = Vector3.Dot(_grab.edgeDir, bodyRight) >= 0f ? _grab.edgeDir : -_grab.edgeDir;

            float hw = (_rig != null ? _rig.shoulderWidth : 0.42f) * 0.5f;
            float ofs = Mathf.Min(hw, _grab.halfExtent * 0.9f);
            Vector3 lh = _grab.edgeCenter - edgeRight * ofs;
            Vector3 rh = _grab.edgeCenter + edgeRight * ofs;
            if (_rig != null) _rig.Show(lh, rh);

            // 교차 기울임 — 횟수는 당김 시간 비례, 첫 방향은 모서리 치우침에서.
            if (_feel != null)
            {
                float cycles = Mathf.Max(1f, _pullDur * swayFrequency);
                float side = Vector3.Dot(_grab.edgeCenter - _grab.edgeMid, edgeRight) >= 0f ? -1f : 1f;
                _feel.BeginPullSway(cycles, swayAmplitude, side);
            }

            if (logDecisions) Debug.Log($"[Mantle] 래치 — 당김 {_pullDur:F2}초");
        }

        void TickMantle(float dt)
        {
            _t += dt;

            if (_state == State.Pull)
            {
                float u = Mathf.Clamp01(_t / _pullDur);
                float c = u * u * (3f - 2f * u);   // smoothstep — 당길수록 힘이 실렸다 풀림
                transform.position = Vector3.Lerp(_pullStart, _pullEnd, c);
                if (_feel != null) _feel.SetPullProgress(u);
                if (u >= 1f) { _state = State.Over; _t = 0f; }
                return;
            }

            // Over — 모서리를 넘어 착지 지점으로. 이 구간은 발이 모서리 아래에서 윗면까지
            // (팔 길이만큼) 한 번에 올라온다. 양 끝이 부드러운 smoothstep이라야 안 튄다.
            float v = Mathf.Clamp01(_t / overDuration);
            float e = v * v * (3f - 2f * v);
            transform.position = Vector3.Lerp(_pullEnd, _overEnd, e);
            if (v >= 1f) EndMantle();
        }

        void EndMantle()
        {
            transform.position = _overEnd;
            _cc.enabled = true;         // 비행부터 꺼져 있던 것을 여기서 되살린다
            Physics.SyncTransforms();   // 물리 쪽 CC 위치를 지금 트랜스폼에 맞춘다(안 하면 한 프레임 튄다)

            if (_rig != null) _rig.Hide();
            if (_feel != null)
            {
                _feel.EndPullSway();
                _feel.OnMantleFinish();   // 착지 연출이 아니라 안착 — 높이 무관 고정 소량
            }
            _fpp.SuppressLand(0.3f);      // 일반 착지 감지가 이 순간을 낙하 착지로 오인하지 않게

            FinishToControl(-_grab.faceNormal);
            if (logDecisions) Debug.Log("[Mantle] 완료");
        }

        // ── 제어권 반환 ──────────────────────────────────────────────────

        /// <summary>관성 보존 종료 — 진입 속도 유지 + 전방 감쇠 임펄스로 달려나간다.</summary>
        void FinishToControl(Vector3 forward)
        {
            forward.y = 0f;
            Vector2 flat = forward.sqrMagnitude > 1e-4f
                ? new Vector2(forward.x, forward.z).normalized
                : new Vector2(_lastMoveDir.x, _lastMoveDir.z);

            _fpp.move.SetVelocity(flat * _entrySpeed);
            _fpp.move.AddBoost(flat, exitBoost, exitBoostDuration);
            _fpp.VerticalVelocity = 0f;
            _fpp.ExternalMotion = false;
            _state = State.Idle;
            _cooldownLeft = cooldown;
        }

        void OnDisable()
        {
            // 등반 중 컴포넌트가 꺼져도 CC가 꺼진 채 남지 않게.
            if (_cc != null && !_cc.enabled) _cc.enabled = true;
            if (_fpp != null) _fpp.ExternalMotion = false;
            if (_rig != null) _rig.Hide();
            _state = State.Idle;
        }

        // ── 궤적: 베지어 수평 + 정점 보장 탄도 수직 + 발구름 워핑 ────────────

        struct JumpArc
        {
            public Vector3 start, end;
            public Vector2 p0, p1, p2;      // 수평 베지어(XZ)
            public float y0, vy, g, total, apexY;

            /// <summary>착지 순간 수직 속도(음수). 착지 연출 강도 기준.</summary>
            public float LandingVy() => vy - g * total;

            /// <summary>궤적 끝의 수평 진행 방향 — 착지 후 관성이 향할 쪽.</summary>
            public Vector3 EndTangent()
            {
                Vector2 t = (p2 - p1);   // 2차 베지어의 s=1 접선 방향
                if (t.sqrMagnitude < 1e-6f) t = p2 - p0;
                return t.sqrMagnitude > 1e-6f
                    ? new Vector3(t.x, 0f, t.y).normalized
                    : Vector3.zero;
            }
        }

        /// <summary>
        /// 궤적 계산. 수직은 정점 보장 탄도, 수평은 진행 방향으로 치우친 2차 베지어.
        ///
        /// <para><b>시간과 중력의 관계</b>: <c>total = k/√g</c> (<c>k = √(2·rise) + √(2·fall)</c>).
        /// 그래서 원하는 비행 시간이 정해지면 중력은 <c>g = (k/total)²</c>로 <b>파생</b>된다.
        /// 시간을 늘리든 줄이든 같은 식 하나로 처리되고, <b>정점·착지점은 그대로</b>다(모양 불변·재생 시간만 변경).
        /// <see cref="flightGravity"/>는 클램프 전 기준값을 주는 힌트일 뿐이다.</para>
        /// </summary>
        JumpArc BuildArc(Vector3 start, Vector3 end, Vector3 moveDir)
        {
            var a = new JumpArc { start = start, end = end, y0 = start.y };

            a.p0 = new Vector2(start.x, start.z);
            a.p2 = new Vector2(end.x, end.z);
            float dist = Vector2.Distance(a.p0, a.p2);

            a.apexY = Mathf.Max(start.y, end.y) + Mathf.Max(0.05f, clearance);
            float rise = Mathf.Max(0.0001f, a.apexY - start.y);
            float fall = Mathf.Max(0.0001f, a.apexY - end.y);

            float k = Mathf.Sqrt(2f * rise) + Mathf.Sqrt(2f * fall);   // total = k/√g

            // 비행 시간은 <b>실제 이동량</b>(수평+수직)으로 정한다.
            // 예전엔 기준 중력의 탄도 시간을 하한으로 썼는데, 그러면 0.5m 단차도 0.45초짜리 큰 포물선이
            // 되어 작은 턱을 넘을 때마다 조작이 끊겼다. 중력은 어차피 시간에서 역산되므로 필요 없다.
            float travel = Vector3.Distance(start, end);
            float byTravel = airSpeedCap > 0.01f ? travel / airSpeedCap : 0f;

            float lo = Mathf.Max(0.05f, minFlightTime);
            float hi = Mathf.Max(lo, maxFlightTime);
            a.total = Mathf.Clamp(byTravel, lo, hi);

            a.g = (k / a.total) * (k / a.total);
            a.vy = Mathf.Sqrt(2f * a.g * rise);

            // 수평: 출발 접선 = 달리던 방향(관성), 그 뒤 목표로 휘어 들어감.
            // ★ 휘는 정도는 <b>정렬도에 비례</b>시킨다. 목표가 정면이면 제어점이 직선 위라 그대로 직선이지만,
            //   옆으로 벌어진 목표에 관성을 그대로 살리면 몸이 엉뚱한 쪽으로 나갔다가 크게 휘어 들어와
            //   대각선 도약만 유독 어색해진다. 벌어질수록 직선에 가깝게 간다.
            Vector2 toTarget = a.p2 - a.p0;
            Vector2 d0 = new Vector2(moveDir.x, moveDir.z);
            if (d0.sqrMagnitude < 1e-4f) d0 = toTarget;          // 정지 상태에서 발동한 경우

            Vector2 mid = Vector2.Lerp(a.p0, a.p2, 0.5f);        // 휘지 않는 직선
            if (d0.sqrMagnitude > 1e-6f && toTarget.sqrMagnitude > 1e-6f)
            {
                float align = Mathf.Clamp01(Vector2.Dot(d0.normalized, toTarget.normalized));
                Vector2 biased = a.p0 + d0.normalized * (dist * Mathf.Max(0.05f, curveBias));
                a.p1 = Vector2.Lerp(mid, biased, align);
            }
            else a.p1 = mid;
            return a;
        }

        /// <summary>워핑 없는 실제 경과 시각의 위치 — 이게 '경로' 자체다(경로 검사·모양 판단용).</summary>
        static Vector3 ArcPointRaw(in JumpArc a, float tw)
        {
            float s = a.total > 0f ? Mathf.Clamp01(tw / a.total) : 1f;
            Vector2 flat = (1f - s) * (1f - s) * a.p0 + 2f * (1f - s) * s * a.p1 + s * s * a.p2;
            float y = a.y0 + a.vy * tw - 0.5f * a.g * tw * tw;
            return new Vector3(flat.x, y, flat.y);
        }

        /// <summary>
        /// 재생 위치 — <b>실제 시간 그대로</b>다. 시간 워핑을 하지 않는다.
        ///
        /// <para>수직이 이미 탄도라 <b>가속·감속이 물리적으로 들어 있다</b>(올라갈 땐 중력이 깎고,
        /// 내려갈 땐 붙이고, 정점에서 자연스레 뜬다). 여기에 이징을 또 씌우면 감속이 <b>이중</b>으로 걸려
        /// 정점에서 멈칫하고 중력감이 죽는다 — 실제로 그렇게 만들었다가 되돌린 자리다.</para>
        /// </summary>
        Vector3 ArcPos(in JumpArc a, float t) => ArcPointRaw(a, Mathf.Clamp(t, 0f, a.total));

        // ── 기즈모 ───────────────────────────────────────────────────────

        void OnDrawGizmosSelected()
        {
            if (!drawGizmos || _cc == null) return;
            Vector3 feet = Feet;

            // 도약 원뿔 + 검색 반경. 축은 <b>이동 방향</b>이다(정지 중엔 정면으로 대신 그린다).
            Vector3 f = transform.forward; f.y = 0f;
            if (f.sqrMagnitude > 1e-4f) f.Normalize();
            Vector3 axis = Application.isPlaying && _lastMoveDir.sqrMagnitude > 0.5f ? _lastMoveDir : f;

            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.3f);
            Gizmos.DrawWireSphere(feet, jumpSearchRadius);
            Quaternion l = Quaternion.AngleAxis(-coneAngle, Vector3.up);
            Quaternion r = Quaternion.AngleAxis(coneAngle, Vector3.up);
            Gizmos.DrawRay(transform.position, l * axis * jumpSearchRadius);
            Gizmos.DrawRay(transform.position, r * axis * jumpSearchRadius);

            // 가장자리 낙차 프로브
            Vector3 dir = axis;
            dir.y = 0f;
            if (dir.sqrMagnitude > 1e-4f)
            {
                dir.Normalize();
                Gizmos.color = Color.yellow;
                Vector3 probe = feet + dir * edgeProbeAhead + Vector3.up * 0.1f;
                Gizmos.DrawLine(probe, probe + Vector3.down * (safeDrop + 0.1f));
            }

            // 진행 중 궤적
            if (_state == State.Flight)
            {
                Gizmos.color = Color.green;
                Vector3 prev = ArcPos(_arc, 0f);
                for (int i = 1; i <= 24; i++)
                {
                    Vector3 cur = ArcPos(_arc, _arc.total * i / 24f);
                    Gizmos.DrawLine(prev, cur);
                    prev = cur;
                }
            }
        }
    }
}
