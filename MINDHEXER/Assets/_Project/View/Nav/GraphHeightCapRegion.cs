using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 예측 그래프 굽기 전용 <b>지역 높이 상한</b> 마커. 런타임 동작 없음 — PredictionGraphBaker만 읽는다.
    ///
    /// 굽기의 "높이 상한"(천장·지붕 노드 컷)은 절대 y 기준이라, 아레나마다 지형 기준 높이가 다르면
    /// 전역 값 하나로 안 된다(실측: Arena_1~3 바닥 ≤9.4m·지붕 12~27m, Arena_4 바닥 8~32m·탑 35~57m).
    /// 이 박스 안(XZ)에 있는 점은 전역 상한 대신 <see cref="maxNodeHeight"/>를 쓴다.
    /// </summary>
    public class GraphHeightCapRegion : MonoBehaviour
    {
        [Tooltip("이 지역 안(XZ)의 노드 최대 높이(월드 y). 이보다 높은 노드는 그래프에서 제외")]
        public float maxNodeHeight = 33f;

        [Tooltip("지역 크기(월드 단위, XZ만 사용). 중심은 이 오브젝트의 위치")]
        public Vector3 size = new Vector3(100f, 100f, 100f);

        /// <summary>점 p가 이 지역 안(XZ 기준)인가.</summary>
        public bool ContainsXZ(Vector3 p)
        {
            Vector3 c = transform.position;
            return Mathf.Abs(p.x - c.x) <= size.x * 0.5f
                && Mathf.Abs(p.z - c.z) <= size.z * 0.5f;
        }

        void OnDrawGizmosSelected()
        {
            // 상한 높이에 지역 평면을 그려 "이 위는 잘린다"가 눈에 보이게 한다.
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
            Vector3 c = transform.position; c.y = maxNodeHeight;
            Gizmos.DrawWireCube(c, new Vector3(size.x, 0.05f, size.z));
        }
    }
}
