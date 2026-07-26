using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>포즈 스냅샷 한 칸. 뼈·칼·그립·씬뷰 카메라를 통째로 담는다.</summary>
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

        [Header("칼")]
        public bool       hasKatana;
        public Vector3    katanaPos;
        public Quaternion katanaRot = Quaternion.identity;
        public Vector3    katanaScale = Vector3.one;

        [Header("그립")]
        public bool hasGripR; public Vector3 gripRPos; public Quaternion gripRRot = Quaternion.identity;
        public bool hasGripL; public Vector3 gripLPos; public Quaternion gripLRot = Quaternion.identity;

        [Header("IK / 손가락")]
        public bool  hasIk;
        public float ikWeightR, ikWeightL;
        public float fingerGripR, fingerGripL;
    }

    /// <summary>
    /// 포즈 스냅샷 모음. 키포즈(idle·윈드업·타격·후딜 등)를 이름 붙여 저장해 두고
    /// 불러가며 클립에 찍는 용도. 에셋이라 Unity를 껐다 켜도 유지된다.
    ///
    /// ★ 뼈를 이름 경로로 저장하므로, 뼈를 지우거나 이름을 바꾸면 그 슬롯은 못 불러온다.
    ///   안 쓰는 뼈 정리는 스냅샷을 만들기 전에 끝내둘 것.
    /// </summary>
    [CreateAssetMenu(fileName = "PoseSnapshot", menuName = "Precog/포즈 스냅샷")]
    public class PoseSnapshot : ScriptableObject
    {
        public List<PoseSlot> slots = new List<PoseSlot>();
    }
}
