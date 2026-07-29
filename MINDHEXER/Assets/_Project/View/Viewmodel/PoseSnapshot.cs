using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>포즈 스냅샷 한 칸. 뼈·씬뷰 카메라를 통째로 담는다. (Precog에서 포팅)
    /// 무기(칼/그립) 필드는 우리 게임엔 해당 없어 항상 비어있지만, 스키마 호환을 위해 남겨둔다 —
    /// 나중에 손에 뭔가(도구 등) 쥐는 기능이 생기면 그대로 쓸 수 있다.</summary>
    [System.Serializable]
    public class PoseSlot
    {
        public string name = "새 포즈";

        [Header("뼈 (뷰모델 루트 기준 경로 → localRotation)")]
        public string[]     bonePaths;
        public Quaternion[] boneRots;

        [Header("씬 뷰 카메라")]
        public bool       hasCam;
        public Vector3    camPivot;
        public Quaternion camRot;
        public float      camSize = 2f;

        [Header("쥔 물체 (해당 없으면 항상 false)")]
        public bool       hasKatana;
        public Vector3    katanaPos;
        public Quaternion katanaRot = Quaternion.identity;
        public Vector3    katanaScale = Vector3.one;

        public bool hasGripR; public Vector3 gripRPos; public Quaternion gripRRot = Quaternion.identity;
        public bool hasGripL; public Vector3 gripLPos; public Quaternion gripLRot = Quaternion.identity;

        [Header("IK / 손가락")]
        public bool  hasIk;
        public float ikWeightR, ikWeightL;
        public float fingerGripR, fingerGripL;
    }

    /// <summary>
    /// 포즈 스냅샷 모음. 키포즈(idle·조준·상호작용 등)를 이름 붙여 저장해 두고 불러가며 클립에 찍는 용도.
    /// ★ 뼈를 이름 경로로 저장하므로, 뼈를 지우거나 이름을 바꾸면 그 슬롯은 못 불러온다.
    /// </summary>
    [CreateAssetMenu(fileName = "PoseSnapshot", menuName = "MINDHEXER/포즈 스냅샷")]
    public class PoseSnapshot : ScriptableObject
    {
        public List<PoseSlot> slots = new List<PoseSlot>();
    }
}
