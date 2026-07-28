using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 자동 지형 통과 — <b>버튼 없이</b> 전부 자동. 판정은 3중 게이트로 엄격하게(후한 판정은 오히려 불편).
    ///
    /// <para><b>시선 도약</b>: 실제 낙차가 있는 가장자리에서 전진 중일 때, 시야 원뿔 안의
    /// <see cref="ClimbLedge"/> 중 도달 가능한 최적 목표로 자동 도약한다.
    ///  · 목표가 낮거나 조금 위 → 한 번의 도약으로 착지
    ///  · 키보다 높음(≤maxMantleUp) → 도약으로 모서리를 잡고(손 월드 고정) 당겨 올라감
    ///  · 도달 가능한 목표가 하나도 없음 → <b>가장자리 정지</b>: 바깥 방향 속도를 깎아 떨어지지 않는다.</para>
    ///
    /// <para><b>걸어서 오르기</b>: 가장자리가 아니어도, 눈앞의 ClimbLedge를 향해 밀면 자동으로 오른다.</para>
    ///
    /// <para><b>이어진 발판</b>: 가장자리 판정이 콜라이더 경계가 아니라 <b>낙차</b>(safeDrop 이상) 기준이라
    /// 붙어 있는 바닥 이음매에서는 아무것도 발동하지 않는다.</para>
    ///
    /// <para><b>궤적</b>: 수평 = 진행 방향으로 치우친 베지어(몸이 관성을 이기고 휘어 들어가는 대각선),
    /// 수직 = 정점 보장 탄도(모서리를 긁지 않음), 발구름 가속 워핑 <c>u=1-(1-x)^shape</c>.
    /// 비행은 CharacterController를 켠 채 델타로 몰아 관통을 막고, 도착 오차가 크면 중단한다.</para>
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

        [Tooltip("이 속도(m/s) 미만이면 발동하지 않는다.")]
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

        [Header("시선 도약")]
        [Tooltip("도약 목표 검색 반경(m).")]
        public float jumpSearchRadius = 8f;

        [Tooltip("시야 원뿔 반각(도) — 수평(yaw)만 본다. 위아래로 두리번거려도 선정이 안 흔들린다.")]
        public float coneAngle = 25f;

        [Tooltip("거리 감점(1m당). 점수 = yaw정렬도 − 거리×이 값. 0=순수 정면 우선, 크게=최근접 우선.")]
        public float distancePenalty = 0.06f;

        [Tooltip("수평 속도 상한(m/s). 비행 시간 = max(탄도 시간, 거리/이 값) — 먼 도약이 총알처럼 빠르지 않게.")]
        public float airSpeedCap = 7f;

        [Tooltip("이 높이(m)까지는 한 번의 도약으로 바로 올라선다.")]
        public float maxDirectUp = 1.1f;

        [Tooltip("이 높이(m)까지는 도약+잡고 올라가기로 처리. 이상이면 도달 불가.")]
        public float maxMantleUp = 2.0f;

        [Tooltip("목표가 발보다 이보다 많이 낮으면 후보에서 제외(그건 추락).")]
        public float maxDropTarget = 6f;

        [Tooltip("도약 중력(m/s²). 클수록 짧고 스냅하게.")]
        public float flightGravity = 22f;

        [Tooltip("모서리 위 여유 높이(m).")]
        public float clearance = 0.35f;

        [Tooltip("발구름 가속 지수. 1=등속, 클수록 초반 폭발.")]
        public float launchShape = 1.6f;

        [Tooltip("수평 경로가 진행 방향으로 치우치는 정도(0=직선, 대각선 휘어짐).")]
        [Range(0f, 0.8f)] public float curveBias = 0.35f;

        [Header("걸어서 오르기")]
        [Tooltip("눈앞 ClimbLedge 탐지 반경(m).")]
        public float detectRadius = 1.3f;

        [Tooltip("오를 최소 높이. stepOffset 이하는 엔진이 처리.")]
        public float minHeight = 0.3f;

        [Header("잡고 올라가기(맨틀)")]
        [Tooltip("팔 길이(m) — 매달렸을 때 눈이 모서리에서 이만큼 아래.")]
        public float armLength = 0.55f;

        public float pullDurationMin = 0.25f;
        public float pullDurationMax = 0.45f;
        public float overDuration = 0.15f;

        [Tooltip("당김 중 좌우 교차 기울임 빈도(Hz) — 교차 횟수 = 당김 시간 × 이 값.")]
        public float swayFrequency = 3f;

        [Tooltip("교차 기울임 진폭(도).")]
        public float swayAmplitude = 2.5f;

        [Tooltip("동작 종료 시 전방 감쇠 임펄스(m/s) — 올라서자마자 달려나가게.")]
        public float exitBoost = 4.5f;

        [Tooltip("종료 임펄스 지속(초). 짧게 '탁' 밀고 즉시 일반 조작으로 넘어간다.")]
        public float exitBoostDuration = 0.08f;

        [Header("디버그")]
        public bool logDecisions;
        public bool drawGizmos = true;

        FirstPersonPlayer _fpp;
        CharacterController _cc;
        MotionFeel _feel;
        MantleRig _rig;
        readonly Collider[] _overlap = new Collider[12];

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

            if (_cooldownLeft > 0f) { _cooldownLeft -= dt; return; }

            Vector2 v = _fpp.move.Output;
            if (v.magnitude >= minSpeed)
            {
                _lastMoveTime = Time.time;
                _lastMoveDir = new Vector3(v.x, 0f, v.y).normalized;
                _entrySpeed = v.magnitude;
            }
            bool moving = Time.time - _lastMoveTime <= inputBuffer && _lastMoveDir.sqrMagnitude > 0.5f;
            if (!moving || !_fpp.Grounded) return;

            Vector3 dir = _lastMoveDir;

            if (DropAhead(dir))
            {
                if (TryGazeJump()) return;
                EdgeStop(dir);                      // 도달 가능한 목표 없음 → 낙하 방지
                return;
            }

            TryWalkUp(dir);
        }

        // ── 가장자리: 낙차 기준 ───────────────────────────────────────────

        bool DropAhead(Vector3 dir)
        {
            Vector3 probe = Feet + dir * edgeProbeAhead + Vector3.up * 0.1f;
            return !Physics.Raycast(probe, Vector3.down, safeDrop + 0.1f,
                                    obstacleMask, QueryTriggerInteraction.Ignore);
        }

        // 클램프는 FPP가 적분을 끝낸 뒤에 적용한다 — 여기서 직접 깎으면 같은 프레임에 재가속돼 샌다.
        void EdgeStop(Vector3 dir) => _fpp.BlockDirection(new Vector2(dir.x, dir.z));

        // ── 시선 도약 ─────────────────────────────────────────────────────

        bool TryGazeJump()
        {
            Vector3 feet = Feet;

            // 수평(yaw)만 본다 — 높은 난간을 노리려고 고개를 들어도 선정이 흔들리지 않는다.
            // 높이는 아래 dh 조건이 따로 거른다.
            Vector3 look = _fpp.FlatForward;
            float cosCone = Mathf.Cos(coneAngle * Mathf.Deg2Rad);

            float bestScore = float.NegativeInfinity;
            ClimbLedge.GrabInfo bestGrab = default;
            bool found = false, bestMantle = false;

            for (int i = 0; i < ClimbLedge.All.Count; i++)
            {
                ClimbLedge ledge = ClimbLedge.All[i];
                if (ledge == null) continue;

                Vector3 approach = ledge.transform.position - feet; approach.y = 0f;
                if (approach.sqrMagnitude < 1e-4f) continue;
                if (!ledge.TryResolve(feet, approach.normalized, out ClimbLedge.GrabInfo g)) continue;

                float dist = Vector3.Distance(feet, g.landingFeet);
                if (dist > jumpSearchRadius || dist < 0.6f) continue;

                float dh = g.landingFeet.y - feet.y;
                if (dh > maxMantleUp || dh < -maxDropTarget) continue;
                bool needMantle = dh > maxDirectUp;

                Vector3 flatTo = g.edgeCenter - feet; flatTo.y = 0f;
                if (flatTo.sqrMagnitude < 1e-4f) continue;
                float align = Vector3.Dot(look, flatTo.normalized);
                if (align < cosCone) continue;                 // 원뿔은 필터로만

                if (Blocked(g.landingFeet + Vector3.up * 0.03f)) continue;
                if (needMantle && Blocked(HangFeet(g))) continue;

                // 정면 우선 + 거리 감점. distancePenalty로 두 성향 사이를 조절.
                float score = align - dist * distancePenalty;
                if (score > bestScore) { bestScore = score; bestGrab = g; bestMantle = needMantle; found = true; }
            }

            if (!found) return false;

            Vector3 target = bestMantle
                ? HangFeet(bestGrab) + Vector3.up * FeetToOrigin
                : bestGrab.landingFeet + Vector3.up * FeetToOrigin;

            StartFlight(target, bestMantle, bestGrab);
            if (logDecisions) Debug.Log($"[Jump] 시선 도약 → {(bestMantle ? "잡고 오르기" : "직행")} dist={Vector3.Distance(Feet, bestGrab.landingFeet):F1}m");
            return true;
        }

        // ── 걸어서 오르기 ─────────────────────────────────────────────────

        void TryWalkUp(Vector3 dir)
        {
            Vector3 feet = Feet;
            int n = Physics.OverlapSphereNonAlloc(feet + Vector3.up * 0.9f, detectRadius,
                                                  _overlap, obstacleMask, QueryTriggerInteraction.Ignore);
            float bestDist = float.MaxValue;
            ClimbLedge.GrabInfo bestGrab = default;
            bool found = false;

            for (int i = 0; i < n; i++)
            {
                var ledge = _overlap[i] != null ? _overlap[i].GetComponentInParent<ClimbLedge>() : null;
                if (ledge == null) continue;
                if (!ledge.TryResolve(feet, dir, out ClimbLedge.GrabInfo g)) continue;

                float h = g.landingFeet.y - feet.y;
                if (h < minHeight || h > maxMantleUp)
                {
                    if (logDecisions && h > maxMantleUp) Debug.Log($"[Climb] 거부 — 높이 {h:F2}m > {maxMantleUp}m @{ledge.name}");
                    continue;
                }
                if (Blocked(g.landingFeet + Vector3.up * 0.03f)) continue;

                float d = (g.landingFeet - feet).sqrMagnitude;
                if (d < bestDist) { bestDist = d; bestGrab = g; found = true; }
            }

            if (!found) return;

            float dh = bestGrab.landingFeet.y - feet.y;
            bool needMantle = dh > maxDirectUp;
            if (needMantle && Blocked(HangFeet(bestGrab))) return;

            Vector3 target = needMantle
                ? HangFeet(bestGrab) + Vector3.up * FeetToOrigin
                : bestGrab.landingFeet + Vector3.up * FeetToOrigin;

            StartFlight(target, needMantle, bestGrab);
            if (logDecisions) Debug.Log($"[Climb] 걸어서 오르기 → {(needMantle ? "잡고 오르기" : "직행")} h={dh:F2}m");
        }

        /// <summary>매달림 발 위치 — 몸은 벽면 밖, 눈은 모서리 아래 팔길이.</summary>
        Vector3 HangFeet(in ClimbLedge.GrabInfo g)
        {
            Vector3 xz = g.edgeCenter + g.faceNormal * (_cc.radius + 0.08f);
            float eyeY = g.topY - armLength;                       // transform 원점 = 눈(카메라)
            return new Vector3(xz.x, eyeY - FeetToOrigin, xz.z);
        }

        bool Blocked(Vector3 feetPos)
        {
            float r = _cc.radius * 0.95f;
            Vector3 p0 = feetPos + Vector3.up * (r + 0.02f);
            Vector3 p1 = feetPos + Vector3.up * (_cc.height - r);
            return Physics.CheckCapsule(p0, p1, r, obstacleMask, QueryTriggerInteraction.Ignore);
        }

        // ── 비행(도약) ────────────────────────────────────────────────────

        void StartFlight(Vector3 target, bool thenMantle, in ClimbLedge.GrabInfo grab)
        {
            Vector3 moveDir = _lastMoveDir.sqrMagnitude > 0.5f
                ? _lastMoveDir
                : (target - transform.position).normalized;

            _arc = SolveArc(transform.position, target, clearance, flightGravity, moveDir, curveBias, airSpeedCap);
            _arcThenMantle = thenMantle;
            _grab = grab;
            _t = 0f;
            _state = State.Flight;

            _fpp.ExternalMotion = true;
            _fpp.VerticalVelocity = 0f;
            _cc.enabled = false;   // 위치 직접 구동 — 출발·도착 공간은 이미 검증했다

            float rise = Mathf.Max(_arc.apexY - transform.position.y, 0.2f);
            if (_feel != null) _feel.OnJumpLaunch(rise);
        }

        void TickFlight(float dt)
        {
            _t += dt;
            transform.position = ArcPos(_arc, Mathf.Min(_t, _arc.total), launchShape);
            if (_t < _arc.total) return;

            transform.position = _arc.end;

            if (_arcThenMantle) { BeginPull(); return; }   // CC는 계속 꺼둔 채 당김으로

            // 직행 착지 — 연출은 탄도의 실제 착지 수직 속도로.
            _cc.enabled = true;
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

            _pullStart = transform.position;
            _pullEnd = new Vector3(_pullStart.x, _grab.topY + 0.35f, _pullStart.z);   // 눈이 모서리 위로
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

            // Over — 모서리를 넘어 착지 지점으로.
            float v = Mathf.Clamp01(_t / overDuration);
            float e = 1f - (1f - v) * (1f - v);
            transform.position = Vector3.Lerp(_pullEnd, _overEnd, e);
            if (v >= 1f) EndMantle();
        }

        void EndMantle()
        {
            transform.position = _overEnd;
            _cc.enabled = true;   // 비행부터 꺼져 있던 것을 여기서 되살린다

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

        static JumpArc SolveArc(Vector3 start, Vector3 end, float clearance, float g,
                                Vector3 moveDir, float bias, float speedCap)
        {
            var a = new JumpArc { start = start, end = end, y0 = start.y, g = g };

            a.p0 = new Vector2(start.x, start.z);
            a.p2 = new Vector2(end.x, end.z);
            float dist = Vector2.Distance(a.p0, a.p2);

            // 수직: 정점 보장 탄도. 여기서 나온 시간이 '최소' 비행 시간이다.
            a.apexY = Mathf.Max(start.y, end.y) + Mathf.Max(0.05f, clearance);
            float rise = Mathf.Max(0.0001f, a.apexY - start.y);
            float fall = Mathf.Max(0.0001f, a.apexY - end.y);
            float ballistic = Mathf.Sqrt(2f * g * rise) / g + Mathf.Sqrt(2f * fall / g);

            // 거리 반영 — 먼 도약이 총알처럼 빠르지 않게. 시간이 늘면 중력도 같이 낮춰
            // 정점·착지점이 그대로 유지되도록 다시 푼다(궤적 모양 불변).
            float byDistance = speedCap > 0.01f ? dist / speedCap : 0f;
            a.total = Mathf.Max(ballistic, byDistance);

            if (a.total > ballistic + 1e-4f)
            {
                // total = sqrt(2·g'·rise)/g' + sqrt(2·fall/g') = (sqrt(2·rise) + sqrt(2·fall))/sqrt(g')
                float k = Mathf.Sqrt(2f * rise) + Mathf.Sqrt(2f * fall);
                a.g = (k / a.total) * (k / a.total);
            }
            a.vy = Mathf.Sqrt(2f * a.g * rise);

            // 수평: 출발 접선 = 달리던 방향(관성), 그 뒤 목표로 휘어 들어감.
            Vector2 d0 = new Vector2(moveDir.x, moveDir.z);
            a.p1 = d0.sqrMagnitude > 1e-4f
                ? a.p0 + d0.normalized * (dist * Mathf.Max(0.05f, bias))
                : Vector2.Lerp(a.p0, a.p2, 0.5f);
            return a;
        }

        static Vector3 ArcPos(in JumpArc a, float t, float shape)
        {
            if (a.total <= 0f) return a.end;
            float x = Mathf.Clamp01(t / a.total);
            float u = 1f - Mathf.Pow(1f - x, Mathf.Max(0.05f, shape));   // 발구름 가속
            float tw = u * a.total;

            float s = tw / a.total;
            Vector2 flat = (1f - s) * (1f - s) * a.p0 + 2f * (1f - s) * s * a.p1 + s * s * a.p2;
            float y = a.y0 + a.vy * tw - 0.5f * a.g * tw * tw;
            return new Vector3(flat.x, y, flat.y);
        }

        // ── 기즈모 ───────────────────────────────────────────────────────

        void OnDrawGizmosSelected()
        {
            if (!drawGizmos || _cc == null) return;
            Vector3 feet = Feet;

            // 시야 원뿔(대략) + 검색 반경
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.3f);
            Gizmos.DrawWireSphere(feet, jumpSearchRadius);
            Vector3 f = transform.forward;
            Quaternion l = Quaternion.AngleAxis(-coneAngle, Vector3.up);
            Quaternion r = Quaternion.AngleAxis(coneAngle, Vector3.up);
            Gizmos.DrawRay(transform.position, l * f * jumpSearchRadius);
            Gizmos.DrawRay(transform.position, r * f * jumpSearchRadius);

            // 가장자리 낙차 프로브
            Vector3 dir = Application.isPlaying && _lastMoveDir.sqrMagnitude > 0.5f ? _lastMoveDir : f;
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
                Vector3 prev = ArcPos(_arc, 0f, launchShape);
                for (int i = 1; i <= 24; i++)
                {
                    Vector3 cur = ArcPos(_arc, _arc.total * i / 24f, launchShape);
                    Gizmos.DrawLine(prev, cur);
                    prev = cur;
                }
            }
        }
    }
}
