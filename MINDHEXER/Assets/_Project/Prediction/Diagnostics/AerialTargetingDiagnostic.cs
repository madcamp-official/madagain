using System.Text;
using UnityEngine;
using Game.Sim;

namespace Game.Prediction
{
    /// <summary>
    /// [진단, 2026-07-22] "미리보기 경로가 공중 적을 아예 안 노린다"의 원인을 실제 씬에서 특정하기
    /// 위한 일회성 도구. EditMode 테스트(AerialTargetingTests)는 8개 전부 통과하는데 게임에서만
    /// 실패하므로, 테스트가 못 보는 것들(실제 Physics LOS, 실제 스폰 구성, 개체별 호버 편차)을
    /// 실제 F 입력 시점의 월드에서 그대로 찍는다.
    ///
    /// 예전에도 정확히 같은 형태의 실패가 있었다 — StubCollision이 SampleGround를 항상 성공시키는
    /// 바람에 "런지 후보가 아예 생성되지 않는" 버그가 테스트를 그대로 통과했다. 그래서 공중 관련
    /// 회귀는 반드시 실제 씬에서 관문별로 찍어봐야 한다.
    ///
    /// 원인을 잡고 나면 이 파일과 PredictionController.Enter의 호출 한 줄을 지우면 된다.
    /// </summary>
    public static class AerialTargetingDiagnostic
    {
        /// <summary>
        /// 진단 로그를 켤지. 기본은 꺼둔다 — F를 누를 때마다 콘솔을 채우기 때문이다.
        /// 공중 타겟팅이 또 이상하면 여기를 true로 바꾸고 F를 한 번 누르면, 적별로 어느 관문
        /// (높이/사거리/LOS)에서 걸리는지와 "관문은 통과했는데 후보 경쟁에서 밀렸는지"까지 나온다.
        /// </summary>
        public static bool Enabled = false;

        public static void Report(in SimWorld w, in SimServices services)
        {
            if (!Enabled) return;

            var sb = new StringBuilder();
            sb.AppendLine("=== [진단] 공중 타겟팅 관문 점검 ===");
            sb.AppendLine($"적 {w.enemyCount}마리 · 플레이어 y={w.player.pos.y:F2} yaw={w.player.yaw:F0}");
            sb.AppendLine($"설정값: LungeHeightTolerance={CombatConfig.LungeHeightTolerance} " +
                          $"LungeMinRange={CombatConfig.LungeMinRange} LungeMaxRange={CombatConfig.LungeMaxRange} " +
                          $"LungeAimRadius={CombatConfig.LungeAimRadius} " +
                          $"LungeDoomStyle={CombatConfig.LungeDoomStyle} LungeTravel={CombatConfig.LungeTravel} " +
                          $"AttackHeightTolerance={CombatConfig.AttackHeightTolerance}");
            sb.AppendLine($"        FlyHoverOffset={AIConfig.FlyHoverOffset} FlyHoverJitter={AIConfig.FlyHoverJitter}");

            int flying = 0;
            for (int i = 0; i < w.enemyCount; i++)
            {
                ref readonly EnemySim e = ref w.enemies[i];
                if (e.ai.mobility != MobilityType.Flying) continue;
                flying++;

                float heightDelta = e.pos.y - w.player.pos.y;
                float flatDistance = CombatMath.FlatDistance(w.player.pos, e.pos);

                // ActionGenerator.CanLungeTarget과 똑같이 "대상을 정확히 바라보는 가상 플레이어"를
                // 만들어 실제 Sim의 공개 게이트에 물어본다 — 예측이 실제로 쓰는 경로 그대로다.
                PlayerSim aimed = w.player;
                Vector3 eye = w.player.pos + Vector3.up * (SimConfig.PlayerHeight * 0.7f);
                Vector3 center = e.pos + Vector3.up * (e.height * 0.5f);
                Vector3 dir = center - eye;
                if (dir.sqrMagnitude > 1e-8f)
                {
                    Vector3 lookEuler = Quaternion.LookRotation(dir).eulerAngles;
                    aimed.yaw = lookEuler.y;
                    aimed.aimPitch = lookEuler.x;
                }
                bool canLunge = PlayerCombat.CanLunge(in w, in aimed, in services, e.id, out Vector3 dest);

                // 관문을 하나씩 따로 찍어서 "어디서" 걸리는지 특정한다.
                bool aliveOk = e.alive && e.combat.gloryStage == 0;
                bool heightOk = Mathf.Abs(heightDelta) <= CombatConfig.LungeHeightTolerance;
                float along = Vector3.Dot(center - eye, dir.normalized);
                bool rangeOk = along >= CombatConfig.LungeMinRange
                               && along <= CombatConfig.LungeMaxRange + e.radius;
                float losLength = dir.magnitude;
                bool losOk = losLength <= 1e-4f
                             || !services.Collision.Raycast(eye, dir / losLength, losLength).hit;

                sb.AppendLine(
                    $"  적#{e.id} {e.ai.combat}/{e.ai.size}/{e.ai.state} 높이차 {heightDelta:F2}m 수평 {flatDistance:F2}m " +
                    $"반경 {e.radius:F2} 키 {e.height:F2}");
                sb.AppendLine(
                    $"      생존={aliveOk} 높이관문={heightOk} 사거리관문={rangeOk}(along {along:F2}) " +
                    $"LOS={losOk} → CanLunge={canLunge}" + (canLunge ? $" dest={dest}" : ""));
            }

            if (flying == 0)
                sb.AppendLine("  !! 공중(Flying) 적이 하나도 없다 — 스폰 구성이나 mobility 분류부터 확인할 것.");

            ReportRanking(in w, in services, sb);
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// 관문(CanLunge)을 통과하는 것과 실제 후보가 되는 것은 다르다 — ActionGenerator는
        /// 런지 후보를 <b>현재 바라보는 방향(dot) 기준 상위 2개</b>만 만든다. 그래서 공중 적이
        /// 게이트를 전부 통과해도 정면의 지상 적 둘에게 밀리면 후보에 아예 안 오른다.
        /// "게이트에서 걸림"과 "경쟁에서 밀림"은 고칠 곳이 완전히 다르므로 따로 찍는다.
        /// </summary>
        static void ReportRanking(in SimWorld w, in SimServices services, StringBuilder sb)
        {
            sb.AppendLine("  ── 런지 후보 경쟁(정면 dot 상위 2개만 후보가 된다) ──");
            Vector3 forward = CombatMath.Forward(w.player.yaw);

            // 런지 가능한 적을 전부 모아 ActionGenerator.IsBetterLungeTarget과 같은 기준
            // (dot 내림차순 → 거리 오름차순 → id 오름차순)으로 정렬한다.
            var ranked = new System.Collections.Generic.List<(int id, float dot, float dist, bool flying)>();
            for (int i = 0; i < w.enemyCount; i++)
            {
                ref readonly EnemySim e = ref w.enemies[i];
                if (!Lungeable(in w, in services, in e)) continue;
                ranked.Add((
                    e.id,
                    Vector3.Dot(forward, CombatMath.FlatDirection(w.player.pos, e.pos)),
                    CombatMath.FlatDistance(w.player.pos, e.pos),
                    e.ai.mobility == MobilityType.Flying));
            }
            if (ranked.Count == 0) { sb.AppendLine("    런지 가능한 적이 하나도 없다."); return; }

            ranked.Sort((a, b) =>
            {
                if (Mathf.Abs(a.dot - b.dot) > 1e-6f) return b.dot.CompareTo(a.dot);
                if (Mathf.Abs(a.dist - b.dist) > 1e-5f) return a.dist.CompareTo(b.dist);
                return a.id.CompareTo(b.id);
            });

            for (int i = 0; i < ranked.Count && i < 6; i++)
                sb.AppendLine($"    {i + 1}위: 적#{ranked[i].id} dot={ranked[i].dot:F3} " +
                              $"거리={ranked[i].dist:F2}m {(ranked[i].flying ? "[공중]" : "[지상]")}" +
                              (i < 2 ? "  ← 후보로 생성됨" : "  (후보에서 탈락)"));
        }

        static bool Lungeable(in SimWorld w, in SimServices services, in EnemySim e)
        {
            if (!e.alive || e.combat.gloryStage > 0) return false;
            PlayerSim aimed = w.player;
            Vector3 eye = w.player.pos + Vector3.up * (SimConfig.PlayerHeight * 0.7f);
            Vector3 dir = e.pos + Vector3.up * (e.height * 0.5f) - eye;
            if (dir.sqrMagnitude > 1e-8f)
            {
                Vector3 lookEuler = Quaternion.LookRotation(dir).eulerAngles;
                aimed.yaw = lookEuler.y;
                aimed.aimPitch = lookEuler.x;
            }
            return PlayerCombat.CanLunge(in w, in aimed, in services, e.id, out _);
        }
    }
}
