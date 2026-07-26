using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 모든 해킹 대상에 붙는 단일 마커. 동작 로직은 없고 "무엇을·어떻게 해킹하나"의 식별 정보만 담는다.
    /// 하이라이트 시스템·시선 Raycast·해킹 미니게임·입력 컨텍스트가 전부 이 컴포넌트만 보고 동작한다.
    ///
    /// 설계 접점(기초_설계안):
    ///  - controlType → 입력 컨텍스트 분기(§2.5) + 하이라이트 색(§7)
    ///  - PatternLineCount → 해킹 미니게임 난이도(§2.4)
    /// </summary>
    [DisallowMultipleComponent]
    public class Hackable : MonoBehaviour
    {
        [Tooltip("해킹 대상 종류(9종). 이 값에서 controlType·선 개수 기본이 정해진다.")]
        public HackableKind kind = HackableKind.RailCarrier;

        [Tooltip("결과 종류(색·연출·컨텍스트 분기). 보통 kind 기본값과 같다.")]
        public ControlType controlType = ControlType.ExternalControl;

        [Tooltip("점 패턴 선 개수 = 난이도. §2.4: 외부=5, 시점/보스=7. 0 이하면 kind 기본값 사용.")]
        public int patternLineCount = 0;

        [Tooltip("해킹 사거리 (placeholder, 튜닝 대상 §9).")]
        public float hackRange = 15f;

        [Tooltip("시선 Raycast용 콜라이더. 모델보다 넉넉하게, Hackable 레이어. 비우면 자식에서 탐색.")]
        public Collider gazeCollider;

        [Tooltip("하이라이트(초록/청록 발광) 대상 렌더러. EnemyGlow 기법 재사용 예정(§7).")]
        public Renderer[] glowRenderers;

        /// <summary>실제 사용할 선 개수. patternLineCount 미지정(0 이하) 시 kind 기본값.</summary>
        public int PatternLineCount
        {
            get { return patternLineCount > 0 ? patternLineCount : kind.DefaultPatternLineCount(); }
        }

        // 인스펙터에서 컴포넌트 추가/리셋 시 종류 기반 기본값 자동 채움 (편집 편의).
        void Reset()
        {
            controlType = kind.DefaultControlType();
            patternLineCount = kind.DefaultPatternLineCount();
            if (gazeCollider == null) gazeCollider = GetComponentInChildren<Collider>();
            if (glowRenderers == null || glowRenderers.Length == 0)
                glowRenderers = GetComponentsInChildren<Renderer>();
        }
    }
}
