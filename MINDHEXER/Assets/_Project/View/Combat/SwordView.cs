using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 1인칭 칼 뷰모델. ★ combat 소유·독립 — Main/EntityViews 안 건드림. 읽기 전용(SimWorld).
    ///
    /// 구조(하이브리드): 스윙 = Animation 창에서 authoring한 클립,
    ///   재생 시점은 Sim phase가 몬다(틱 동기). 그 위에 절차 레이어(착지·숨·피격)를 additive.
    ///   (이펙트는 별도 — 현재 없음)
    ///
    /// 계층: KatanaViewmodel(root, 절차 오프셋) > PoseTarget(클립) > Katana(메시) > Tip/Root(궤적 앵커)
    /// </summary>
    public class SwordView : MonoBehaviour
    {
        const string ViewmodelPrefab = "KatanaViewmodel";

        public static SwordView Instance { get; private set; }   // 콘솔 프리뷰 진입점

        Transform vmRoot;
        Animator  anim;
        Camera    cam;

        // 클립 state 해시(state 이름 = 클립 이름)
        static readonly int HIdle   = Animator.StringToHash("Katana_Idle");
        static readonly int HSlash1 = Animator.StringToHash("Katana_Slash1");
        static readonly int HSlash2 = Animator.StringToHash("Katana_Slash2");
        static readonly int HThrust = Animator.StringToHash("Katana_Thrust");

        int   prevHash;
        float prevNt;

        // ── 콘솔 프리뷰(전투 없이 스윙 재생. Sim 안 건드림 → 결정론 무관) ──
        bool  previewOn, previewLoop;
        int   previewHash;
        float previewT;
        public float previewDuration = 0.5f;

        // 사운드 전이 감지
        byte prevAttack; bool prevDash; byte prevLunge;
        int prevLungeStacks; bool lungeStacksInit; float lungeSoundCooldownUntil;   // 찌르기음 이중 감지
        int prevJumpCount; bool prevGrounded = true;   // 점프·착지 사운드 감지용
        float airborneTime;                            // 연속 공중 시간(계단 깜빡임 무시용)
        const float MinAirborneForLand = 0.30f;        // 이만큼 떠 있었어야 착지음 발생

        // ── 절차 레이어는 ViewmodelMotion이 전담한다(루트의 유일한 작성자).
        //    아래는 기존 콘솔 명령 호환용 위임.
        public void KickLand(float impact) { var m = ViewmodelMotion.Instance; if (m != null) m.KickLand(impact); }
        public void KickHit()              { var m = ViewmodelMotion.Instance; if (m != null) m.KickHit(); }
        public void ResetProcedural()      { var m = ViewmodelMotion.Instance; if (m != null) m.ResetAll(); }

        /// <summary>숨 연출 HP 강제(테스트). 음수면 실제 HP 사용.</summary>
        public float breatheHpOverride
        {
            get { var m = ViewmodelMotion.Instance; return m != null ? m.hpOverride : -1f; }
            set { var m = ViewmodelMotion.Instance; if (m != null) m.hpOverride = value; }
        }

        void Awake() { Instance = this; }

        void Update()
        {
            var main = Main.Instance;
            if (main == null) return;

            if (vmRoot == null)
            {
                cam = main.Cam;
                if (cam == null) return;
                if (!BuildViewmodel()) return;
            }

            ref readonly PlayerSim p = ref main.World.player;
            DetectAndSound(in p);   // 소리·전이 감지는 포즈 구동 중에도 계속 돈다

            // 포즈 시스템이 자세를 쥐고 있으면 Animator 구동만 건너뛴다.
            var pp = PosePlayer.Instance;
            if (pp != null && pp.IsDriving) return;

            if (previewOn) DrivePreview();
            else           DriveFromSim(in p.combat);
            // 루트 오프셋(절차 레이어)은 ViewmodelMotion이 LateUpdate에서 처리한다.
        }

        // ── 뷰모델 준비 ──
        bool BuildViewmodel()
        {
            GameObject go = FindExistingViewmodel();
            if (go == null)
            {
                var prefab = Resources.Load<GameObject>(ViewmodelPrefab);
                if (prefab == null) { Debug.LogError("[SwordView] KatanaViewmodel 프리팹 없음"); enabled = false; return false; }
                go = Object.Instantiate(prefab);
                go.name = ViewmodelPrefab;
            }

            vmRoot = go.transform;
            if (vmRoot.parent != cam.transform) vmRoot.SetParent(cam.transform, false);
            // 루트 위치는 리셋하지 않는다 — 씬에서 잡아둔 배치가 기준이고,
            // ViewmodelMotion이 그 위에 절차 오프셋을 얹는다.

            anim = go.GetComponent<Animator>();
            if (anim != null) anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            return true;
        }

        /// <summary>뷰모델을 버리고 Resources 프리팹에서 다시 만든다(콘솔 vm).
        /// 실제 생성은 다음 Update의 BuildViewmodel이 한다.</summary>
        public bool RebuildViewmodel()
        {
            var main = Main.Instance;
            if (main == null) return false;
            cam = main.Cam;
            if (cam == null) return false;

            var old = FindExistingViewmodel();
            if (old != null)
            {
                old.name = "~KatanaViewmodel_old";   // 같은 프레임에 다시 안 잡히게 개명 후 파괴
                Destroy(old);
            }
            vmRoot = null;
            anim = null;

            // 다른 시스템이 들고 있던 참조도 비운다
            var pp = PosePlayer.Instance;      if (pp != null) pp.ForgetRoot();
            var vc = ViewmodelCamera.Instance; if (vc != null) vc.ForgetRoot();
            var kc = KatanaClipper.Instance;   if (kc != null) kc.Forget();
            return true;
        }

        GameObject FindExistingViewmodel()
        {
            Transform t = cam.transform.Find(ViewmodelPrefab);
            if (t != null) return t.gameObject;
            return GameObject.Find(ViewmodelPrefab);
        }

        // ── Sim 구동 ──
        void DriveFromSim(in PlayerCombatState c)
        {
            int hash; float nt;
            if (c.gloryPhase != CombatConfig.GlNone)        { hash = HSlash1; nt = 0.5f; }
            else if (c.lungePhase != CombatConfig.LgNone)   { hash = HThrust; nt = LungeNt(in c); }
            else if (c.attackPhase != CombatConfig.PhNone)  { hash = HSlash1; nt = AttackNt(in c); }   // TODO 콤보로 Slash1/2 교대
            else                                           { hash = HIdle;   nt = 0f; }
            Sample(hash, nt);
        }

        void DrivePreview()
        {
            previewT += Time.deltaTime / Mathf.Max(0.01f, previewDuration);
            if (previewT >= 1f)
            {
                if (previewLoop) previewT = 0f;
                else { previewOn = false; Sample(HIdle, 0f); return; }
            }
            Sample(previewHash, Mathf.Clamp01(previewT));
        }

        /// <summary>클립을 nt(0~1) 시점으로 수동 샘플 — 틱 동기 유지.</summary>
        void Sample(int hash, float nt)
        {
            if (anim == null) return;
            anim.Play(hash, 0, nt);
            anim.Update(0f);
            prevHash = hash; prevNt = nt;
        }

        static float AttackNt(in PlayerCombatState c)
        {
            // 콤보 단계별로 틱이 다르다(평타1은 후딜 짧음, 평타2는 길음)
            int wu = CombatConfig.AtkWindup(c.attackStep),
                ac = CombatConfig.AtkActive(c.attackStep),
                re = CombatConfig.AtkRecovery(c.attackStep);
            float total = Mathf.Max(1, wu + ac + re);
            float e = c.attackPhase == CombatConfig.PhWindup ? c.attackPhaseTicks
                    : c.attackPhase == CombatConfig.PhActive ? wu + c.attackPhaseTicks
                    :                                          wu + ac + c.attackPhaseTicks;
            return Mathf.Clamp01(e / total);
        }

        static float LungeNt(in PlayerCombatState c)
        {
            int wu = CombatConfig.LungeWindupTicks, tr = Mathf.Max(1, c.lungeTravelTicks), re = CombatConfig.LungeRecoveryTicks;
            float total = Mathf.Max(1, wu + tr + re);
            float e = c.lungePhase == CombatConfig.LgWindup ? c.lungeTicks
                    : c.lungePhase == CombatConfig.LgTravel ? wu + c.lungeTicks
                    :                                         wu + tr + c.lungeTicks;
            return Mathf.Clamp01(e / total);
        }

        // ── 콘솔 API ──
        /// <summary>전투 없이 스윙 재생(궤적 튜닝용). which: 1=평타1, 2=평타2, t=찌르기.</summary>
        public bool PreviewSwing(string which, bool loop)
        {
            int h;
            switch (which)
            {
                case "1": h = HSlash1; break;
                case "2": h = HSlash2; break;
                case "t": case "thrust": h = HThrust; break;
                default: return false;
            }
            previewHash = h; previewT = 0f; previewLoop = loop; previewOn = true;
            return true;
        }

        public void StopPreview()
        {
            previewOn = false;
        }

        // ── 사운드 전이 ──
        void DetectAndSound(in PlayerSim p)
        {
            byte atk = p.combat.attackPhase;
            if (atk != prevAttack)
            {
                if (prevAttack == CombatConfig.PhNone && atk == CombatConfig.PhWindup) CombatAudio.Swing();
                prevAttack = atk;
            }
            bool dash = p.dashTicks > 0;
            if (dash && !prevDash) CombatAudio.Dash();
            prevDash = dash;
            // 찌르기(런지) 발동음 — 두 신호를 함께 본다.
            //  (1) 위상 전이 LgNone→발동: 즉각 반응하지만 찌르기 구간이 짧아 프레임 샘플링에
            //      놓칠 수 있다(sim이 프레임 사이에 그 구간을 통째로 지나갈 때).
            //  (2) 런지 스택 감소: 찌르기 쓰면 스택이 1 줄고 그 값이 유지되므로 안 놓친다.
            //  두 신호 중 아무거나 잡되, 짧은 쿨다운으로 같은 찌르기가 두 번 나는 것만 막는다.
            byte lg = p.combat.lungePhase;
            int ls = p.combat.lungeStacks;
            if (!lungeStacksInit) { prevLungeStacks = ls; lungeStacksInit = true; }
            bool phaseEdge = lg != prevLunge && prevLunge == CombatConfig.LgNone && lg != CombatConfig.LgNone;
            bool stackDrop = ls < prevLungeStacks;
            if ((phaseEdge || stackDrop) && Time.unscaledTime >= lungeSoundCooldownUntil)
            {
                CombatAudio.Backstrike();
                lungeSoundCooldownUntil = Time.unscaledTime + 0.12f;
            }
            prevLunge = lg;
            prevLungeStacks = ls;

            // [2026-07-22] 점프 — jumpCount가 늘어난 순간. 2단째면 이단점프음.
            if (p.jumpCount > prevJumpCount)
            {
                if (p.jumpCount >= 2) CombatAudio.DoubleJump();
                else                  CombatAudio.Jump();
            }
            prevJumpCount = p.jumpCount;

            // [2026-07-22] 착지 — 공중에서 지면으로 전이.
            // 계단을 미끄러지면 grounded가 순간순간 깜빡여 착지음이 연속으로 났다 —
            // "충분히 공중에 떠 있었을 때만" 착지로 친다(짧은 깜빡임은 무시).
            if (p.grounded)
            {
                if (!prevGrounded && airborneTime >= MinAirborneForLand) CombatAudio.Landing();
                airborneTime = 0f;
            }
            else airborneTime += Time.deltaTime;
            prevGrounded = p.grounded;
        }
    }

    /// <summary>Play 시 칼 뷰모델 자동 부착.</summary>
    public static class SwordBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<SwordView>() == null)
                new GameObject("[SwordView]").AddComponent<SwordView>();
        }
    }
}
