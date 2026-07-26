using System.Text;
using UnityEditor;
using UnityEngine;
using Game.Sim;

namespace Game.Prediction.Editor
{
    /// <summary>
    /// "분기점(Fork) 실행"이 성립하는지 확인하는 진단 도구. PlanByProfile이 내놓는
    /// 안전형/기회형/공격형 세 경로가 <b>초반 얼마 동안 겹치는가</b>를 잰다.
    ///
    /// 겹치는 구간이 있어야 View에서 "한 줄기로 출발 → 어느 시점에 갈라짐"을 보여줄 수
    /// 있고, 그 갈라지는 틱이 곧 분기점 후보다. 세 경로가 t=0부터 제각각이면 분기점
    /// 연출은 성립하지 않고 다른 방식(후보 그룹핑/다양성 강제)이 필요하다.
    ///
    /// 두 가지를 따로 잰다:
    ///   1) 매크로 시퀀스 공통 접두사 — 탐색이 고른 "행동"이 같은가(15틱=0.25초 단위)
    ///   2) 실제 위치 발산 — 재생된 60Hz 궤적이 눈에 띄게 벌어지는 첫 틱
    /// 행동이 달라도 궤적은 한동안 붙어 있을 수 있으므로(예: 전진 vs 전진대시) 연출
    /// 관점에서는 2번이 더 중요하다.
    ///
    /// 스텁 충돌·경로찾기 기준이라 실제 씬의 지형 영향은 안 들어간다 — 경향 확인용.
    /// </summary>
    public static class RouteDivergenceDiagnostic
    {
        /// <summary>이 거리(m) 이상 벌어지면 "눈에 보이게 갈라졌다"고 본다.</summary>
        const float VisibleSplitMeters = 0.6f;
        /// <summary>이 거리(m) 이상이면 완전히 다른 경로.</summary>
        const float FullSplitMeters = 2.0f;

        [MenuItem("Precog/경로 분기점 진단 (세 프로필 겹침)")]
        static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 세 프로필(안전형/기회형/공격형) 경로 겹침 진단 ===");
            sb.AppendLine($"기준: Full(3초) · 스텁 충돌/경로찾기 · 갈라짐 판정 {VisibleSplitMeters}m / 완전분기 {FullSplitMeters}m");
            sb.AppendLine();

            Diagnose(sb, "원형 포위 8마리", BuildCircle(8, 8f, 2f));
            Diagnose(sb, "원형 포위 16마리", BuildCircle(16, 10f, 2f));
            Diagnose(sb, "정면 근접 무리 6마리", BuildFrontPack());
            Diagnose(sb, "공중 슈터 + 지상 혼합", BuildMixedAerial());
            Diagnose(sb, "한쪽으로 몰린 무리 (탈출 방향 명확)", BuildOneSidedPack());

            Debug.Log(sb.ToString());
        }

        static void Diagnose(StringBuilder sb, string title, SimWorld world)
        {
            var services = new SimServices(new FlatGroundCollision(), new StraightLinePathfinder());
            PredictionSettings settings = PredictionSettings.Full;

            CandidatePath[] plans = PredictionPlanner.PlanByProfile(in world, in services, settings);
            sb.AppendLine($"### {title} (적 {world.enemyCount}마리, 후보 {plans.Length}개) ###");

            if (plans.Length < 2)
            {
                sb.AppendLine("  후보가 2개 미만 — 비교 불가.");
                sb.AppendLine();
                return;
            }

            // 궤적을 얻으려면 정밀 재생이 필요하다(탐색 중엔 frames가 비어 있는 계약).
            for (int i = 0; i < plans.Length; i++)
            {
                if (!CandidateReplayer.Replay(in world, in services, plans[i], settings.macroTicks))
                    sb.AppendLine($"  ! [{plans[i].profileLabel}] 재생 실패 — 이 후보는 궤적 비교에서 제외");
            }

            // ── 1) 매크로 시퀀스 ──
            for (int i = 0; i < plans.Length; i++)
            {
                sb.AppendLine($"  [{plans[i].profileLabel,-4}] kills={plans[i].killCount} score={plans[i].TotalScore:F0}  {Sequence(plans[i])}");
            }
            int macroPrefix = CommonMacroPrefix(plans);
            sb.AppendLine($"  · 세 경로 공통 매크로 접두사: {macroPrefix}스텝 " +
                          $"({macroPrefix * settings.macroTicks / 60f:F2}초 / 전체 {settings.macroDepth}스텝)");

            // ── 2) 실제 궤적 발산 ──
            int minLen = int.MaxValue;
            for (int i = 0; i < plans.Length; i++)
            {
                int len = plans[i].predictedFrames == null ? 0 : plans[i].predictedFrames.Length;
                if (len < minLen) minLen = len;
            }
            if (minLen <= 0)
            {
                sb.AppendLine("  · 궤적 없음 — 위치 비교 불가.");
                sb.AppendLine();
                return;
            }

            int visibleTick = -1, fullTick = -1;
            float maxSpread = 0f;
            for (int t = 0; t < minLen; t++)
            {
                float spread = MaxPairDistance(plans, t);
                if (spread > maxSpread) maxSpread = spread;
                if (visibleTick < 0 && spread >= VisibleSplitMeters) visibleTick = t;
                if (fullTick < 0 && spread >= FullSplitMeters) fullTick = t;
            }

            sb.AppendLine($"  · 궤적이 {VisibleSplitMeters}m 벌어지는 첫 틱: {Describe(visibleTick, minLen)}");
            sb.AppendLine($"  · 궤적이 {FullSplitMeters}m 벌어지는 첫 틱: {Describe(fullTick, minLen)}");
            sb.AppendLine($"  · 최대 벌어짐 {maxSpread:F2}m (비교 구간 {minLen}틱 = {minLen / 60f:F2}초)");

            // 0.5초 간격 스냅샷 — 언제부터 어떻게 벌어지는지 눈으로 보기.
            sb.Append("  · 0.5초 간격 벌어짐:");
            for (int t = 0; t < minLen; t += 30)
                sb.Append($"  {t / 60f:F1}s={MaxPairDistance(plans, t):F1}m");
            sb.AppendLine();
            sb.AppendLine();
        }

        static string Describe(int tick, int total)
            => tick < 0 ? $"없음 (끝까지 {VisibleSplitMeters}m 안쪽 — 사실상 같은 경로)"
                        : $"{tick}틱 ({tick / 60f:F2}초, 전체의 {100f * tick / total:F0}% 지점)";

        static float MaxPairDistance(CandidatePath[] plans, int tick)
        {
            float max = 0f;
            for (int a = 0; a < plans.Length; a++)
            {
                if (plans[a].predictedFrames == null || tick >= plans[a].predictedFrames.Length) continue;
                for (int b = a + 1; b < plans.Length; b++)
                {
                    if (plans[b].predictedFrames == null || tick >= plans[b].predictedFrames.Length) continue;
                    float d = Vector3.Distance(
                        plans[a].predictedFrames[tick].playerPosition,
                        plans[b].predictedFrames[tick].playerPosition);
                    if (d > max) max = d;
                }
            }
            return max;
        }

        static int CommonMacroPrefix(CandidatePath[] plans)
        {
            int shortest = int.MaxValue;
            for (int i = 0; i < plans.Length; i++)
                shortest = Mathf.Min(shortest, plans[i].actions == null ? 0 : plans[i].actions.Length);

            int prefix = 0;
            for (; prefix < shortest; prefix++)
            {
                MacroAction first = plans[0].actions[prefix];
                for (int i = 1; i < plans.Length; i++)
                {
                    MacroAction other = plans[i].actions[prefix];
                    if (other.type != first.type || other.lungeTargetId != first.lungeTargetId)
                        return prefix;
                }
            }
            return prefix;
        }

        static string Sequence(CandidatePath plan)
        {
            if (plan.actions == null || plan.actions.Length == 0) return "(없음)";
            var sb = new StringBuilder();
            for (int i = 0; i < plan.actions.Length; i++)
            {
                if (i > 0) sb.Append('→');
                sb.Append(plan.actions[i].type);
                if (plan.actions[i].lungeTargetId >= 0) sb.Append($"#{plan.actions[i].lungeTargetId}");
            }
            return sb.ToString();
        }

        // ───────────────────────── 시나리오 ─────────────────────────

        static SimWorld BuildCircle(int count, float radius, float jitter)
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            for (int i = 0; i < count; i++)
            {
                float angle = (float)i / count * Mathf.PI * 2f;
                float r = radius + (i % 3 - 1) * jitter;
                world.AddEnemy(new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r));
            }
            return world;
        }

        /// <summary>정면에 근접 무리 — 붙을지 뺄지가 갈리기 쉬운 상황.</summary>
        static SimWorld BuildFrontPack()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.player.yaw = 0f;
            Vector3[] pos =
            {
                new Vector3(-1.5f, 0f, 4f), new Vector3(1.5f, 0f, 4f),
                new Vector3(0f, 0f, 6f), new Vector3(-3f, 0f, 6.5f),
                new Vector3(3f, 0f, 6.5f), new Vector3(0f, 0f, 9f),
            };
            for (int i = 0; i < pos.Length; i++)
                world.AddEnemy(pos[i], i % 3 == 0 ? CombatType.Ranged : CombatType.Melee,
                    MobilityType.Ground, SizeClass.Normal);
            return world;
        }

        /// <summary>공중 슈터 + 지상 근접 — 공중을 노릴지 지상을 정리할지가 갈리기 쉬운 상황.</summary>
        static SimWorld BuildMixedAerial()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.player.yaw = 0f;
            world.AddEnemy(new Vector3(0f, AIConfig.FlyHoverOffset + 1.5f, 4f),
                CombatType.Ranged, MobilityType.Flying, SizeClass.Normal);
            world.enemies[0].ai.state = EnemyState.Aim;
            world.AddEnemy(new Vector3(3f, AIConfig.FlyHoverOffset + 2f, 6f),
                CombatType.Ranged, MobilityType.Flying, SizeClass.Normal);
            world.enemies[1].ai.state = EnemyState.Aim;

            Vector3[] ground =
            {
                new Vector3(-2f, 0f, 5f), new Vector3(2f, 0f, 5.5f),
                new Vector3(-4f, 0f, 7f), new Vector3(4f, 0f, 7.5f),
            };
            for (int i = 0; i < ground.Length; i++)
                world.AddEnemy(ground[i], CombatType.Melee, MobilityType.Ground, SizeClass.Normal);
            return world;
        }

        /// <summary>한쪽으로 몰린 무리 — "도망 방향"이 하나뿐이라 세 프로필이 겹치기 쉬운 대조군.</summary>
        static SimWorld BuildOneSidedPack()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.player.yaw = 0f;
            for (int i = 0; i < 7; i++)
            {
                float x = -3f + i * 1.1f;
                world.AddEnemy(new Vector3(x, 0f, 5f + (i % 2) * 1.5f),
                    i % 4 == 0 ? CombatType.Ranged : CombatType.Melee,
                    MobilityType.Ground, SizeClass.Normal);
            }
            return world;
        }
    }
}
