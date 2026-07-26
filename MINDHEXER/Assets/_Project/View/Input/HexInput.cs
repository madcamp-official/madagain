using UnityEngine;

namespace Game.View
{
    /// <summary>입력 컨텍스트. 같은 물리 입력이 상황마다 뜻이 다르므로 항상 하나만 활성. (기초_설계안 §2.5)</summary>
    public enum ControlContext
    {
        Player,          // 본체 평상시
        Hacking,         // 점 패턴 그리는 중 (시점 고정)
        ExternalControl, // 외부 조종 (버튼+스크롤 3축, 마우스=시점 자유)
        ViewEntry,       // 시점 진입(빙의)
    }

    /// <summary>순간 플릭 방향(빠른 이동). (§2.5 아날로그 vs 플릭)</summary>
    public enum FlickDir { None, Left, Right, Up, Down, DepthNear, DepthFar }

    /// <summary>
    /// 해킹·조종·빙의 전용 입력 채널(디바이스 무관 스냅샷). 이동/시점은 기존 InputCmd(Sim)가 담당.
    /// PC=HexInputReader / VR=UDP 패킷이 같은 구조를 채운다 → 게임플레이는 입력 소스에 무관.
    /// (기초_설계안 §2.5 / 이식_환경 §9 InputCmd 값-스냅샷)
    /// </summary>
    public struct HexInput
    {
        public ControlContext context;

        // 해킹 (Player/ViewEntry 공통)
        public bool    hackHeld;     // Space 홀드 — 해킹 시도·유지 / 릴레이
        public Vector2 strokeDir;    // 해킹 중 마우스 방향 (K4 워크 입력, §2.4)

        // 외부 조종 3축 (§2.5)
        public float    axisH;       // 좌클릭(-) / 우클릭(+)
        public float    axisV;       // Shift+좌클릭(+) / Shift+우클릭(-)
        public float    axisDepth;   // 스크롤 위(+앞) / 아래(-뒤)
        public FlickDir flick;       // 더블클릭 · 빠른 스크롤

        // 시점 진입
        public bool primary;         // LMB 눌림(엣지) — 발사/쥐기
        public bool primaryHeld;     // LMB 유지 — 연사
        public bool returnToBody;    // Q — 복귀·해제

        public static HexInput Empty
        {
            get { return new HexInput { context = ControlContext.Player, flick = FlickDir.None }; }
        }
    }
}
