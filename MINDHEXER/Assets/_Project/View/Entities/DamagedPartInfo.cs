using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 파손 변형 프리팹에 붙는 표식. 에디터 툴(Tools/몹/파손 변형 생성)이 심는다.
    /// 런타임은 이걸 읽어 어느 본에 전선을 걸고 어떤 조각을 매달지 안다.
    /// </summary>
    public class DamagedPartInfo : MonoBehaviour
    {
        [Tooltip("전선이 붙을 본 이름(부분 일치) — 떨어져 나간 부위의 뿌리")]
        public string socketBoneName;

        [Tooltip("전선 끝에 매달릴 조각 메시(몸에서 걷어낸 삼각형)")]
        public Mesh partMesh;

        [Tooltip("이 변형에서 떨어진 부위 전체(로그·디버그용)")]
        public string[] allParts;
    }
}
