using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 보스 머리 찌그러짐 자세 3개를 담는 애셋. (보스전_설계 §3)
    ///
    /// <para><b>왜 애셋인가</b>: 자세를 만드는 곳(에디터에서 판때기를 직접 잡는 작업)과 쓰는 곳
    /// (런타임 단계 보간)을 분리한다. 파츠를 <b>이름으로</b> 매칭하므로 프리팹이 나중에 바뀌어도
    /// 자세가 살아남고, 게임 씬에서 판때기를 만지지 않아도 된다.</para>
    ///
    /// <para><b>자세 3개</b>
    /// <list type="bullet">
    /// <item><see cref="home"/> — 변환 직후의 원래 자세. 안전망이다. 판때기를 만진 채 프리팹이
    ///       저장되는 사고가 나도 이걸로 되돌린다.</item>
    /// <item><see cref="crush"/> — <b>최종 찌그러짐</b>. 스테이지 단계 보간의 종점.</item>
    /// <item><see cref="flat"/> — <b>완전히 납작</b>. 마지막 스테이지에서 crush 도달 후
    ///       약간의 텀을 두고 여기로 가며 사망한다.</item>
    /// </list></para>
    /// </summary>
    [CreateAssetMenu(menuName = "MINDHEXER/보스 머리 찌그러짐 자세", fileName = "BossHeadCrushPose")]
    public class BossHeadCrushPose : ScriptableObject
    {
        [System.Serializable]
        public class PartPose
        {
            public string name;
            public Vector3 pos;
            public Quaternion rot = Quaternion.identity;
            public Vector3 scale = Vector3.one;
        }

        [Tooltip("변환 직후 원래 자세(안전망). 이걸 기준으로 보간한다.")]
        public List<PartPose> home = new List<PartPose>();

        [Tooltip("최종 찌그러짐 — 단계 보간의 종점.")]
        public List<PartPose> crush = new List<PartPose>();

        [Tooltip("완전히 납작 — 사망 연출 전용.")]
        public List<PartPose> flat = new List<PartPose>();

        /// <summary>이름으로 찾는다. 없으면 null — 파츠가 늘거나 줄어도 터지지 않는다.</summary>
        public static PartPose Find(List<PartPose> list, string n)
        {
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].name == n) return list[i];
            return null;
        }
    }
}
