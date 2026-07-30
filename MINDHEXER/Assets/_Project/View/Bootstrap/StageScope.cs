using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 스테이지 하나의 <b>리셋 범위</b>. (메인씬_통합과_클리어_설계 §2.1 · 사망_부활_연출_설계 §4)
    ///
    /// <para>죽으면 <b>죽은 스테이지만</b> 초기화되고 앞 스테이지에서 움직여 둔 것들은 보존된다.
    /// 전부 한 씬에 들어 있으므로(MainScene) 씬 단위로는 못 나눈다 — <b>계층이 곧 범위</b>다.</para>
    ///
    /// <para><b>왜 <see cref="IRunResettable"/>에 스테이지 번호를 안 넣는가</b>: 컴포넌트마다 번호를
    /// 손으로 맞추는 것은 반드시 틀린다. 새 기믹을 놓고 번호를 안 넣으면 조용히 리셋에서 빠진다.
    /// 계층은 눈에 보이고, 스테이지 밑에 놓기만 하면 자동으로 참여한다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class StageScope : MonoBehaviour
    {
        [Tooltip("스테이지 번호. 로그·순서 판단용.")]
        public int index;

        [Tooltip("이 스테이지의 시작·부활 지점. 비우면 자식에서 찾는다.")]
        public Checkpoint checkpoint;

        /// <summary>이 범위의 시작 지점. 없으면 null.</summary>
        public Checkpoint Spawn
        {
            get
            {
                if (checkpoint == null) checkpoint = GetComponentInChildren<Checkpoint>(true);
                return checkpoint;
            }
        }

        /// <summary>
        /// 이 범위 안의 모든 <see cref="IRunResettable"/>을 초기 상태로 되돌린다.
        /// 연출 없이 즉시 — 부르는 쪽이 암전 중에 호출한다.
        /// </summary>
        public void ResetScope()
        {
            var all = GetComponentsInChildren<IRunResettable>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                all[i].ResetForRestart();
            }
            Debug.Log($"[스테이지] {name}(#{index}) 리셋 — 대상 {all.Length}개");
        }

        void OnDrawGizmosSelected()
        {
            var rs = GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return;
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.5f);
            Gizmos.DrawWireCube(b.center, b.size);
        }
    }
}
