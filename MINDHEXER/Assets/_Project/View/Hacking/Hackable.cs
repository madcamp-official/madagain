using System.Collections.Generic;
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

        [Tooltip("사거리를 무시하고 <b>어디서든</b> 해킹된다. 거대 유압프레스 전용(§0.4 보스 파트).\n" +
                 "★ 조준은 그대로다 — 레이가 닿아야 한다. 거리 판정만 빠진다.\n" +
                 "HackableGlitchManager의 전역 사거리 덮어쓰기(hackRangeOverride)도 우회한다.")]
        public bool ignoreRange = false;

        /// <summary>
        /// 이 거리에서 해킹이 가능한가. <b>사거리 판정은 전부 여기를 지난다</b> —
        /// 판정이 세 곳(조준·치지직·손가락)에 흩어져 있어서 한 곳만 고치면 나머지가 어긋난다.
        /// </summary>
        public bool WithinHackRange(float distance)
            => ignoreRange || distance <= Mathf.Max(0.01f, hackRange);

        [Tooltip("시선 Raycast용 콜라이더. 모델보다 넉넉하게, Hackable 레이어. 비우면 자식에서 탐색.")]
        public Collider gazeCollider;

        [Tooltip("하이라이트(초록/청록 발광) 대상 렌더러. EnemyGlow 기법 재사용 예정(§7).")]
        public Renderer[] glowRenderers;

        // === 이음새(런타임 상태) — gameplay가 매 프레임 쓰고, 비주얼(환경 하이라이트)이 읽는다. 단방향. ===
        // 저장 안 함(런타임 전용). 로직 없이 비주얼만 개발할 땐 HackableStateMock이 이 값들을 수동 세팅.
        // (기초_설계안 §7 시각 언어 / ADR 계획 v4 이음새 계약)
        [System.NonSerialized] public float DistanceToPlayer;                        // 테두리 두께
        [System.NonSerialized] public bool  InRange;                                 // 사거리 안 = 글리치 on
        [System.NonSerialized] public bool  IsGazed;                                 // 중앙 레티클 조준 = 격화
        [System.NonSerialized] public CaptureState captureState = CaptureState.None; // 초록→파랑(장악) 전환

        // 한 번이라도 해킹 성공한 적 있는지 — captureState와 달리 조종 해제·재조준으로 리셋되지 않고
        // 영구히 유지된다. 재해킹 시 패턴 생략(즉시 성공) 판정 + 하늘색 영구 표시의 근거(전체 해킹 규칙).
        [System.NonSerialized] public bool everHacked;

        // 이 인스턴스의 고정 점 패턴 — 처음 해킹 시 1회 생성해 캐시. 재해킹해도 같은 패턴. (§2.4)
        [System.NonSerialized] public DotPattern pattern;

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

        /// <summary>씬에 살아 있는 모든 Hackable — <see cref="ClimbLedge.All"/>과 같은 패턴(순회용 등록,
        /// 동작 로직 아님). 환경 하이라이트(치지직 등)가 매 프레임 이 목록을 훑는다.</summary>
        public static readonly List<Hackable> All = new List<Hackable>();

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }
    }
}
