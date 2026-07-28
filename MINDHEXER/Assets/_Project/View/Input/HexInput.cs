using UnityEngine;

namespace Game.View
{
    /// <summary>입력 컨텍스트. 같은 물리 입력이 상황마다 뜻이 다르므로 항상 하나만 활성. (기초_설계안 §2.5)</summary>
    /// <remarks>
    /// 외부 조종 전용 컨텍스트는 없다 — §6.5 "1회 장악": 패턴 성공 후 대상은 파랑으로 남고
    /// 플레이어는 Player 컨텍스트 그대로 그 대상을 바라보며 조종한다("조종 종료" 개념 최소화).
    /// </remarks>
    public enum ControlContext
    {
        Player,          // 본체 평상시 (장악한 대상을 바라보면 그 대상 조종도 여기서)
        Hacking,         // 점 패턴 그리는 중 (시점 고정)
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

        // 해킹·복귀 (Player/ViewEntry 공통) — 전부 Space. raw 상태·엣지만 싣고,
        // 홀드(=본체 복귀) / 탭(=해킹·취소·조종해제) 판단은 HackDriver가 한다.
        // 이렇게 두면 VR 소스도 이 세 필드만 채우면 같은 판정을 그대로 얻는다.
        public bool    hackHeld;     // Space 눌림 상태(raw)
        public bool    hackPressed;  // Space 눌린 프레임(엣지)
        public bool    hackReleased; // Space 뗀 프레임(엣지)
        public Vector2 strokeDir;    // 해킹 중 마우스 방향 (K4 워크 입력, §2.4)

        // 외부 조종 2축 (§2.5) — 장악(파랑)한 대상을 바라보는 동안 유효
        public float    axisH;       // 1순위 축: 좌클릭(-) / 우클릭(+)
        public float    axisV;       // 2순위 축: Shift+좌클릭(+) / Shift+우클릭(-)
        public FlickDir flick;       // 더블클릭 = 플릭

        // 시점 진입
        public bool primary;         // LMB 눌림(엣지) — 발사/쥐기
        public bool primaryHeld;     // LMB 유지 — 연사

        public static HexInput Empty
        {
            get { return new HexInput { context = ControlContext.Player, flick = FlickDir.None }; }
        }
    }
}
