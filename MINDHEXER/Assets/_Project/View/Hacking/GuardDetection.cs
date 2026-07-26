using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 경비병 감지 범위 값(이음새). 비주얼(B4 위험 범위)이 `DetectRadius`를 읽어 빨강 구+3D 도트를 그린다.
    /// 실제 감지 판정 로직은 별도(gameplay) — 이 컴포넌트는 값 노출·시각화용 계약만. (계획 v4 §1·B4)
    /// </summary>
    public class GuardDetection : MonoBehaviour
    {
        [Tooltip("감지 반경 (m, 튜닝 §9). 빨강 구·위험 범위의 크기.")]
        public float DetectRadius = 6f;

        [Tooltip("현재 이 경비병이 활성 위협인지(비주얼 on/off). 나중에 gameplay가 세팅.")]
        [System.NonSerialized] public bool Active = true;
    }
}
