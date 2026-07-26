using UnityEngine;

namespace Game.Sim
{
    /// <summary>
    /// 플레이어 전투 상태머신. ★ combat 소유. SimStep이 PlayerMovement 다음에 호출.
    /// 평타(좌클릭) · 타깃 런지(우클릭) · 대형몹 글로리킬 컷신.
    /// 판정/대미지는 CombatResolve. 여기선 단계 진행 + 런지 이동(pos 구동) + 타깃 선정.
    /// </summary>
    public static class PlayerCombat
    {
        public static void Step(ref SimWorld w, in InputCmd cmd, in SimServices svc, float dt)
        {
            ref PlayerSim p = ref w.player;
            ref PlayerCombatState c = ref p.combat;

            // 글로리킬 처형 중이면 그것만 (최우선 — 무적·조작잠금은 SimStep/CombatResolve가 처리)
            if (c.gloryPhase != CombatConfig.GlNone) { StepGlory(ref w, in svc, dt); return; }

            if (c.hitStunTicks > 0) c.hitStunTicks--;
            if (c.lungeCooldown > 0) c.lungeCooldown--;
            if (c.lungeBufferTicks > 0) c.lungeBufferTicks--;
            // 쿨 막판(남은 쿨 ≤ 예약구간)에 우클 → 예약(쿨 끝나는 즉시 발동)
            if (c.lungeCooldown > 0 && cmd.lunge && c.lungeCooldown <= CombatConfig.LungeReserveWindow)
                c.lungeBufferTicks = CombatConfig.LungeReserveWindow + 2;

            // ── 런지 진행 중이면 그것만 (Travel 이동·에임 고정) ──
            if (c.lungePhase != CombatConfig.LgNone) { StepLunge(ref w, in svc, dt); return; }

            // ── 런지 시작: (우클 or 예약) + 스택>0 + 쿨0 + 유효 대상 ──
            bool devFree = CombatConfig.DevLungeFree;   // 개발용: 스택·쿨·대상 무시
            if ((cmd.lunge || c.lungeBufferTicks > 0) && (c.lungeStacks > 0 || devFree)
                && (c.lungeCooldown == 0 || devFree) && c.hitStunTicks == 0)
            {
                int targetId = cmd.lungeTargetId >= 0
                    ? cmd.lungeTargetId
                    : FindLungeTarget(in w, in p, in svc);
                Vector3 dest = p.pos;
                bool haveDest = targetId >= 0 && TryLockDestination(in w, in p, in svc, targetId, out dest);

                // 대상이 없어도 발동 — 조준 방향 앞으로 블링크(벽은 뚫지 않는다)
                if (!haveDest && devFree)
                {
                    targetId = -1;
                    dest = DevFreeDestination(in p, in svc);
                    haveDest = true;
                }

                if (haveDest)
                {
                    int travel = Mathf.Max(1, CombatConfig.LungeTravel);   // 방식에 따라 3틱(블링크) 또는 8틱(돌진)

                    c.lungePhase = CombatConfig.LgTravel;   // 윈드업 없음 — 즉시 발동
                    c.lungeTicks = 0;
                    c.lungeTargetId = targetId;
                    c.lungeStart = p.pos;
                    c.lungeDest = dest;
                    c.lungeTravelTicks = travel;
                    c.lungeHitDone = false;
                    // 개발 모드에선 쿨·스택을 소모하지 않는다(연속 시전으로 애니메이션 확인)
                    c.lungeCooldown = devFree ? 0 : CombatConfig.LungeCooldownTicks;
                    if (!devFree) c.lungeStacks--;   // 스택 1 소모
                    c.lungeBufferTicks = 0;          // 예약 소비
                    p.jumpCount = 0;           // 우클 직후 더블점프 리필

                    // 표적 이동봉쇄(bind): 블링크 동안만 위치·중력 동결(공중이면 공중에). 공격은 계속.
                    int ti = FindEnemyIndex(in w, targetId);
                    if (ti >= 0)
                        w.enemies[ti].combat.bindTicks = travel + CombatConfig.LungeBindExtraTicks;

                    // 평타 중이었으면 즉시 캔슬 + 콤보 초기화(찌르기는 언제든 발동 가능)
                    c.attackPhase = CombatConfig.PhNone;
                    c.attackPhaseTicks = 0;
                    c.attackStep = 0;
                    c.comboStep = 0;
                    c.comboWindow = 0;
                    c.attackBuffered = false;
                    // 대상 응시
                    Vector3 face = dest - p.pos; face.y = 0f;
                    if (face.sqrMagnitude > 1e-4f) p.yaw = Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg;
                    return;
                }
                // 대상 없으면 발동 안 함
            }

            // ── 평타 진행/시작 (2연타 콤보) ──
            if (c.attackPhase == CombatConfig.PhNone)
            {
                // 콤보창 감소 — 만료되면 다음 좌클릭은 다시 평타1
                if (c.comboWindow > 0)
                {
                    c.comboWindow--;
                    if (c.comboWindow == 0) c.comboStep = 0;
                }

                // 선입력 포함해서 발동 판정
                bool want = cmd.attack || c.attackBuffered;
                if (want && c.hitStunTicks == 0)
                {
                    c.attackStep = c.comboStep;          // 0=평타1, 1=평타2
                    c.attackPhase = CombatConfig.PhWindup;
                    c.attackPhaseTicks = 0;
                    c.attackHitDone = false;
                    c.attackBuffered = false;
                    c.attackHitMask0 = 0UL;              // 새 스윙 — 때린 적 기록 초기화
                    c.attackHitMask1 = 0UL;
                    c.attackElapsed = 0;                 // 즉발 판정창 기준
                    c.comboWindow = 0;                   // 발동했으니 창은 닫는다
                }
                else if (!want)
                {
                    c.attackBuffered = false;
                }
                return;
            }

            // 공격 중 좌클릭 → 선입력으로 기억(콤보창이 열리는 순간 자동 발동)
            if (cmd.attack) c.attackBuffered = true;

            // 대시로는 캔슬되지 않는다(설계상 찌르기만 평타를 끊는다 — 위쪽 런지 분기에서 처리).

            c.attackPhaseTicks++;
            c.attackElapsed++;                 // 공격 시작부터의 총 경과(즉발 판정창 기준)
            switch (c.attackPhase)
            {
                case CombatConfig.PhWindup:
                    if (c.attackPhaseTicks >= CombatConfig.AtkWindup(c.attackStep))
                    {
                        // 즉발이면 판정은 이미 0틱부터 돌았으므로 Active 페이즈에 머물 필요가 없다.
                        // (머물면 그만큼 동작만 길어진다 — "판정 늘리니 느려진다"의 원인)
                        c.attackPhase = CombatConfig.AttackInstantJudge
                                      ? CombatConfig.PhRecovery : CombatConfig.PhActive;
                        c.attackPhaseTicks = 0;
                    }
                    break;
                case CombatConfig.PhActive:
                    if (c.attackPhaseTicks >= CombatConfig.AtkActive(c.attackStep))
                    { c.attackPhase = CombatConfig.PhRecovery; c.attackPhaseTicks = 0; }
                    break;
                case CombatConfig.PhRecovery:
                    if (c.attackPhaseTicks >= CombatConfig.AtkRecovery(c.attackStep))
                    {
                        c.attackPhase = CombatConfig.PhNone;
                        c.attackPhaseTicks = 0;
                        // 평타1이 끝났으면 콤보창을 연다. 평타2는 마무리라 창 없이 초기화.
                        if (c.attackStep == 0)
                        {
                            c.comboStep = 1;
                            c.comboWindow = CombatConfig.ComboWindowTicks;
                        }
                        else
                        {
                            c.comboStep = 0;
                            c.comboWindow = 0;
                            c.attackBuffered = false;   // 마무리 후 눌러둔 입력은 흘린다
                        }
                    }
                    break;
            }
        }

        /// <summary>
        /// 런지 진행(윈드업 없음). Travel: 직선 ease-out으로 도착점까지, 매 틱 에임을 타깃에 고정,
        /// 3D 캡슐 스윕으로 관통 차단. 벽에 막히면 즉시 캔슬(피해 없음, 바인드 해제).
        /// 도착 시 LgRecovery(0틱)로 넘어가 CombatResolve가 임팩트 1회 처리 후 즉시 종료.
        /// </summary>
        static void StepLunge(ref SimWorld w, in SimServices svc, float dt)
        {
            ref PlayerSim p = ref w.player;
            ref PlayerCombatState c = ref p.combat;
            c.lungeTicks++;

            if (c.lungePhase == CombatConfig.LgRecovery)
            {
                if (c.lungeTicks >= CombatConfig.LungeRecoveryTicks)
                { c.lungePhase = CombatConfig.LgNone; c.lungeTicks = 0; c.lungeTargetId = -1; }
                return;
            }

            // ── LgTravel ──
            // 에임 타깃 고정: 표적을 매 틱 바라봄(마우스 무시). 표적은 바인드로 정지 상태.
            int ti = FindEnemyIndex(in w, c.lungeTargetId);
            Vector3 look = (ti >= 0 ? w.enemies[ti].pos : c.lungeDest) - p.pos; look.y = 0f;
            if (look.sqrMagnitude > 1e-4f) p.yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;

            // 이동: 마지막 틱에 도착점 도달. 캡슐 스윕으로 3D 관통 차단.
            //   기존(블링크) — 남은 거리를 남은 틱으로 등분(등속)
            //   둠식(돌진)   — 시작점→도착점을 ease-out으로 보간해 끝에서 감속(돌진이 보인다)
            int total = Mathf.Max(1, c.lungeTravelTicks);
            Vector3 delta;
            if (CombatConfig.LungeDoomStyle)
            {
                float t = Mathf.Clamp01(c.lungeTicks / (float)total);
                float inv = 1f - t;
                float e = 1f - inv * inv * inv;                    // cubic ease-out
                delta = LungeArcPoint(in c, e) - p.pos;
            }
            else
            {
                int remain = Mathf.Max(1, total - c.lungeTicks + 1);
                delta = (c.lungeDest - p.pos) / remain;
            }
            float dist = delta.magnitude;
            if (dist > 1e-5f)
            {
                Vector3 dir = delta / dist;
                Vector3 bottom = p.pos + Vector3.up * SimConfig.PlayerRadius;
                Vector3 topC   = p.pos + Vector3.up * (SimConfig.PlayerHeight - SimConfig.PlayerRadius);
                CastHit hit = svc.Collision.CapsuleCast(bottom, topC, SimConfig.PlayerRadius, dir, dist);
                float moved = hit.hit ? Mathf.Max(0f, hit.distance - 0.02f) : dist;
                p.pos += dir * moved;

                // 벽에 막힘(거의 못 나아감) + 아직 도착 전 → 즉시 캔슬
                if (hit.hit && moved < dist * 0.1f && Vector3.Distance(p.pos, c.lungeDest) > 0.3f)
                { CancelLunge(ref w); return; }
            }
            p.vel = Vector3.zero;

            // 블링크 끝 → 임팩트(CombatResolve) + 즉시 조작 복귀(후딜 0)
            if (c.lungeTicks >= total)
            { c.lungePhase = CombatConfig.LgRecovery; c.lungeTicks = 0; }
        }

        /// <summary>
        /// 찌르기 포물선 위의 한 점. e는 이징이 적용된 진행도(0=시작, 1=도착).
        ///
        /// 시작→도착 직선에 <b>대상 높이 방향으로 볼록한 호</b>를 더한다.
        ///   위 적 → 위로 부풂 · 아래 적 → 아래로 부풂 (찌르는 동작이라 항상 위로 솟으면 어색)
        /// 부푸는 양은 sin(πe)라 양 끝에서 0 — 시작점·도착점은 직선과 정확히 같다.
        ///
        /// ★ 뷰(카메라)도 이 함수를 그대로 써서 에임이 실제 경로를 따라가게 한다.
        ///   따로 계산하면 몸과 시선이 어긋난다.
        /// </summary>
        public static Vector3 LungeArcPoint(in PlayerCombatState c, float e)
        {
            Vector3 straight = Vector3.Lerp(c.lungeStart, c.lungeDest, e);
            if (!CombatConfig.LungeDoomStyle) return straight;

            float dy = c.lungeDest.y - c.lungeStart.y;
            // 높이차에 비례하되, 수평 대상에서도 밋밋하지 않게 최소치를 준다.
            float bulge = Mathf.Abs(dy) * CombatConfig.LungeArcAmount;
            if (bulge < CombatConfig.LungeArcMinBulge) bulge = CombatConfig.LungeArcMinBulge;
            if (bulge > CombatConfig.LungeArcMaxBulge) bulge = CombatConfig.LungeArcMaxBulge;
            // 대상이 위면 위로, 아래면 아래로. 수평이면 살짝 위로(찌르며 파고드는 느낌).
            float dir = dy >= 0f ? 1f : -1f;

            straight.y += dir * bulge * Mathf.Sin(Mathf.PI * Mathf.Clamp01(e));
            return straight;
        }

        /// <summary>런지 즉시 종료: 표적 바인드 해제, 피해 없음(도착 전 벽 캔슬용).</summary>
        static void CancelLunge(ref SimWorld w)
        {
            ref PlayerCombatState c = ref w.player.combat;
            int ti = FindEnemyIndex(in w, c.lungeTargetId);
            if (ti >= 0) w.enemies[ti].combat.bindTicks = 0;
            c.lungePhase = CombatConfig.LgNone;
            c.lungeTicks = 0;
            c.lungeTargetId = -1;
            c.lungeHitDone = false;
        }

        /// <summary>
        /// 대형몹 글로리킬 처형 컷신. Slash1→Slash2→Dash. 대상은 gloryStage로 얼려둠.
        /// slash 단계는 제자리 응시, dash 단계는 고정 방향 관통 러쉬. 끝에 실제 사망.
        /// 절단 3단계 연출은 뷰(Dismemberment)가 gloryStage 읽어 구동.
        /// </summary>
        static void StepGlory(ref SimWorld w, in SimServices svc, float dt)
        {
            ref PlayerSim p = ref w.player;
            ref PlayerCombatState c = ref p.combat;
            c.gloryTicks++;

            ref EnemySim target = ref w.enemies[c.gloryTargetId];

            switch (c.gloryPhase)
            {
                case CombatConfig.GlSlash1:
                    if (c.gloryTicks >= CombatConfig.GlorySlashTicks)
                    { c.gloryPhase = CombatConfig.GlSlash2; c.gloryTicks = 0; target.combat.gloryStage = 2; }
                    break;
                case CombatConfig.GlSlash2:
                    if (c.gloryTicks >= CombatConfig.GlorySlashTicks)
                    {
                        c.gloryPhase = CombatConfig.GlDash; c.gloryTicks = 0;
                        target.combat.gloryStage = 3;   // 폭발 단계
                        Vector3 gd = target.pos - p.pos; gd.y = 0f;
                        c.gloryDir = gd.sqrMagnitude > 1e-4f
                            ? gd.normalized
                            : new Vector3(Mathf.Sin(p.yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(p.yaw * Mathf.Deg2Rad));
                    }
                    break;
                case CombatConfig.GlDash:
                    p.pos = CharacterMotor.MoveHorizontal(svc.Collision, p.pos,
                                                          c.gloryDir * CombatConfig.GloryDashSpeed * dt,
                                                          SimConfig.PlayerRadius, SimConfig.PlayerHeight);
                    if (c.gloryTicks >= CombatConfig.GloryDashTicks)
                    {
                        c.gloryPhase = CombatConfig.GlNone; c.gloryTicks = 0;
                        target.alive = false;             // 실제 사망(뷰 폭발은 gloryStage=3로 이미 처리)
                        target.combat.deathTick = w.tick;
                        c.lungeStacks = Mathf.Min(CombatConfig.LungeMaxStacks, c.lungeStacks + 1);   // 처형 = 스택 +1
                    }
                    break;
            }

            // 슬래시 단계엔 대상 응시(dash는 이동 중이라 스킵)
            if (c.gloryPhase == CombatConfig.GlSlash1 || c.gloryPhase == CombatConfig.GlSlash2)
            {
                Vector3 face = target.pos - p.pos; face.y = 0f;
                if (face.sqrMagnitude > 1e-4f) p.yaw = Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg;
            }
        }

        /// <summary>
        /// 런지 자동 타깃: 3D 조준 레이(yaw+aimPitch)로부터의 "수직 거리"가 가장 작은 적.
        /// 각도가 아니라 수직 거리라, 같은 각도면 먼 적이 불리 → "가깝고 조준점에 걸린 적"을 우선.
        /// 게이트: 레이 앞쪽 거리 min~max + 수직거리 ≤ 보정반경 + 높이차 + LOS.
        /// 우선순위 = 수직거리 → 레이앞거리 → id (전부 결정론적). 예측도 이 함수 재사용.
        /// </summary>
        public static int FindLungeTarget(in SimWorld w, in PlayerSim p, in SimServices svc)
        {
            Vector3 eye = p.pos + Vector3.up * (SimConfig.PlayerHeight * 0.7f);
            Vector3 dir = AimDir(p.yaw, p.aimPitch);

            int bestId = -1;
            float bestPerp = float.MaxValue;
            float bestAlong = float.MaxValue;

            for (int i = 0; i < w.enemyCount; i++)
            {
                ref readonly EnemySim e = ref w.enemies[i];
                if (!IsLungeable(in p, in e, in svc, eye, dir, out float perp, out float along)) continue;

                bool better = perp < bestPerp - 1e-5f
                    || (Mathf.Abs(perp - bestPerp) <= 1e-5f
                        && (along < bestAlong - 1e-5f
                            || (Mathf.Abs(along - bestAlong) <= 1e-5f && e.id < bestId)));
                if (!better) continue;
                bestId = e.id; bestPerp = perp; bestAlong = along;
            }
            return bestId;
        }

        /// <summary>
        /// 런지로 targetId를 칠 수 있으면 true + 실제 착지점(대상 고도 반영)을 out으로 준다.
        /// 실제 발동(Step)이 쓰는 IsLungeable + TryLockDestination과 <b>동일 경로</b> — 판정 규칙은 Sim 한 곳뿐.
        /// 예측이 자기 재구현 대신 이걸 부르면 "예측 됨 → 실제 안 됨" 괴리가 사라진다.
        ///
        /// ※ 판정은 p.yaw/p.aimPitch(현재 조준) 기준이다. 예측은 후보 대상을 <b>향하도록 조준을 세팅한</b>
        ///   가상 PlayerSim을 넣어야 실제와 일치한다(그냥 현재 조준으로 부르면 다른 결과가 나옴).
        /// 순수 함수(월드+충돌 질의)라 결정론 불변.
        /// </summary>
        public static bool CanLunge(in SimWorld w, in PlayerSim p, in SimServices svc,
                                    int targetId, out Vector3 destination)
        {
            destination = p.pos;
            int idx = FindEnemyIndex(in w, targetId);
            if (idx < 0) return false;

            Vector3 eye = p.pos + Vector3.up * (SimConfig.PlayerHeight * 0.7f);
            Vector3 dir = AimDir(p.yaw, p.aimPitch);
            ref readonly EnemySim e = ref w.enemies[idx];
            if (!IsLungeable(in p, in e, in svc, eye, dir, out _, out _)) return false;

            return TryLockDestination(in w, in p, in svc, targetId, out destination);
        }

        static Vector3 AimDir(float yaw, float pitch)
            => Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;

        /// <summary>
        /// 개발용(DevLungeFree): 대상이 없을 때의 도착점. 조준 방향 수평 성분으로 전진하되
        /// 벽에 막히면 그 앞에서 멈춘다. 지면 스냅은 이동 처리가 알아서 한다.
        /// </summary>
        static Vector3 DevFreeDestination(in PlayerSim p, in SimServices svc)
        {
            float want = Mathf.Max(0f, CombatConfig.DevLungeBlinkDist);
            if (want <= 0.01f) return p.pos;

            Vector3 dir = AimDir(p.yaw, p.aimPitch); dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) return p.pos;
            dir.Normalize();

            // 허리 높이에서 전방 검사 — 벽을 뚫고 나가지 않게
            Vector3 from = p.pos + Vector3.up * (SimConfig.PlayerHeight * 0.5f);
            var hit = svc.Collision.Raycast(from, dir, want + SimConfig.PlayerRadius);
            float dist = hit.hit ? Mathf.Max(0f, hit.distance - SimConfig.PlayerRadius) : want;
            return p.pos + dir * dist;
        }

        /// <summary>런지 유효 대상인가 + 조준 레이 기준 perp(수직거리)·along(앞거리) 산출.</summary>
        static bool IsLungeable(in PlayerSim p, in EnemySim e, in SimServices svc,
                                Vector3 eye, Vector3 dir, out float perp, out float along)
        {
            perp = float.MaxValue; along = float.MaxValue;
            if (!e.alive || e.combat.gloryStage > 0) return false;
            if (Mathf.Abs(e.pos.y - p.pos.y) > CombatConfig.LungeHeightTolerance) return false;

            Vector3 c = e.pos + Vector3.up * (e.height * 0.5f);   // 적 중심
            Vector3 v = c - eye;
            along = Vector3.Dot(v, dir);                          // 레이 앞쪽 투영 거리
            if (along < CombatConfig.LungeMinRange || along > CombatConfig.LungeMaxRange + e.radius) return false;

            perp = (v - dir * along).magnitude;                   // 레이까지 수직 거리(조준 벗어난 정도)
            if (perp > CombatConfig.LungeAimRadius + e.radius) return false;   // 조준 보정 밖

            // LOS: 눈 → 적 중심
            Vector3 d = c - eye; float len = d.magnitude;
            if (len > 1e-4f && svc.Collision.Raycast(eye, d / len, len).hit) return false;
            return true;
        }

        /// <summary>도착점 = 적 앞 LungeStopDistance 지점(지면 스냅). 시작 순간 1회 고정.</summary>
        static bool TryLockDestination(in SimWorld w, in PlayerSim p, in SimServices svc,
                                       int targetId, out Vector3 dest)
        {
            dest = p.pos;
            int idx = FindEnemyIndex(in w, targetId);
            if (idx < 0) return false;
            ref readonly EnemySim e = ref w.enemies[idx];

            // 수평 방향으로만 정지간격을 두어 "적 옆(수평 인접)"에 서게. 높이는 적 + 살짝 위.
            Vector3 flat = e.pos - p.pos; flat.y = 0f;
            if (flat.sqrMagnitude < 1e-6f)
                flat = new Vector3(Mathf.Sin(p.yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(p.yaw * Mathf.Deg2Rad));
            flat.Normalize();
            dest = e.pos - flat * (CombatConfig.LungeStopDistance + e.radius);
            dest.y = e.pos.y + CombatConfig.LungeAimUp;   // 적과 같은 높이 + 살짝 위(위든 아래든 나란히)
            return true;
        }

        /// <summary>id로 살아있는 적 인덱스. 없으면 -1.</summary>
        public static int FindEnemyIndex(in SimWorld w, int id)
        {
            for (int i = 0; i < w.enemyCount; i++)
                if (w.enemies[i].id == id && w.enemies[i].alive) return i;
            return -1;
        }
    }
}
