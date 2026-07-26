using UnityEngine;

namespace Game.Sim
{
    /// <summary>플레이어 논리 상태. pos = 발밑(캡슐 바닥 중심).</summary>
    public struct PlayerSim
    {
        public Vector3 pos;
        public Vector3 vel;
        public float   yaw;
        public float   aimPitch;   // 조준 상하각(cmd.pitch 반영) — 런지 타깃 레이용
        public bool    grounded;

        /// <summary>
        /// 마지막으로 접지했던 위치. ★ 적 추적 목표로 쓴다.
        /// 공중에 뜬 좌표를 그대로 목표로 삼으면 NavMesh 샘플링(반경 4m)이 실패해
        /// 경로가 없다고 판정되고 몹이 <b>완전히 정지</b>하는 버그가 있었다.
        /// 이 값은 정의상 접지 가능한 지점이라 그런 실패가 없고, 레이캐스트도 필요 없어
        /// 예측 포크(≈337k회)에서 질의 비용이 0이다.
        /// </summary>
        public Vector3 lastGroundedPos;

        public int     jumpCount;
        public int     jumpBufferTicks;   // 착지 직전 점프 선입력 버퍼
        public int     jumpBoostTicks;    // 2단 점프 수평 임펄스 남은 틱
        public Vector3 jumpBoostDir;      // 임펄스 방향(발동 순간 고정)

        // 4방향 대시 (임펄스: 초기 속도 후 드래그 감쇠. 방향은 시작 순간 고정)
        public int     dashTicks;      // >0이면 대시 중
        public Vector3 dashDir;        // 수평 방향(고정)
        public float   dashSpeed;      // 현재 대시 속도(매 틱 드래그로 감쇠)
        public int     dashCharges;
        public int     dashRecharge;   // 다음 충전까지 남은 틱
        public int     dashBufferTicks; // 대시 막판 예약(>0이면 현재 대시 끝나는 즉시 다음 대시)

        public PlayerCombatState combat;   // ← combat 세션 소유 (필드는 그쪽 파일에서 늘림)

        public static PlayerSim Spawn(Vector3 at) => new PlayerSim
        {
            pos = at,
            lastGroundedPos = at,
            grounded = true,
            dashCharges = SimConfig.DashMaxCharges,
            combat = PlayerCombatState.Initial,
        };
    }
}
