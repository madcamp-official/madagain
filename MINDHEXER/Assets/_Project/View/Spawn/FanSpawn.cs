using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Fan을 "몹이 튀어나오는 실제 출구"로 표시하는 컴포넌트. Fan 프리팹 루트에 붙인다.
    ///
    /// 역할
    ///  · 스폰 원점(mouth)과 출구 방향(exitEuler)을 제공 — 몹의 sim 스폰 위치.
    ///  · 이 Fan에 속한 SpawnDrop 링크 목록(dropLinks)을 보관 — 자동배치 툴(⑤)이 채운다.
    ///
    /// 애니메이션·소환 로직은 이 컴포넌트가 하지 않는다:
    ///  · Fan 연출(준비·또잉·수납) = FanSpawnActor (View, 순수 연출).
    ///  · 실제 소환 스케줄 = ArenaWaves/WaveRunner (이 Fan을 PipeEmission.marker로 참조).
    ///  · 몹은 스폰 순간 dropLinks 중 하나를 순번으로 골라 타고 내려온다(펄스 폐기).
    /// </summary>
    [DisallowMultipleComponent]
    public class FanSpawn : MonoBehaviour
    {
        [Header("출구")]
        [Tooltip("몹이 나오는 방향(로컬 오일러). 기본 (0,90,90) — 배치된 Fan 기준. 툴에서 조절.")]
        public Vector3 exitEuler = new Vector3(0f, 90f, 90f);

        [Tooltip("스폰 원점(입) — Fan 로컬 오프셋. 몹이 여기서 생겨 아래로 떨어진다.")]
        public Vector3 mouthLocal = Vector3.zero;

        [Header("스폰 낙하 링크 (자동배치 툴이 채움)")]
        [Tooltip("이 Fan에 속한 SpawnDrop 링크들. 몹은 스폰 순번 % 개수 로 하나를 골라 탄다.")]
        public List<TraversalLink> dropLinks = new List<TraversalLink>();

        /// <summary>스폰 원점(월드).</summary>
        public Vector3 Mouth => transform.TransformPoint(mouthLocal);

        /// <summary>출구 방향(월드). Fan 회전 + exitEuler.</summary>
        public Vector3 ExitDir => (transform.rotation * Quaternion.Euler(exitEuler)) * Vector3.forward;

        /// <summary>스폰 순번에 배정할 드롭 링크(결정론: index % 개수). 없으면 null.</summary>
        public TraversalLink LinkForIndex(int spawnIndex)
        {
            int n = 0;
            for (int i = 0; i < dropLinks.Count; i++) if (dropLinks[i] != null) n++;
            if (n == 0) return null;
            int pick = ((spawnIndex % n) + n) % n;
            int seen = 0;
            for (int i = 0; i < dropLinks.Count; i++)
            {
                if (dropLinks[i] == null) continue;
                if (seen == pick) return dropLinks[i];
                seen++;
            }
            return null;
        }

        void OnDrawGizmosSelected()
        {
            Vector3 m = Mouth;
            Gizmos.color = new Color(1f, 0.5f, 0.1f);
            Gizmos.DrawWireSphere(m, 0.25f);
            Gizmos.DrawLine(m, m + ExitDir * 2f);        // 출구 방향
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
            foreach (var l in dropLinks)                  // 드롭 링크 착지점
                if (l != null) Gizmos.DrawLine(m, l.Low);
        }
    }
}
