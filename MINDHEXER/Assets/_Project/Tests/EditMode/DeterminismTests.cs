using NUnit.Framework;
using UnityEngine;
using Game.Sim;

namespace Game.Sim.Tests
{
    /// <summary>
    /// 예측(Beam Search)이 Sim 위에 쌓이려면 "같은 스냅샷 + 같은 입력 = 같은 결과"가
    /// 항상 성립해야 한다. 이 스위트는 그 최소 전제를 씬·Physics 없이 확인한다.
    /// 적 50마리·180틱·100회 결정론 벤치마크는 아래 Benchmark_ 테스트 참고.
    /// </summary>
    public class DeterminismTests
    {
        const int Ticks = 40;
        const int Repeats = 20;

        static SimWorld BuildWorld()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(new Vector3(0f, 0f, 20f));
            world.AddEnemy(new Vector3(0f, 0f, 10f));
            world.AddEnemy(new Vector3(1.5f, 0f, 9f));
            world.AddEnemy(new Vector3(-1.5f, 0f, 11f));
            return world;
        }

        /// <summary>결정론적 고정 입력 시퀀스. tick마다 같은 값을 반환한다(난수 없음).</summary>
        static InputCmd GetInput(int tick)
        {
            var cmd = new InputCmd { yaw = 180f, move = new Vector2(0f, 1f) };
            switch (tick)
            {
                case 10:
                    cmd.dash = true;
                    cmd.dashDirection = DashDirection.Forward;
                    break;
                case 19:
                    cmd.attack = true;
                    break;
                case 30:
                    cmd.lunge = true;
                    cmd.lungeTargetId = -1;
                    break;
            }
            return cmd;
        }

        static ulong RunAndHash(int ticks)
        {
            SimWorld world = BuildWorld();
            SimServices services = StubServices.Create();
            for (int t = 0; t < ticks; t++)
            {
                InputCmd cmd = GetInput(t);
                SimStep.Run(ref world, in cmd, in services);
            }
            return WorldHash.Compute(in world);
        }

        [Test]
        public void FixedInputSequence_ProducesSameHash_EveryRepeat()
        {
            ulong expected = RunAndHash(Ticks);
            for (int i = 0; i < Repeats; i++)
            {
                ulong actual = RunAndHash(Ticks);
                Assert.AreEqual(expected, actual, $"반복 {i}번째에서 WorldHash가 달라짐 (비결정적 동작 의심)");
            }
        }

        [Test]
        public void EmptyInputSequence_ProducesSameHash_EveryRepeat()
        {
            // 입력이 전부 비어있어도(적 AI만 굴러가도) 결정론이 성립해야 한다.
            SimWorld world = BuildWorld();
            SimServices services = StubServices.Create();
            InputCmd empty = InputCmd.Empty;
            for (int t = 0; t < Ticks; t++)
                SimStep.Run(ref world, in empty, in services);
            ulong expected = WorldHash.Compute(in world);

            for (int i = 0; i < Repeats; i++)
            {
                SimWorld w2 = BuildWorld();
                for (int t = 0; t < Ticks; t++)
                    SimStep.Run(ref w2, in empty, in services);
                Assert.AreEqual(expected, WorldHash.Compute(in w2));
            }
        }

        /// <summary>docs/shared/OPTIMIZATION.md §14/§15가 요구하는 "180틱×100회" 결정론
        /// 벤치마크. 적 50마리(요청 규모, MaxEnemies=64 안쪽)로 근접·원거리·대형몹이 다
        /// 섞인 상태에서 대시·평타·런지가 뒤섞인 180틱 입력을 100번 반복해도 WorldHash가
        /// 항상 같은지 확인한다.</summary>
        const int BenchmarkEnemyCount = 50;
        const int BenchmarkTicks = 180;
        const int BenchmarkRepeats = 100;

        static SimWorld BuildBenchmarkWorld()
        {
            SimWorld world = SimWorld.Create();
            world.player = PlayerSim.Spawn(Vector3.zero);
            for (int i = 0; i < BenchmarkEnemyCount; i++)
            {
                float angle = (float)i / BenchmarkEnemyCount * Mathf.PI * 2f;
                float radius = 8f + (i % 5) * 1.5f;   // 살짝 흩어서 겹침 분리도 같이 걸린다
                world.AddEnemy(new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }
            return world;
        }

        /// <summary>180틱 동안 대시·평타·런지를 여러 번 섞은 고정 입력(난수 없음).</summary>
        static InputCmd GetBenchmarkInput(int tick)
        {
            var cmd = new InputCmd { yaw = (tick * 3f) % 360f, move = new Vector2(0f, 1f) };
            int phase = tick % 45;
            switch (phase)
            {
                case 10:
                    cmd.dash = true;
                    cmd.dashDirection = (DashDirection)((tick / 45) % 4);
                    break;
                case 20:
                    cmd.attack = true;
                    break;
                case 35:
                    cmd.lunge = true;
                    cmd.lungeTargetId = -1;
                    break;
            }
            return cmd;
        }

        static ulong RunBenchmarkAndHash()
        {
            SimWorld world = BuildBenchmarkWorld();
            SimServices services = StubServices.Create();
            for (int t = 0; t < BenchmarkTicks; t++)
            {
                InputCmd cmd = GetBenchmarkInput(t);
                SimStep.Run(ref world, in cmd, in services);
            }
            return WorldHash.Compute(in world);
        }

        [Test]
        public void Benchmark_180Ticks_50Enemies_ProducesSameHash_Across100Repeats()
        {
            ulong expected = RunBenchmarkAndHash();
            for (int i = 0; i < BenchmarkRepeats; i++)
            {
                ulong actual = RunBenchmarkAndHash();
                Assert.AreEqual(expected, actual,
                    $"반복 {i}번째에서 WorldHash가 달라짐 (적 {BenchmarkEnemyCount}마리 규모에서 비결정적 동작 의심)");
            }
        }

        [Test]
        public void Snapshot_CopiesProjectileState_Independently()
        {
            SimWorld source = BuildWorld();
            source.SpawnProjectile(new Vector3(1f, 2f, 3f), new Vector3(4f, 0f, 0f));

            SimWorld copy = Snapshot.Clone(in source);
            Assert.AreEqual(WorldHash.Compute(in source), WorldHash.Compute(in copy));
            Assert.AreNotSame(source.enemies, copy.enemies);
            Assert.AreNotSame(source.projectiles, copy.projectiles);

            copy.projectiles[0].pos += Vector3.right;
            Assert.AreNotEqual(WorldHash.Compute(in source), WorldHash.Compute(in copy));
            Assert.AreEqual(new Vector3(1f, 2f, 3f), source.projectiles[0].pos);
        }

        [Test]
        public void ProjectileSimulation_AdvancesFixedState_Deterministically()
        {
            SimWorld first = BuildWorld();
            first.SpawnProjectile(Vector3.zero, new Vector3(12f, 0f, 0f));
            SimWorld second = Snapshot.Clone(in first);
            SimServices services = StubServices.Create();

            for (int i = 0; i < 5; i++)
            {
                InputCmd empty = InputCmd.Empty;
                SimStep.Run(ref first, in empty, in services);
                SimStep.Run(ref second, in empty, in services);
            }

            Assert.AreEqual(WorldHash.Compute(in first), WorldHash.Compute(in second));
            Assert.AreEqual(1f, first.projectiles[0].pos.x, 1e-5f);
        }

        [Test]
        public void ExperimentalAutoSpawn_IsAerialBiased_AndNeverLarge()
        {
            int flying = 0;
            int groundMelee = 0;
            int charge = 0;
            int groundRanged = 0;

            for (int i = 0; i < 10; i++)
            {
                var (combat, mobility, size) = SimWorld.ExperimentalAutoSpawn(i);
                Assert.AreEqual(SizeClass.Normal, size, $"sequence {i} spawned a large enemy");

                if (mobility == MobilityType.Flying) flying++;
                else if (mobility == MobilityType.Charge) charge++;
                else if (combat == CombatType.Ranged) groundRanged++;
                else groundMelee++;
            }

            Assert.AreEqual(7, flying);
            Assert.AreEqual(1, groundMelee);
            Assert.AreEqual(1, charge);
            Assert.AreEqual(1, groundRanged);
        }
    }
}
