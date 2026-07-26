using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// [2026-07-22] 플레이어 발소리. ★ 순수 연출·독립 — Main.Instance.World만 읽는다.
    /// 이동 <b>거리</b>로 페이스를 잡는다(시간 아님) — 걸으면 일정 간격, 빨리 움직이면 더 자주.
    ///   · 지상에서 움직인 수평 거리를 누적, StepDistance마다 한 발.
    ///   · 공중·대시 중엔 발소리 없음(대시는 자체 사운드가 있고, 공중엔 발이 안 닿는다).
    ///   · 예측 실행 중에도 자연스레 울린다(거리 기반이라 배속 무관).
    /// </summary>
    public class PlayerFootstep : MonoBehaviour
    {
        const float StepDistance = 4.0f;   // 이 거리(m)마다 한 발 (간격 2배로 늘림)
        const float ResetHold    = 2.2f;   // 공중/대시 후 착지 시 반 보쯤 남겨 첫 발이 바로 안 나오게

        Vector3 prevPos;
        bool havePrev;
        float distAccum;

        void Update()
        {
            var main = Main.Instance;
            if (main == null) return;
            ref readonly PlayerSim p = ref main.World.player;

            if (!havePrev) { prevPos = p.pos; havePrev = true; return; }

            Vector3 d = p.pos - prevPos; d.y = 0f;
            prevPos = p.pos;

            // 공중이거나 대시 중이면 발소리 없음 — 누적을 반 보쯤으로 잡아두고 나간다.
            if (!p.grounded || p.dashTicks > 0)
            {
                distAccum = ResetHold;
                return;
            }

            distAccum += d.magnitude;
            if (distAccum >= StepDistance)
            {
                distAccum -= StepDistance;
                CombatAudio.PlayerStep();
            }
        }
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class PlayerFootstepBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<PlayerFootstep>() == null)
                new GameObject("[PlayerFootstep]").AddComponent<PlayerFootstep>();
        }
    }
}
