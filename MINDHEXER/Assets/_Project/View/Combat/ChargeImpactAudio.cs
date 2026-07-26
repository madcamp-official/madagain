using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// [2026-07-22] 돌진몹이 벽/플레이어에 박는 순간의 박치기음. ★ 순수 연출·독립(World만 읽음).
    /// 돌진(ChargeRun) → 회복(Recovery) 전이 = 돌진이 접촉/벽으로 멈춘 순간이다(EnemyAI.StepCharge).
    /// (열린 곳에서 최대 거리까지 달려 멈추는 경우도 드물게 같이 잡히지만, 대개는 벽/플레이어 충돌.)
    /// </summary>
    public class ChargeImpactAudio : MonoBehaviour
    {
        EnemyState[] prevState = System.Array.Empty<EnemyState>();
        int[] prevId = System.Array.Empty<int>();
        bool[] seen = System.Array.Empty<bool>();

        void Update()
        {
            var main = Main.Instance;
            if (main == null) return;
            ref readonly SimWorld w = ref main.World;
            int n = w.enemyCount;

            if (prevState.Length < n)
            {
                System.Array.Resize(ref prevState, n);
                System.Array.Resize(ref prevId, n);
                System.Array.Resize(ref seen, n);
            }

            for (int i = 0; i < n; i++)
            {
                ref readonly EnemySim e = ref w.enemies[i];
                bool sameId = seen[i] && e.id == prevId[i];

                if (sameId && e.ai.mobility == MobilityType.Charge
                    && prevState[i] == EnemyState.ChargeRun
                    && e.ai.state == EnemyState.Recovery)
                {
                    CombatAudio.ChargeImpact();
                }

                prevState[i] = e.ai.state;
                prevId[i] = e.id;
                seen[i] = true;
            }
        }
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class ChargeImpactAudioBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<ChargeImpactAudio>() == null)
                new GameObject("[ChargeImpactAudio]").AddComponent<ChargeImpactAudio>();
        }
    }
}
