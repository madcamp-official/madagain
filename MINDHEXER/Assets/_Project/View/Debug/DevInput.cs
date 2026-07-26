namespace Game.View
{
    /// <summary>
    /// 개발 패널이 sim에 입력을 한 틱만 밀어넣는 통로.
    ///
    /// 패널을 조작하려면 커서를 풀어야 하고, 그동안 Main은 플레이어 입력을 막는다.
    /// 그래서 "콤보 자동 반복" 같은 테스트가 평소 경로로는 공격을 낼 수 없다.
    /// 여기 플래그를 세우면 Main이 다음 틱에 한 번만 소비한다(눌렀다 뗀 것과 동일).
    ///
    /// ★ sim 입력을 만드는 것이므로 결정론에 영향을 준다 — 테스트 전용이다.
    /// </summary>
    public static class DevInput
    {
        static bool attackPulse;

        /// <summary>다음 틱에 좌클릭 1회를 넣는다.</summary>
        public static void PressAttack() => attackPulse = true;

        /// <summary>Main이 매 틱 호출 — 세워져 있으면 true를 주고 내린다.</summary>
        public static bool ConsumeAttack()
        {
            if (!attackPulse) return false;
            attackPulse = false;
            return true;
        }

        public static void Clear() => attackPulse = false;
    }
}
