using UnityEngine;

namespace Game.Sim
{
    /// <summary>
    /// 몹 정체성 = 3축 조합. 전투(공격 방식) × 기동(이동 방식) × 크기(스탯 변형).
    /// 개별 종 나열이 아니라 조합 → 돌진·비행은 mobility 값 추가, 대형은 size 플래그만.
    /// </summary>
    public enum CombatType   : byte { Melee = 0, Ranged = 1 }
    /// <summary>
    /// 이동 방식.
    ///
    /// ★ <b>Traversal(층이동)은 사실상 폐기 예정</b>입니다 (2026-07-22 결정, 확정은 아님).
    ///   앞으로 이 특성을 쓰지 않기로 했으므로 <b>여기에 새 기능을 붙이지 마십시오.</b>
    ///   기존 코드(TraversalPhase·TraversalLink·그래프 링크 굽기)는 동작하므로 그대로 두지만,
    ///   버그 수정·연출 작업의 우선순위에서 제외합니다.
    ///   뷰도 이미 전용 모델이 없어 근접/원거리와 같은 모습으로 그려집니다(EntityViews.KindFor).
    /// </summary>
    public enum MobilityType : byte { Ground = 0, Charge = 1, Traversal = 2, Flying = 3, Orb = 4 }
    public enum SizeClass    : byte { Normal = 0, Large = 1 }

    /// <summary>
    /// 몹 AI 상태. 근접: Chase→Windup→Active→Recovery. 원거리(다음 단계): Reposition→Aim→Fire.
    /// stunTicks/descentPhase와 직교 — 그쪽이 우선순위 위(경직/하강 중엔 AI 정지).
    /// </summary>
    public enum EnemyState : byte
    {
        Chase = 0, Windup = 1, Active = 2, Recovery = 3,   // 근접 (돌진도 Windup·Recovery 재사용)
        Reposition = 4, Aim = 5, Fire = 6,                 // 원거리
        ChargeRun = 7,                                     // 돌진 직진
        Hide = 8, Emerge = 9,                              // 보스(Orb) 전용: 숨기(하강+30s 대기)/재등장(상승)
    }

    /// <summary>몹 AI 상태 묶음. ★ AI 세션 소유. EnemySim이 품기만 한다(필드 늘려도 공유파일 안 건드림).</summary>
    public struct EnemyAI
    {
        public CombatType   combat;      // 근접/원거리 — 공격 방식
        public MobilityType mobility;    // 지상/돌진/층이동/비행 — 이동 방식
        public SizeClass    size;        // 잡/대형 — 스탯 변형
        public EnemyState state;
        public int        stateTicks;
        public Vector3    committedDir;   // 공격 시작 시 고정 → 예지 회피 성립
        public int        attackCooldown; // 원거리 재발사 쿨
        public bool       hitDone;        // 이번 공격 판정 1회
        public Vector3    beamDir;        // 보스(Orb) 빔의 현재 방향. 발사 중 매 틱 플레이어로 각속도 상한 회전. 뷰도 읽음.
        public Vector3    anchor;         // 보스(Orb) 고정 좌표(스폰 지점). x·z는 평생 이 값, y 오프셋의 기준점.
        public bool       anchorSet;      // anchor를 첫 틱에 1회 설정했는가

        public static EnemyAI Spawn(CombatType combat, MobilityType mobility, SizeClass size) => new EnemyAI
        {
            combat = combat,
            mobility = mobility,
            size = size,
            state = combat == CombatType.Melee ? EnemyState.Chase : EnemyState.Reposition,
        };
    }

    /// <summary>
    /// 몹 AI 상태머신. ★ AI 세션 소유. SimStep이 적마다 호출(EnemyMovement 대신 이게 진입점).
    /// 공격 시퀀스는 committed(방향 고정 + 제자리). 이동(추격/하강)은 기존 EnemyMovement 재사용.
    /// 적→플레이어 데미지는 직접 안 씀 → world 히트 큐에 넣고 CombatResolve가 방어판정 후 적용.
    /// </summary>
    public static class EnemyBrain
    {
        // 분리 스티어링 스크래치: 매 틱 SimStep이 ComputeSeparation으로 채우고, 이동부가 추격방향에 가중.
        static readonly Vector3[] sepScratch = new Vector3[SimConfig.MaxEnemies];

        /// <summary>
        /// 각 몹의 "이웃 회피 방향"을 O(N²)로 1회 계산(boids 분리). 매 틱 SimStep이 적 루프 전에 호출.
        /// 겹치기 전에 미리 벌어지게 함(사후 밀기 Separate는 안전망으로 유지). 결정론(난수 없음).
        /// </summary>
        public static void ComputeSeparation(in SimWorld w)
        {
            for (int i = 0; i < w.enemyCount; i++) sepScratch[i] = Vector3.zero;
            for (int i = 0; i < w.enemyCount; i++)
            {
                if (!w.enemies[i].alive) continue;
                for (int j = i + 1; j < w.enemyCount; j++)
                {
                    if (!w.enemies[j].alive) continue;
                    Vector3 d = w.enemies[i].pos - w.enemies[j].pos; d.y = 0f;
                    float range = w.enemies[i].radius + w.enemies[j].radius + AIConfig.SeparationRadius;
                    float sq = d.sqrMagnitude;
                    if (sq >= range * range || sq < 1e-6f) continue;
                    float dist = Mathf.Sqrt(sq);
                    Vector3 push = d / (dist * dist);   // 멀어질 방향, 가까울수록 강함(1/dist)
                    sepScratch[i] += push;
                    sepScratch[j] -= push;
                }
            }
            for (int i = 0; i < w.enemyCount; i++)
                sepScratch[i] = Vector3.ClampMagnitude(sepScratch[i], AIConfig.SeparationMaxPush);
        }

        public static void Step(ref SimWorld w, int i, in SimServices svc, float dt)
        {
            ref EnemySim e = ref w.enemies[i];
            if (!e.alive) return;
            if (e.combat.gloryStage > 0) return;   // 글로리킬 처형 중 — AI·이동 정지(얼림)

            // 스폰 펄스로 날아가는 중엔 상태머신을 막는다 — 안 막으면 추격 이동이 초기 속도를
            // 즉시 덮어써 펄스가 사라지고, 착지 전에 공격도 나간다(설계 §4: 착지까지 무공격).
            if (e.launchTicks > 0)
            { EnemyMovement.StepLaunch(ref e, in svc, dt); return; }

            // 층이동(도약) 중엔 상태머신이 끼어들지 못하게 한다. 안 막으면 공중에서 공격 시퀀스로
            // 전환해 Plant/RangedMove가 위치를 덮어써 궤적이 뚝뚝 끊기거나 순간이동한다.
            // 도약은 한번 시작하면 착지까지 커밋된다.
            if (e.traversalPhase != TraversalPhase.None)
            { EnemyMovement.Step(ref w, i, Vector3.zero, in svc, dt); return; }

            // 돌진몹(Charge)은 이동·공격 융합 자체 시퀀스 — mobility로 먼저 분기(바인드도 내부 처리)
            if (e.ai.mobility == MobilityType.Charge) { StepCharge(ref w, i, in svc, dt); return; }
            // 공중몹(Flying)도 자체 기동(비행 호버 + 벽 우회) + 기존 원거리 공격 재사용
            if (e.ai.mobility == MobilityType.Flying) { StepFlying(ref w, i, in svc, dt); return; }
            // 보스(Orb): 부유 추격 + 차지 + 추적 레이저. 자기완결 시퀀스.
            if (e.ai.mobility == MobilityType.Orb) { StepBoss(ref w, i, in svc, dt); return; }

            // 런지 표적 이동봉쇄(bind): 위치·중력 동결(공중이면 공중에 얼음).
            // 공격 시퀀스는 계속 진행한다(붙잡혀도 반격 가능) — Plant/RangedMove가 내부에서 위치만 스킵.
            if (e.combat.bindTicks > 0)
            {
                if (e.ai.combat == CombatType.Ranged)
                {
                    if (e.ai.state == EnemyState.Aim || e.ai.state == EnemyState.Fire)
                        AdvanceRanged(ref w, i, in svc, dt);
                }
                else if (e.ai.state != EnemyState.Chase)
                    AdvanceMelee(ref w, i, in svc, dt);
                return;
            }

            if (e.ai.combat == CombatType.Ranged) StepRanged(ref w, i, in svc, dt);
            else                                           StepMelee(ref w, i, in svc, dt);
        }

        static void StepMelee(ref SimWorld w, int i, in SimServices svc, float dt)
        {
            ref EnemySim e = ref w.enemies[i];
            ref EnemyAI ai = ref e.ai;

            // 공격 시퀀스 진행 중
            if (ai.state != EnemyState.Chase)
            {
                if (e.combat.stunTicks > 0)     // 피격 = 카운터: 공격 취소
                { ai.state = EnemyState.Chase; ai.stateTicks = 0; }
                else { AdvanceMelee(ref w, i, in svc, dt); return; }
            }

            // 개시 판단 (경직/하강 아니고, 사거리 + 시야)
            if (e.combat.stunTicks == 0 && e.descentPhase == DescentPhase.None && e.traversalPhase == TraversalPhase.None)
            {
                Vector3 to = w.player.pos - e.pos; to.y = 0f;
                float hd = to.magnitude;
                if (hd <= AIConfig.MeleeRangeFor(e.radius) && HasLOS(in e, in w.player, in svc))
                {
                    ai.state = EnemyState.Windup;
                    ai.stateTicks = 0;
                    ai.hitDone = false;
                    ai.committedDir = hd > 1e-4f ? to / hd : Forward(e.yaw);
                    Plant(ref e, in svc, dt);
                    return;
                }
            }

            // 접근/하강/경직/idle → 기존 이동 (추격에 분리 스티어링 가중)
            EnemyMovement.Step(ref w, i, sepScratch[i], in svc, dt);
        }

        static void AdvanceMelee(ref SimWorld w, int i, in SimServices svc, float dt)
        {
            ref EnemySim e = ref w.enemies[i];
            ref EnemyAI ai = ref e.ai;
            ai.stateTicks++;

            float yaw = Mathf.Atan2(ai.committedDir.x, ai.committedDir.z) * Mathf.Rad2Deg;
            e.yaw = yaw;   // committed 방향 고정 응시

            switch (ai.state)
            {
                case EnemyState.Windup:
                    if (ai.stateTicks >= AIConfig.MeleeWindupTicks)
                    { ai.state = EnemyState.Active; ai.stateTicks = 0; }
                    break;
                case EnemyState.Active:
                    if (!ai.hitDone)
                    {
                        ai.hitDone = true;
                        if (CombatHit.InCone(e.pos, yaw, w.player.pos,
                                             AIConfig.MeleeRangeFor(e.radius) + AIConfig.MeleeHitExtra,
                                             AIConfig.MeleeHitHalfAngle))
                            w.QueuePlayerHit(ai.committedDir, AIConfig.MeleeDamage);
                    }
                    if (ai.stateTicks >= AIConfig.MeleeActiveTicks)
                    { ai.state = EnemyState.Recovery; ai.stateTicks = 0; }
                    break;
                case EnemyState.Recovery:
                    if (ai.stateTicks >= AIConfig.MeleeRecoveryTicks)
                    { ai.state = EnemyState.Chase; ai.stateTicks = 0; }
                    break;
            }
            Plant(ref e, in svc, dt);
        }

        // ───────────────────────── 돌진 (핑키) ─────────────────────────

        /// <summary>
        /// 돌진몹: Chase(추격+하강 재사용) → Windup(committed 텔레그래프) → ChargeRun(직진, 접촉/벽/최대거리 정지)
        /// → Recovery(성공 짧은딜/실패 긴딜). 완주 — 피격으로 안 끊긴다(회피는 옆으로). 반경 1.5배(Spawn에서).
        /// </summary>
        static void StepCharge(ref SimWorld w, int i, in SimServices svc, float dt)
        {
            ref EnemySim e = ref w.enemies[i];
            ref EnemyAI ai = ref e.ai;

            if (e.combat.bindTicks > 0) return;   // 런지 바인드: 그 자리 동결(공중이면 공중에)

            switch (ai.state)
            {
                case EnemyState.Windup:
                    e.yaw = Mathf.Atan2(ai.committedDir.x, ai.committedDir.z) * Mathf.Rad2Deg;
                    ai.stateTicks++;
                    if (ai.stateTicks >= AIConfig.ChargeWindupTicks)
                    { ai.state = EnemyState.ChargeRun; ai.stateTicks = 0; ai.hitDone = false; }
                    Plant(ref e, in svc, dt);
                    break;

                case EnemyState.ChargeRun:
                {
                    ai.stateTicks++;
                    Vector3 before = e.pos;
                    // ★ 이번 틱의 이동량 = 거리곡선의 차분(∫v). 속도×dt로 잡으면 가속 구간에서 오차가 쌓인다.
                    //   가속이 꺼져 있으면 ChargeDistAt이 선형이라 예전과 정확히 같은 값이 나온다.
                    float tNow  = ai.stateTicks * dt;
                    float wish  = AIConfig.ChargeDistAt(tNow) - AIConfig.ChargeDistAt(tNow - dt);
                    e.pos = CharacterMotor.MoveHorizontal(svc.Collision, e.pos, ai.committedDir * wish, e.radius, e.height);
                    CharacterMotor.ResolveVertical(svc.Collision, ref e.pos, ref e.vel, dt, out e.grounded);
                    float moved = FlatDist(before, e.pos);

                    // 접촉 → 피해 1회 (히트 큐)
                    float contact = e.radius + SimConfig.PlayerRadius + 0.15f;
                    if (!ai.hitDone && FlatDist(e.pos, w.player.pos) <= contact)
                    { w.QueuePlayerHit(ai.committedDir, AIConfig.ChargeDamage); ai.hitDone = true; }

                    // 벽 판정: 이번 틱에 "가려던 만큼" 못 갔으면 막힌 것.
                    // ★ 가속 초반엔 wish가 0에 가까워 이 비교가 무의미하므로, 의미 있는 이동량일 때만 본다.
                    bool wall  = wish > 1e-4f && moved < wish * AIConfig.ChargeWallStopFrac;
                    // 누적 거리도 곡선 적분으로 — stateTicks × wish 는 가속 구간에서 과대평가된다.
                    bool maxed = AIConfig.ChargeDistAt(tNow) >= AIConfig.ChargeMaxDist;
                    if (ai.hitDone || wall || maxed)
                    { ai.state = EnemyState.Recovery; ai.stateTicks = 0; }
                    break;
                }

                case EnemyState.Recovery:
                    ai.stateTicks++;
                    Plant(ref e, in svc, dt);
                    if (ai.stateTicks >= (ai.hitDone ? AIConfig.ChargeHitRecovery : AIConfig.ChargeMissRecovery))
                    { ai.state = EnemyState.Chase; ai.stateTicks = 0; ai.hitDone = false; }
                    break;

                default:   // Chase(및 초기 상태) — 추격 + 개시 판단
                    if (e.descentPhase == DescentPhase.None)
                    {
                        Vector3 to = w.player.pos - e.pos; to.y = 0f;
                        float hd = to.magnitude;
                        if (hd <= AIConfig.ChargeMinRange && hd <= SimConfig.EnemyAggroRange
                            && HasLOS(in e, in w.player, in svc))
                        {
                            ai.committedDir = hd > 1e-4f ? to / hd : Forward(e.yaw);
                            e.yaw = Mathf.Atan2(ai.committedDir.x, ai.committedDir.z) * Mathf.Rad2Deg;
                            ai.state = EnemyState.Windup;
                            ai.stateTicks = 0;
                            Plant(ref e, in svc, dt);
                            return;
                        }
                    }
                    EnemyMovement.Step(ref w, i, sepScratch[i], in svc, dt);   // 추격 + 하강 + 분리
                    break;
            }
        }

        static float FlatDist(Vector3 a, Vector3 b)
        { float dx = a.x - b.x, dz = a.z - b.z; return Mathf.Sqrt(dx * dx + dz * dz); }

        // ───────────────────────── 공중 원거리 (커코데몬) ─────────────────────────

        /// <summary>
        /// 공중몹: 낮게 부유(플레이어 y + 오프셋 수렴) + 밴드 유지. 몹끼리는 분리 스티어링,
        /// 벽은 MoveHorizontal 슬라이드. 공격(Aim→Fire·투사체·리드)은 지상 원거리와 공유. 조준 중 제자리 호버.
        /// </summary>
        static void StepFlying(ref SimWorld w, int i, in SimServices svc, float dt)
        {
            ref EnemySim e = ref w.enemies[i];
            ref EnemyAI ai = ref e.ai;

            if (ai.attackCooldown > 0) ai.attackCooldown--;
            if (e.combat.bindTicks > 0) return;   // 런지 바인드: 공중에 그대로 동결

            // 조준/발사 진행 중 → 제자리 호버 + 기존 원거리 시퀀스
            if (ai.state == EnemyState.Aim || ai.state == EnemyState.Fire)
            {
                // ★ 지상 원거리와 동일 — 응시는 플레이어 정면, 발사 방향(committedDir)은 그대로 리드+빗맞힘.
                Vector3 faceP = w.player.pos - e.pos; faceP.y = 0f;
                float faceD = faceP.magnitude;
                e.yaw = faceD > 1e-4f
                    ? Mathf.Atan2(faceP.x, faceP.z) * Mathf.Rad2Deg
                    : Mathf.Atan2(ai.committedDir.x, ai.committedDir.z) * Mathf.Rad2Deg;
                ai.stateTicks++;
                // ★ 공중은 지상 원거리와 타이밍을 분리한다(Fly* 상수) — 회피 난이도가 달라서.
                if (ai.state == EnemyState.Aim)
                {
                    if (ai.stateTicks >= AIConfig.FlyAimTicks)
                    { ai.state = EnemyState.Fire; ai.stateTicks = 0; }
                }
                else // Fire
                {
                    Vector3 origin = e.pos + Vector3.up * AIConfig.EnemyEyeHeight;
                    w.SpawnProjectile(origin, ai.committedDir * AIConfig.ProjectileSpeed);
                    ai.attackCooldown = AIConfig.FlyCooldown;
                    ai.state = EnemyState.Reposition;
                    ai.stateTicks = 0;
                }
                // 호버(위치 유지). ★ 관성이 켜져 있으면 그 자리에 딱 멈추는 게 아니라
                //   남은 속도가 drag로 잦아들며 미끄러져 멈춘다(의도 0으로 FlyMove 호출).
                if (AIConfig.FlyInertiaOn)
                    FlyMove(ref e, Vector3.zero, w.player.pos.y + AIConfig.FlyHoverFor(e.personality), Vector3.zero, in svc, dt);
                return;
            }

            // ── Reposition: 밴드 유지 비행 + 벽 우회 → 조준 개시 ──
            Vector3 toP = w.player.pos - e.pos; toP.y = 0f;
            float hd = toP.magnitude;
            if (hd > SimConfig.EnemyAggroRange) { FlyMove(ref e, Vector3.zero, e.pos.y, sepScratch[i], in svc, dt); return; }

            Vector3 face = hd > 1e-4f ? toP / hd : Forward(e.yaw);
            e.yaw = Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg;
            bool los = HasLOS(in e, in w.player, in svc);

            Vector3 wish = Vector3.zero;
            if (hd < AIConfig.FlyBandMin)                wish = -face;   // 너무 가까움 → 후퇴
            else if (hd > AIConfig.FlyBandMax || !los)   wish =  face;   // 멀거나 시야 없음 → 접근

            FlyMove(ref e, wish, w.player.pos.y + AIConfig.FlyHoverFor(e.personality), sepScratch[i], in svc, dt);

            if (hd >= AIConfig.FlyBandMin && hd <= AIConfig.FlyBandMax && los && ai.attackCooldown == 0)
            {
                ai.state = EnemyState.Aim;
                ai.stateTicks = 0;
                Vector3 pVel = (w.player.pos - w.prevPlayerPos) / dt; pVel.y = 0f;
                ai.committedDir = AimDir(in e, in w.player, pVel);
            }
        }

        /// <summary>비행 이동: (수평 wish + 분리) 벽 슬라이드 + 목표 고도로 FlySpeed 수렴. 중력 없음.</summary>
        static void FlyMove(ref EnemySim e, Vector3 wishDir, float targetY, Vector3 sep, in SimServices svc, float dt)
        {
            Vector3 dir = wishDir + sep * AIConfig.SeparationWeight;   // 이동 의도 + 이웃 회피
            Vector3 horiz;

            if (AIConfig.FlyInertiaOn)
            {
                // ── 관성 비행 ──
                // 속도를 상태(e.vel.x/z)로 들고 가감속한다. 급선회하면 원래 가던 방향으로 미끄러진다.
                // ★ 다른 몹은 e.vel 수평 성분을 안 쓰지만(이동은 변위로 처리) 공중몹은 여기 저장해 쓴다.
                Vector3 v = new Vector3(e.vel.x, 0f, e.vel.z);
                Vector3 want = dir.sqrMagnitude > 1e-6f ? dir.normalized * AIConfig.FlySpeed : Vector3.zero;

                if (want.sqrMagnitude > 1e-6f)
                    v += (want - v) * Mathf.Clamp01(AIConfig.FlyAccel * dt);   // 목표를 향해 가속
                else
                    v *= Mathf.Max(0f, 1f - AIConfig.FlyDrag * dt);            // 의도 없으면 관성으로 미끄러짐

                v = Vector3.ClampMagnitude(v, Mathf.Max(0.01f, AIConfig.FlyMaxSpeed));
                e.vel.x = v.x; e.vel.z = v.z;
                horiz = v * dt;
            }
            else
            {
                // 기존 동작 — 원하는 방향으로 즉시 최고 속도
                horiz = dir.sqrMagnitude > 1e-6f ? dir.normalized * AIConfig.FlySpeed * dt : Vector3.zero;
                e.vel.x = e.vel.z = 0f;
            }

            Vector3 beforeXZ = e.pos;
            e.pos = CharacterMotor.MoveHorizontal(svc.Collision, e.pos, horiz, e.radius, e.height);
            // 벽에 막혔으면 그 방향 속도를 죽인다 — 안 그러면 벽에 붙은 채 관성이 계속 쌓인다.
            if (AIConfig.FlyInertiaOn && horiz.sqrMagnitude > 1e-8f)
            {
                Vector3 movedV = e.pos - beforeXZ; movedV.y = 0f;
                if (movedV.sqrMagnitude < horiz.sqrMagnitude * 0.25f) { e.vel.x *= 0.3f; e.vel.z *= 0.3f; }
            }

            // ── 수직 ──
            float newY;
            if (AIConfig.FlyInertiaOn)
            {
                // 수평과 같은 원리로 가감속한다. 목표 높이가 갑자기 바뀌면(플레이어 점프·낙하)
                // 즉시 붙지 않고 지나쳤다가 되돌아온다 = 위아래로도 미끄러진다.
                float gap = targetY - e.pos.y;
                float wantY = Mathf.Clamp(gap * AIConfig.FlySpeed, -AIConfig.FlyMaxSpeedY, AIConfig.FlyMaxSpeedY);
                e.vel.y += (wantY - e.vel.y) * Mathf.Clamp01(AIConfig.FlyAccelY * dt);
                e.vel.y *= Mathf.Max(0f, 1f - AIConfig.FlyDragY * dt);
                e.vel.y = Mathf.Clamp(e.vel.y, -AIConfig.FlyMaxSpeedY, AIConfig.FlyMaxSpeedY);
                newY = e.pos.y + e.vel.y * dt;
            }
            else
            {
                float step = AIConfig.FlySpeed * dt;
                newY = e.pos.y + Mathf.Clamp(targetY - e.pos.y, -step, step);
                e.vel.y = 0f;
            }

            if (svc.Collision.SampleGround(e.pos, 500f, out float gy))
            {
                float floor = gy + AIConfig.FlyMinClearance;
                // 바닥에 닿으면 아래로 향한 속도를 죽인다 — 안 그러면 바닥에 붙은 채 계속 눌린다.
                if (newY < floor) { newY = floor; if (e.vel.y < 0f) e.vel.y = 0f; }
            }
            e.pos.y = newY;
            e.grounded = false;
        }

        // ───────────────────────── 보스 (빛나는 구 코어 · 추적 레이저) ─────────────────────────

        /// <summary>
        /// 보스(Orb): <b>고정 포탑</b> — 이동하지 않는다(2026-07-23 개편, 이전의 부유 추격 폐기).
        /// 스폰 지점(anchor) 위 BossRevealYOffset에 떠서 충전(Windup 5s) → 페이즈별 레이저(Fire,
        /// 빔만 각속도 상한 추적·지형 부분 차단) → 쿨(Recovery 10s)을 반복한다.
        /// 페이즈 전환은 CombatResolve.HitEnemy가 담당 — 누적 피해가 15씩 깎일 때마다 state=Hide로
        /// 바꾼다 → y 하강해 30s 숨고(그동안 EMP 해제 = 예지 사용 가능) → Emerge 상승 → 다음 페이즈.
        /// y는 숨기/재등장(및 스폰 직후 부상)에만 움직인다. 페이즈는 HP에서 파생(AIConfig.BossPhase).
        /// 결정론: svc.Collision + 플레이어 위치 + 순수 수학(RotateTowards/MoveTowards)만 쓴다.
        /// </summary>
        static void StepBoss(ref SimWorld w, int i, in SimServices svc, float dt)
        {
            ref EnemySim e = ref w.enemies[i];
            ref EnemyAI ai = ref e.ai;

            if (!ai.anchorSet) { ai.anchorSet = true; ai.anchor = e.pos; }   // 스폰 지점 = 기준 좌표(1회)
            float revealY = ai.anchor.y + AIConfig.BossRevealYOffset;
            float hiddenY = ai.anchor.y + AIConfig.BossHideYOffset;

            // 고정 포탑 — 밀림·관성 무효. x·z는 항상 anchor, y만 아래 상태들이 움직인다.
            e.pos.x = ai.anchor.x; e.pos.z = ai.anchor.z;
            e.vel = Vector3.zero; e.grounded = false;

            Vector3 emitter = e.pos + Vector3.up * AIConfig.BossEmitterHeight;   // 코어 발사점
            Vector3 target  = w.player.pos + Vector3.up * AIConfig.PlayerTorso;  // 겨냥점(플레이어 몸통)
            Vector3 toP = w.player.pos - e.pos; toP.y = 0f;
            float hd = toP.magnitude;
            Vector3 faceH = hd > 1e-4f ? toP / hd : Forward(e.yaw);
            e.yaw = Mathf.Atan2(faceH.x, faceH.z) * Mathf.Rad2Deg;   // 항상 플레이어 응시(텔레그래프)

            switch (ai.state)
            {
                case EnemyState.Hide:     // 숨기 — 하강 후 바닥에서 BossHideTicks 대기(EMP 해제 구간)
                    if (e.pos.y > hiddenY + 1e-3f)
                        e.pos.y = Mathf.MoveTowards(e.pos.y, hiddenY, AIConfig.BossHideMoveSpeed * dt);
                    else if (++ai.stateTicks >= AIConfig.BossHideTicks)
                    { ai.state = EnemyState.Emerge; ai.stateTicks = 0; }
                    break;

                case EnemyState.Emerge:   // 재등장 — 상승(이 순간부터 EMP 재개). 도착 시 다음 페이즈 사이클
                    e.pos.y = Mathf.MoveTowards(e.pos.y, revealY, AIConfig.BossHideMoveSpeed * dt);
                    if (Mathf.Abs(e.pos.y - revealY) < 1e-3f)
                    { ai.state = EnemyState.Windup; ai.stateTicks = 0; BossAimInit(ref ai, emitter, target, e.yaw); }
                    break;

                case EnemyState.Windup:   // 충전(5s) — 빔 예비 추적(조준 맞춰둠)
                    ai.stateTicks++;
                    ai.beamDir = RotateTowards(ai.beamDir, (target - emitter),
                                               AIConfig.BossChargeTurnRate * dt);
                    if (ai.stateTicks >= AIConfig.BossChargeTicks)
                    { ai.state = EnemyState.Fire; ai.stateTicks = 0; }
                    break;

                case EnemyState.Fire:     // 발사(페이즈별 1.5/2.2/3.0s) — 빔만 각속도 상한 추적
                    ai.stateTicks++;
                    ai.beamDir = RotateTowards(ai.beamDir, (target - emitter),
                                               AIConfig.BossBeamTurnRate * dt);
                    // 부분 차단: 플레이어 발자국 단면 표본 중 하나라도 안 막히고 도달하면 피해.
                    // 매 틱 히트 큐잉 → 무적시간이 실제 간격을 조절.
                    if (BeamHitsPlayer(in svc, emitter, ai.beamDir, target,
                                       AIConfig.BossBeamRadius, SimConfig.PlayerRadius))
                        w.QueuePlayerHit(ai.beamDir, AIConfig.BossBeamDamage);
                    if (ai.stateTicks >= AIConfig.BossFireTicksFor(AIConfig.BossPhase(e.combat.health)))
                    { ai.state = EnemyState.Recovery; ai.stateTicks = 0; }
                    break;

                case EnemyState.Recovery: // 쿨(10s) — 플레이어 딜 타임. 끝나면 곧장 다음 충전
                    ai.stateTicks++;
                    if (ai.stateTicks >= AIConfig.BossRecoverTicks)
                    { ai.state = EnemyState.Windup; ai.stateTicks = 0; BossAimInit(ref ai, emitter, target, e.yaw); }
                    break;

                default:   // Chase(스폰 초기) = 대기 — 등장 높이로 떠오른 뒤 아그로+시야면 사이클 개시
                    e.pos.y = Mathf.MoveTowards(e.pos.y, revealY, AIConfig.BossHideMoveSpeed * dt);
                    if (hd <= SimConfig.EnemyAggroRange && HasLOS(in e, in w.player, in svc))
                    { ai.state = EnemyState.Windup; ai.stateTicks = 0; BossAimInit(ref ai, emitter, target, e.yaw); }
                    break;
            }
        }

        /// <summary>보스 빔 조준 초기화 — 현재 겨냥점 방향으로 스냅(충전이 이어서 예비 추적).</summary>
        static void BossAimInit(ref EnemyAI ai, Vector3 emitter, Vector3 target, float yaw)
        {
            Vector3 aim0 = target - emitter;
            ai.beamDir = aim0.sqrMagnitude > 1e-6f ? aim0.normalized : Forward(yaw);
        }

        /// <summary>cur를 want 방향으로 최대 maxDeg만큼만 회전(정규화). 순수 수학 → 결정론.</summary>
        static Vector3 RotateTowards(Vector3 cur, Vector3 want, float maxDeg)
        {
            if (cur.sqrMagnitude < 1e-8f) cur = want;
            Vector3 r = Vector3.RotateTowards(cur, want, maxDeg * Mathf.Deg2Rad, 0f);
            return r.sqrMagnitude > 1e-8f ? r.normalized : cur.normalized;
        }

        /// <summary>
        /// <b>부분 차단</b> 빔 명중 판정. 굵은 빔을 단면 여러 평행 하위광선으로 보고, <b>플레이어가 차지하는
        /// 단면 영역</b>만 표본화한다(예측 포크 비용 억제 — 빔 전체가 아니라 플레이어 발자국만).
        /// 표본 중 <b>하나라도</b> 지형에 안 막히고 플레이어 거리까지 도달하면 명중("조금이라도 세면 피해").
        /// 각 하위광선이 독립적으로 막히므로 단면 일부만 가려져도 나머지는 샌다. dir은 정규화 전제.
        /// </summary>
        static bool BeamHitsPlayer(in SimServices svc, Vector3 emitter, Vector3 dir, Vector3 target, float R, float Pr)
        {
            Vector3 rel = target - emitter;
            float t = Vector3.Dot(rel, dir);
            if (t <= 0f) return false;                       // 발사점 뒤
            Vector3 perp = rel - dir * t;                    // 축 → 플레이어 수직벡터
            float sum = R + Pr;
            if (perp.sqrMagnitude > sum * sum) return false; // 빔 단면 밖 → 완전 빗나감

            BeamBasis(dir, out Vector3 u, out Vector3 v);
            Vector3 c = ClampToRadius(perp, R);              // 플레이어 중심을 빔 원반 안으로
            // 표본 5개: 중심 + ±Pr·u, ±Pr·v (각 원반 R 이내로 클램프, 플레이어 발자국 Pr 안인 것만 유효)
            if (BeamSampleClear(in svc, emitter, dir, t, c,          perp, R, Pr)) return true;
            if (BeamSampleClear(in svc, emitter, dir, t, c + u * Pr, perp, R, Pr)) return true;
            if (BeamSampleClear(in svc, emitter, dir, t, c - u * Pr, perp, R, Pr)) return true;
            if (BeamSampleClear(in svc, emitter, dir, t, c + v * Pr, perp, R, Pr)) return true;
            if (BeamSampleClear(in svc, emitter, dir, t, c - v * Pr, perp, R, Pr)) return true;
            return false;
        }

        /// <summary>표본 하나: 빔 원반 안 + 플레이어 발자국(Pr) 안이고, 그 평행광선이 플레이어 거리까지 안 막히면 true.</summary>
        static bool BeamSampleClear(in SimServices svc, Vector3 emitter, Vector3 dir, float t,
                                    Vector3 off, Vector3 perp, float R, float Pr)
        {
            off = ClampToRadius(off, R);
            if ((off - perp).sqrMagnitude > Pr * Pr) return false;      // 플레이어 발자국 밖 표본
            return !svc.Collision.Raycast(emitter + off, dir, t).hit;   // 안 막히고 도달
        }

        /// <summary>축 dir에 수직인 두 단위 기저(u,v). 결정론(고정 규칙).</summary>
        static void BeamBasis(Vector3 dir, out Vector3 u, out Vector3 v)
        {
            Vector3 refv = Mathf.Abs(dir.y) < 0.99f ? Vector3.up : Vector3.forward;
            u = Vector3.Normalize(Vector3.Cross(dir, refv));
            v = Vector3.Cross(dir, u);   // dir·u 직교 단위벡터
        }

        static Vector3 ClampToRadius(Vector3 off, float R)
        {
            float m = off.magnitude;
            return m > R ? off * (R / m) : off;
        }

        // ───────────────────────── 원거리 솔저 ─────────────────────────

        static void StepRanged(ref SimWorld w, int i, in SimServices svc, float dt)
        {
            ref EnemySim e = ref w.enemies[i];
            ref EnemyAI ai = ref e.ai;

            if (ai.attackCooldown > 0) ai.attackCooldown--;

            // 피격 = 카운터: 조준/발사 취소하고 정지
            if (e.combat.stunTicks > 0)
            {
                if (ai.state == EnemyState.Aim || ai.state == EnemyState.Fire)
                { ai.state = EnemyState.Reposition; ai.stateTicks = 0; }
                Plant(ref e, in svc, dt);
                return;
            }

            // 조준/발사 진행 중
            if (ai.state == EnemyState.Aim || ai.state == EnemyState.Fire)
            { AdvanceRanged(ref w, i, in svc, dt); return; }

            // ── Reposition: 선호 밴드 유지 + 시야 확보, 되면 조준 개시 ──
            Vector3 toP = w.player.pos - e.pos; toP.y = 0f;
            float hd = toP.magnitude;

            if (hd > SimConfig.EnemyAggroRange) { Plant(ref e, in svc, dt); return; }  // 인지 밖 대기

            Vector3 face = hd > 1e-4f ? toP / hd : Forward(e.yaw);
            e.yaw = Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg;   // 항상 플레이어 응시(텔레그래프)
            bool los = HasLOS(in e, in w.player, in svc);

            if (hd < AIConfig.RangedBandMin)                RangedMove(ref e, -face, sepScratch[i], in svc, dt);  // 후퇴
            else if (hd > AIConfig.RangedBandMax || !los)   RangedMove(ref e,  face, sepScratch[i], in svc, dt);  // 접근/시야확보
            else if (ai.attackCooldown == 0)   // 밴드 안 + 시야 + 쿨 준비 → 조준 개시
            {
                ai.state = EnemyState.Aim;
                ai.stateTicks = 0;
                ai.hitDone = false;
                Vector3 pVel = (w.player.pos - w.prevPlayerPos) / dt; pVel.y = 0f;   // 이번 틱 수평 속도
                ai.committedDir = AimDir(in e, in w.player, pVel);   // 리드+빗맞힘 포함, 여기서 확정
                Plant(ref e, in svc, dt);
            }
            else RangedMove(ref e, Vector3.zero, sepScratch[i], in svc, dt);   // 쿨 대기: 분리로 서로 벌어짐
        }

        static void AdvanceRanged(ref SimWorld w, int i, in SimServices svc, float dt)
        {
            ref EnemySim e = ref w.enemies[i];
            ref EnemyAI ai = ref e.ai;
            ai.stateTicks++;

            // ★ 시각(응시)은 항상 플레이어 정면 — 발사 방향(committedDir, 리드+빗맞힘 포함)과 분리.
            // SpawnProjectile은 committedDir을 직접 쓰므로 여기서 e.yaw만 바꿔도 탄도엔 영향 없음.
            Vector3 faceP = w.player.pos - e.pos; faceP.y = 0f;
            float faceD = faceP.magnitude;
            e.yaw = faceD > 1e-4f
                ? Mathf.Atan2(faceP.x, faceP.z) * Mathf.Rad2Deg
                : Mathf.Atan2(ai.committedDir.x, ai.committedDir.z) * Mathf.Rad2Deg;

            switch (ai.state)
            {
                case EnemyState.Aim:
                    if (ai.stateTicks >= AIConfig.RangedAimTicks)
                    { ai.state = EnemyState.Fire; ai.stateTicks = 0; }
                    break;
                case EnemyState.Fire:
                    Vector3 origin = e.pos + Vector3.up * AIConfig.EnemyEyeHeight;
                    w.SpawnProjectile(origin, ai.committedDir * AIConfig.ProjectileSpeed);
                    ai.attackCooldown = AIConfig.RangedCooldown;
                    ai.state = EnemyState.Reposition;
                    ai.stateTicks = 0;
                    break;
            }
            Plant(ref e, in svc, dt);
        }

        /// <summary>
        /// 발사 방향(3D). 대시 중이면 일부러 빗나가게(id 결정론), 아니면 플레이어 속도로 "약간" 리드.
        /// 리드는 투사체 도달시간 × LeadFactor 만큼만 앞을 겨냥 → 완벽 아님(저글 회피 가능).
        /// </summary>
        static Vector3 AimDir(in EnemySim e, in PlayerSim p, Vector3 pVel)
        {
            Vector3 origin = e.pos + Vector3.up * AIConfig.EnemyEyeHeight;
            Vector3 target = p.pos + Vector3.up * AIConfig.PlayerTorso;

            if (p.dashTicks > 0)   // 대시로 빠르게 이동 중 → 리드 안 하고 일부러 빗나감(안전 경로)
            {
                Vector3 miss = target - origin;
                float sign = (e.id % 2 == 0) ? 1f : -1f;
                miss = Quaternion.AngleAxis(AIConfig.MissOffsetDeg * sign, Vector3.up) * miss;
                return miss.sqrMagnitude > 1e-6f ? miss.normalized : Forward(e.yaw);
            }

            // 부분 리드: 현재 거리로 도달시간 추정 → 그만큼 앞을 겨냥(계수로 약화)
            float dist = (target - origin).magnitude;
            float travelTime = dist / AIConfig.ProjectileSpeed;
            Vector3 predicted = target + pVel * (travelTime * AIConfig.LeadFactor);
            Vector3 aim = predicted - origin;
            return aim.sqrMagnitude > 1e-6f ? aim.normalized : Forward(e.yaw);
        }

        /// <summary>원거리 재배치 이동(수평 wish + 분리). 응시(yaw)는 호출부가 정한다. 바인드 중 동결.</summary>
        static void RangedMove(ref EnemySim e, Vector3 dir, Vector3 sep, in SimServices svc, float dt)
        {
            if (e.combat.bindTicks > 0) return;
            dir.y = 0f;
            Vector3 steer = dir + sep * AIConfig.SeparationWeight;
            Vector3 horiz = steer.sqrMagnitude > 1e-6f ? steer.normalized * AIConfig.RangedMoveSpeed * dt : Vector3.zero;
            e.pos = CharacterMotor.MoveHorizontal(svc.Collision, e.pos, horiz, e.radius, e.height);
            CharacterMotor.ResolveVertical(svc.Collision, ref e.pos, ref e.vel, dt, out bool g);
            e.grounded = g;
        }

        /// <summary>제자리 정지(수평 0) + 중력·지면. 공격 중 committed 유지용. 바인드 중 위치 동결(공중 얼음).</summary>
        static void Plant(ref EnemySim e, in SimServices svc, float dt)
        {
            if (e.combat.bindTicks > 0) return;
            e.vel.x = 0f; e.vel.z = 0f;
            e.pos = CharacterMotor.MoveHorizontal(svc.Collision, e.pos, Vector3.zero, e.radius, e.height);
            CharacterMotor.ResolveVertical(svc.Collision, ref e.pos, ref e.vel, dt, out bool g);
            e.grounded = g;
        }

        /// <summary>적 눈 → 플레이어 몸통 레이가 지형에 안 막히면 시야 있음.</summary>
        static bool HasLOS(in EnemySim e, in PlayerSim p, in SimServices svc)
        {
            Vector3 eye = e.pos + Vector3.up * AIConfig.EnemyEyeHeight;
            Vector3 tgt = p.pos + Vector3.up * AIConfig.PlayerTorso;
            Vector3 d = tgt - eye; float dist = d.magnitude;
            if (dist < 1e-4f) return true;
            return !svc.Collision.Raycast(eye, d / dist, dist).hit;
        }

        static Vector3 Forward(float yaw)
            => new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));
    }

    /// <summary>
    /// 보스 파생 상태 질의 — 이미 해시되는 sim 상태(alive/mobility/state)에서만 계산하는
    /// 순수 함수라 별도 필드·해시가 필요 없다. View(예지 차단·연출)가 읽는다.
    /// </summary>
    public static class BossQuery
    {
        /// <summary>
        /// 보스 EMP 교란이 활성인가 — <b>레이저 충전~발사 동안</b>(살아 있고 Windup 또는 Fire)만 true.
        /// 충전 진입(Windup)에 켜지고 발사가 끝나는 순간(Fire 종료) 꺼진다.
        /// 그 외(Recovery 딜 타임·Hide·Emerge) 동안은 예지를 쓸 수 있다.
        /// </summary>
        public static bool EmpActive(in SimWorld w)
        {
            for (int i = 0; i < w.enemyCount; i++)
            {
                ref readonly EnemySim e = ref w.enemies[i];
                if (!e.alive || e.ai.mobility != MobilityType.Orb) continue;
                if (e.ai.state == EnemyState.Windup || e.ai.state == EnemyState.Fire) return true;
            }
            return false;
        }
    }
}
