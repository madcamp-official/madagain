using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Game.Sim;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Game.Prediction.Editor
{
    /// <summary>
    /// 예측 결과를 텍스트로 확인하는 개발용 도구. 실제 씬/Physics 없이 평지 스텁으로
    /// 미리 정한 다양한 상황을 돌려 Beam Search가 실제로 어떤 행동 시퀀스와 궤적을
    /// 찾아내는지 콘솔에 출력한다. 회귀 검증용이 아니라 눈으로 확인하는 용도.
    /// </summary>
    public static class PredictionVisualizer
    {
        const string ContractVersion = "1.9";
        const string AggressiveFixtureVersion = "A-v2-complex";
        const string SafeFixtureVersion = "S-v2-dense";

        [Serializable]
        sealed class BaselineLog
        {
            public BaselineRow[] rows;
        }

        [Serializable]
        sealed class BaselineRow
        {
            public string scenarioId;
            public string fixtureVersion;
            public string weightStage;
            public string profile;
            public float salientTargetMul;
            public float terminalPositionMul;
            public float difficultyPenaltyMul;
            public bool difficultyTerminalOnly;
            public string contractVersion;
            public int mapVersion;
            public string initialSnapshotHash;
            public int repetition;
            public int candidateRank;
            public string actionSequence;
            public int salientEnemyId;
            public float salientTargetProgress;
            public int killCount;
            public int damageDealt;
            public int initialEnemyCount;
            public int finalAliveEnemyCount;
            public float terminalNearestEnemyDistance;
            public int terminalNearbyEnemyCount;
            public float terminalHeightDelta;
            public bool terminalActionable;
            public float terminalPositionQuality;
            public float executionDifficulty;
            public int expectedHits;
            public int durationTicks;
            public float safetyScore;
            public float killScore;
            public float difficultyScore;
            public float totalScore;
            public double elapsedMs;
            public string worldHashAfterReplay;
            public bool replayValid;
            public int auditRank;
            public bool auditRecommended;
            public bool auditDiffersFromBeamWinner;
            public string auditReason;
        }

        readonly struct AggressiveScenario
        {
            public readonly string id;
            public readonly string title;
            public readonly SimWorld world;

            public AggressiveScenario(string id, string title, SimWorld world)
            {
                this.id = id;
                this.title = title;
                this.world = world;
            }
        }

        readonly struct SafeScenario
        {
            public readonly string id;
            public readonly string title;
            public readonly SimWorld world;
            public readonly bool hasJumpEscape;

            public SafeScenario(string id, string title, SimWorld world, bool hasJumpEscape = false)
            {
                this.id = id;
                this.title = title;
                this.world = world;
                this.hasJumpEscape = hasJumpEscape;
            }
        }

        readonly struct WeightStage
        {
            public readonly string name;
            public readonly ScoreProfile profile;

            public WeightStage(string name, ScoreProfile profile)
            {
                this.name = name;
                this.profile = profile;
            }
        }

        [MenuItem("Precog/프로필 검증/안정형 S1-S4 단계별 가중치 비교 내보내기")]
        static void ExportSafeWeightStages()
        {
            const int Repetitions = 10;
            WeightStage[] stages =
            {
                new WeightStage("0-baseline", ScoreProfile.SafeDiagnostic),
                new WeightStage("1-terminal", ScoreProfile.SafeTerminalOnly),
                new WeightStage("2-difficulty", ScoreProfile.SafeTerminalDifficulty),
                new WeightStage("3-current-final", ScoreProfile.Safe),
            };
            SafeScenario[] scenarios = BuildSafeScenarios();
            var rows = new List<BaselineRow>(stages.Length * scenarios.Length * Repetitions * 3);

            foreach (WeightStage stage in stages)
            foreach (SafeScenario safeScenario in scenarios)
            {
                SimServices services = SafeServices(safeScenario.hasJumpEscape);
                PlayerIntentContext intent = PlayerIntentEvaluator.Capture(in safeScenario.world);
                var scenario = new AggressiveScenario(safeScenario.id, safeScenario.title, safeScenario.world);
                for (int repetition = 0; repetition < Repetitions; repetition++)
                {
                    ScoreProfile profile = stage.profile;
                    var stopwatch = Stopwatch.StartNew();
                    CandidatePath[] candidates = BeamSearch.Run(
                        in safeScenario.world, in services, PredictionSettings.Full, in profile);
                    stopwatch.Stop();
                    var runRows = new List<BaselineRow>(candidates.Length);
                    for (int rank = 0; rank < candidates.Length; rank++)
                    {
                        CandidatePath candidate = candidates[rank];
                        SimWorld finalWorld = ReplayToWorld(
                            in safeScenario.world, in services, candidate, PredictionSettings.Full.macroTicks,
                            out bool replayValid);
                        BaselineRow row = CreateBaselineRow(
                            in scenario, in intent, in profile, stage.name, candidate, rank + 1, repetition + 1,
                            stopwatch.Elapsed.TotalMilliseconds, in finalWorld, replayValid);
                        row.fixtureVersion = SafeFixtureVersion;
                        runRows.Add(row);
                    }
                    AnnotateSafeAudit(runRows);
                    rows.AddRange(runRows);
                }
            }

            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "PredictionLogs"));
            Directory.CreateDirectory(directory);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string jsonPath = Path.Combine(directory, $"safe_weight_stages_{stamp}.json");
            string csvPath = Path.Combine(directory, $"safe_weight_stages_{stamp}.csv");
            File.WriteAllText(jsonPath, JsonUtility.ToJson(new BaselineLog { rows = rows.ToArray() }, true), Encoding.UTF8);
            File.WriteAllText(csvPath, BuildCsv(rows), Encoding.UTF8);
            Debug.Log($"안정형 S1~S4 단계별 비교 {rows.Count}행 저장 완료\nJSON: {jsonPath}\nCSV: {csvPath}");
            EditorUtility.RevealInFinder(csvPath);
        }

        [MenuItem("Precog/프로필 검증/안정형 S1-S4 콘솔 재생")]
        static void ReplaySafeScenarios()
        {
            foreach (SafeScenario scenario in BuildSafeScenarios())
            {
                RunScenario($"{scenario.id}. {scenario.title}", scenario.world);
                Separator();
            }
        }

        [MenuItem("Precog/프로필 검증/공격형 A1-A4 기준선 로그 내보내기")]
        static void ExportAggressiveBaseline()
        {
            const int Repetitions = 10;
            AggressiveScenario[] scenarios = BuildAggressiveScenarios();
            var rows = new List<BaselineRow>(scenarios.Length * Repetitions * 3);
            SimServices services = new SimServices(new FlatGroundCollision(), new StraightLinePathfinder());

            foreach (AggressiveScenario scenario in scenarios)
            {
                PlayerIntentContext intent = PlayerIntentEvaluator.Capture(in scenario.world);
                for (int repetition = 0; repetition < Repetitions; repetition++)
                {
                    ScoreProfile profile = ScoreProfile.AggressiveDiagnostic;
                    var stopwatch = Stopwatch.StartNew();
                    CandidatePath[] candidates = BeamSearch.Run(
                        in scenario.world, in services, PredictionSettings.Full, in profile);
                    stopwatch.Stop();

                    var runRows = new List<BaselineRow>(candidates.Length);
                    for (int rank = 0; rank < candidates.Length; rank++)
                    {
                        CandidatePath candidate = candidates[rank];
                        SimWorld finalWorld = ReplayToWorld(
                            in scenario.world, in services, candidate, PredictionSettings.Full.macroTicks,
                            out bool replayValid);
                        runRows.Add(CreateBaselineRow(
                            in scenario, in intent, in profile, "0-baseline", candidate, rank + 1, repetition + 1,
                            stopwatch.Elapsed.TotalMilliseconds, in finalWorld, replayValid));
                    }
                    AnnotateAudit(runRows);
                    rows.AddRange(runRows);
                }
            }

            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "PredictionLogs"));
            Directory.CreateDirectory(directory);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string jsonPath = Path.Combine(directory, $"aggressive_baseline_{stamp}.json");
            string csvPath = Path.Combine(directory, $"aggressive_baseline_{stamp}.csv");
            File.WriteAllText(jsonPath, JsonUtility.ToJson(new BaselineLog { rows = rows.ToArray() }, true), Encoding.UTF8);
            File.WriteAllText(csvPath, BuildCsv(rows), Encoding.UTF8);

            int auditDisagreements = 0;
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].candidateRank == 1 && rows[i].auditDiffersFromBeamWinner) auditDisagreements++;
            Debug.Log($"공격형 A1~A4 기준선 {rows.Count}행 저장 완료\n" +
                      $"관측축 추천과 Beam 1위 불일치: {auditDisagreements}/{scenarios.Length * Repetitions}회\n" +
                      $"JSON: {jsonPath}\nCSV: {csvPath}");
            EditorUtility.RevealInFinder(csvPath);
        }

        [MenuItem("Precog/프로필 검증/공격형 단계별 가중치 비교 내보내기")]
        static void ExportAggressiveWeightStages()
        {
            const int Repetitions = 10;
            WeightStage[] stages =
            {
                new WeightStage("0-baseline", ScoreProfile.AggressiveDiagnostic),
                new WeightStage("1-target", ScoreProfile.AggressiveTargetOnly),
                new WeightStage("2-terminal", ScoreProfile.AggressiveTargetTerminal),
                new WeightStage("3-difficulty-final", ScoreProfile.Aggressive),
            };
            AggressiveScenario[] scenarios = BuildAggressiveScenarios();
            var rows = new List<BaselineRow>(stages.Length * scenarios.Length * Repetitions * 3);
            SimServices services = new SimServices(new FlatGroundCollision(), new StraightLinePathfinder());

            foreach (WeightStage stage in stages)
            foreach (AggressiveScenario scenario in scenarios)
            {
                PlayerIntentContext intent = PlayerIntentEvaluator.Capture(in scenario.world);
                for (int repetition = 0; repetition < Repetitions; repetition++)
                {
                    ScoreProfile profile = stage.profile;
                    var stopwatch = Stopwatch.StartNew();
                    CandidatePath[] candidates = BeamSearch.Run(
                        in scenario.world, in services, PredictionSettings.Full, in profile);
                    stopwatch.Stop();
                    var runRows = new List<BaselineRow>(candidates.Length);
                    for (int rank = 0; rank < candidates.Length; rank++)
                    {
                        CandidatePath candidate = candidates[rank];
                        SimWorld finalWorld = ReplayToWorld(
                            in scenario.world, in services, candidate, PredictionSettings.Full.macroTicks,
                            out bool replayValid);
                        runRows.Add(CreateBaselineRow(
                            in scenario, in intent, in profile, stage.name, candidate, rank + 1, repetition + 1,
                            stopwatch.Elapsed.TotalMilliseconds, in finalWorld, replayValid));
                    }
                    AnnotateAudit(runRows);
                    rows.AddRange(runRows);
                }
            }

            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "PredictionLogs"));
            Directory.CreateDirectory(directory);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string jsonPath = Path.Combine(directory, $"aggressive_weight_stages_{stamp}.json");
            string csvPath = Path.Combine(directory, $"aggressive_weight_stages_{stamp}.csv");
            File.WriteAllText(jsonPath, JsonUtility.ToJson(new BaselineLog { rows = rows.ToArray() }, true), Encoding.UTF8);
            File.WriteAllText(csvPath, BuildCsv(rows), Encoding.UTF8);
            Debug.Log($"공격형 단계별 가중치 비교 {rows.Count}행 저장 완료\nJSON: {jsonPath}\nCSV: {csvPath}");
            EditorUtility.RevealInFinder(csvPath);
        }

        [MenuItem("Precog/프로필 검증/공격형 A1-A4 콘솔 재생")]
        static void ReplayAggressiveScenarios()
        {
            foreach (AggressiveScenario scenario in BuildAggressiveScenarios())
            {
                RunScenario($"{scenario.id}. {scenario.title}", scenario.world);
                Separator();
            }
        }

        [MenuItem("Precog/예측 시각화 (텍스트, 콘솔)")]
        static void RunDemo()
        {
            RunScenario("1. 여유 있는 거리 (적 3마리)", BuildRoomyWorld());
            Separator();
            RunScenario("2. 코앞에 적 (런지 사거리 안, 적 2마리)", BuildCloseQuartersWorld());
            Separator();
            RunScenario("3. 체력 위험 (HP 1, 적 2마리 근접)", BuildLowHpWorld());
            Separator();
            RunScenario("4. 적 공격 임박 (Windup 중, 회피 시급)", BuildImminentAttackWorld());
            Separator();
            RunScenario("5. 대시 소진 상태 (도보 이동만 가능)", BuildDashExhaustedWorld());
            Separator();
            RunScenario("6. 좁은 구석 (한쪽 부채꼴에 몰림, 적 4마리)", BuildCorneredWorld());
            Separator();
            RunScenario("7. 혼합 거리 (근접 3마리 + 낙오자 원거리 1마리)", BuildMixedRangeWorld());
            Separator();
            RunScenario("8. 대칭 포위 (적 6마리)", BuildSymmetricWorld());
            Separator();
            RunScenario("9. 완전 원형 포위 (적 12마리)", BuildCircleWorld(12, 9f, 3f));
            Separator();
            RunScenario("10. 대규모 웨이브 (적 30마리, 스트레스 테스트)", BuildCircleWorld(30, 10f, 2f));
            Separator();
            RunScenario("11. OPTIMIZATION.md 벤치마크 기준점 (적 8마리, 목표 200ms 내외/하드리밋 300ms)", BuildCircleWorld(8, 8f, 2f));
        }

        /// <summary>
        /// OPTIMIZATION.md 14장의 적 수별 벤치마크(8/16/32/50/64마리)를 Full 설정 그대로와,
        /// PredictionSettings.Degrade(적 수 기반 동적 축소) 적용 버전을 나란히 돌려서 실제
        /// 절감 효과를 비교한다(스텁 충돌·경로찾기 — 실제 Physics/NavMesh 비용 미포함).
        /// EnemySimulationTier(Precise/Coarse/Dormant) 없이 예측 쪽만으로 낼 수 있는 완화가
        /// 어느 정도인지 확인하는 용도 — 전체 재생 로그는 안 찍고 시간만 요약해서 콘솔에 남긴다.
        /// </summary>
        [MenuItem("Precog/예측 성능 벤치마크 (적 수별)")]
        static void RunEnemyCountBenchmark()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 적 수별 Beam Search 성능 벤치마크 (기준: Full: Beam 12 / 3초 / 180틱) ===");
            sb.AppendLine("주의: 스텁 충돌·경로찾기 사용 — 실제 Physics.CapsuleCast/NavMesh 비용은 포함 안 됨.");
            sb.AppendLine();

            int[] counts = { 8, 16, 32, 50, 64 };
            foreach (int count in counts)
            {
                SimWorld world = BuildCircleWorld(count, 6f + count * 0.25f, 2f);
                SimServices services = new SimServices(new FlatGroundCollision(), new StraightLinePathfinder());

                double rawMs = TimePlan(in world, in services, PredictionSettings.Full);
                PredictionSettings degraded = PredictionSettings.Degrade(PredictionSettings.Full, count);
                double degradedMs = TimePlan(in world, in services, degraded);

                sb.AppendLine($"적 {count,3}마리:  원본 {rawMs,7:F2}ms  →  축소 후 {degradedMs,7:F2}ms  " +
                              $"(Beam {degraded.beamWidth}, 깊이 {degraded.macroDepth}, 행동상한 {degraded.maxActionsPerNode})");
            }

            Debug.Log(sb.ToString());
        }

        static double TimePlan(in SimWorld world, in SimServices services, PredictionSettings settings)
        {
            var stopwatch = Stopwatch.StartNew();
            PredictionPlanner.Plan(in world, in services, settings);
            stopwatch.Stop();
            return stopwatch.Elapsed.TotalMilliseconds;
        }

        static SafeScenario[] BuildSafeScenarios() => new[]
        {
            new SafeScenario("S1", "양측 포위 + 저체력 탈출구 차단 적", BuildSafeS1()),
            new SafeScenario("S2", "지상 추격대 + 좌측 고지대 JumpUp 탈출", BuildSafeS2(), hasJumpEscape: true),
            new SafeScenario("S3", "교차 투사체 + 정면 돌진의 사선 이탈", BuildSafeS3()),
            new SafeScenario("S4", "조준 중 공중 위협 제거 후 지상 포위 이탈", BuildSafeS4()),
        };

        static SimServices SafeServices(bool hasJumpEscape) =>
            new SimServices(
                new FlatGroundCollision(),
                hasJumpEscape ? (IPathfinder)new ElevatedEscapePathfinder() : new StraightLinePathfinder());

        static SimWorld BuildSafeS1()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.player.yaw = 0f;
            Vector3[] ring =
            {
                new Vector3(-3f, 0f, 1f), new Vector3(3f, 0f, 1f),
                new Vector3(-2.5f, 0f, -2.5f), new Vector3(2.5f, 0f, -2.5f),
                new Vector3(0f, 0f, -3.5f),
            };
            for (int i = 0; i < ring.Length; i++)
                world.AddEnemy(ring[i], CombatType.Melee, MobilityType.Ground, SizeClass.Normal);
            for (int i = 0; i < 6; i++)
            {
                float angle = (i + 0.5f) * 60f * Mathf.Deg2Rad;
                world.AddEnemy(
                    new Vector3(Mathf.Sin(angle) * 7f, 0f, Mathf.Cos(angle) * 7f),
                    i % 2 == 0 ? CombatType.Ranged : CombatType.Melee,
                    MobilityType.Ground, SizeClass.Normal);
            }
            world.AddEnemy(new Vector3(0f, 0f, 2.2f),
                CombatType.Melee, MobilityType.Ground, SizeClass.Normal);
            world.enemies[world.enemyCount - 1].combat.health = CombatConfig.Damage;
            return world;
        }

        static SimWorld BuildSafeS2()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.player.yaw = 0f;
            for (int i = 0; i < 14; i++)
            {
                float x = -6.5f + i;
                float z = 2.5f + (i % 3) * 1.5f;
                world.AddEnemy(new Vector3(x, 0f, z),
                    i % 4 == 0 ? CombatType.Ranged : CombatType.Melee,
                    i == 6 ? MobilityType.Charge : MobilityType.Ground,
                    SizeClass.Normal);
            }
            return world;
        }

        static SimWorld BuildSafeS3()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.player.yaw = 0f;
            world.SpawnProjectile(
                new Vector3(-6f, AIConfig.PlayerTorso, 0f), new Vector3(14f, 0f, 0f));
            world.SpawnProjectile(
                new Vector3(0f, AIConfig.PlayerTorso, 6f), new Vector3(0f, 0f, -14f));
            world.AddEnemy(new Vector3(0f, 0f, 7f),
                CombatType.Melee, MobilityType.Charge, SizeClass.Normal);
            world.enemies[0].ai.state = EnemyState.Windup;
            world.enemies[0].ai.stateTicks = Mathf.Max(0, AIConfig.ChargeWindupTicks - 12);
            world.enemies[0].ai.committedDir = Vector3.back;
            world.AddEnemy(new Vector3(-5f, 0f, 4f),
                CombatType.Ranged, MobilityType.Ground, SizeClass.Normal);
            world.AddEnemy(new Vector3(5f, 0f, 4f),
                CombatType.Ranged, MobilityType.Ground, SizeClass.Normal);
            return world;
        }

        static SimWorld BuildSafeS4()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.player.yaw = 0f;
            world.AddEnemy(new Vector3(0f, AIConfig.FlyHoverOffset + 1.5f, 4f),
                CombatType.Ranged, MobilityType.Flying, SizeClass.Normal);
            world.enemies[0].ai.state = EnemyState.Aim;
            world.enemies[0].ai.stateTicks = Mathf.Max(0, AIConfig.RangedAimTicks - 15);
            Vector3[] ground =
            {
                new Vector3(-2.5f, 0f, 2f), new Vector3(2.5f, 0f, 2f),
                new Vector3(-3f, 0f, -2f), new Vector3(3f, 0f, -2f),
                new Vector3(0f, 0f, -3.5f),
            };
            for (int i = 0; i < ground.Length; i++)
                world.AddEnemy(ground[i],
                    i < 2 ? CombatType.Ranged : CombatType.Melee,
                    MobilityType.Ground, SizeClass.Normal);
            for (int i = 0; i < 6; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                world.AddEnemy(new Vector3(side * (5f + i * 0.3f), 0f, 3f + i),
                    i % 3 == 0 ? CombatType.Ranged : CombatType.Melee,
                    MobilityType.Ground, SizeClass.Normal);
            }
            return world;
        }

        static AggressiveScenario[] BuildAggressiveScenarios() => new[]
        {
            new AggressiveScenario("A1", "정면 12마리 밀집 + 후방 유인 4마리", BuildAggressiveA1()),
            new AggressiveScenario("A2", "다층 공중 4마리 + 지상 군집 8마리", BuildAggressiveA2()),
            new AggressiveScenario("A3", "저체력 연계 통로 + 양측 군집 14마리", BuildAggressiveA3()),
            new AggressiveScenario("A4", "양옆 포위 18마리 + 중앙 공격 임박 강적", BuildAggressiveA4()),
        };

        static SimWorld BuildAggressiveA1()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.player.yaw = 0f;
            // 정면은 부채꼴 3열 밀집. 가까운 후방 적은 카메라 밖 유인책이라 먼저 돌면 실패다.
            Vector3[] front =
            {
                new Vector3(0f, 0f, 3f),
                new Vector3(-1.5f, 0f, 3.6f), new Vector3(1.5f, 0f, 3.6f),
                new Vector3(-3f, 0f, 4.8f), new Vector3(0f, 0f, 4.8f), new Vector3(3f, 0f, 4.8f),
                new Vector3(-4.5f, 0f, 6.5f), new Vector3(-1.5f, 0f, 6.2f),
                new Vector3(1.5f, 0f, 6.2f), new Vector3(4.5f, 0f, 6.5f),
                new Vector3(-2.5f, 0f, 8f), new Vector3(2.5f, 0f, 8f),
            };
            for (int i = 0; i < front.Length; i++)
            {
                SizeClass size = i == 4 || i == 9 ? SizeClass.Large : SizeClass.Normal;
                CombatType combat = i % 4 == 3 ? CombatType.Ranged : CombatType.Melee;
                world.AddEnemy(front[i], combat, MobilityType.Ground, size);
            }
            world.AddEnemy(new Vector3(0f, 0f, -1.8f));
            world.AddEnemy(new Vector3(-1.8f, 0f, -3f));
            world.AddEnemy(new Vector3(1.8f, 0f, -3.2f));
            world.AddEnemy(new Vector3(0f, 0f, -5f), CombatType.Melee, MobilityType.Ground, SizeClass.Large);
            return world;
        }

        static SimWorld BuildAggressiveA2()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.player.yaw = 0f;
            Vector3[] flying =
            {
                new Vector3(0f, AIConfig.FlyHoverOffset + 1.5f, 4f),
                new Vector3(-3f, AIConfig.FlyHoverOffset + 3f, 6f),
                new Vector3(3f, AIConfig.FlyHoverOffset + 2f, 6.5f),
                new Vector3(0f, AIConfig.FlyHoverOffset + 4f, 8f),
            };
            for (int i = 0; i < flying.Length; i++)
            {
                world.AddEnemy(flying[i], CombatType.Ranged, MobilityType.Flying, SizeClass.Normal);
                world.enemies[i].ai.state = i < 2 ? EnemyState.Aim : EnemyState.Reposition;
            }
            Vector3[] ground =
            {
                new Vector3(-1.5f, 0f, 5.5f), new Vector3(1.5f, 0f, 5.5f),
                new Vector3(-4f, 0f, 4.5f), new Vector3(4f, 0f, 4.5f),
                new Vector3(-5f, 0f, 8f), new Vector3(5f, 0f, 8f),
                new Vector3(-2f, 0f, 10f), new Vector3(2f, 0f, 10f),
            };
            for (int i = 0; i < ground.Length; i++)
                world.AddEnemy(ground[i], i % 3 == 0 ? CombatType.Ranged : CombatType.Melee,
                    MobilityType.Ground, i == 6 ? SizeClass.Large : SizeClass.Normal);
            return world;
        }

        static SimWorld BuildAggressiveA3()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.player.yaw = 0f;
            // 중앙 저체력 4마리는 연계 통로, 좌우 군집은 잘못 새면 포위되는 압력이다.
            Vector3[] chain =
            {
                new Vector3(0f, 0f, 2.4f), new Vector3(0.7f, 0f, 4.2f),
                new Vector3(-0.5f, 0f, 6f), new Vector3(0.4f, 0f, 7.8f),
            };
            for (int i = 0; i < chain.Length; i++)
            {
                world.AddEnemy(chain[i]);
                world.enemies[i].combat.health = CombatConfig.Damage;
            }
            Vector3[] flanks =
            {
                new Vector3(-3f, 0f, 3f), new Vector3(-4.5f, 0f, 4.5f),
                new Vector3(-5f, 0f, 6.5f), new Vector3(-3.5f, 0f, 8.5f),
                new Vector3(-6f, 0f, 10f),
                new Vector3(3f, 0f, 3f), new Vector3(4.5f, 0f, 4.5f),
                new Vector3(5f, 0f, 6.5f), new Vector3(3.5f, 0f, 8.5f),
                new Vector3(6f, 0f, 10f),
            };
            for (int i = 0; i < flanks.Length; i++)
                world.AddEnemy(flanks[i], i % 2 == 0 ? CombatType.Melee : CombatType.Ranged,
                    MobilityType.Ground, i == 2 || i == 7 ? SizeClass.Large : SizeClass.Normal);
            return world;
        }

        static SimWorld BuildAggressiveA4()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.player.yaw = 0f;
            world.AddEnemy(new Vector3(-1.2f, 0f, 2.5f));
            world.enemies[0].combat.health = CombatConfig.Damage;
            world.AddEnemy(new Vector3(1.2f, 0f, 3f));
            world.enemies[1].combat.health = CombatConfig.Damage;
            world.AddEnemy(new Vector3(0f, 0f, 1.8f),
                CombatType.Melee, MobilityType.Charge, SizeClass.Large);
            world.enemies[2].ai.state = EnemyState.Windup;
            world.enemies[2].ai.stateTicks = Mathf.Max(0, AIConfig.MeleeWindupTicks - 8);
            world.enemies[2].ai.committedDir = Vector3.back;
            for (int side = -1; side <= 1; side += 2)
            {
                for (int row = 0; row < 5; row++)
                {
                    float x = side * (2.5f + row * 0.7f);
                    float z = -2f + row * 2.2f;
                    world.AddEnemy(new Vector3(x, 0f, z),
                        row % 2 == 0 ? CombatType.Melee : CombatType.Ranged,
                        row == 3 ? MobilityType.Charge : MobilityType.Ground,
                        row == 4 ? SizeClass.Large : SizeClass.Normal);
                }
            }
            world.AddEnemy(new Vector3(-1.5f, 0f, 7f), CombatType.Ranged, MobilityType.Ground, SizeClass.Normal);
            world.AddEnemy(new Vector3(1.5f, 0f, 7f), CombatType.Ranged, MobilityType.Ground, SizeClass.Normal);
            world.AddEnemy(new Vector3(-4f, 0f, -5f), CombatType.Melee, MobilityType.Ground, SizeClass.Large);
            world.AddEnemy(new Vector3(4f, 0f, -5f), CombatType.Melee, MobilityType.Ground, SizeClass.Large);
            world.AddEnemy(new Vector3(0f, AIConfig.FlyHoverOffset + 2f, 6f),
                CombatType.Ranged, MobilityType.Flying, SizeClass.Normal);
            return world;
        }

        static BaselineRow CreateBaselineRow(
            in AggressiveScenario scenario, in PlayerIntentContext intent, in ScoreProfile profile,
            string weightStage, CandidatePath candidate,
            int rank, int repetition, double elapsedMs, in SimWorld finalWorld, bool replayValid)
        {
            MeasureTerminal(in finalWorld, out float nearest, out int nearby, out float heightDelta, out bool actionable);
            return new BaselineRow
            {
                scenarioId = scenario.id,
                fixtureVersion = AggressiveFixtureVersion,
                weightStage = weightStage,
                profile = candidate.profileLabel,
                salientTargetMul = profile.salientTargetMul,
                terminalPositionMul = profile.terminalPositionMul,
                difficultyPenaltyMul = profile.difficultyPenaltyMul,
                difficultyTerminalOnly = profile.difficultyTerminalOnly,
                contractVersion = ContractVersion,
                mapVersion = scenario.world.mapVersion,
                initialSnapshotHash = WorldHash.Compute(in scenario.world).ToString("X16"),
                repetition = repetition,
                candidateRank = rank,
                actionSequence = ActionSequence(candidate.actions),
                salientEnemyId = intent.salientEnemyId,
                salientTargetProgress = candidate.rawSalientTargetProgress,
                killCount = candidate.killCount,
                damageDealt = candidate.damageDealt,
                initialEnemyCount = scenario.world.AliveCount(),
                finalAliveEnemyCount = finalWorld.AliveCount(),
                terminalNearestEnemyDistance = nearest,
                terminalNearbyEnemyCount = nearby,
                terminalHeightDelta = heightDelta,
                terminalActionable = actionable,
                terminalPositionQuality = candidate.rawTerminalPositionQuality,
                executionDifficulty = candidate.rawExecutionDifficulty,
                expectedHits = candidate.expectedHits,
                durationTicks = candidate.durationTicks,
                safetyScore = candidate.safetyScore,
                killScore = candidate.killScore,
                difficultyScore = candidate.difficultyScore,
                totalScore = candidate.TotalScore,
                elapsedMs = elapsedMs,
                worldHashAfterReplay = WorldHash.Compute(in finalWorld).ToString("X16"),
                replayValid = replayValid,
            };
        }

        /// <summary>
        /// 새 축을 Beam에 더하지 않고 후보 1~3위를 사전식으로 후처리한다. 공격형 목적상
        /// 주목 대상 실현과 처치를 먼저 보존하고, 동률에서 종료 상태와 입력 난이도를 비교한다.
        /// </summary>
        static void AnnotateAudit(List<BaselineRow> runRows)
        {
            var ordered = new List<BaselineRow>(runRows);
            ordered.Sort(CompareAggressiveAudit);
            int recommendedCandidateRank = ordered.Count > 0 ? ordered[0].candidateRank : -1;
            BaselineRow beamWinner = runRows.Count > 0 ? runRows[0] : null;

            for (int i = 0; i < ordered.Count; i++)
            {
                BaselineRow row = ordered[i];
                row.auditRank = i + 1;
                row.auditRecommended = i == 0;
                row.auditDiffersFromBeamWinner = recommendedCandidateRank != 1;
                row.auditReason = i == 0 ? ExplainAuditChoice(row, beamWinner) : string.Empty;
            }
        }

        static void AnnotateSafeAudit(List<BaselineRow> runRows)
        {
            var ordered = new List<BaselineRow>(runRows);
            ordered.Sort(CompareSafeAudit);
            int recommendedCandidateRank = ordered.Count > 0 ? ordered[0].candidateRank : -1;
            BaselineRow beamWinner = runRows.Count > 0 ? runRows[0] : null;

            for (int i = 0; i < ordered.Count; i++)
            {
                BaselineRow row = ordered[i];
                row.auditRank = i + 1;
                row.auditRecommended = i == 0;
                row.auditDiffersFromBeamWinner = recommendedCandidateRank != 1;
                row.auditReason = i == 0 ? ExplainSafeAuditChoice(row, beamWinner) : string.Empty;
            }
        }

        static int CompareSafeAudit(BaselineRow a, BaselineRow b)
        {
            int c = b.replayValid.CompareTo(a.replayValid);
            if (c != 0) return c;
            c = a.expectedHits.CompareTo(b.expectedHits);
            if (c != 0) return c;
            c = CompareFloatDescending(a.terminalPositionQuality, b.terminalPositionQuality);
            if (c != 0) return c;
            c = a.terminalNearbyEnemyCount.CompareTo(b.terminalNearbyEnemyCount);
            if (c != 0) return c;
            c = b.terminalActionable.CompareTo(a.terminalActionable);
            if (c != 0) return c;
            c = b.killCount.CompareTo(a.killCount);
            if (c != 0) return c;
            c = a.executionDifficulty.CompareTo(b.executionDifficulty);
            if (c != 0) return c;
            return a.candidateRank.CompareTo(b.candidateRank);
        }

        static string ExplainSafeAuditChoice(BaselineRow choice, BaselineRow beamWinner)
        {
            if (beamWinner == null || choice.candidateRank == beamWinner.candidateRank)
                return "Beam 1위와 안정형 관측 추천 일치";
            if (choice.expectedHits < beamWinner.expectedHits) return "예상 피격 감소";
            if (choice.terminalPositionQuality > beamWinner.terminalPositionQuality + 1e-4f)
                return "종료 안전도와 퇴로 우세";
            if (choice.terminalNearbyEnemyCount < beamWinner.terminalNearbyEnemyCount)
                return "종료 포위 적 감소";
            if (choice.terminalActionable && !beamWinner.terminalActionable)
                return "종료 즉시 행동 가능";
            if (choice.killCount > beamWinner.killCount) return "탈출에 필요한 위협 제거";
            if (choice.executionDifficulty < beamWinner.executionDifficulty - 1e-4f)
                return "입력 난이도 감소";
            return "결정론적 후보 순서";
        }

        static int CompareAggressiveAudit(BaselineRow a, BaselineRow b)
        {
            int c = CompareFloatDescending(a.salientTargetProgress, b.salientTargetProgress);
            if (c != 0) return c;
            c = b.killCount.CompareTo(a.killCount);
            if (c != 0) return c;
            c = b.damageDealt.CompareTo(a.damageDealt);
            if (c != 0) return c;
            c = a.expectedHits.CompareTo(b.expectedHits);
            if (c != 0) return c;
            c = b.terminalActionable.CompareTo(a.terminalActionable);
            if (c != 0) return c;
            c = CompareFloatDescending(a.terminalPositionQuality, b.terminalPositionQuality);
            if (c != 0) return c;
            c = a.executionDifficulty.CompareTo(b.executionDifficulty);
            if (c != 0) return c;
            return a.candidateRank.CompareTo(b.candidateRank);
        }

        static int CompareFloatDescending(float a, float b)
        {
            const float Epsilon = 1e-4f;
            if (a > b + Epsilon) return -1;
            if (a < b - Epsilon) return 1;
            return 0;
        }

        static string ExplainAuditChoice(BaselineRow choice, BaselineRow beamWinner)
        {
            if (beamWinner == null || choice.candidateRank == beamWinner.candidateRank)
                return "Beam 1위와 관측축 추천 일치";
            if (choice.salientTargetProgress > beamWinner.salientTargetProgress + 1e-4f)
                return "주목 대상 진척 우세";
            if (choice.killCount > beamWinner.killCount) return "처치 수 우세";
            if (choice.damageDealt > beamWinner.damageDealt) return "피해량 우세";
            if (choice.expectedHits < beamWinner.expectedHits) return "예상 피격 감소";
            if (choice.terminalActionable && !beamWinner.terminalActionable) return "종료 즉시 행동 가능";
            if (choice.terminalPositionQuality > beamWinner.terminalPositionQuality + 1e-4f)
                return "종료 위치 품질 우세";
            if (choice.executionDifficulty < beamWinner.executionDifficulty - 1e-4f)
                return "입력 난이도 감소";
            return "결정론적 후보 순서";
        }

        static SimWorld ReplayToWorld(
            in SimWorld initial, in SimServices services, CandidatePath candidate, int macroTicks, out bool valid)
        {
            SimWorld world = Snapshot.Clone(in initial);
            valid = true;
            for (int m = 0; m < candidate.actions.Length && world.player.combat.hp > 0; m++)
            {
                MacroAction action = candidate.actions[m];
                for (int tick = 0; tick < macroTicks; tick++)
                {
                    float yaw = action.ResolveYaw(in world, BeamSearch.ComputeAimYaw(in world));
                    InputCmd cmd = action.ToInputCmd(yaw, tick);
                    SimStep.Run(ref world, in cmd, in services);
                    Vector3 p = world.player.pos;
                    if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z))
                    {
                        valid = false;
                        return world;
                    }
                    if (world.player.combat.hp <= 0 && !candidate.isDeadFallback) valid = false;
                }
            }
            return world;
        }

        static void MeasureTerminal(
            in SimWorld world, out float nearest, out int nearby, out float heightDelta, out bool actionable)
        {
            nearest = float.PositiveInfinity;
            nearby = 0;
            heightDelta = 0f;
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim enemy = ref world.enemies[i];
                if (!enemy.alive) continue;
                float distance = CombatMath.FlatDistance(world.player.pos, enemy.pos);
                nearest = Mathf.Min(nearest, distance);
                if (distance <= PredictionScoreConfig.SurroundedRadius) nearby++;
                if (distance <= 8f) heightDelta = Mathf.Max(heightDelta, world.player.pos.y - enemy.pos.y);
            }
            if (float.IsPositiveInfinity(nearest)) nearest = -1f;
            actionable = world.player.combat.attackPhase == CombatConfig.PhNone
                      && world.player.combat.lungePhase == CombatConfig.LgNone;
        }

        static string ActionSequence(MacroAction[] actions)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < actions.Length; i++)
            {
                if (i > 0) sb.Append('>');
                sb.Append(actions[i].type);
                if (actions[i].lungeTargetId >= 0) sb.Append('(').Append(actions[i].lungeTargetId).Append(')');
            }
            return sb.ToString();
        }

        static string BuildCsv(List<BaselineRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("scenarioId,fixtureVersion,weightStage,profile,salientTargetMul,terminalPositionMul,difficultyPenaltyMul,difficultyTerminalOnly,contractVersion,mapVersion,initialSnapshotHash,repetition,candidateRank,actionSequence,salientEnemyId,salientTargetProgress,killCount,damageDealt,initialEnemyCount,finalAliveEnemyCount,terminalNearestEnemyDistance,terminalNearbyEnemyCount,terminalHeightDelta,terminalActionable,terminalPositionQuality,executionDifficulty,expectedHits,durationTicks,safetyScore,killScore,difficultyScore,totalScore,elapsedMs,worldHashAfterReplay,replayValid,auditRank,auditRecommended,auditDiffersFromBeamWinner,auditReason");
            foreach (BaselineRow r in rows)
            {
                sb.Append(r.scenarioId).Append(',').Append(r.fixtureVersion).Append(',').Append(r.weightStage).Append(',')
                  .Append(r.profile).Append(',').Append(F(r.salientTargetMul)).Append(',')
                  .Append(F(r.terminalPositionMul)).Append(',').Append(F(r.difficultyPenaltyMul)).Append(',')
                  .Append(r.difficultyTerminalOnly).Append(',')
                  .Append(r.contractVersion).Append(',')
                  .Append(r.mapVersion).Append(',').Append(r.initialSnapshotHash).Append(',').Append(r.repetition).Append(',')
                  .Append(r.candidateRank).Append(',').Append('"').Append(r.actionSequence).Append('"').Append(',')
                  .Append(r.salientEnemyId).Append(',').Append(F(r.salientTargetProgress)).Append(',')
                  .Append(r.killCount).Append(',').Append(r.damageDealt).Append(',')
                  .Append(r.initialEnemyCount).Append(',').Append(r.finalAliveEnemyCount).Append(',')
                  .Append(F(r.terminalNearestEnemyDistance)).Append(',').Append(r.terminalNearbyEnemyCount).Append(',')
                  .Append(F(r.terminalHeightDelta)).Append(',').Append(r.terminalActionable).Append(',')
                  .Append(F(r.terminalPositionQuality)).Append(',').Append(F(r.executionDifficulty)).Append(',')
                  .Append(r.expectedHits).Append(',')
                  .Append(r.durationTicks).Append(',').Append(F(r.safetyScore)).Append(',')
                  .Append(F(r.killScore)).Append(',').Append(F(r.difficultyScore)).Append(',')
                  .Append(F(r.totalScore)).Append(',').Append(r.elapsedMs.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                  .Append(r.worldHashAfterReplay).Append(',').Append(r.replayValid).Append(',')
                  .Append(r.auditRank).Append(',').Append(r.auditRecommended).Append(',')
                  .Append(r.auditDiffersFromBeamWinner).Append(',').Append('"').Append(r.auditReason).Append('"').AppendLine();
            }
            return sb.ToString();
        }

        static string F(float value) => value.ToString("F3", CultureInfo.InvariantCulture);

        static void Separator()
        {
            Debug.Log(new string('=', 60));
        }

        static SimWorld BuildRoomyWorld()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(new Vector3(0f, 0f, 10f));
            world.AddEnemy(new Vector3(0f, 0f, 4f));
            world.AddEnemy(new Vector3(2f, 0f, 3f));
            world.AddEnemy(new Vector3(-6f, 0f, 5f));
            return world;
        }

        static SimWorld BuildCloseQuartersWorld()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(new Vector3(0f, 0f, 4f));
            world.AddEnemy(new Vector3(0f, 0f, 1f));
            world.AddEnemy(new Vector3(1.5f, 0f, 1.5f));
            return world;
        }

        /// <summary>HP 1 — 즉사는 아니지만 한 대만 더 맞으면 끝. 신중하게 노는지 확인.</summary>
        static SimWorld BuildLowHpWorld()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(new Vector3(0f, 0f, 5f));
            world.player.combat.hp = 1;
            world.AddEnemy(new Vector3(0f, 0f, 2f));
            world.AddEnemy(new Vector3(2f, 0f, 3f));
            return world;
        }

        /// <summary>적 하나가 이미 Windup 중(명중까지 8틱) — 그 자리에서 맞을지 피할지 시급하게 판단해야 함.</summary>
        static SimWorld BuildImminentAttackWorld()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(new Vector3(0f, 0f, 3f));
            world.AddEnemy(new Vector3(0f, 0f, 1f));
            world.AddEnemy(new Vector3(3f, 0f, 4f));

            ref EnemySim urgent = ref world.enemies[0];
            urgent.ai.state = EnemyState.Windup;
            urgent.ai.stateTicks = AIConfig.MeleeWindupTicks - 8;
            urgent.ai.committedDir = new Vector3(0f, 0f, -1f);
            return world;
        }

        /// <summary>대시 충전 0, 재충전 중 — 대시 없이 순수 도보 이동으로만 대응해야 함.</summary>
        static SimWorld BuildDashExhaustedWorld()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(new Vector3(0f, 0f, 6f));
            world.player.dashCharges = 0;
            world.player.dashRecharge = SimConfig.DashRechargeTicks;
            world.AddEnemy(new Vector3(0f, 0f, 3f));
            world.AddEnemy(new Vector3(-2f, 0f, 4f));
            return world;
        }

        /// <summary>적 4마리가 한쪽 부채꼴(+x 쪽)을 덮고 있어 그쪽으로는 못 빠짐 — 반대쪽(-x)만 열려있음.</summary>
        static SimWorld BuildCorneredWorld()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.AddEnemy(new Vector3(2f, 0f, 4f));
            world.AddEnemy(new Vector3(4f, 0f, 2f));
            world.AddEnemy(new Vector3(4f, 0f, -2f));
            world.AddEnemy(new Vector3(2f, 0f, -4f));
            return world;
        }

        /// <summary>가까운 3마리 + 멀리 떨어진 낙오자 1마리 — 가까운 위협에 집중하고 낙오자는 무시하는지 확인.</summary>
        static SimWorld BuildMixedRangeWorld()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            world.AddEnemy(new Vector3(2f, 0f, 2f));
            world.AddEnemy(new Vector3(2.5f, 0f, 1.5f));
            world.AddEnemy(new Vector3(1.5f, 0f, 2.5f));
            world.AddEnemy(new Vector3(0f, 0f, -15f));
            return world;
        }

        /// <summary>거울대칭 쌍을 포함한 6마리, 전부 한 방향(-z)에 몰려있음.</summary>
        static SimWorld BuildSymmetricWorld()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(new Vector3(0f, 0f, 30f));
            world.AddEnemy(new Vector3(2f, 0f, 10f));
            world.AddEnemy(new Vector3(-2f, 0f, 10f));
            world.AddEnemy(new Vector3(5f, 0f, 8f));
            world.AddEnemy(new Vector3(-5f, 0f, 8f));
            world.AddEnemy(new Vector3(0f, 0f, 12f));
            world.AddEnemy(new Vector3(0f, 0f, 6f));
            return world;
        }

        /// <summary>플레이어를 중심으로 count마리를 원형으로 배치. radiusJitter로 거리에 약간 변화를 줌.</summary>
        static SimWorld BuildCircleWorld(int count, float radius, float radiusJitter)
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            for (int i = 0; i < count; i++)
            {
                float angle = (float)i / count * Mathf.PI * 2f;
                float r = radius + (i % 3 - 1) * radiusJitter;
                Vector3 pos = new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
                world.AddEnemy(pos);
            }
            return world;
        }

        static void RunScenario(string title, SimWorld world)
        {
            var sb = new StringBuilder();
            SimServices services = new SimServices(new FlatGroundCollision(), new StraightLinePathfinder());

            sb.AppendLine($"### {title} ###");
            sb.AppendLine("--- 초기 상태 (tick 0) ---");
            PrintWorld(sb, in world);

            PredictionSettings settings = PredictionSettings.Full;
            sb.AppendLine();
            sb.AppendLine($"설정: macroTicks={settings.macroTicks} macroDepth={settings.macroDepth} beamWidth={settings.beamWidth} " +
                          $"(총 {settings.macroTicks * settings.macroDepth}틱 = {settings.macroTicks * settings.macroDepth / 60f:F2}초 예측)");

            var stopwatch = Stopwatch.StartNew();
            CandidatePath[] plans = PredictionPlanner.Plan(in world, in services, settings);
            stopwatch.Stop();
            CandidatePath plan = plans[0];

            sb.AppendLine();
            sb.AppendLine($"--- 예측 결과 ({plans.Length}개 후보 중 최상위 표시, 계산 {stopwatch.Elapsed.TotalMilliseconds:F2}ms) ---");
            sb.AppendLine($"score={plan.TotalScore:F1} (안전{plan.safetyScore:F0}/처치{plan.killScore:F0}/난이도{plan.difficultyScore:F0})  " +
                          $"kills={plan.killCount}  damageDealt={plan.damageDealt}  dash={plan.dashCount}  lunge={plan.lungeCount}  " +
                          $"hitsTaken={plan.expectedHits}  durationTicks={plan.durationTicks}  deadFallback={plan.isDeadFallback}");
            sb.AppendLine($"행동 시퀀스 ({plan.actions.Length}개 매크로 스텝, 각 {settings.macroTicks}틱={settings.macroTicks / 60f:F2}초):");
            for (int i = 0; i < plan.actions.Length; i++)
            {
                MacroAction a = plan.actions[i];
                string label = a.type.ToString();
                if (a.type == MacroActionType.Lunge || a.type == MacroActionType.LungeStrike) label += $" -> 적 id={a.lungeTargetId}";
                sb.AppendLine($"  [{i}] {label}");
            }

            sb.AppendLine();
            sb.AppendLine("--- 실제로 틱마다 재생한 궤적 (찾은 행동을 SimStep.Run으로 그대로 실행) ---");
            Replay(sb, world, services, plan, settings);

            Debug.Log(sb.ToString());
        }

        static void Replay(StringBuilder sb, SimWorld world, SimServices services, CandidatePath plan, PredictionSettings settings)
        {
            int tick = 0;
            for (int m = 0; m < plan.actions.Length; m++)
            {
                MacroAction action = plan.actions[m];
                sb.AppendLine($"  매크로 {m}: {action.type}{((action.type == MacroActionType.Lunge || action.type == MacroActionType.LungeStrike) ? " (id=" + action.lungeTargetId + ")" : "")}");
                for (int t = 0; t < settings.macroTicks; t++)
                {
                    float yaw = AimAtNearestEnemy(in world);
                    InputCmd cmd = action.ToInputCmd(action.ResolveYaw(in world, yaw), t);
                    SimStep.Run(ref world, in cmd, in services);
                    tick++;

                    bool lastTickOfMacro = t == settings.macroTicks - 1;
                    if (tick % 15 == 0 || lastTickOfMacro || world.player.combat.hp <= 0)
                        PrintTick(sb, tick, in world);

                    if (world.player.combat.hp <= 0)
                    {
                        sb.AppendLine("    ** 플레이어 사망 — 재생 중단 **");
                        return;
                    }
                }
            }
            sb.AppendLine();
            sb.AppendLine("--- 재생 종료 (계획 완주) ---");
            PrintWorld(sb, in world);
        }

        static float AimAtNearestEnemy(in SimWorld world)
        {
            float best = float.MaxValue;
            Vector3 target = default;
            bool found = false;
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim e = ref world.enemies[i];
                if (!e.alive) continue;
                float d = CombatMath.FlatDistance(world.player.pos, e.pos);
                if (d < best) { best = d; target = e.pos; found = true; }
            }
            if (!found) return world.player.yaw;
            Vector3 dir = target - world.player.pos;
            return Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        }

        static void PrintWorld(StringBuilder sb, in SimWorld world)
        {
            sb.AppendLine($"  플레이어 pos=({world.player.pos.x:F1},{world.player.pos.z:F1}) hp={world.player.combat.hp} alive={world.player.combat.hp > 0}");
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim e = ref world.enemies[i];
                sb.AppendLine($"  적{e.id} pos=({e.pos.x:F1},{e.pos.z:F1}) hp={e.combat.health} alive={e.alive} state={e.ai.state}");
            }
        }

        static void PrintTick(StringBuilder sb, int tick, in SimWorld world)
        {
            float nearest = float.MaxValue;
            int aliveCount = 0;
            for (int i = 0; i < world.enemyCount; i++)
            {
                ref readonly EnemySim e = ref world.enemies[i];
                if (!e.alive) continue;
                aliveCount++;
                float d = CombatMath.FlatDistance(world.player.pos, e.pos);
                if (d < nearest) nearest = d;
            }
            string nearestStr = aliveCount > 0 ? nearest.ToString("F1") : "-";
            sb.AppendLine($"    [t={tick,4}] player=({world.player.pos.x,6:F1},{world.player.pos.z,6:F1}) " +
                          $"hp={world.player.combat.hp} atk={world.player.combat.attackPhase} lg={world.player.combat.lungePhase} " +
                          $"생존적={aliveCount} 최근접={nearestStr}");
        }
    }

    /// <summary>평지·벽 없음 가정의 최소 ICollision. Tests/EditMode/Support/StubServices와 같은
    /// 개념이지만, 에디터 툴이 Tests 어셈블리에 의존하지 않도록 여기서 따로 둔다.</summary>
    sealed class FlatGroundCollision : ICollision
    {
        public CastHit CapsuleCast(Vector3 bottom, Vector3 top, float radius, Vector3 dir, float maxDist) => default;
        public CastHit Raycast(Vector3 origin, Vector3 dir, float maxDist) => default;
        public bool SampleGround(Vector3 feet, float maxDown, out float groundY) { groundY = 0f; return true; }
        public bool HasLineOfSight(Vector3 from, Vector3 to) => true;
        public bool CanOccupyCapsule(Vector3 feet, float radius, float height) => true;
        public Vector3 Depenetrate(Vector3 feet, float radius, float height) => Vector3.zero;   // 평지 가정 — 겹침 없음
    }

    /// <summary>from→to 직선만 반환하는 최소 IPathfinder. 씬 NavMesh 없이도 예측 시각화 데모가 돌게 한다.</summary>
    sealed class StraightLinePathfinder : IPathfinder
    {
        public PathStep NextStep(Vector3 from, Vector3 to, int agentMask)
            => new PathStep { kind = MoveKind.Walk, next = to, currentNodeId = 0, nextNodeId = 0,
                destinationNodeId = 0, linkId = -1, floorId = 0, destinationFloorId = 0 };
        public int FloorIdAt(Vector3 position) => 0;
        public bool ClampToWalkable(Vector3 pos, float maxDist, out Vector3 onMesh)
        { onMesh = pos; return false; }
        public bool NextCorner(Vector3 from, Vector3 to, out Vector3 next) { next = to; return true; }
        public float PathLength(Vector3 from, Vector3 to) => Vector3.Distance(from, to);
        public bool NearestDropEdge(Vector3 from, out Vector3 edge, out Vector3 landing)
        {
            edge = from; landing = from; return false;
        }
    }

    /// <summary>S2 fixture 전용 베이크 그래프 대역. 적 압력 반대편 요청을 좌측 고지 JumpUp으로 연결한다.</summary>
    sealed class ElevatedEscapePathfinder : IPathfinder
    {
        public PathStep NextStep(Vector3 from, Vector3 to, int agentMask)
        {
            Vector3 next = from + Vector3.left * 3f + Vector3.up * 2.5f;
            return new PathStep
            {
                kind = MoveKind.JumpUp,
                next = next,
                currentNodeId = 0,
                nextNodeId = 1,
                destinationNodeId = 1,
                linkId = 0,
                floorId = 0,
                destinationFloorId = 1,
            };
        }
        public int FloorIdAt(Vector3 position) => position.y > 1f ? 1 : 0;
        public bool ClampToWalkable(Vector3 pos, float maxDist, out Vector3 onMesh)
        { onMesh = pos; return true; }
        public bool NextCorner(Vector3 from, Vector3 to, out Vector3 next)
        { next = from + Vector3.left * 3f + Vector3.up * 2.5f; return true; }
        public float PathLength(Vector3 from, Vector3 to) => Vector3.Distance(from, to);
        public bool NearestDropEdge(Vector3 from, out Vector3 edge, out Vector3 landing)
        { edge = from; landing = from; return false; }
    }
}
