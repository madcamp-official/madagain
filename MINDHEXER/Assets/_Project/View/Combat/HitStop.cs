using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 히트스톱(A안 = sim 틱 스킵). 타격 순간 아주 잠깐 Sim만 얼려 손맛을 준다.
    /// ★ combat 소유·독립.
    ///
    /// <b>찌르기 임팩트에만</b> 건다. 예전엔 "적 총 HP 감소"를 타격으로 보고 평타에도 걸었는데,
    /// 즉발 판정 2연타를 칠 때마다 0.05초씩 세상이 멈춰 콤보 리듬을 깼다. 게다가 HP 총합만
    /// 보기 때문에 적이 낙사하거나 서로 죽여도 얼어붙는 오작동이 있었다.
    /// 평타의 손맛은 히트스톱 대신 이펙트·셰이크·적 경직(stunTicks)이 담당한다.
    ///
    /// timeScale=0(구식)과 달리 <b>timeScale은 1로 유지</b>한다. 얼릴 틱 수(FrozenTicks)만
    /// 요청하고, Main.FixedUpdate가 그만큼 SimStep.Run 호출을 건너뛴다. 그래서:
    ///  - Sim(적·플레이어)만 멈추고, 파티클·카메라 셰이크·화면효과·칼 애니메이션은 계속 재생
    ///    → "얼어붙은 세계에 타격만 꽂히는" 둠식 느낌.
    ///  - 결정론 안전: 틱을 스킵(지연)할 뿐 틱 내용·순서를 안 바꿔 WorldHash 시퀀스 불변.
    /// </summary>
    public class HitStop : MonoBehaviour
    {
        /// <summary>남은 스킵 틱. Main.FixedUpdate가 매 틱 1씩 소비하며 sim을 건너뛴다.</summary>
        public static int FrozenTicks;

        // [예측 추적 모드, 2026-07-22] 프리즈는 sim 틱을 통째로 건너뛰므로 뷰(파티클·애니메이션)
        // 만 돌고 적·투사체는 멈춘다 — 평소엔 그게 타격감이지만, 런지가 거의 매 순간인 예측
        // 추적 모드에서는 "몹만 얼어붙은" 것처럼 보인다. 그런 모드가 매 프레임 켜고 끈다
        // (PredictionController.Tick). 예측이 아닐 때는 항상 false로 돌아온다.
        /// <summary>true면 새 프리즈 요청을 무시하고 남은 프리즈도 즉시 해제한다.</summary>
        public static bool Suppressed;

        byte prevLunge;

        /// <summary>얼릴 틱 요청(더 긴 요청이 우선). 여러 타격이 겹쳐도 최댓값 유지.</summary>
        static void Freeze(int ticks) { if (ticks > FrozenTicks) FrozenTicks = ticks; }

        void Update()
        {
            if (Main.Instance == null) return;

            if (Suppressed) { FrozenTicks = 0; prevLunge = Main.Instance.World.player.combat.lungePhase; return; }

            ref readonly SimWorld w = ref Main.Instance.World;

            // 런지 임팩트(Travel→Recovery)에만 프리즈 — 평타는 걸지 않는다(연타 리듬 보존)
            byte lg = w.player.combat.lungePhase;
            if (prevLunge == CombatConfig.LgTravel && lg == CombatConfig.LgRecovery)
                Freeze(CombatConfig.LungeHitStopTicks);
            prevLunge = lg;
        }

        void OnDisable() { FrozenTicks = 0; }   // Play 정지·비활성 시 프리즈 해제
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class HitStopBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<HitStop>() == null)
                new GameObject("[HitStop]").AddComponent<HitStop>();
        }
    }
}
