using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 파츠 무리의 자세 스냅샷을 <b>여러 개</b> 담는 범용 애셋. (보스전_설계 §3의 방식을 일반화)
    ///
    /// <para><b>용도</b>: 스테이지 입구가 보스 팔에 봉쇄되며 찌그러지는 것, 6초를 놓쳤을 때
    /// 낑긴 곳이 부서지는 것 — 둘 다 "정해진 자세로 빠르게 가속해 간다"는 같은 물건이다.
    /// 물리 시뮬을 쓰지 않으므로(기초_설계안 §12) 결과가 항상 같고 타이밍이 프레임 단위로 고정된다.</para>
    ///
    /// <para><b>왜 애셋인가</b>: 자세를 만드는 곳(에디터에서 파츠를 손으로 잡는 작업)과 쓰는 곳
    /// (런타임 보간)을 분리한다. 파츠를 <b>경로로</b> 매칭하므로 프리팹이 나중에 바뀌어도 자세가
    /// 살아남고, 자세를 만들다 만 상태가 씬에 굳지 않는다.</para>
    ///
    /// <para><b>이름이 아니라 경로로 매칭하는 이유</b>: <see cref="BossHeadCrushPose"/>는 이름으로
    /// 찾는데, 그건 머리 판때기가 전부 <c>tripo_part_*</c>로 유일했기 때문이다. 입구 지오메트리는
    /// 같은 모듈 프리팹을 여러 번 붙여 만들므로 <b>같은 이름이 여러 개</b> 나온다 — 이름으로 찾으면
    /// 첫 번째만 맞고 나머지는 조용히 틀린다. 루트 기준 상대 경로는 항상 유일하다.</para>
    ///
    /// <para>첫 항목을 <b>홈(원래 자세)</b>으로 쓰는 규약이다. 안전망이자 보간의 시작점이다.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "MINDHEXER/파츠 자세 모음", fileName = "PartsPose")]
    public class PartsPose : ScriptableObject
    {
        [System.Serializable]
        public class PartPose
        {
            [Tooltip("루트 기준 상대 경로(예: \"Frame/Panel_03\").")]
            public string path;

            public Vector3 pos;
            public Quaternion rot = Quaternion.identity;
            public Vector3 scale = Vector3.one;
        }

        [System.Serializable]
        public class Snapshot
        {
            [Tooltip("이 자세의 이름. 코드에서 이 문자열로 찾는다(예: \"홈\", \"찌그러짐\", \"부서짐\").")]
            public string name = "홈";

            public List<PartPose> parts = new List<PartPose>();
        }

        [Tooltip("자세 목록. ★ 첫 항목이 홈(원래 자세)이라는 규약이다.")]
        public List<Snapshot> snapshots = new List<Snapshot>();

        /// <summary>홈 자세 — 규약상 첫 항목. 없으면 null.</summary>
        public Snapshot Home => (snapshots != null && snapshots.Count > 0) ? snapshots[0] : null;

        /// <summary>이름으로 자세를 찾는다. 없으면 null — 오타로 터지는 대신 로그가 남게 호출측에서 처리한다.</summary>
        public Snapshot Find(string n)
        {
            if (snapshots == null) return null;
            for (int i = 0; i < snapshots.Count; i++)
                if (snapshots[i] != null && snapshots[i].name == n) return snapshots[i];
            return null;
        }

        /// <summary>경로로 파츠 자세를 찾는다. 없으면 null — 파츠가 늘거나 줄어도 터지지 않는다.</summary>
        public static PartPose FindPart(Snapshot s, string path)
        {
            if (s == null || s.parts == null) return null;
            for (int i = 0; i < s.parts.Count; i++)
                if (s.parts[i] != null && s.parts[i].path == path) return s.parts[i];
            return null;
        }
    }
}
