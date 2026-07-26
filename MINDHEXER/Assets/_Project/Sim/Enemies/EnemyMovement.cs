using UnityEngine;

namespace Game.Sim
{
    /// <summary>
    /// 적 이동 + 층이동(traversal) 실행. core-controls 분리 스티어링 유지.
    ///
    /// 층이동 개편(docs/shared/층이동_개편_설계.md):
    ///  - 공중 이동이 선형보간+대칭사인이 아니라 <b>정점 보장 탄도</b>(TraversalBallistics).
    ///    비행 시간은 물리로 결정되고, 하강도 먼저 솟았다 가속하며 떨어진다.
    ///  - 주저·멈칫은 링크에 구워진 값(직선 길이 비례).
    ///  - 동시 도약 혼잡: <b>착지 슬롯 점유 + 링크 정원 대기</b>. 점유 상태를 위한 별도 자료구조 없이
    ///    적 배열을 훑어(activeTraversalLinkId + traversalSlot) 판정한다 → 포크 시 자동 복사.
    ///  - 추적 목표는 플레이어 생좌표가 아니라 <b>lastGroundedPos</b>(공중일 때 몹이 굳던 버그 제거).
    /// </summary>
    public static class EnemyMovement
    {
        /// <summary>추적 목표: 접지 중이면 현재 위치, 공중이면 마지막 접지 위치.</summary>
        public static Vector3 ChaseTarget(in PlayerSim player)
            => player.grounded ? player.pos : player.lastGroundedPos;

        public static void Step(ref SimWorld w, int selfIndex, Vector3 sep, in SimServices svc, float dt)
        {
            Vector3 target = ChaseTarget(in w.player);
            ref EnemySim e = ref w.enemies[selfIndex];
            if (!e.alive) return;
            if (e.combat.stunTicks > 0)
            { e.vel.x = e.vel.z = 0f; Move(ref e, Vector3.zero, in svc, dt); return; }

            if (e.traversalPhase != TraversalPhase.None)
            { StepTraversal(ref e, in svc); return; }
            if (FlatSqrDist(e.pos, target) > SimConfig.EnemyAggroRange * SimConfig.EnemyAggroRange)
            { Move(ref e, Vector3.zero, in svc, dt); return; }

            int agentMask = 1 << (int)e.ai.mobility;
            PathStep step = svc.Pathfinder.NextStep(e.pos, target, agentMask);
            e.currentNavNodeId = step.currentNodeId;
            e.destinationNavNodeId = step.destinationNodeId;
            e.nextNavNodeId = step.nextNodeId;
            e.currentFloorId = step.floorId;

            if (step.kind == MoveKind.None)
            {
                // 경로 없음 → 정지. (수평 방향으로 밀어붙이면 낙사 위험이 있어 안전망은 보류.)
                e.hasWaypoint = false;
                Move(ref e, Vector3.zero, in svc, dt);
                return;
            }

            if (IsTraversal(step.kind))
            {
                Vector3 toStart = step.traversalStart - e.pos; toStart.y = 0f;
                if (toStart.magnitude <= SimConfig.EnemyArriveDist)
                {
                    // 발판 도착 — 정원이 남아 있고 빈 슬롯이 있어야 도약한다. 아니면 대기(제자리).
                    int slot = ClaimSlot(in w, selfIndex, in step);
                    if (slot >= 0) StartTraversal(ref e, in step, slot, in svc);
                    else Move(ref e, Vector3.zero, in svc, dt);   // 순번 대기
                    return;
                }
                WalkTowards(ref e, step.traversalStart, sep, target, in svc, dt);
                return;
            }

            WalkTowards(ref e, step.next, sep, target, in svc, dt);
        }

        /// <summary>
        /// 스폰 펄스 비행(설계 §4). 배관에서 받은 초기 속도로 날아가며, 그동안 AI·공격은 정지한다.
        /// 수평은 등속(공기저항 없음), 수직은 CharacterMotor의 중력 — 닫힌 물리라 포크 재현이 보장된다.
        /// 지상몹: 최소 체공 후 착지하면 해제. 공중몹: 착지가 없으므로 타이머로 해제.
        /// 어느 쪽이든 안전 타임아웃이 있어 어디 걸려도 영구 정지하지 않는다.
        /// </summary>
        public static void StepLaunch(ref EnemySim e, in SimServices svc, float dt)
        {
            e.launchTicks++;

            Vector3 horiz = new Vector3(e.vel.x, 0f, e.vel.z) * dt;
            e.pos = CharacterMotor.MoveHorizontal(svc.Collision, e.pos, horiz, e.radius, e.height);
            CharacterMotor.ResolveVertical(svc.Collision, ref e.pos, ref e.vel, dt, out bool grounded);
            e.grounded = grounded;

            if (e.ai.mobility == MobilityType.Flying)
            {
                if (e.launchTicks >= SimConfig.SpawnLaunchFlyTicks) EndLaunch(ref e);
                return;
            }
            if (e.launchTicks >= SimConfig.SpawnLaunchMinTicks && grounded) EndLaunch(ref e);
            else if (e.launchTicks >= SimConfig.SpawnLaunchMaxTicks) EndLaunch(ref e);   // 안전망
        }

        static void EndLaunch(ref EnemySim e)
        {
            e.launchTicks = 0;
            e.vel.x = e.vel.z = 0f;   // 평상시 수평속도는 0이 기본(이동은 변위로 처리)
            e.hasWaypoint = false;
        }

        static bool IsTraversal(MoveKind k)
            => k == MoveKind.Drop || k == MoveKind.Boost || k == MoveKind.JumpUp;

        /// <summary>
        /// 링크 정원·슬롯 점유 판정. 적 배열을 훑어 같은 링크를 쓰는 적을 세고, 비어 있는 최소 슬롯을 준다.
        /// 낮은 인덱스(= 낮은 id 경향)부터 훑으므로 대기 순서도 결정론적.
        /// 반환 -1 = 지금은 못 감(정원 초과 또는 빈 슬롯 없음).
        /// </summary>
        static int ClaimSlot(in SimWorld w, int selfIndex, in PathStep step)
        {
            int linkId = step.linkId;
            if (linkId < 0) return 0;   // 링크 식별 불가(NavMesh 경로 등) → 슬롯 개념 없이 통과

            int capacity = Mathf.Max(1, SimConfig.TraversalLinkCapacity);
            int slotMax  = Mathf.Clamp(step.traversalTicks > 0 ? SimConfig.TraversalSlotMax : SimConfig.TraversalSlotMax,
                                       1, SimConfig.TraversalSlotMax);

            int inFlight = 0;
            int usedMask = 0;
            for (int i = 0; i < w.enemyCount; i++)
            {
                if (i == selfIndex) continue;
                ref readonly EnemySim o = ref w.enemies[i];
                if (!o.alive || o.traversalPhase == TraversalPhase.None) continue;
                if (o.activeTraversalLinkId != linkId) continue;
                inFlight++;
                if (o.traversalSlot >= 0 && o.traversalSlot < 32) usedMask |= 1 << o.traversalSlot;
            }
            if (inFlight >= capacity) return -1;

            for (int s = 0; s < slotMax; s++)
                if ((usedMask & (1 << s)) == 0) return s;
            return -1;
        }

        /// <summary>
        /// 실제 착지 지점 결정. 슬롯 오프셋을 적용하되 <b>거기가 정말 설 수 있는 자리인지 검증</b>한다.
        /// 검증 없이 오프셋을 쓰면 발판 밖 허공에 착지해, 도약이 끝나는 순간 중력·클램프가 잡아채
        /// "공중에 도착했다가 발판 위로 순간이동"하는 현상이 난다. 못 쓰면 정확한 착지점으로 폴백.
        /// (도약 시작 시 1회만 질의 — 매 틱이 아니라 예측 포크에도 부담 없음.)
        /// </summary>
        static Vector3 ResolveLanding(Vector3 landing, int slot, in PathStep step, in EnemySim e, in SimServices svc)
        {
            Vector3 candidate = landing + SlotOffset(slot, step.slotCount, step.slotSpread);
            if (svc.Collision.SampleGround(candidate, SlotGroundProbe, out float groundY))
            {
                Vector3 onGround = new Vector3(candidate.x, groundY, candidate.z);
                if (svc.Collision.CanOccupyCapsule(onGround, e.radius, e.height)) return onGround;
            }
            return landing;   // 슬롯이 허공/막힘 → 원래 착지점
        }

        const float SlotGroundProbe = 2.5f;   // 슬롯 아래로 이만큼까지 바닥을 찾는다

        /// <summary>착지 슬롯 오프셋. 링 위 균등 배치 — 마커 에디터의 GetSlots와 같은 규칙.</summary>
        public static Vector3 SlotOffset(int slot, int slotCount, float spread)
        {
            if (slot <= 0 && slotCount <= 1) return Vector3.zero;
            int n = Mathf.Clamp(slotCount, 1, SimConfig.TraversalSlotMax);
            if (n == 1) return Vector3.zero;
            float ang = (360f / n) * Mathf.Clamp(slot, 0, n - 1) * Mathf.Deg2Rad;
            float r = spread > 0f ? spread : Mathf.Max(0.8f, SimConfig.EnemyRadius * SimConfig.TraversalSlotGapMul);
            return new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * r;
        }

        static void WalkTowards(ref EnemySim e, Vector3 target, Vector3 sep, Vector3 faceAt,
                                in SimServices svc, float dt)
        {
            e.waypoint = target; e.hasWaypoint = true;
            Vector3 face = faceAt - e.pos; face.y = 0f;
            if (face.sqrMagnitude > 1e-6f) e.yaw = Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg;
            Vector3 to = target - e.pos; to.y = 0f;
            float d = to.magnitude;
            Vector3 horiz = Vector3.zero;
            if (d > 1e-4f)
            {
                Vector3 dir = to / d;
                // 개체 고정 개성값으로 분리 세기를 낮춘다(전부 같은 가중치라 정면 대칭 진동이 나던 문제 해소).
                float sepScale = Mathf.Lerp(AIConfig.SeparationScaleMin, 1f, e.personality);
                Vector3 steer = dir + sep * (AIConfig.SeparationWeight * sepScale);
                if (steer.sqrMagnitude > 1e-6f) dir = steer.normalized;
                // 돌진몹은 커밋 전 평소 추격만 살짝 느리게(ChargeRun 본 속도는 안 건드림) — 실물 모델 Walk
                // 애니메이션이 전속력을 못 따라가 미끄러지듯 보이는 문제.
                // 근접 그런트(mobility=Ground, combat=Melee)는 별도로 0.7배(요청). 돌진(mobility=Charge)이
                // 우선 판정돼, combat이 같은 Melee여도 돌진 배율만 적용된다.
                float speedMul = e.ai.mobility == MobilityType.Charge ? AIConfig.ChargeChaseSpeedMul
                               : e.ai.combat == CombatType.Melee ? AIConfig.MeleeChaseMul : 1f;
                horiz = dir * SimConfig.EnemyMoveSpeed * speedMul * dt;
            }
            Move(ref e, horiz, in svc, dt);
        }

        /// <summary>
        /// 스폰 즉시 SpawnDrop 링크를 타게 한다(펄스 폐기 — 지상·돌진몹). 몹이 팬 아래 입에서
        /// 생겨나 착지점으로 수직 낙하한다. 주저 없이 곧바로 Airborne으로 진입.
        ///
        /// 결정론: 입력(착지·clearance·중력)이 고정이면 StepTraversal이 매 틱 같은 아치로 재구성한다
        /// (에디터 프리뷰·예측 포크와 동일). 링크 그래프 id는 없으므로 -1.
        /// </summary>
        public static void BeginSpawnDrop(ref EnemySim e, Vector3 landing, float clearance, float gravity)
        {
            e.activeMoveKind = MoveKind.Drop;
            e.activeTraversalLinkId = -1;
            e.traversalSlot = 0;
            e.traversalTicks = 0;
            e.jumpStart = e.pos;
            e.jumpEnd = landing;
            e.traversalClearance = clearance;
            e.traversalGravity = gravity > 0f ? gravity : SimConfig.TraversalGravity;
            BallisticArc arc = TraversalBallistics.Solve(e.jumpStart, e.jumpEnd, e.traversalClearance, e.traversalGravity);
            e.jumpDuration = arc.flightTicks;
            e.traversalPauseTicks = 0;                                  // 스폰 즉시 낙하(주저 없음)
            e.traversalRecoverTicks = SimConfig.TraversalRecoveryTicks; // 착지 후 잠깐 경직
            e.launchTicks = 0;                                          // 펄스 아님
            e.vel = Vector3.zero;
            e.grounded = false;
            e.traversalPhase = TraversalPhase.Airborne;
        }

        static void StartTraversal(ref EnemySim e, in PathStep step, int slot, in SimServices svc)
        {
            e.activeMoveKind = step.kind;
            e.activeTraversalLinkId = step.linkId;
            e.traversalSlot = slot;
            e.traversalTicks = 0;
            e.jumpStart = e.pos;
            e.jumpEnd = ResolveLanding(step.next, slot, in step, in e, in svc);
            e.nextNavNodeId = step.nextNodeId;
            e.vel = Vector3.zero;

            // 링크가 구워둔 주저·비행·멈칫. traversalTicks에 총 소요를 실어 보내지 않고
            // 여기서 탄도를 다시 푼다 — 에디터 프리뷰와 완전히 같은 함수라 궤적이 일치한다.
            // 궤적 파라미터를 들고 있어야 비행 중 매 틱 "같은 아치"로 재구성된다.
            e.traversalClearance = step.clearance;
            e.traversalGravity   = step.gravity;
            BallisticArc arc = TraversalBallistics.Solve(e.jumpStart, e.jumpEnd, step.clearance, step.gravity);
            e.jumpDuration = arc.flightTicks;
            // 링크에 구워진 값을 그대로 쓴다. 0도 유효한 값(주저·멈칫 없음)이라
            // "0이면 기본값으로 대체"하면 안 된다 — 튜닝으로 0까지 내릴 수 있어야 한다.
            e.traversalPauseTicks   = Mathf.Max(0, step.pauseTicks);
            e.traversalRecoverTicks = Mathf.Max(0, step.recoverTicks);

            // 주저가 0이면 Pause 단계를 아예 건너뛴다(0 = 완전히 없음이어야 함).
            e.traversalPhase = e.traversalPauseTicks > 0 ? TraversalPhase.Pause : TraversalPhase.Airborne;
        }

        static void StepTraversal(ref EnemySim e, in SimServices svc)
        {
            switch (e.traversalPhase)
            {
                case TraversalPhase.Pause:
                    e.vel = Vector3.zero;
                    if (++e.traversalTicks >= e.traversalPauseTicks)
                    { e.traversalPhase = TraversalPhase.Airborne; e.traversalTicks = 0; }
                    break;

                case TraversalPhase.Airborne:
                {
                    e.traversalTicks++;
                    // 탄도: 닫힌 형식으로 tick 위치를 직접 계산(누적 적분 아님) → 포크 재현 보장.
                    BallisticArc arc = ArcOf(in e);
                    e.pos = arc.At(e.traversalTicks);
                    e.grounded = false;
                    if (e.traversalTicks >= e.jumpDuration)
                    {
                        e.pos = e.jumpEnd;
                        if (e.traversalRecoverTicks > 0)
                        { e.traversalPhase = TraversalPhase.Recovery; e.traversalTicks = 0; }
                        else FinishTraversal(ref e, in svc);
                    }
                    break;
                }

                case TraversalPhase.Recovery:
                    e.vel = Vector3.zero;
                    if (++e.traversalTicks >= e.traversalRecoverTicks) FinishTraversal(ref e, in svc);
                    break;
            }
        }

        static void FinishTraversal(ref EnemySim e, in SimServices svc)
        {
            e.traversalPhase = TraversalPhase.None;
            e.activeMoveKind = MoveKind.None;
            e.activeTraversalLinkId = -1;
            e.traversalSlot = -1;
            e.currentNavNodeId = e.nextNavNodeId;
            e.currentFloorId = svc.Pathfinder.FloorIdAt(e.pos);
            e.hasWaypoint = false;
            e.traversalTicks = 0;
        }

        /// <summary>
        /// 비행 중 궤적 재구성. 시작 시 복사해 둔 clearance·gravity를 그대로 써야
        /// 출발할 때 계획한 아치와 동일한 궤적이 나온다(에디터 고스트와도 일치).
        /// </summary>
        static BallisticArc ArcOf(in EnemySim e)
        {
            var arc = TraversalBallistics.Solve(e.jumpStart, e.jumpEnd, e.traversalClearance, e.traversalGravity);
            arc.flightTicks = Mathf.Max(1, e.jumpDuration);   // 시작 시 확정한 비행 틱을 그대로 유지
            return arc;
        }

        static void Move(ref EnemySim e, Vector3 horiz, in SimServices svc, float dt)
        {
            e.pos = CharacterMotor.MoveHorizontal(svc.Collision, e.pos, horiz, e.radius, e.height);
            CharacterMotor.ResolveVertical(svc.Collision, ref e.pos, ref e.vel, dt, out bool grounded);
            e.grounded = grounded;
        }

        static float FlatSqrDist(Vector3 a, Vector3 b)
        { float dx = a.x - b.x, dz = a.z - b.z; return dx * dx + dz * dz; }
    }
}
