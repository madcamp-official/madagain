using UnityEngine;

namespace Game.Sim
{
    /// <summary>4방향 대시 방향(카메라 기준). 시작 순간 고정.</summary>
    public enum DashDirection : byte { Forward = 0, Backward = 1, Left = 2, Right = 3 }

    /// <summary>한 틱분 플레이어 입력. 시뮬레이션이 아는 유일한 입력 형태.</summary>
    public struct InputCmd
    {
        public Vector2 move;   // (x=좌우, y=전후), -1..1
        public float   yaw;    // 바라보는 방향(도, 수평)
        public float   pitch;  // 시선 상하(도) — 런지 조준 레이·공중 타깃팅에 사용
        public bool    jump;
        public bool    dash;            // Shift — 4방향 대시(이동 전용)
        public DashDirection dashDirection;
        public bool    attack;          // 좌클릭 — 평타
        public bool    lunge;           // 우클릭 — 타깃 런지
        public int     lungeTargetId;   // -1=자동 선택. 예측 재생은 확정 id를 넣는다.

        public static InputCmd Empty => new InputCmd { lungeTargetId = -1 };
    }
}
