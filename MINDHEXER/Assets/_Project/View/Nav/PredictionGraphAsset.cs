using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 예측용으로 구운 맵 그래프(노드·링크). ★ 예측·following이 이걸 읽는다.
    ///
    /// 왜 굽는가: 예측은 월드를 포크해 sim을 그대로 돌리는데, NavMesh는 결정론적 포크가 불가라
    /// (계약 §8.3) 별도의 고정 그래프가 필요하다. 기존에는 SampleScene용 그래프가 코드에
    /// 하드코딩돼 있었고, 다른 맵에는 그래프가 없어 예측이 무의미했다.
    ///
    /// 굽기: Tools/층이동 링크/예측 그래프 굽기.
    /// 로드: Resources에서 씬 이름으로 찾는다(<see cref="ResourceName"/>). 없으면 코드 그래프로 폴백.
    ///
    /// ⚠️ 맵이나 마커를 고치면 <b>다시 구워야 한다</b>. 안 그러면 예측만 옛 지형을 본다.
    ///    아래 bakedAt/sceneName/markerCount로 언제 무엇을 구웠는지 확인할 수 있다.
    /// </summary>
    public class PredictionGraphAsset : ScriptableObject
    {
        [Tooltip("굽기 대상 씬 이름")]
        public string sceneName;
        [Tooltip("구운 시각(로컬)")]
        public string bakedAt;
        [Tooltip("굽기에 쓰인 층이동 마커 수")]
        public int markerCount;
        [Tooltip("노드 수 / 링크 수 (참고용)")]
        public int nodeCount, linkCount;

        [SerializeField] ArenaNavNode[] nodes = System.Array.Empty<ArenaNavNode>();
        [SerializeField] ArenaNavLink[] links = System.Array.Empty<ArenaNavLink>();

        public bool HasData => nodes != null && nodes.Length > 0;

        public ArenaMapBake ToBake() => new ArenaMapBake { nodes = nodes, links = links };

        public void Store(ArenaNavNode[] n, ArenaNavLink[] l)
        {
            nodes = n; links = l;
            nodeCount = n.Length; linkCount = l.Length;
        }

        /// <summary>씬 이름 → Resources 경로. 씬마다 하나씩 굽는다.</summary>
        public static string ResourceName(string scene) => "PredictionGraph_" + scene;
    }
}
