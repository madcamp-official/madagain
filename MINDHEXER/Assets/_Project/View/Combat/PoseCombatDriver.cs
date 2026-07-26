using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 실제 전투(sim)에 포즈 시퀀스를 연동한다.
    /// 마우스를 직접 읽지 않고 <b>sim의 공격 상태 전이</b>를 보므로,
    /// 쿨다운·선입력·히트판정 등 게임 규칙을 그대로 따른다(= 일반 플레이에서도 동작).
    ///
    ///   공격 시작(PhNone → PhWindup)  → slash1 / slash2 번갈아
    ///   런지 시작(LgNone → 그 외)      → thrust1  (윈드업 0틱이라 LgTravel로 직행)
    ///   글로리                         → glory1 (없으면 무시)
    ///
    /// 애니메이션끼리는 항상 순간이동(각 공격이 새 시퀀스를 처음부터 재생).
    /// </summary>
    public class PoseCombatDriver : MonoBehaviour
    {
        public static PoseCombatDriver Instance { get; private set; }

        [Tooltip("실제 전투에 포즈를 연동한다")]
        public bool active = true;

        [Tooltip("좌클릭(평타)에서 번갈아 쓸 접두어")]
        public string[] attackPrefixes = { "slash1_", "slash2_" };
        [Tooltip("우클릭(찌르기) 접두어")]
        public string   lungePrefix = "thrust1_";
        [Tooltip("글로리킬 접두어 (해당 포즈가 없으면 무시)")]
        public string   gloryPrefix = "glory1_";

        [Tooltip("포즈당 시간(초) — 프로파일이 저장돼 있으면 그쪽이 우선")]
        public float segTime = 0.15f;

        [Tooltip("명중 순간 피격 버스트를 띄운다 (평타1·2·찌르기 공통)")]
        public bool hitBurst = true;
        [Tooltip("피격 지점을 못 찾을 때 카메라 앞 이 거리에 띄운다(m)")]
        public float fallbackHitDist = 2.4f;

        // 콤보 단계는 sim(PlayerCombatState.attackStep)이 정한다 — 뷰는 읽기만 한다.
        byte prevAttack, prevLunge, prevGlory;
        bool prevHitDone;             // 평타 명중 전이 감지
        bool primed;                  // 첫 프레임의 상태를 기준으로 잡아 시작하자마자 터지지 않게

        void Awake() { Instance = this; }

        /// <summary>콤보를 처음(slash1)으로.</summary>
        public void ResetCombo() { }   // sim이 콤보를 소유하므로 뷰에서 할 일 없음

        void Update()
        {
            if (!active) return;
            var main = Main.Instance;
            if (main == null) return;

            ref readonly PlayerSim p = ref main.World.player;
            byte atk = p.combat.attackPhase;
            byte lg  = p.combat.lungePhase;
            byte gl  = p.combat.gloryPhase;

            if (!primed) { prevAttack = atk; prevLunge = lg; prevGlory = gl; primed = true; return; }

            // F3에서 시퀀스를 튜닝하는 중에 클릭으로 slash가 끼어들면 작업이 날아간다.
            // 단 PosePlayer를 직접 만지는 패널일 때만 막는다 — F6(콤보)은 재생돼야 한다.
            if (DevPanels.BlocksPoseDriver) { prevAttack = atk; prevLunge = lg; prevGlory = gl; return; }

            // 글로리 > 런지 > 평타 순으로 우선
            if (gl != prevGlory)
            {
                if (prevGlory == CombatConfig.GlNone && gl != CombatConfig.GlNone) PlayPrefix(gloryPrefix, true);
                prevGlory = gl;
            }
            if (lg != prevLunge)
            {
                // ★ 찌르기는 윈드업이 0틱이라 LgNone → LgTravel 로 바로 간다.
                //   LgWindup 을 기다리면 영영 발동하지 않는다(그래서 우클릭이 먹통이었다).
                if (prevLunge == CombatConfig.LgNone && lg != CombatConfig.LgNone)
                { PlayPrefix(lungePrefix, false); Fx("찌르기"); }   // 찌르기는 선딜이 없다
                prevLunge = lg;
            }
            if (atk != prevAttack)
            {
                // ★ 번갈아 재생하지 않는다 — sim의 콤보 단계를 그대로 읽는다.
                //   콤보가 끊기면 sim이 0으로 되돌리므로 자동으로 평타1로 돌아온다.
                if (prevAttack == CombatConfig.PhNone && atk == CombatConfig.PhWindup)
                    PlayAttack(p.combat.attackStep);
                prevAttack = atk;
            }

            // ── 명중 순간 피격 버스트 (평타1·2·찌르기 공통) ──
            bool hit = p.combat.attackHitDone;
            // 즉발이면 판정이 0틱에 끝나므로 버스트도 선딜만큼 늦춰야 칼과 맞는다.
            if (hitBurst && hit && !prevHitDone)
                Burst(main, p.combat.lungeTargetId, WindupDelay(p.combat.attackStep));
            prevHitDone = hit;
        }

        /// <summary>피격 지점에서 방사형 참격. 대상 적이 있으면 그 몸통 높이에, 없으면 카메라 앞에.</summary>
        void Burst(Main main, int targetId, float delay = 0f)
        {
            var fx = SlashFxDriver.Instance;
            if (fx == null) return;
            if (delay < 0f) delay = 0f;

            Vector3 pos;
            if (!TryEnemyPos(main, targetId, out pos))
            {
                var cam = main.Cam != null ? main.Cam : Camera.main;
                if (cam == null) return;
                pos = cam.transform.position + cam.transform.forward * fallbackHitDist;
            }
            fx.BurstAt(pos, null, delay);
        }

        /// <summary>id로 적을 찾아 가슴 높이 좌표를 돌려준다.</summary>
        static bool TryEnemyPos(Main main, int id, out Vector3 pos)
        {
            pos = Vector3.zero;
            if (id < 0) return false;
            ref readonly SimWorld w = ref main.World;
            for (int i = 0; i < w.enemyCount; i++)
                if (w.enemies[i].id == id && w.enemies[i].alive)
                {
                    pos = w.enemies[i].pos + Vector3.up * (w.enemies[i].height * 0.55f);
                    return true;
                }
            return false;
        }

        [Tooltip("즉발 판정일 때 이펙트를 선딜만큼 늦춰 낸다 — 판정은 이미 0틱에 끝났지만 연출은 칼이 나갈 때")]
        public bool fxAfterWindup = true;

        /// <summary>sim이 정한 콤보 단계(0=평타1, 1=평타2)로 재생.</summary>
        void PlayAttack(byte step)
        {
            if (attackPrefixes == null || attackPrefixes.Length == 0) return;
            int idx = Mathf.Clamp(step, 0, attackPrefixes.Length - 1);
            PlayPrefix(attackPrefixes[idx], false);
            Fx(idx == 0 ? "평타1" : "평타2", WindupDelay(step));
        }

        /// <summary>선딜(초). 즉발 판정에서 "판정은 즉시, 연출은 칼이 나갈 때"를 만든다.</summary>
        static float WindupDelay(byte step) =>
            CombatConfig.AttackInstantJudge
                ? CombatConfig.AtkWindup(step) * SimConfig.TickDelta
                : -1f;   // 기존 방식이면 슬롯에 설정된 지연을 그대로 사용

        /// <summary>베기 이펙트 발동(있을 때만).</summary>
        static void Fx(string slot, float delay = -1f)
        {
            var fx = SlashFxDriver.Instance;
            if (fx != null) fx.Fire(slot, delay);
        }

        /// <summary>해당 접두어 시퀀스를 처음부터 재생. quiet=true면 포즈가 없어도 조용히 넘어간다.</summary>
        void PlayPrefix(string prefix, bool quiet)
        {
            var pp = PosePlayer.Instance;
            if (pp == null || string.IsNullOrEmpty(prefix)) return;
            int n = pp.Play(prefix, segTime, false);
            if (n < 2 && !quiet)
                Debug.LogWarning($"[PoseCombatDriver] '{prefix}*' 포즈가 2개 미만입니다.");
        }
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class PoseCombatDriverBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<PoseCombatDriver>() == null)
                new GameObject("[PoseCombatDriver]").AddComponent<PoseCombatDriver>();
        }
    }
}
