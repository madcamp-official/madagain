namespace Game.Sim
{
    /// <summary>
    /// 적 전투 상태. ★ combat 세션 소유.
    /// stunTicks는 rebuild의 EnemyMovement가 읽는다(스턴 중 AI 정지) — 읽기 전용 접점.
    /// health/처치는 combat의 CombatResolve가 관리.
    /// </summary>
    public struct EnemyCombatState
    {
        public int health;      // 스폰 시 설정 (일반 2 / 중형 3 / 대형 4)
        public int stunTicks;   // 피격 경직. >0이면 AI·이동 정지(공격도 취소). CombatResolve가 부여·감소
        public int bindTicks;   // >0이면 런지 표적 이동봉쇄(위치·중력 동결, 공격은 지속)
        public int deathTick;   // 처치된 틱 (사지절단 연출 타이밍용, 0=생존)

        // 대형몹 글로리킬 단계: 0 정상 / 1 slash1 / 2 slash2 / 3 폭발(dash). >0이면 처형 중(AI·판정·분리 제외).
        public byte gloryStage;

        public static EnemyCombatState Spawn(int hp) => new EnemyCombatState { health = hp };
    }
}
