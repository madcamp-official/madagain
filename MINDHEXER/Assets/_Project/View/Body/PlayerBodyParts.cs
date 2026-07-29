using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 플레이어 모델의 파츠를 <b>팔</b>과 <b>나머지</b>로 나눠 들고 있는 데이터.
    /// 설계: `docs/KJH/design/플레이어_신체표현_설계.md` §5.4
    ///
    /// <para><b>왜 필요한가</b>: 우리 Protag는 AI가 생성한 모델이라 메시가 70개 파츠로 쪼개져 있고
    /// 이름이 <c>tripo_part_21_037</c> 식이라 <b>이름으로는 어디 부위인지 알 수 없다</b>.
    /// 각 파츠의 본 가중치를 봐야 안다. 그 분류 결과를 여기 담아둔다.</para>
    ///
    /// <para><b>메시를 자르지 않는다.</b> 활성화만 바꾸므로 언제든 되돌릴 수 있고,
    /// 1인칭↔3인칭 전환이 그냥 SetActive 두 번이다.</para>
    ///
    /// 채우는 방법: <c>Tools/뷰모델/신체 파츠 분류</c> (BodyPartTaggerTool)
    /// </summary>
    public class PlayerBodyParts : MonoBehaviour
    {
        [Header("분류 결과 (BodyPartTaggerTool이 채운다)")]
        [Tooltip("팔·손 계열 파츠. 1인칭에서 유일하게 보이는 것들.")]
        public List<Renderer> armParts = new List<Renderer>();

        [Tooltip("몸통·다리·머리 등 나머지. 1인칭에서 끈다.")]
        public List<Renderer> bodyParts = new List<Renderer>();

        [Header("부착 지점")]
        [Tooltip("1인칭일 때 모델이 붙을 곳(카메라 하위). 비우면 Camera.main을 쓴다.")]
        public Transform viewmodelAnchor;

        [Tooltip("3인칭일 때 모델이 있을 곳(월드). 비우면 시작 위치를 기억해 쓴다.")]
        public Transform worldAnchor;

        [Header("뼈 참조 (거미·IK가 쓴다)")]
        [Tooltip("오른손 뼈. 비우면 이름으로 찾는다(R_Hand).")]
        public Transform rightHand;
        public Transform leftHand;

        public int TotalParts => armParts.Count + bodyParts.Count;

        void Reset() { AutoFindBones(); }

        /// <summary>손 뼈를 이름으로 찾는다. 우리 리그는 R_/L_ 접두사를 쓴다.</summary>
        public void AutoFindBones()
        {
            if (rightHand == null) rightHand = FindBone("R_Hand");
            if (leftHand  == null) leftHand  = FindBone("L_Hand");
        }

        public Transform FindBone(string boneName)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == boneName) return t;
            return null;
        }

        /// <summary>분류가 유효한가 — 둘 다 비어 있으면 아직 태깅을 안 한 것.</summary>
        public bool IsTagged => armParts.Count > 0 || bodyParts.Count > 0;
    }
}
