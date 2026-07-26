using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 스폰 실체화 재생 박스. 팬 아래(몹이 나오는 길목)에 두는 <b>콜라이더 없는 논리 볼륨</b>.
    ///
    /// 몹·플레이어엔 콜라이더가 없어서 물리 트리거(OnTriggerEnter)가 안 먹는다. 그래서 물리 대신
    /// <see cref="Contains"/>로 몹의 sim 위치가 이 박스(월드 축 정렬 AABB) 안에 들어왔는지 좌표로 본다.
    /// EntityViews가 매 프레임 각 몹 위치를 이 박스들과 대조해, 처음 들어오는 순간
    /// <see cref="SpawnMaterialize.Reveal"/>를 부른다(카메라 시야와 무관 — 박스를 지나는 지점에서 걷힘).
    /// 몹은 스폰 즉시 <see cref="SpawnMaterialize.Prepare"/>로 숨겨져 있다가 박스에서 드러난다.
    ///
    /// "투명 관통" — 렌더러도 콜라이더도 없다. 씬에선 기즈모 와이어박스로만 보인다.
    /// </summary>
    [DisallowMultipleComponent]
    public class SpawnRevealVolume : MonoBehaviour
    {
        [Tooltip("박스 크기(월드 m, 축 정렬). 이 오브젝트 위치가 중심.")]
        public Vector3 size = new Vector3(2.5f, 3f, 2.5f);

        static readonly List<SpawnRevealVolume> all = new List<SpawnRevealVolume>();

        /// <summary>현재 씬에 활성화된 재생 박스 개수(진단용).</summary>
        public static int Count => all.Count;

        void OnEnable()  { if (!all.Contains(this)) all.Add(this); }
        void OnDisable() { all.Remove(this); }

        /// <summary>월드 점이 이 박스 안인가(축 정렬 AABB).</summary>
        public bool ContainsPoint(Vector3 world)
        {
            Vector3 d = world - transform.position;
            return Mathf.Abs(d.x) <= size.x * 0.5f
                && Mathf.Abs(d.y) <= size.y * 0.5f
                && Mathf.Abs(d.z) <= size.z * 0.5f;
        }

        /// <summary>어느 재생 박스든 이 점을 품고 있으면 true.</summary>
        public static bool Contains(Vector3 world)
        {
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].ContainsPoint(world)) return true;
            return false;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 1f, 0.6f, 0.15f);
            Gizmos.DrawCube(transform.position, size);
            Gizmos.color = new Color(0.3f, 1f, 0.6f, 0.9f);
            Gizmos.DrawWireCube(transform.position, size);
        }
    }
}
