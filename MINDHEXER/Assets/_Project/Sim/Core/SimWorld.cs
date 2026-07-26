using UnityEngine;

namespace Game.Sim
{
    /// <summary>월드 전체 상태. 이 하나를 복제(Snapshot)하면 그 시점으로 되돌아간다.</summary>
    public struct SimWorld
    {
        public int       tick;
        public PlayerSim player;
        public EnemySim[] enemies;   // 용량 MaxEnemies 고정
        public int       enemyCount;
        public int       nextEnemyId;   // 재사용되지 않는 단조 고유 id 발급기(슬롯 재사용과 무관)
        public uint      rngState;   // 시드 고정 결정론 RNG 상태 (DetRng)

        // >>> [예측 세션 추가, 2026-07-18] 원래 없던 필드 3개. 예측(Game.Prediction) 상태
        // 중복제거·후보 태깅용 — PREDICTION_CONTRACT.md 참고. 되돌리려면 이 3줄과 아래
        // Create()의 mapVersion=1, Snapshot.cs의 해당 복사 3줄, WorldHash.cs의 해당 해시 3줄을
        // 같이 제거해야 한다(안 그러면 컴파일은 되지만 예측 쪽이 이 필드를 계속 참조해서 깨짐).
        public bool spawnLocked;
        public int  waveId;
        public int  mapVersion;
        // <<< [예측 세션 추가 끝]

        // ── 적→플레이어 히트 큐 (AI 세션). AI가 큐잉 → CombatResolve가 방어판정 후 적용.
        //    한 틱 안에서 생성·소비되고 CombatResolve가 비우므로 틱 경계엔 항상 비어 있음 → 해시 불필요.
        public PlayerHit[] pendingHits;
        public int         pendingHitCount;

        // ── 투사체 (AI 세션). 틱 넘어 지속 → 해시·깊은복사 대상.
        public Projectile[] projectiles;
        public int          projectileCount;   // 죽은 슬롯 포함 최고수위

        // 직전 틱 플레이어 위치 → 이번 틱 속도 산출용(원거리 리드 조준). 매 틱 시작에 갱신(전이적, 해시 불필요).
        public Vector3 prevPlayerPos;

        public static SimWorld Create()
        {
            return new SimWorld
            {
                tick = 0,
                player = PlayerSim.Spawn(Vector3.zero),
                enemies = new EnemySim[SimConfig.MaxEnemies],
                enemyCount = 0,
                nextEnemyId = 0,
                rngState = 0x1234_5678u,   // 고정 시드
                pendingHits = new PlayerHit[SimConfig.MaxEnemies],
                pendingHitCount = 0,
                projectiles = new Projectile[128],
                projectileCount = 0,
                mapVersion = 1,   // [예측 세션 추가] 위 spawnLocked/waveId/mapVersion 필드 초기값
            };
        }

        /// <summary>죽은 슬롯을 재사용(있으면)해 스폰. 없으면 새 슬롯 append. 성공 시 true.</summary>
        public bool AddEnemy(Vector3 at)
        {
            // 3축 배분: 슬롯 인덱스로 결정론(5마다 1 대형근접, 나머지 중 3마다 1 원거리, 그 외 근접).
            for (int i = 0; i < enemyCount; i++)
                if (!enemies[i].alive) { var (c, m, s) = PickSpawn(i); enemies[i] = EnemySim.Spawn(nextEnemyId++, at, c, m, s); return true; }

            if (enemyCount >= SimConfig.MaxEnemies) return false;
            var (c2, m2, s2) = PickSpawn(enemyCount);
            enemies[enemyCount] = EnemySim.Spawn(nextEnemyId++, at, c2, m2, s2);
            enemyCount++;
            return true;
        }

        /// <summary>지정 조합으로 스폰(개발 콘솔용). 죽은 슬롯 재사용, 없으면 append.</summary>
        public bool AddEnemy(Vector3 at, CombatType combat, MobilityType mobility, SizeClass size)
        {
            for (int i = 0; i < enemyCount; i++)
                if (!enemies[i].alive) { enemies[i] = EnemySim.Spawn(nextEnemyId++, at, combat, mobility, size); return true; }

            if (enemyCount >= SimConfig.MaxEnemies) return false;
            enemies[enemyCount] = EnemySim.Spawn(nextEnemyId++, at, combat, mobility, size);
            enemyCount++;
            return true;
        }

        /// <summary>모든 적·투사체 제거(개발 콘솔용). 슬롯은 재사용되므로 alive만 내림.</summary>
        public void DevClearEnemies()
        {
            for (int i = 0; i < enemyCount; i++) enemies[i].alive = false;
            if (projectiles != null)
                for (int i = 0; i < projectileCount; i++) projectiles[i].alive = false;
        }

        /// <summary>
        /// 실험용 자동 스폰 분포. 10회 중 공중 원거리 7, 지상 근접/돌진/원거리 각 1, 대형 0.
        /// 명시적 개발 콘솔 스폰에는 적용하지 않는다.
        /// </summary>
        public static (CombatType, MobilityType, SizeClass) ExperimentalAutoSpawn(int sequence)
        {
            int index = sequence % 10;
            if (index < 0) index += 10;
            if (index < 7) return (CombatType.Ranged, MobilityType.Flying, SizeClass.Normal);
            if (index == 7) return (CombatType.Melee, MobilityType.Ground, SizeClass.Normal);
            if (index == 8) return (CombatType.Melee, MobilityType.Charge, SizeClass.Normal);
            return (CombatType.Ranged, MobilityType.Ground, SizeClass.Normal);
        }

        static (CombatType, MobilityType, SizeClass) PickSpawn(int slot) =>
            ExperimentalAutoSpawn(slot);

        /// <summary>적→플레이어 히트 큐잉(AI). 적용은 CombatResolve가 방어판정 후.</summary>
        public void QueuePlayerHit(Vector3 dir, int dmg)
        {
            if (pendingHits == null || pendingHitCount >= pendingHits.Length) return;
            pendingHits[pendingHitCount].dir = dir;
            pendingHits[pendingHitCount].dmg = dmg;
            pendingHitCount++;
        }

        /// <summary>투사체 스폰. 죽은 슬롯 재사용, 없으면 append.</summary>
        public void SpawnProjectile(Vector3 pos, Vector3 vel)
        {
            if (projectiles == null) return;
            for (int i = 0; i < projectileCount; i++)
                if (!projectiles[i].alive)
                { projectiles[i] = new Projectile { pos = pos, vel = vel, ttl = AIConfig.ProjectileTtl, alive = true }; return; }

            if (projectileCount >= projectiles.Length) return;
            projectiles[projectileCount++] = new Projectile { pos = pos, vel = vel, ttl = AIConfig.ProjectileTtl, alive = true };
        }

        /// <summary>살아있는 적 수(상한 검사·소환 판단용). enemyCount는 죽은 슬롯 포함 최고수위.</summary>
        public int AliveCount()
        {
            int n = 0;
            for (int i = 0; i < enemyCount; i++) if (enemies[i].alive) n++;
            return n;
        }
    }
}
