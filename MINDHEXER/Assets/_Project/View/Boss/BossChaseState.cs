using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 지금 <b>보스 추격 중인가</b>. 게임 전체에서 하나뿐인 상태다. (기초_설계안 §0.4)
    ///
    /// <para><b>왜 필요한가</b> — 플레이어는 <b>같은 입구를 두 번 지난다.</b>
    /// <code>
    /// 전반부  1 → 2 → 3   지나감. 아무 일도 없어야 한다
    /// 후반부  3 → 2 → 1   지나감. 보스가 막아야 한다
    /// </code>
    /// 입구 트리거가 방향을 모르면 <b>전반부에 보스가 튀어나온다.</b> 진행 방향으로 판정하는 방법도
    /// 있지만 옆걸음·후진에서 오작동한다 — "추격이 시작됐는가"라는 <b>상태 하나</b>로 가르는 것이
    /// 정확하고, 나중에 "추격 중에만"이 필요한 다른 것들도 같은 곳을 보면 된다.</para>
    ///
    /// <para><b>정적 상태인 이유</b>: 입구가 여럿이고 각자 <see cref="StageEntranceFlow"/>를 갖는데,
    /// 그것들이 서로를 참조하게 만들면 배선이 N² 로 늘고 하나만 빠져도 조용히 안 된다.
    /// 읽는 쪽이 많고 쓰는 쪽이 하나뿐인 값이라 정적이 맞다.</para>
    ///
    /// <para>씬을 다시 로드하지 않고 판을 재시작하므로(§IRunResettable) <see cref="ResetStatics"/>로
    /// 명시적으로 되돌린다 — 안 그러면 죽고 나서 시작하자마자 추격 중인 상태가 된다.</para>
    /// </summary>
    public static class BossChaseState
    {
        /// <summary>추격이 시작됐는가. 입구 트리거는 이게 true일 때만 발동한다.</summary>
        public static bool Active { get; private set; }

        /// <summary>지금까지 보스를 몇 번 찍었는가. 회차(<see cref="StageEntranceFlow.index"/>)와 별개로
        /// 진행도를 알고 싶은 쪽(HUD·사운드·난이도)이 읽는다.</summary>
        public static int CrushCount { get; private set; }

        /// <summary>추격 시작/종료 순간. 음악·조명 등이 구독한다.</summary>
        public static event System.Action<bool> OnActiveChanged;

        /// <summary>추격 시작. 첫 낑김(§0.4 전환점)에서 <see cref="StageEntranceFlow"/>가 부른다.</summary>
        public static void Begin()
        {
            if (Active) return;
            Active = true;
            Debug.Log("[보스] 추격 시작 — 이제부터 입구 트리거가 살아난다.");
            OnActiveChanged?.Invoke(true);
        }

        /// <summary>추격 종료(보스 사망).</summary>
        public static void End()
        {
            if (!Active) return;
            Active = false;
            Debug.Log($"[보스] 추격 종료 — 총 {CrushCount}회 찍음.");
            OnActiveChanged?.Invoke(false);
        }

        /// <summary>한 번 찍었다.</summary>
        public static void CountCrush() => CrushCount++;

        /// <summary>판 재시작. 씬을 다시 안 읽으므로 정적 상태를 손으로 되돌려야 한다.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetStatics()
        {
            Active = false;
            CrushCount = 0;
            OnActiveChanged = null;
        }
    }
}
