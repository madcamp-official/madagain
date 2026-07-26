using UnityEngine;

namespace Game.Sim
{
    /// <summary>
    /// 대미지·스턴·HP·처치 정리. ★ combat 소유. SimStep이 적 이동 다음에 호출.
    /// 평타(구 오버랩 또는 부채꼴) · 런지 임팩트(단일 대상) · 적→플레이어 히트 큐 적용.
    /// 명중하면 피해 + 경직(= 그 공격의 히트스톱 + StunExtraTicks)을 준다.
    /// 대형몹 막타는 글로리킬 컷신 진입.
    /// </summary>
    public static class CombatResolve
    {
        // 스윙당 적 1회를 보장하는 비트마스크(적 인덱스 0~127)
        static bool HitMaskHas(in PlayerCombatState c, int i) =>
            i < 64 ? (c.attackHitMask0 & (1UL << i)) != 0UL
                   : (c.attackHitMask1 & (1UL << (i - 64))) != 0UL;

        static void HitMaskSet(ref PlayerCombatState c, int i)
        {
            if (i < 64) c.attackHitMask0 |= 1UL << i;
            else        c.attackHitMask1 |= 1UL << (i - 64);
        }

        public static void Run(ref SimWorld w, in SimServices svc, float dt)
        {
            // 타이머 감소 (스턴 = 피격 경직 · 바인드 = 런지 표적 이동봉쇄)
            for (int i = 0; i < w.enemyCount; i++)
            {
                if (w.enemies[i].combat.stunTicks > 0) w.enemies[i].combat.stunTicks--;
                if (w.enemies[i].combat.bindTicks > 0) w.enemies[i].combat.bindTicks--;
            }

            ref PlayerCombatState pc = ref w.player.combat;

            // 무적 타이머 감소(틱당 1회). 아래 히트 적용보다 먼저 — "이번 틱 피격 → 다음 틱부터 카운트다운".
            if (pc.invulnTicks > 0) pc.invulnTicks--;

            // ── 평타 판정 ──
            // 즉발: 공격 시작(0틱)부터 판정창이 열린다 — 선딜을 기다리지 않는다.
            //       선딜은 연출 타이밍일 뿐이고, 캔슬해도 이미 때린 것은 유효하다.
            // 기존: 선딜이 끝난 Active 페이즈에서만.
            bool judging = CombatConfig.AttackInstantJudge
                ? (pc.attackPhase != CombatConfig.PhNone &&
                   pc.attackElapsed < CombatConfig.AtkJudge(pc.attackStep))
                : (pc.attackPhase == CombatConfig.PhActive);

            if (judging)
            {
                if (CombatConfig.UseSphereMelee)
                {
                    // 오버워치식: 시선 앞 구 오버랩을 <b>활성 틱마다</b> 검사.
                    // 각도 개념이 없어 부채꼴 경계에서 빗나가는 일이 없고, 피치가 그대로 반영된다.
                    // 휘두르는 중 시선을 돌리면 구도 따라가므로 조준을 늦게 고쳐도 맞는다.
                    Vector3 eye = w.player.pos + Vector3.up * CombatConfig.MeleeEyeHeight;
                    Vector3 dir = CombatHit.LookDir(w.player.yaw, w.player.aimPitch);
                    Vector3 center = eye + dir * CombatConfig.MeleeOffset;

                    for (int i = 0; i < w.enemyCount; i++)
                    {
                        ref EnemySim e = ref w.enemies[i];
                        if (!e.alive || e.combat.gloryStage > 0) continue;
                        if (HitMaskHas(in pc, i)) continue;              // 이 스윙에서 이미 때린 적
                        // 보스(Orb)는 구 히트박스 — 구(평타) vs 구(중심 e.pos·반경 e.radius). 나머지는 캡슐.
                        bool hit = e.ai.mobility == MobilityType.Orb
                            ? (center - e.pos).sqrMagnitude <=
                              (CombatConfig.MeleeRadius + e.radius) * (CombatConfig.MeleeRadius + e.radius)
                            : CombatHit.SphereHitsCapsule(center, CombatConfig.MeleeRadius, e.pos, e.radius, e.height);
                        if (!hit) continue;
                        HitMaskSet(ref pc, i);
                        HitEnemy(ref w, i, CombatConfig.AtkStun(pc.attackStep));
                    }
                    pc.attackHitDone = true;   // 뷰(이펙트·소리)가 보는 플래그는 그대로 유지
                }
                else if (!pc.attackHitDone)
                {
                    // 기존 부채꼴: Active 진입 시 1회, 수평 평면 + 높이차 컷오프
                    pc.attackHitDone = true;
                    for (int i = 0; i < w.enemyCount; i++)
                    {
                        ref EnemySim e = ref w.enemies[i];
                        if (!e.alive || e.combat.gloryStage > 0) continue;   // 처형 중 적은 제외
                        if (Mathf.Abs(e.pos.y - w.player.pos.y) > CombatConfig.AttackHeightTolerance) continue;
                        if (!CombatHit.InCone(w.player.pos, w.player.yaw, e.pos,
                                              CombatConfig.AttackConeRange + e.radius, CombatConfig.AttackConeHalfAngle))
                            continue;   // + e.radius = 표면까지(대형몹 자동 반영)
                        HitEnemy(ref w, i, CombatConfig.AtkStun(pc.attackStep));
                    }
                }
            }

            // ── 런지 임팩트: Travel 완료(Recovery 진입) 후 1회, 고정 대상만 ──
            //    이동 중 대상이 죽었으면 도착만 하고 추가 피해 없음.
            if (pc.lungePhase == CombatConfig.LgRecovery && !pc.lungeHitDone)
            {
                pc.lungeHitDone = true;
                int idx = PlayerCombat.FindEnemyIndex(in w, pc.lungeTargetId);
                if (idx >= 0 && w.enemies[idx].combat.gloryStage == 0)
                    HitEnemy(ref w, idx, CombatConfig.LungeStun);
            }

            // ── 적 공격 적용: AI가 큐잉한 히트를 플레이어 HP에 반영 + 피격 경직 ──
            //    글로리킬 처형 중엔 무적(큐만 비움). 막기는 폐기 — 방어판정 없음.
            for (int k = 0; k < w.pendingHitCount; k++)
            {
                if (pc.gloryPhase != CombatConfig.GlNone) break;   // 글로리킬 처형 중 무적(기존)
                if (pc.invulnTicks > 0) continue;                  // 무적: 이 히트 무시(HP 안 깎음)
                pc.hp -= w.pendingHits[k].dmg;
                if (pc.hp < 0) pc.hp = 0;
                pc.hitStunTicks = CombatConfig.PlayerHitStunTicks;
                pc.invulnTicks  = CombatConfig.PlayerInvulnTicks;  // 피격 순간 무적 시작 → 같은 틱 나머지 + 이후 N틱 무시
            }
            w.pendingHitCount = 0;
        }

        /// <summary>
        /// 적 1타 처리. 대형몹 막타면 즉사 대신 글로리킬 컷신 진입(동시 1건).
        /// </summary>
        /// <param name="stunTicks">이 타격이 줄 경직. 이미 더 긴 경직 중이면 유지(덮어쓰기 = 최댓값).</param>
        static void HitEnemy(ref SimWorld w, int i, int stunTicks)
        {
            ref EnemySim e = ref w.enemies[i];
            ref PlayerCombatState pc = ref w.player.combat;

            // 보스(구 코어): 경직·넉백·글로리킬 없이 <b>HP만</b> 깎는다(대형 코어라 부적합).
            // 페이즈 경계(BossPhaseHp=15씩, 30/15)를 이번 타격으로 넘으면 숨는다(Hide) —
            // EnemyBrain.StepBoss가 하강→30s 대기→다음 페이즈로 재등장시킨다. 소진(0)되면 사망.
            if (e.ai.mobility == MobilityType.Orb)
            {
                // 숨기/재등장 이동 중엔 무적 — 위치상 닿지 않지만 하강/상승 중 스침 방지 가드.
                if (e.ai.state == EnemyState.Hide || e.ai.state == EnemyState.Emerge) return;
                int before = e.combat.health;
                e.combat.health -= CombatConfig.Damage;
                if (e.combat.health <= 0)
                {
                    // 처치 — ★ 죽음 연출 미정(TODO): 일단 일반 제거 경로로 사라진다.
                    e.combat.health = 0;
                    e.alive = false;
                    e.combat.deathTick = w.tick;
                    return;
                }
                int hi = AIConfig.BossPhaseHp * 2, lo = AIConfig.BossPhaseHp;   // 경계 6/3
                bool crossed = (before > hi && e.combat.health <= hi) || (before > lo && e.combat.health <= lo);
                if (AIConfig.BossCanHide && crossed)
                { e.ai.state = EnemyState.Hide; e.ai.stateTicks = 0; }   // 숨김 켜졌을 때만: 패턴 중단하고 숨음
                return;
            }

            if (e.ai.size == SizeClass.Large && pc.gloryPhase == CombatConfig.GlNone
                && e.combat.health - CombatConfig.Damage <= 0)
            {
                pc.gloryPhase = CombatConfig.GlSlash1;
                pc.gloryTicks = 0;
                pc.gloryTargetId = i;
                e.combat.gloryStage = 1;     // 처형 시작(view: slash1 절단), 얼림
                e.combat.stunTicks = 0;
                // 죽이지 않음: health 유지, gloryStage로 이후 판정·AI·분리 제외
                return;
            }

            e.combat.health -= CombatConfig.Damage;
            // 경직 부여 — 더 긴 쪽 유지(약한 타격이 강한 경직을 깎지 않게)
            if (stunTicks > e.combat.stunTicks) e.combat.stunTicks = stunTicks;
            if (e.combat.health <= 0)
            {
                e.alive = false;
                e.combat.deathTick = w.tick;
                pc.lungeStacks = Mathf.Min(CombatConfig.LungeMaxStacks, pc.lungeStacks + 1);   // 처치 = 스택 +1
            }
        }
    }
}
