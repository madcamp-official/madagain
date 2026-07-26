using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 보스 처치 엔딩 감독. 순수 View — sim에서 보스(Orb) 사망 전이를 감지해 아래 시퀀스를 튼다.
    ///
    ///   ① <b>슬로모</b> — 막타 순간(평타·찌르기는 모션 시작 직후 즉발 판정이라 보스 사망 = 모션 초반).
    ///      배속을 낮추고, 사라진 오브 자리에 발광 잔해 프롭을 세운다(뷰가 죽은 적을 즉시 숨기므로).
    ///   ② <b>암전</b> — 평타(attackPhase)·찌르기(lungePhase) 모션이 끝나는 순간 화면을 즉시 검게.
    ///      Cutscene.Active로 입력·sim 정지. 이때 죽음 카메라를 미리 세운다. 1초 유지.
    ///   ③ <b>원래모습 노출</b> — 암전이 '팍' 풀리며 BossDeathCam 구도로 <b>온전한 아레나</b>를 잠시 보여준다.
    ///   ④ <b>맵 폭발 = 끝</b> — <b>슬로모 상태</b>에서 아레나 콜라이더 전부 비활성(충돌판정 제거) 후
    ///      렌더러 조각 전부를 <b>보스 코어에서 바깥으로</b> 날린다 — 코어에 가까울수록 큰 힘, 멀수록
    ///      작은 힘(선형). Rigidbody 없이 직접 적분(convex·물리비용 무관). 발광 플래시 + 셰이크를
    ///      얹고, 수 초 뒤 서서히 암전한 채 종료(크레딧 없음).
    ///
    /// 배속 소유권: PredictionController가 매 프레임 Time.timeScale을 쓰므로, 슬로모는
    /// PredictionController.CutsceneTimeScaleOverride 훅으로 잡는다(직접 쓰면 즉시 1로 되돌려짐).
    /// </summary>
    [DisallowMultipleComponent]
    public class BossDeathDirector : MonoBehaviour, IRunResettable
    {
        [Header("배선")]
        [Tooltip("폭발시킬 아레나 콘텐츠 루트(Arena_4_Content). 비우면 이름으로 찾는다.")]
        public Transform arenaRoot;
        [Tooltip("엔딩 시 정지시킬 웨이브 러너(A4 _Waves). 비우면 arenaRoot에서 찾는다.")]
        public WaveRunner runner;

        [Header("① 슬로모")]
        [Tooltip("막타 순간 배속.")]
        public float slowMoScale = 0.15f;
        [Tooltip("모션 끝 감지 실패 대비 슬로모 최대 시간(실시간 초).")]
        public float slowMoMaxSeconds = 3f;

        [Header("② 암전")]
        public float blackoutSeconds = 1f;

        [Header("③ 원래모습 노출(폭발 직전)")]
        [Tooltip("암전이 풀리고 폭발 전, 온전한 아레나를 보여주는 시간(실시간 초).")]
        public float revealHoldSeconds = 1.2f;

        [Header("④ 폭발 — 조각 (코어에서 방사)")]
        [Tooltip("보스 코어에 가장 가까운 조각의 초기 속도(m/s). 가까울수록 강하게.")]
        public float pieceSpeedNear = 60f;
        [Tooltip("가장 먼 조각의 초기 속도(m/s). 멀수록 약하게.")]
        public float pieceSpeedFar = 12f;
        [Tooltip("조각에 걸 중력(m/s²). 0 = 직선 비행.")]
        public float pieceGravity = 9.8f;
        [Tooltip("조각 회전 속도 상한(도/초).")]
        public float pieceSpinMax = 540f;
        [Tooltip("방향 랜덤 흔들기(0=정확히 코어 바깥, 1=완전 랜덤).")]
        [Range(0f, 1f)] public float directionJitter = 0.12f;
        [Tooltip("위쪽 치우침(0=수평 방사 그대로, 1=위로).")]
        [Range(0f, 1f)] public float upwardBias = 0.15f;

        [Header("④ 폭발 — 슬로모·연출·마무리")]
        [Tooltip("폭발 진행 배속(슬로모). 0.3 = 30% 속도.")]
        public float explosionSlowScale = 0.3f;
        [Tooltip("발광 플래시 개수(폭발 초반에 시차를 두고 터짐).")]
        public int flashCount = 14;
        [Tooltip("셰이크 세기(m)·시간(초, 슬로모 기준).")]
        public float shakeAmp = 0.5f;
        public float shakeSeconds = 1.5f;
        [Tooltip("폭발 감상 시간(초, 슬로모 기준) — 이후 코어 피날레로.")]
        public float explosionSeconds = 3f;
        public float endFadeSeconds = 2f;

        [Header("⑤ 코어 피날레 (폭발 뒤) — 축소→확대(화면 삼킴)→암전→로고")]
        [Tooltip("코어 축소 시간(실시간 초).")]
        public float coreShrinkSeconds = 3f;
        [Tooltip("코어 확대(화면 삼킴) 시간(실시간 초).")]
        public float coreExpandSeconds = 0.7f;
        [Tooltip("빨강→검정 암전 시간(실시간 초).")]
        public float finalFadeSeconds = 1f;
        [Range(0.1f, 1f)] [Tooltip("축소 배율(원래 대비).")]
        public float coreShrinkTo = 0.6f;
        [Tooltip("카메라를 확실히 삼킬 여유 반경(m).")]
        public float coreEngulfMargin = 50f;

        enum Phase { Idle, SlowMo, Blackout, Reveal, Explosion, CoreShrink, CoreExpand, FinalBlack, Logo }
        Phase phase = Phase.Idle;
        float phaseStartReal;      // 실시간 기준(슬로모·암전·노출 단계)
        float phaseStartT;         // 스케일 시간 기준(폭발·마무리 단계 — 슬로모 반영)
        bool  bossSeenAlive;
        bool  fired;               // 1회성
        Vector3 bossDeathPos;
        float bossVisualSize = 8f;

        Transform dyingOrb;        // 슬로모 동안 오브 잔해
        Material  dyingOrbMat;
        Transform vcamGo;          // 죽음 카메라(생성 후 파괴 안 함 — 엔딩 화면 유지)
        Vector3   vcamBasePos;

        Transform scrapRoot;
        Transform coreOrb; Material coreOrbMat;      // 피날레 코어(양면 발광 빨강)
        float coreExpandStart, coreExpandTarget;
        struct Piece { public Transform t; public Vector3 vel; public Vector3 axis; public float spin; }
        readonly List<Piece> pieces = new List<Piece>();
        struct Flash { public Transform t; public float start; public float life; public Material mat; }
        readonly List<Flash> flashes = new List<Flash>();
        System.Random rng;

        float blackAlpha;          // OnGUI 오버레이(0~1)

        void Awake()
        {
            if (arenaRoot == null)
            {
                var go = GameObject.Find("Arena_4_Content");
                if (go != null) arenaRoot = go.transform;
            }
            if (runner == null && arenaRoot != null)
                runner = arenaRoot.GetComponentInChildren<WaveRunner>(true);
            rng = new System.Random(20260723);
        }

        void OnDestroy()
        {
            // 안전 원복 — 씬 전환·정지 시 배속/잠금이 남지 않게.
            PredictionController.CutsceneTimeScaleOverride = -1f;
            if (dyingOrbMat != null) Destroy(dyingOrbMat);
            if (coreOrbMat != null) Destroy(coreOrbMat);
        }

        void Update()
        {
            var main = Main.Instance;
            if (main == null) return;
            ref readonly SimWorld w = ref main.World;

            if (phase == Phase.Idle)
            {
                WatchBoss(in w);
                return;
            }

            switch (phase)
            {
                case Phase.SlowMo:
                {
                    // 잔해 오브 깜빡임(죽어가는 코어).
                    if (dyingOrbMat != null)
                    {
                        float flicker = 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 24f));
                        dyingOrbMat.SetColor("_BaseColor", new Color(6f * flicker, 0.5f * flicker, 0.18f * flicker));
                    }
                    bool motionDone = w.player.combat.attackPhase == CombatConfig.PhNone
                                   && w.player.combat.lungePhase == CombatConfig.LgNone;
                    if (motionDone || Time.unscaledTime - phaseStartReal >= slowMoMaxSeconds) BeginBlackout();
                    break;
                }

                case Phase.Blackout:
                    if (Time.unscaledTime - phaseStartReal >= blackoutSeconds) BeginReveal();
                    break;

                case Phase.Reveal:
                    // 온전한 아레나를 잠시 보여준 뒤 폭발.
                    if (Time.unscaledTime - phaseStartReal >= revealHoldSeconds) BeginExplosion();
                    break;

                case Phase.Explosion:
                {
                    Time.timeScale = Mathf.Clamp(explosionSlowScale, 0.02f, 1f);   // 슬로모 강제 유지
                    float st = Time.time - phaseStartT;   // 슬로모(스케일) 기준 경과
                    StepPieces(Time.deltaTime);           // 배속 반영 → 조각이 느리게 난다
                    StepFlashes();
                    // 셰이크 — 죽음 카메라를 직접 흔든다(내가 만든 vcam이라 안전).
                    if (vcamGo != null)
                    {
                        float k = Mathf.Clamp01(1f - st / Mathf.Max(0.01f, shakeSeconds));
                        vcamGo.position = vcamBasePos + (k > 0f
                            ? new Vector3(NextSym(), NextSym(), NextSym()) * (shakeAmp * k)
                            : Vector3.zero);
                    }
                    if (st >= explosionSeconds) BeginCoreShrink();   // 폭발 뒤 → 코어 피날레
                    break;
                }

                case Phase.CoreShrink:
                {
                    Time.timeScale = Mathf.Clamp(explosionSlowScale, 0.02f, 1f);
                    StepPieces(Time.deltaTime); StepFlashes();       // 조각은 계속 날아감(슬로모)
                    float u = Mathf.Clamp01((Time.unscaledTime - phaseStartReal) / Mathf.Max(0.01f, coreShrinkSeconds));
                    if (coreOrb != null)
                        coreOrb.localScale = Vector3.one * Mathf.Lerp(bossVisualSize, bossVisualSize * coreShrinkTo, EaseInOut(u));
                    if (u >= 1f) BeginCoreExpand();
                    break;
                }

                case Phase.CoreExpand:
                {
                    Time.timeScale = Mathf.Clamp(explosionSlowScale, 0.02f, 1f);
                    StepPieces(Time.deltaTime); StepFlashes();
                    float u = Mathf.Clamp01((Time.unscaledTime - phaseStartReal) / Mathf.Max(0.01f, coreExpandSeconds));
                    if (coreOrb != null)
                        coreOrb.localScale = Vector3.one * Mathf.Lerp(coreExpandStart, coreExpandTarget, u * u);   // 가속(팍)
                    if (u >= 1f) { phase = Phase.FinalBlack; phaseStartReal = Time.unscaledTime; }
                    break;
                }

                case Phase.FinalBlack:
                {
                    Time.timeScale = Mathf.Clamp(explosionSlowScale, 0.02f, 1f);
                    float u = Mathf.Clamp01((Time.unscaledTime - phaseStartReal) / Mathf.Max(0.01f, finalFadeSeconds));
                    blackAlpha = u;   // 빨강 → 검정
                    if (u >= 1f) BeginLogo();
                    break;
                }

                case Phase.Logo:
                    // 타이틀(메인 로고)이 스스로 돈다 — 여기선 대기.
                    break;
            }
        }

        /// <summary>보스(Orb) 사망 전이 감지 — 살아 있는 걸 본 뒤 죽으면 1회 발동.</summary>
        void WatchBoss(in SimWorld w)
        {
            if (fired) return;
            for (int i = 0; i < w.enemyCount; i++)
            {
                ref readonly EnemySim e = ref w.enemies[i];
                if (e.ai.mobility != MobilityType.Orb) continue;
                if (e.alive) { bossSeenAlive = true; return; }
                if (bossSeenAlive)
                {
                    bossDeathPos = e.pos;
                    bossVisualSize = e.radius * 2f;   // EntityViews와 동일 — 구 지름 = sim 히트박스
                    BeginSlowMo();
                    return;
                }
            }
        }

        /// <summary>
        /// 콘솔용: 보스 유무·막타와 무관하게 엔딩을 즉시 시작(테스트용). 기본은 곧장 암전→노출→슬로모 폭발.
        /// 살아 있거나 방금 죽은 보스가 있으면 그 위치를, 없으면 플레이어 앞을 코어(사망 지점)로 잡는다.
        /// <paramref name="withIntroSlowMo"/>=true면 앞에 막타 슬로모 연출을 붙여 시연한다.
        /// </summary>
        public bool TriggerFromConsole(bool withIntroSlowMo)
        {
            if (fired) return false;
            var main = Main.Instance;
            if (main == null) return false;

            ref readonly SimWorld w = ref main.World;
            bool found = false;
            for (int i = 0; i < w.enemyCount; i++)
            {
                ref readonly EnemySim e = ref w.enemies[i];
                if (e.ai.mobility != MobilityType.Orb) continue;
                bossDeathPos = e.pos;
                bossVisualSize = e.radius * 2f;
                found = true;
                break;
            }
            if (!found) { bossDeathPos = w.player.pos + Vector3.up * 2f; bossVisualSize = 8f; }

            if (withIntroSlowMo) { bossSeenAlive = true; BeginSlowMo(); }
            else                 { fired = true; BeginBlackout(); }
            return true;
        }

        void BeginSlowMo()
        {
            fired = true;
            phase = Phase.SlowMo;
            phaseStartReal = Time.unscaledTime;
            PredictionController.CutsceneTimeScaleOverride = Mathf.Clamp(slowMoScale, 0.02f, 1f);

            // 죽은 오브 잔해 — 뷰가 죽은 적을 즉시 숨기므로 같은 자리에 발광 구를 세운다.
            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "~BossDyingOrb";
            Destroy(orb.GetComponent<Collider>());
            dyingOrbMat = MakeGlow(new Color(6f, 0.5f, 0.18f));
            var r = orb.GetComponent<Renderer>();
            r.sharedMaterial = dyingOrbMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            orb.transform.position = bossDeathPos;
            orb.transform.localScale = Vector3.one * bossVisualSize;
            dyingOrb = orb.transform;
        }

        void BeginBlackout()
        {
            phase = Phase.Blackout;
            phaseStartReal = Time.unscaledTime;
            blackAlpha = 1f;                                          // 즉시 암전
            PredictionController.CutsceneTimeScaleOverride = -1f;     // 배속 원복(노출 단계는 정상 속도)
            Cutscene.Active = true;                                   // 입력·sim 정지(이후 계속 유지 = 엔딩)
            HideHudAndViewmodel();                                    // 모든 UI + 칼·손 끔(엔딩 내내)
            if (runner != null) runner.Stop();
            if (dyingOrb != null) Destroy(dyingOrb.gameObject);
            SetupDeathCam();                                          // 암전 아래에서 죽음 카메라를 미리 세운다
        }

        void BeginReveal()
        {
            phase = Phase.Reveal;
            phaseStartReal = Time.unscaledTime;
            blackAlpha = 0f;   // 암전 '팍' 해제 → 온전한 아레나(죽음 카메라 구도)를 잠시 보여준다
        }

        void BeginExplosion()
        {
            phase = Phase.Explosion;
            phaseStartT = Time.time;
            // 슬로모 시작 — 예측이 tick하면 이 오버라이드가, 아니면 Update의 직접 설정이 배속을 잡는다.
            PredictionController.CutsceneTimeScaleOverride = Mathf.Clamp(explosionSlowScale, 0.02f, 1f);
            Time.timeScale = Mathf.Clamp(explosionSlowScale, 0.02f, 1f);

            if (arenaRoot == null) return;

            // 1) 충돌판정 완전 제거 — 조각은 아무것과도 안 부딪히고 날아간다.
            foreach (var c in arenaRoot.GetComponentsInChildren<Collider>(true))
                c.enabled = false;

            // 2) 조각 수집·분리 — 렌더러 트랜스폼을 각자 독립 조각으로 떼어낸다(활성만).
            scrapRoot = new GameObject("~ArenaScrap").transform;
            var seen = new HashSet<Transform>();
            foreach (var r in arenaRoot.GetComponentsInChildren<Renderer>(false))
                seen.Add(r.transform);

            // 코어 = 보스 사망 지점. 가장 먼 조각까지의 거리로 힘을 정규화한다.
            Vector3 core = bossDeathPos;
            float maxDist = 1f;
            foreach (var t in seen)
            {
                float d = (t.position - core).magnitude;
                if (d > maxDist) maxDist = d;
            }

            foreach (var t in seen)
            {
                t.SetParent(scrapRoot, true);
                // 방향 = 코어에서 바깥으로(요청). 약간의 랜덤·위쪽 편향은 선택.
                Vector3 delta = t.position - core;
                float dist = delta.magnitude;
                Vector3 dir = dist > 0.05f ? delta / dist : RandomUnit();
                if (directionJitter > 0f) dir = Vector3.Slerp(dir, RandomUnit(), directionJitter).normalized;
                if (upwardBias > 0f)      dir = Vector3.Slerp(dir, Vector3.up, upwardBias).normalized;
                // 힘 = 코어에 가까울수록 강하게, 멀수록 약하게(선형).
                float f = Mathf.Clamp01(dist / maxDist);
                float speed = Mathf.Lerp(pieceSpeedNear, pieceSpeedFar, f);
                pieces.Add(new Piece
                {
                    t = t,
                    vel = dir * speed,
                    axis = RandomUnit(),
                    spin = (float)rng.NextDouble() * pieceSpinMax,
                });
            }

            // 3) 발광 플래시 — 코어 주변에 시차를 두고 터진다(스케일 시간 기준).
            for (int i = 0; i < flashCount; i++)
            {
                var fo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                fo.name = "~ExplosionFlash";
                Destroy(fo.GetComponent<Collider>());
                var mat = MakeGlow(new Color(8f, 4.5f, 1.2f));
                var rr = fo.GetComponent<Renderer>();
                rr.sharedMaterial = mat;
                rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Vector3 off = new Vector3(NextSym() * 60f, NextSym() * 12f, NextSym() * 60f);
                fo.transform.position = core + off;
                fo.transform.localScale = Vector3.zero;
                flashes.Add(new Flash
                {
                    t = fo.transform,
                    start = Time.time + (float)rng.NextDouble() * 1.2f,
                    life = 0.7f,
                    mat = mat,
                });
            }
            Debug.Log($"[BossDeathDirector] 폭발 — 조각 {pieces.Count}개(코어 방사), 플래시 {flashes.Count}개.");
        }

        GameObject hiddenVmCam, hiddenKatana;   // 재시작 때 되살리려고 참조를 들고 있는다

        /// <summary>모든 게임 UI + 뷰모델(칼·손) 끔. 엔딩 진입(암전) 시 1회.</summary>
        void HideHudAndViewmodel()
        {
            UiVisibility.Set(true);                                   // 전투 HUD·예측 표시 숨김
            if (ViewmodelCamera.Instance != null)                    // 칼·손(오버레이 카메라) 끔
            { hiddenVmCam = ViewmodelCamera.Instance.gameObject; hiddenVmCam.SetActive(false); }
            hiddenKatana = GameObject.Find("KatanaViewmodel");
            if (hiddenKatana != null) hiddenKatana.SetActive(false);
        }

        /// <summary>
        /// 게임 재시작 시 엔딩 연출 상태를 완전히 걷어낸다 — 엔딩이 떴다면 Cutscene.Active·배속·UI숨김·
        /// 뷰모델off가 남아 재시작한 게임이 얼어붙는다. 그걸 원복하고 연출 오브젝트를 정리한다.
        /// (폭발로 흩어진 아레나 조각 <see cref="scrapRoot"/>은 아레나 렌더러라 지우지 않는다 —
        ///  완전한 지형 복구는 씬 리로드가 필요하다. 여기선 '게임이 다시 돌게' 하는 데 집중.)
        /// </summary>
        public void ResetForRestart()
        {
            fired = false;
            bossSeenAlive = false;
            phase = Phase.Idle;
            blackAlpha = 0f;
            PredictionController.CutsceneTimeScaleOverride = -1f;
            Time.timeScale = 1f;
            Cutscene.Active = false;
            UiVisibility.Set(false);                                  // UI 복구
            if (hiddenVmCam != null) hiddenVmCam.SetActive(true);     // 칼·손 복구
            if (hiddenKatana != null) hiddenKatana.SetActive(true);
            hiddenVmCam = hiddenKatana = null;

            if (dyingOrb != null) { Destroy(dyingOrb.gameObject); dyingOrb = null; }
            if (coreOrb != null)  { Destroy(coreOrb.gameObject);  coreOrb = null; }
            if (vcamGo != null)   { Destroy(vcamGo.gameObject);   vcamGo = null; }
            for (int i = 0; i < flashes.Count; i++)
                if (flashes[i].t != null) Destroy(flashes[i].t.gameObject);
            flashes.Clear();
            pieces.Clear();

            // 폭발로 껐던 아레나 콜라이더 복구(지형 충돌 되살림).
            if (arenaRoot != null)
                foreach (var c in arenaRoot.GetComponentsInChildren<Collider>(true)) c.enabled = true;
        }

        void BeginCoreShrink()
        {
            phase = Phase.CoreShrink;
            phaseStartReal = Time.unscaledTime;
            // 코어 = 보스 사망 지점에 다시 맺힌 발광 빨강 구. 양면(Cull Off)이라 카메라가 삼켜져도 빨강.
            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "~EndingCore";
            Destroy(orb.GetComponent<Collider>());
            coreOrbMat = MakeGlowTwoSided(new Color(6f, 0.06f, 0.03f));   // 강렬 빨강(HDR)
            var r = orb.GetComponent<Renderer>();
            r.sharedMaterial = coreOrbMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            orb.transform.position = bossDeathPos;
            orb.transform.localScale = Vector3.one * bossVisualSize;
            coreOrb = orb.transform;
        }

        void BeginCoreExpand()
        {
            phase = Phase.CoreExpand;
            phaseStartReal = Time.unscaledTime;
            // 카메라를 확실히 삼킬 지름 = 2×(카메라~코어 거리 + 여유).
            Vector3 camPos = vcamGo != null ? vcamGo.position
                : (Main.Instance != null && Main.Instance.Cam != null ? Main.Instance.Cam.transform.position : bossDeathPos);
            float dist = (camPos - bossDeathPos).magnitude;
            coreExpandTarget = (dist + coreEngulfMargin) * 2f;
            coreExpandStart  = bossVisualSize * coreShrinkTo;
        }

        void BeginLogo()
        {
            phase = Phase.Logo;
            if (coreOrb != null) Destroy(coreOrb.gameObject);
            PredictionController.CutsceneTimeScaleOverride = -1f;
            Time.timeScale = 1f;
            // 메인화면 로고 '그대로' — 타이틀 스크린을 재생성(어두운 배경 + 로고 리빌).
            if (UnityEngine.Object.FindFirstObjectByType<TitleScreen>() == null)
                new GameObject("[TitleScreen]").AddComponent<TitleScreen>();
            blackAlpha = 0f;   // OnGUI 검정 끔 → 타이틀 캔버스가 화면을 이어받음(어두운 배경이라 이음새 없음)
        }

        static float EaseInOut(float u) => u * u * (3f - 2f * u);   // smoothstep

        void SetupDeathCam()
        {
            var marker = BossDeathCam.Find();
            var main = Main.Instance;
            var go = new GameObject("~BossDeathVcam");
            var vcam = go.AddComponent<CinemachineCamera>();
            if (marker != null)
            {
                go.transform.SetPositionAndRotation(marker.transform.position, marker.transform.rotation);
                if (main != null && main.GameplayVcam != null) vcam.Lens = main.GameplayVcam.Lens;
                var lens = vcam.Lens; lens.FieldOfView = marker.fov; vcam.Lens = lens;
            }
            else
            {
                // 마커가 없으면 현재 카메라 자리에서 그대로(구도만 못 잡을 뿐 진행은 한다).
                Debug.LogWarning("[BossDeathDirector] BossDeathCam 마커가 없음 — 현재 시점에서 폭발을 보여줍니다.");
                if (main != null && main.Cam != null)
                    go.transform.SetPositionAndRotation(main.Cam.transform.position, main.Cam.transform.rotation);
            }
            vcam.Priority.Value = 200;   // 컷신(100)보다 높게 — 즉시 컷 전환
            vcamGo = go.transform;
            vcamBasePos = go.transform.position;
        }

        void StepPieces(float dt)
        {
            for (int i = 0; i < pieces.Count; i++)
            {
                Piece p = pieces[i];
                if (p.t == null) continue;
                p.vel += Vector3.down * (pieceGravity * dt);
                p.t.position += p.vel * dt;
                p.t.Rotate(p.axis, p.spin * dt, Space.World);
                pieces[i] = p;
            }
        }

        void StepFlashes()
        {
            for (int i = 0; i < flashes.Count; i++)
            {
                Flash f = flashes[i];
                if (f.t == null) continue;
                float u = (Time.time - f.start) / f.life;   // 스케일 시간 → 슬로모 반영
                if (u < 0f) continue;
                if (u >= 1f) { Destroy(f.t.gameObject); continue; }
                f.t.localScale = Vector3.one * Mathf.Lerp(2f, 26f, 1f - (1f - u) * (1f - u));
                float dim = 1f - u;
                f.mat.SetColor("_BaseColor", new Color(8f * dim, 4.5f * dim, 1.2f * dim));
            }
        }

        // ── 유틸 ──

        float NextSym() => (float)(rng.NextDouble() * 2.0 - 1.0);

        Vector3 RandomUnit()
        {
            // 구면 균등에 가까운 랜덤 단위벡터(간단 리젝션 없이 정규화 — 연출용으로 충분).
            Vector3 v = new Vector3(NextSym(), NextSym(), NextSym());
            return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.up;
        }

        static Material MakeGlow(Color hdr)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            var m = new Material(sh);
            m.SetColor("_BaseColor", hdr);
            m.SetColor("_Color", hdr);
            return m;
        }

        /// <summary>양면 발광(Cull Off) — 구가 카메라를 삼켜 안쪽에서 봐도 빨강으로 꽉 차게.</summary>
        static Material MakeGlowTwoSided(Color hdr)
        {
            var m = MakeGlow(hdr);
            m.SetFloat("_Cull", 0f);   // 0=Off(양면). URP/Unlit
            return m;
        }

        void OnGUI()
        {
            if (blackAlpha <= 0f) return;
            // 엔딩 오버레이 — UI 숨김(UiVisibility)과 무관하게 최상단에 그린다.
            GUI.depth = -1000;
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, blackAlpha);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
