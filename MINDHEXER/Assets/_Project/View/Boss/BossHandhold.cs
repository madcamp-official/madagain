using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 보스가 추격 중 손으로 짚는 벽 지점 마커. 레벨에 직접 배치한다(보스전_설계 §4).
    ///
    /// <para><b>왜 마커인가</b>: 레이캐스트만으로 짚을 곳을 고르면 기둥 모서리·문틀 같은 애매한
    /// 지점을 잡아 손이 허공을 짚는다. 마커를 두면 "짚는 곳 = 부서지는 곳"을 레벨 디자인이
    /// 완전히 소유한다 — IK 지점을 직접 설정하겠다는 결정 그대로.</para>
    ///
    /// <para>전방(+Z 로컬)이 <b>벽에서 바깥으로 나오는 방향</b>이 되게 배치할 것 — 손바닥이
    /// 이 방향을 마주보고 닿는다.</para>
    /// </summary>
    public class BossHandhold : MonoBehaviour
    {
        public enum Side { Any, Left, Right }

        [Tooltip("어느 손으로 짚는가. Any면 보스 진행 방향 기준 좌/우를 자동 판정.")]
        public Side side = Side.Any;

        [Tooltip("짚을 때 함께 부서질 벽 조각들(선택). 손이 닿는 프레임에 비활성화/파편 처리된다.")]
        public GameObject[] breakGroup;

        [Tooltip("이미 짚어서(=부숴서) 소모됐는가. 런타임 상태.")]
        [System.NonSerialized] public bool consumed;

        /// <summary>씬에 살아 있는 모든 마커 — Hackable.All과 같은 순회용 등록 패턴.</summary>
        public static readonly List<BossHandhold> All = new List<BossHandhold>();

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        /// <summary>짚는 순간 파괴 그룹 발동. 지금은 비활성화만 — 파편·먼지는 §12 파이프라인에서.</summary>
        public void Consume()
        {
            if (consumed) return;
            consumed = true;
            if (breakGroup == null) return;
            foreach (var go in breakGroup)
                if (go != null) go.SetActive(false);
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            Gizmos.color = consumed ? Color.gray
                : side == Side.Left ? new Color(1f, 0.5f, 0.2f)
                : side == Side.Right ? new Color(0.2f, 0.7f, 1f)
                : Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 0.25f);
            Gizmos.DrawRay(transform.position, transform.forward * 0.8f);   // 벽에서 나오는 방향
        }
#endif
    }
}
