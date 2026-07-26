using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// <b>이 오브젝트 위치</b>에 보스(구 코어)를 1회 소환한다. 보스 앵커(활동 위치)에 배치하라 —
    /// StepBoss가 첫 틱에 이 위치를 anchor로 잡고, 활동 y = anchor.y + AIConfig.BossRevealYOffset(현재 0)이
    /// 된다. 즉 <b>둔 자리에 그대로 뜬다</b>. Main이 준비되면 소환하고 역할을 마친다(중복 방지).
    ///
    /// 트리거:
    ///  · <see cref="spawnOnStart"/>=true → 씬 시작 시 자동(단일 아레나 테스트용).
    ///  · false → <see cref="SpawnNow"/>를 외부(예: ArenaRoom.onLock)가 부를 때만. 마스터 씬처럼
    ///    아레나 여럿이 한 씬이면 이걸 써야 한다 — 안 그러면 게임 시작부터 보스가 존재해
    ///    EMP가 내내 켜져(예지 차단) 다른 아레나가 망가진다.
    ///
    /// 순수 View — sim에 1회 주입할 뿐.
    /// </summary>
    [DisallowMultipleComponent]
    public class BossEntrySpawner : MonoBehaviour, IRunResettable
    {
        [Tooltip("씬 시작 시 자동 소환. 마스터 씬(아레나 여럿)에선 끄고 ArenaRoom.onLock → SpawnNow로 연결하라.")]
        public bool spawnOnStart = true;

        [Tooltip("소환까지 대기(초). 0=즉시. 진입 연출·로딩 여유가 필요하면 조금 준다.")]
        public float delaySeconds = 0f;

        bool  done;
        bool  armed;     // SpawnNow로 예약됨(지연 카운트 시작)
        float elapsed;

        void Start()
        {
            if (spawnOnStart) armed = true;
        }

        /// <summary>외부 트리거(ArenaRoom.onLock 등)에서 호출 — 지연 뒤 1회 소환.</summary>
        public void SpawnNow()
        {
            if (done) return;
            armed = true;
            elapsed = 0f;
        }

        /// <summary>재시작 시 '이미 소환함' 잠금을 풀어 다시 소환될 수 있게 한다.</summary>
        public void ResetForRestart()
        {
            done = false;
            armed = spawnOnStart;   // 자동소환이면 재무장, onLock 방식이면 다음 진입까지 대기
            elapsed = 0f;
        }

        void Update()
        {
            if (done || !armed) return;
            var main = Main.Instance;
            if (main == null) return;   // 아직 초기화 전 — 준비되면 소환

            elapsed += Time.deltaTime;
            if (elapsed < delaySeconds) return;

            int id = main.SpawnEnemyAt(transform.position,
                                       CombatType.Melee, MobilityType.Orb, SizeClass.Normal);
            done = true;
            if (id < 0)
                Debug.LogWarning("[BossEntrySpawner] 보스 소환 실패(스폰 상한 등). 위치=" + transform.position);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.2f, 0.1f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.6f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.down * 2f);
        }
    }
}
