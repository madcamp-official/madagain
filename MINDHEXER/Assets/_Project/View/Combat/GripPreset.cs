using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 기본 그립 프리셋 — "이 칼을 양손으로 어떻게 쥐는가"를 통째로 저장한다.
    /// 팔 포즈는 담지 않는다(그건 클립 몫이고, 왼팔은 IK가 매 프레임 계산한다).
    ///
    /// 담는 것:
    ///   1) 칼 ↔ 오른손 오프셋
    ///   2) 오른손 손가락 기준 포즈
    ///   3) Grip_L 의 칼 기준 위치·회전 (왼손이 자루 어디를 어떤 손목각으로 잡는가)
    ///   4) 왼손 손가락 기준 포즈
    ///
    /// 무기별로 여러 개 만들어 두고 갈아끼울 수 있다.
    /// </summary>
    [CreateAssetMenu(fileName = "GripPreset", menuName = "Precog/기본 그립 프리셋")]
    public class GripPreset : ScriptableObject
    {
        [Header("① 칼 ↔ 오른손")]
        public bool hasKatana;
        public Vector3    katanaLocalPos;
        public Quaternion katanaLocalRot = Quaternion.identity;
        public Vector3    katanaLocalScale = Vector3.one;

        [Header("③ 왼손 그립 지점 (칼 기준)")]
        public bool hasGripL;
        public Vector3    gripLLocalPos;
        public Quaternion gripLLocalRot = Quaternion.identity;

        [Header("②④ 손가락 기준 포즈 (5손가락 × 3마디)")]
        public Quaternion[] rightFingerRest;
        public Quaternion[] leftFingerRest;

        public bool HasRightFingers => rightFingerRest != null && rightFingerRest.Length > 0;
        public bool HasLeftFingers  => leftFingerRest  != null && leftFingerRest.Length  > 0;
    }
}
