using UnityEngine;

namespace Game.Sim
{
    /// <summary>
    /// 플레이어 전투 상태. ★ combat 소유. PlayerSim이 품기만 하므로 여기 필드 늘려도
    /// 공유 파일 안 건드림. 해시는 CombatHash.MixPlayer 담당.
    /// </summary>
    public struct PlayerCombatState
    {
        // 평타 (좌클릭)
        public byte attackPhase;        // 0 None / 1 Windup / 2 Active / 3 Recovery
        public int  attackPhaseTicks;
        public bool attackHitDone;

        // ── 2연타 콤보 ──
        // comboStep은 "지금 진행 중인/다음에 나갈" 단계다. 0=평타1, 1=평타2.
        // 평타1이 끝나면 comboStep=1 + comboWindow를 열고, 창이 만료되면 0으로 되돌린다.
        // 평타2가 끝나면 창을 열지 않는다 — 그래야 후딜이 실제로 체감된다.
        public byte attackStep;         // 진행 중인 공격이 몇 단계인가 (0=평타1, 1=평타2)
        public byte comboStep;          // 다음 좌클릭이 낼 단계 (0=평타1, 1=평타2)
        public int  comboWindow;        // >0이면 평타2 유효. 매 틱 감소
        public bool attackBuffered;     // 선입력 — 공격 중 누른 클릭을 기억했다가 끝나면 발동

        // 구 방식은 활성 틱마다 판정하므로 "이 스윙에서 이미 때린 적"을 기억해야 한다.
        // MaxEnemies=128 → ulong 2개로 비트마스크. 스윙 시작 시 0으로 초기화.
        public ulong attackHitMask0;    // 적 인덱스 0~63
        public ulong attackHitMask1;    // 적 인덱스 64~127

        /// <summary>공격 시작(윈드업 진입)부터의 경과 틱. 즉발 판정창을 재는 기준.
        /// attackPhaseTicks는 페이즈마다 0으로 리셋되므로 별도로 센다.</summary>
        public int attackElapsed;

        // 체력·피격 (적용은 CombatResolve가)
        public int  hp;
        public int  hitStunTicks;       // >0이면 피격 경직(수평 조작 제한)
        public int  invulnTicks;        // >0이면 피격 무적(들어오는 히트 무시). 매 틱 감소. 빔 등 연속피해를 조절.

        // 타깃 런지 (우클릭) — 시작 순간 targetId·도착점·Travel 틱 고정, 재추적 없음
        public byte    lungePhase;      // LgNone/Windup/Travel/Recovery
        public int     lungeTicks;
        public int     lungeTargetId;   // 대상 적 id (-1=없음)
        public Vector3 lungeStart;
        public Vector3 lungeDest;       // 적 앞 지점(고정)
        public int     lungeTravelTicks; // 거리 비례 Travel 틱(시작 시 계산·고정)
        public bool    lungeHitDone;    // 임팩트 1회 처리 플래그
        public int     lungeCooldown;   // 남은 쿨타임 틱(0.25초 연발 제한)
        public int     lungeStacks;     // 런지 자원(처치로 충전, 발동 1 소모). 시작 2
        public int     lungeBufferTicks; // 쿨 막판 예약(>0이면 쿨 끝나는 즉시 발동)

        // 대형몹 글로리킬 처형 (컷신). 진행 중 무적·조작잠금.
        public byte    gloryPhase;      // GlNone/GlSlash1/GlSlash2/GlDash
        public int     gloryTicks;
        public int     gloryTargetId;   // 처형 대상 적 인덱스
        public Vector3 gloryDir;        // 피니시 러쉬 방향(고정)

        public static PlayerCombatState Initial => new PlayerCombatState
        {
            hp = CombatConfig.PlayerMaxHp,
            lungeTargetId = -1,
            lungeStacks = 2,     // 시작 시 스택 꽉 채움
        };
    }
}
