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

        void Awake() { FixSkinnedBounds(); }

        /// <summary>
        /// 스킨드 메시의 바운드를 <b>매 프레임 실제 정점에서 다시 계산</b>하게 만든다.
        ///
        /// <para><b>왜 필요한가</b> — 유니티는 스킨드 메시의 컬링 바운드를 임포트 시점의 자세로
        /// 한 번 굽고, 뼈가 그 범위를 벗어나도 갱신하지 않는다. 1인칭 뷰모델은 IK가 손을 원래
        /// 자세에서 <b>수십 cm 바깥으로</b> 끌고 가므로 바운드가 전혀 다른 곳에 남는다.
        /// 그러면 팔이 화면 정중앙에 있어도 <c>isVisible=false</c>로 <b>통째로 컬링되어 안 보인다.</b></para>
        ///
        /// <para>실제로 겪었다: <c>R_Hand</c>는 뷰포트 (0.76, 0.10)에 있어 손목에 앉은 거미는 보이는데,
        /// 팔 파츠 11개의 바운드는 뷰포트 −54까지 흩어져 전부 컬링됐다. <b>거미만 보이고 팔은 안
        /// 보이는</b> 기묘한 증상의 원인이 이것이다.</para>
        ///
        /// <para>대가는 파츠당 바운드 재계산 비용이다. 뷰모델은 파츠가 10여 개뿐이라 무시할 만하고,
        /// 안 보이는 것보다 낫다.</para>
        /// </summary>
        public void FixSkinnedBounds()
        {
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                r.updateWhenOffscreen = true;
        }

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
