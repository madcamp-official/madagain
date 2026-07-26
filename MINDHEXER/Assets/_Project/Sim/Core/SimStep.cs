using UnityEngine;

namespace Game.Sim
{
    /// <summary>
    /// 시뮬레이션의 유일한 진입점. 월드를 한 틱 전진.
    /// 실제 게임·예측·검증이 모두 이 함수를 공유한다.
    /// 순서: 플레이어 → 전투 → 적 → 대미지정리 → 겹침 분리 → tick++.
    /// </summary>
    public static class SimStep
    {
        // 분리 밀침 누적용 스크래치 (단일 스레드 재사용, 매 호출 초기화 → 결정론 안전)
        static readonly Vector3[] pushScratch = new Vector3[SimConfig.MaxEnemies];

        public static void Run(ref SimWorld w, in InputCmd cmd, in SimServices svc)
        {
            float dt = SimConfig.TickDelta;

            w.prevPlayerPos = w.player.pos;   // 이동 전 위치 → 원거리 리드 조준용 속도 산출

            // 글로리킬 처형 중엔 조작 잠금(이동·점프·대시·공격·런지). 시선은 통과(카메라 고정이 이김).
            InputCmd pcmd = cmd;
            if (w.player.combat.gloryPhase != CombatConfig.GlNone)
            {
                pcmd.move = Vector2.zero;
                pcmd.jump = pcmd.dash = pcmd.attack = pcmd.lunge = false;
            }

            using (SimStepProfiler.MeasurePlayerMovement())
                PlayerMovement.Step(ref w.player, in pcmd, in svc, dt);
            using (SimStepProfiler.MeasurePlayerCombat())
                PlayerCombat.Step(ref w, in pcmd, in svc, dt);   // ← combat (평타/런지/글로리킬)

            using (SimStepProfiler.MeasureSeparationForce())
                EnemyBrain.ComputeSeparation(in w);          // ← 이웃 회피 벡터 O(N²) 1회 산출(뭉침 방지)
            using (SimStepProfiler.MeasureEnemyAI(w.enemyCount))
                for (int i = 0; i < w.enemyCount; i++)
                    EnemyBrain.Step(ref w, i, in svc, dt);   // ← AI 세션 (상태머신; 내부에서 이동은 EnemyMovement)

            using (SimStepProfiler.MeasureProjectile(w.projectileCount))
                ProjectileSystem.Step(ref w, in svc, dt);       // ← AI 세션 (투사체 이동·충돌 → 히트 큐)
            using (SimStepProfiler.MeasureCombatResolve())
                CombatResolve.Run(ref w, in svc, dt);           // ← combat 세션 (대미지/스턴/처치 + 히트 큐 적용)

            using (SimStepProfiler.MeasureCollisionSeparation())
                Separate(ref w, in svc);
            using (SimStepProfiler.MeasureNavigationClamp())
                ClampToNavMesh(ref w, in svc);   // 틱 끝: 밀려난 지상몹을 걷기 가능 표면으로 되당김(다리 낙하 방지)

            ResolveOverlaps(ref w, in svc);      // 맨 마지막: 지오메트리에 낀 것을 밀어냄(관통 원천 차단)

            w.tick++;
        }

        /// <summary>
        /// 틱 끝 겹침 해소 — 지오메트리에 파고든 캐릭터를 밀어낸다.
        ///
        /// 이동은 스윕 캐스트로 벽을 막지만, 유니티 스윕은 <b>시작 시점에 이미 겹친 콜라이더를
        /// 무시</b>한다. 그래서 한 번 벽 안으로 들어가면(얇은 판·밀침·되당김·순간이동 등) 그 벽이
        /// 없는 것처럼 계속 통과해 버린다. 여기서 매 틱 빼내면 그 상태 자체가 성립하지 않는다.
        ///
        /// 제외: 공중 도약 중(스크립트 궤적이라 밀면 궤적이 깨짐) · 발사 중(웨이브 배관 펄스).
        /// </summary>
        static void ResolveOverlaps(ref SimWorld w, in SimServices svc)
        {
            CharacterMotor.ResolveOverlap(svc.Collision, ref w.player.pos,
                                          SimConfig.PlayerRadius, SimConfig.PlayerHeight);

            for (int i = 0; i < w.enemyCount; i++)
            {
                ref EnemySim e = ref w.enemies[i];
                if (!e.alive) continue;
                if (e.traversalPhase == TraversalPhase.Airborne) continue;   // 도약 궤적 중
                if (e.launchTicks > 0) continue;                             // 스폰 펄스로 날아가는 중
                // 보스(Orb): 고정 포탑 — 숨을 때 지면 아래(BossHideYOffset)로 내려가므로
                // 여기서 밀어내면 하강과 싸운다. StepBoss가 위치를 전적으로 소유한다.
                if (e.ai.mobility == MobilityType.Orb) continue;
                CharacterMotor.ResolveOverlap(svc.Collision, ref e.pos, e.radius, e.height);
            }
        }

        /// <summary>
        /// 이동·겹침밀침으로 걷기 가능 표면(navmesh) 밖으로 밀려난 지상몹을 되당긴다 — 얇은 다리 낙하 방지.
        /// 무엇이 밀었든(추격·분리 스티어링·겹침 밀침) 틱 끝에 한 번 잡으므로 낙하가 원천 차단된다.
        /// 제외: 하강 중(일부러 낙하) · <b>층이동 도약 중</b> · 공중몹(떠 있음) · 돌진 중(오버커밋).
        /// XZ만 당기고 y는 지면 스냅에 맡긴다(수직 팝 방지). navmesh 없으면(그래프 모드) 무동작.
        /// </summary>
        static void ClampToNavMesh(ref SimWorld w, in SimServices svc)
        {
            for (int i = 0; i < w.enemyCount; i++)
            {
                ref EnemySim e = ref w.enemies[i];
                if (!e.alive) continue;
                if (e.descentPhase != DescentPhase.None) continue;   // 하강 중(구식 경로)
                // ★ 층이동 도약 중엔 절대 당기면 안 된다. 공중 궤적은 걷기 가능 표면 밖이라
                //   매 틱 XZ를 바닥으로 되당기면 탄도와 싸워 뚝뚝 끊기고, 착지에 도달하지 못한다.
                if (e.traversalPhase != TraversalPhase.None) continue;
                if (e.ai.mobility == MobilityType.Flying) continue;  // 공중몹
                if (e.ai.mobility == MobilityType.Orb) continue;     // 보스(부유)
                if (e.ai.state == EnemyState.ChargeRun) continue;    // 돌진 중(오버커밋 허용)
                if (svc.Pathfinder.ClampToWalkable(e.pos, SimConfig.EnemyNavClampDist, out Vector3 onMesh))
                { e.pos.x = onMesh.x; e.pos.z = onMesh.z; }
            }
        }

        /// <summary>
        /// 겹침 분리(대칭). 밀친 결과를 CharacterMotor로 적용해 벽/경사를 뚫지 않게 한다.
        /// 밀침량을 먼저 누적(O(N²) 순수계산)한 뒤, 캐릭터당 한 번 이동(충돌 질의)으로 적용.
        /// </summary>
        static void Separate(ref SimWorld w, in SimServices svc)
        {
            float pr = SimConfig.PlayerRadius;

            for (int i = 0; i < w.enemyCount; i++) pushScratch[i] = Vector3.zero;
            Vector3 playerPush = Vector3.zero;

            // 적 ↔ 적 (하강 중인 적 제외) — 개별 반경 합으로 최소거리.
            // 보스(Orb)도 제외: 고정 포탑이라 밀리면 안 되고, 숨기 하강 중 플레이어·몹을 밀치면 안 된다.
            for (int i = 0; i < w.enemyCount; i++)
            {
                if (!w.enemies[i].alive || w.enemies[i].traversalPhase == TraversalPhase.Airborne || w.enemies[i].combat.gloryStage > 0 || w.enemies[i].ai.mobility == MobilityType.Orb) continue;
                for (int j = i + 1; j < w.enemyCount; j++)
                {
                    if (!w.enemies[j].alive || w.enemies[j].traversalPhase == TraversalPhase.Airborne || w.enemies[j].combat.gloryStage > 0 || w.enemies[j].ai.mobility == MobilityType.Orb) continue;
                    Vector3 p = Push(w.enemies[i].pos, w.enemies[i].height,
                                     w.enemies[j].pos, w.enemies[j].height,
                                     w.enemies[i].radius + w.enemies[j].radius,
                                     w.enemies[i].id, w.enemies[j].id);
                    pushScratch[i] += p;
                    pushScratch[j] -= p;
                }
            }

            // 적 ↔ 플레이어 (대칭) — 플레이어 반경 + 개별 적 반경.
            // ★ 보스(Orb)는 여기 '포함' — 플레이어가 보스에 충돌해 통과 못 한다. 단 아래 적용 루프에선
            //   Orb를 계속 제외하므로 보스 자신은 안 밀린다(고정 포탑 유지). 즉 밀치는 건 플레이어만.
            for (int i = 0; i < w.enemyCount; i++)
            {
                if (!w.enemies[i].alive || w.enemies[i].traversalPhase == TraversalPhase.Airborne || w.enemies[i].combat.gloryStage > 0) continue;
                Vector3 p = Push(w.player.pos, SimConfig.PlayerHeight,
                                 w.enemies[i].pos, w.enemies[i].height,
                                 pr + w.enemies[i].radius, -1, w.enemies[i].id);
                playerPush += p;
                pushScratch[i] -= p;
            }

            // 적용 (벽 충돌 거침 → 관통 방지). 0 밀침은 캐스트 없이 통과.
            w.player.pos = CharacterMotor.MoveHorizontal(svc.Collision, w.player.pos, playerPush, pr, SimConfig.PlayerHeight);
            for (int i = 0; i < w.enemyCount; i++)
            {
                if (!w.enemies[i].alive || w.enemies[i].traversalPhase == TraversalPhase.Airborne || w.enemies[i].combat.gloryStage > 0 || w.enemies[i].ai.mobility == MobilityType.Orb) continue;
                w.enemies[i].pos = CharacterMotor.MoveHorizontal(svc.Collision, w.enemies[i].pos,
                                                                 pushScratch[i], w.enemies[i].radius, w.enemies[i].height);
            }
        }

        /// <summary>
        /// a를 b에서 밀어낼 밀침 벡터(대칭이라 절반). 정확히 겹치면 id로 방향.
        /// 3D 판정: 세로 범위([발끝, 발끝+height])가 겹칠 때만 수평 밀침(다른 층 무간섭).
        /// </summary>
        static Vector3 Push(Vector3 a, float aH, Vector3 b, float bH, float minDist, int idA, int idB)
        {
            // 수직 구간 미겹침 → 3D 상 안 닿음 → 밀침 없음
            if (a.y >= b.y + bH || b.y >= a.y + aH) return Vector3.zero;

            Vector3 d = a - b; d.y = 0f;
            float sq = d.sqrMagnitude;
            if (sq >= minDist * minDist) return Vector3.zero;

            float dist = Mathf.Sqrt(sq);
            Vector3 dir = dist > 1e-4f ? d / dist : ((idA < idB) ? Vector3.right : Vector3.left);
            float overlap = minDist - dist;
            return dir * (overlap * SimConfig.SeparationPush);
        }
    }
}
