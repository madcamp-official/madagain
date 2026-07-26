using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

namespace Game.View
{
    // >>> [등장 컷신, 2026-07-23] "새 게임"을 누르면 아레나 한복판에 툭 떨어져 있던 걸
    //
    // <b>히어로 랜딩</b> 한 컷으로 바꾼다 — 하늘에서 낙하 → 착지 충격 → 기립 → 1인칭 인계.
    //
    // <b>왜 Timeline이 아닌가</b> — 옆의 <see cref="CutsceneManager"/>는 씬에 설치한 리그
    // (CutsceneDirector·CutsceneRig)와 손으로 찍은 키프레임을 쓴다. 그건 봉인해제 컷신처럼
    // 씬에 고정된 연출에는 맞지만, 등장 컷신은 <b>플레이어 스폰 지점 기준</b>이라 맵마다·
    // 스폰마다 좌표가 달라진다. 씬 에셋에 좌표를 굽는 순간 맵이 바뀔 때마다 다시 찍어야 한다.
    // 그래서 여기선 스폰 좌표·시선을 런타임에 읽어 <b>코드로 궤도를 계산</b>한다(에셋 0개).
    //
    // <b>주인공은 실물</b>이다 — 인게임은 1인칭이라 몸이 없지만(EntityViews의 PlayerAnchor는
    // 렌더러가 꺼진 캡슐), 히어로 랜딩은 본질적으로 3인칭 그림이다. 그래서 타이틀 화면과
    // 같은 <c>PlayerGhost</c> 프리팹을 잠깐 세우고, 컷신이 끝나는 순간 카메라가 그 머리
    // 안으로 들어가면서 몸을 지운다 — 그 지점이 정확히 1인칭 눈높이다.
    //
    // <b>포즈는 TitleActor와 같은 방식</b>(<c>AnimationClip.SampleAnimation</c> + Animator 정지)이다.
    // 처음엔 Playables 믹서로 여러 클립을 섞으려 했는데, 실제로 리그를 계측해 보니 그래프가 포즈를
    // <b>전혀 적용하지 않고</b> 있었다 — 59프레임 내내 엉덩이 높이가 1.52로 고정, 즉 화면에 나오던
    // "선 자세"는 사실 바인드 포즈였다. 이 프로젝트에서 동작이 확인된 경로로 되돌렸고, 클립을 한 장만
    // 쓰기로 하면서(아래) 섞을 일 자체가 없어져 그래프가 필요 없어졌다.
    //
    // 조작 잠금은 기존 <see cref="Cutscene"/>.Active를 그대로 쓴다 — Main이 이미 그 값으로
    // 입력·1인칭 시점·sim 전진을 전부 잠근다. 새 게이트를 만들 이유가 없다.

    /// <summary>등장 컷신을 재생할지. 전투·포즈 작업 중엔 false로 두면 곧장 조작이 넘어온다.</summary>
    public static class IntroStyle
    {
        public static bool PlayOnNewGame = true;
    }

    /// <summary>
    /// 게임 시작 등장 컷신(히어로 랜딩). 타이틀의 "새 게임" 직후 <see cref="Play"/>로 뜬다.
    /// 키보드 아무 키(ENTER·SPACE·ESC)로 건너뛸 수 있다.
    /// </summary>
    public class IntroCutscene : MonoBehaviour
    {
        public static IntroCutscene Instance { get; private set; }

        /// <summary>컷신 경과(초). 검증 도구가 특정 박자에서 화면을 찍는 데 쓴다 —
        /// 프레임이 느리면 벽시계와 어긋나므로(멈춤 보호로 dt를 자른다) 이 값이 기준이어야 한다.</summary>
        public float Elapsed => t;
        /// <summary>전체 길이(초).</summary>
        public static float Duration => TotalT;

        // ── 박자 ─────────────────────────────────────────────────────────
        // 컷신 전체 길이는 4초 남짓. 이보다 길면 두 번째 플레이부터 건너뛰기 버튼만 찾게 된다.
        const float FallDur    = 1.25f;   // 하늘에서 떨어지는 동안
        const float HoldDur    = 0.55f;   // 착지 자세로 멈춰 있는 동안(충격이 읽히는 시간)
        const float RiseDur    = 1.15f;   // 웅크림 → 기립
        const float HandDur    = 0.85f;   // 카메라가 머리 안으로 들어가 1인칭이 되는 동안
        const float ImpactT    = FallDur;
        const float RiseT      = FallDur + HoldDur;
        const float HandT      = RiseT + RiseDur;
        const float TotalT     = HandT + HandDur;

        const float FallHeightWant = 26f;   // 원하는 낙하 고도(m). 천장이 낮으면 실측으로 줄인다
        const float FallBack   = 3.2f;      // 뒤에서 앞으로 그리는 호 — 수직 낙하는 엘리베이터로 보인다

        // ── 카메라 궤도 ───────────────────────────────────────────────────
        // θ = 주인공 정면(0°)에서 도는 각. 180°면 등 뒤 = 주인공과 같은 방향을 본다.
        //
        // ★ 시작 θ와 거리는 <b>상수가 아니라 실측</b>이다. Arena_3의 스폰 지점을 재 보니 정면
        //   쪽 여유가 1~3m뿐이고 등 뒤가 10m 넘게 트여 있었다 — 상수로 34°를 박아 두면 그 맵에선
        //   카메라가 벽에 처박힌다. 맵마다 좌표를 봐 줄 수 없으므로(코드 궤도의 대가) 시작할 때
        //   사방으로 레이를 쏴 <b>가장 트인 쪽</b>에서 찍기 시작한다.
        // 착지 거리는 <b>자세가 읽히는</b> 거리다. 5.0m에선 무릎 꿇은 실루엣이 너무 작아
        // 공들인 자세가 안 보였다(렌더로 확인) — 좁으면 VisibleDist가 알아서 도로 줄인다.
        // PlayerGhost의 실측 키는 약 1.9m다. 이전 값(9.5m / 3.4m)은 넓은 FOV와 겹쳐
        // 주인공이 배경 소품처럼 작게 보였으므로 인물 중심 구도로 당긴다.
        const float DistFallWant = 7.0f, DistImpactWant = 2.4f;
        const float DistMin = 1.6f;         // 이보다 좁혀지면 구도가 죽는다 — 벽을 뚫는 게 차라리 낫다
        const float WallMargin = 0.5f;
        const float FovFall = 70f, FovImpact = 58f;

        float thetaStart = 34f;             // 실측으로 채운다
        float thetaEnd = 180f;              // 등 뒤가 막혀 있으면 여기서 줄인다
        float distFall = DistFallWant, distImpact = DistImpactWant;
        float fallHeight = FallHeightWant;
        float floorY;                       // 실측 바닥. 스폰 y가 아니다(sim이 멈춰 있어 아직 안 떨어졌다)

        /// <summary>착지 임펄스(카메라 쿵). CutsceneManager의 종료 임펄스와 같은 단위.</summary>
        const float LandImpulse = 1.9f;

        // ── 포즈 ─────────────────────────────────────────────────────────
        // <b>클립 하나(GhostJump)를 시간만 훑는다.</b> 처음엔 낙하/착지/기립을 서로 다른 클립에서
        // 가져와 섞으려 했는데, 실제로 뼈 높이를 재 보니 그럴 필요가 없었다:
        //   GhostJump 3.67초 안에 점프 <b>한 사이클 전체</b>가 들어 있다 —
        //   1.83s 정점(발 높이 1.15, 다리 접힘) · 2.29s 착지(엉덩이 0.78로 최저) · 3.21s 기립.
        // 반면 GhostLunge는 3.67초 내내 엉덩이 0.96으로 <b>웅크림이 아예 없었다</b>(그래서 첫 시도의
        // 착지가 뻣뻣이 선 자세로 나왔다). 한 클립 안의 연속된 구간을 쓰면 사람이 만든 예비동작·
        // 반동이 그대로 살고, 내가 지어낸 크로스페이드가 필요 없다.
        const string ClipJump = "GhostJump";
        const float PoseAir0 = 1.70f, PoseAir1 = 1.83f;   // 공중(다리 접은 정점)
        const float PoseLand = 2.29f;                     // 착지 접지 순간
        const float PoseSettle = 2.45f;                   // 가장 깊이 눌린 지점
        const float PoseStand = 3.21f;                    // 다 일어선 자세
        const float ReachDur = 0.28f;                     // 착지 직전 다리를 뻗는 구간

        Main main;
        Vector3 ground;
        float yaw0;
        float eyeHeight = 1.25f;

        /// <summary>착지 컷신 캐릭터 렌더 크기(PlayerGhost 프리팹은 1). 너무 작게 나오던 것 → 1.6.</summary>
        public static float ActorScale = 1.6f;

        float t;                 // 컷신 경과(unscaled)
        bool ready, finished;
        bool impacted;
        float veil = 1f;         // 검은 장막 알파(시작·건너뛰기를 덮는다)
        float veilFade;          // >0이면 그 속도로 걷힌다
        float flash;             // 착지 순간 흰 섬광

        Transform actorRoot;
        Animator animator;
        Transform toeL, toeR;    // 착지 후 발을 바닥에 정확히 붙이는 데 쓴다
        Transform hipsBone;      // 포즈에 실린 이동을 되물려 몸을 제자리에 고정하는 기준
        Transform kneeL;         // 무릎 꿇는 쪽 — 접지 판정에 발끝과 같이 들어간다
        /// <summary>무릎 뼈는 다리 <b>중심</b>이라 표면보다 이만큼 안쪽에 있다(m).</summary>
        const float KneeRadius = 0.05f;
        Transform rig;           // Animator가 붙은 오브젝트 = 클립을 굽는 대상
        Vector3 rigBasePos;
        Quaternion rigBaseRot;
        AnimationClip jumpClip;

        // 히어로 랜딩 자세(타이틀에서 튜닝한 근육값)를 얹기 위한 것들
        HumanPoseHandler poseHandler;
        HumanPose humanPose;
        int[] kneelMuscle;
        Quaternion poseBaseRot = Quaternion.identity;   // 바인드 자세의 몸통 회전
        GameObject viewmodel;    // 컷신 동안 숨기는 1인칭 칼
        ParticleSystem dust;
        CinemachineImpulseSource impulse;
        LensSettings savedLens;
        bool lensSaved;

        // UI
        Canvas canvas;
        Image veilImg, flashImg, barTop, barBot;
        Text titleText, placeText, hintText, loadingText;

        /// <summary>컷신을 띄운다. 이미 떠 있으면 그걸 그대로 돌려준다.</summary>
        public static IntroCutscene Play()
        {
            if (Instance != null) return Instance;
            return new GameObject("[IntroCutscene]").AddComponent<IntroCutscene>();
        }

        void Awake()
        {
            Instance = this;
            impulse = gameObject.AddComponent<CinemachineImpulseSource>();
            BuildUi();
            // Main.Start(NavMesh 굽기)가 아직 안 끝났을 수 있다 — 그동안은 검은 화면 + "생성 중"이
            // 유일하게 보이는 것이다. 잠금은 지금부터 걸어 둔다(굽는 도중 입력이 새지 않게).
            Cutscene.Active = true;
            UiVisibility.SuppressedByMenu = true;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Restore();
        }

        void Update()
        {
            if (!ready && !TryBegin()) { FadeVeil(); return; }
            if (finished) { FadeVeil(); return; }

            HideViewmodel();
            // ★ 매 프레임 다시 켠다. Awake에서 한 번만 세웠더니 <b>타이틀의 OnDestroy가 그 뒤에
            //   돌면서 도로 꺼</b>, 컷신 내내 체력바·예지 다이얼이 그대로 떠 있었다(실제로 확인).
            //   메뉴·컷신처럼 화면을 점유하는 쪽은 소유권을 매 프레임 주장하는 게 맞다.
            UiVisibility.SuppressedByMenu = true;

            // ★ 프레임 간격을 <b>자른다</b>. 컷신 직전은 Main.Start가 NavMesh를 굽느라 몇 초씩
            //   멈추는 구간이고, 그 뒤 첫 프레임의 unscaledDeltaTime이 통째로 얹히면 t가 단숨에
            //   착지 뒤로 튄다(계측에서 낙하 1.25초가 0.04초 만에 끝나는 걸로 잡혔다).
            //   멈춤은 "그만큼 빨리 감기"가 아니라 "그동안 멈춰 있었다"로 처리해야 한다.
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            t += dt;
            flash = Mathf.Max(0f, flash - dt * 5.5f);
            FadeVeil();

            if (Skipped())
            {
                // 건너뛰기 = 타임라인을 끝으로 밀고, 그 점프컷을 장막 한 번으로 덮는다.
                t = Mathf.Max(t, TotalT - 0.05f);
                veil = 1f;
                veilFade = 3.2f;
            }

            if (!impacted && t >= ImpactT) Impact();

            DriveCamera(t);
            DriveUi(t);

            if (t >= TotalT) Finish();
        }

        void LateUpdate()
        {
            if (!ready || finished) return;
            DriveActor(t);
        }

        // ── 기동 ─────────────────────────────────────────────────────────
        /// <summary>Main.Start가 끝나 스폰 좌표·vcam이 생겼으면 컷신을 시작한다.</summary>
        bool TryBegin()
        {
            main = Main.Instance;
            if (main == null || !main.enabled || main.GameplayVcam == null) return false;

            yaw0 = main.LookYaw;
            eyeHeight = main.EyeHeight;
            ground = MeasureGround(main.World.player.pos);
            yaw0 = ChooseFacing(yaw0);
            MeasureOrbit();

            savedLens = main.GameplayVcam.Lens;
            lensSaved = true;

            BuildActor();
            BuildDust();

            loadingText.gameObject.SetActive(false);
            veilFade = 2.6f;       // 첫 컷은 장막이 걷히면서 시작한다
            ready = true;
            t = 0f;

            CombatAudio.Dash();    // 낙하 바람소리 대용(짧은 휘슬) — 전용 합성음이 생기면 교체
            return true;
        }

        /// <summary>
        /// 1인칭 칼(KatanaViewmodel)을 컷신 동안 치운다 — 3인칭으로 사람을 보고 있는데
        /// 화면 앞에 칼이 떠 있으면 두 몸이 동시에 보인다.
        /// <b>매 프레임 확인</b>하는 이유: SwordView는 Main이 깬 뒤에 뷰모델을 만들므로,
        /// 컷신이 시작할 땐 아직 없다가 도중에 생겨난다.
        /// </summary>
        void HideViewmodel()
        {
            if (viewmodel != null)
            {
                if (viewmodel.activeSelf) viewmodel.SetActive(false);
                return;
            }
            var go = GameObject.Find("KatanaViewmodel");
            if (go == null) return;
            viewmodel = go;
            viewmodel.SetActive(false);
        }

        /// <summary>
        /// 실제 바닥을 찾아 컷신의 지면으로 삼는다.
        ///
        /// <para>★ 스폰 좌표의 y를 그대로 쓰면 안 된다 — 컷신이 sim을 얼려 두므로 플레이어는
        /// <b>아직 한 틱도 떨어지지 않았다</b>. 스폰 지점이 바닥과 조금이라도 어긋나 있으면
        /// (Arena_3은 바닥 0.07 · 스폰 0.00) 주인공이 허공에 뜨거나 바닥에 박힌 채 착지한다.
        /// 여기서 찾은 바닥은 컷신이 끝날 때 sim 플레이어에게도 그대로 넘긴다.</para>
        /// </summary>
        Vector3 MeasureGround(Vector3 spawn)
        {
            RaycastHit hit;
            if (Physics.Raycast(spawn + Vector3.up * 3f, Vector3.down, out hit, 30f,
                                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                floorY = hit.point.y;
                return new Vector3(spawn.x, floorY, spawn.z);
            }
            floorY = spawn.y;
            return spawn;
        }

        /// <summary>
        /// 주인공이 착지해서 <b>바라볼 방향</b>. 스폰 방향이 코앞의 벽을 향하면 아레나 쪽으로 돌린다.
        ///
        /// <para>Arena_3에는 PlayerSpawnPoint가 없어 MapBuilder가 기본값(남쪽 180°)을 쓰는데, 그 방향이
        /// 마침 벽이라 컷신이 <b>벽을 정면으로 보며 끝났다</b>(마지막 컷이 녹슨 철판으로 꽉 찼다).
        /// 컷신은 그 시선을 그대로 조작에 넘기므로 게임도 벽을 보며 시작된다.</para>
        ///
        /// <para>스폰 방향이 충분히 트여 있으면 <b>손대지 않는다</b> — 레벨이 의도한 방향이 있으면
        /// 그게 우선이고, 여기서 고치는 건 "벽에 코를 박고 시작하는" 경우뿐이다.</para>
        /// </summary>
        float ChooseFacing(float spawnYaw)
        {
            Vector3 eye = ground + Vector3.up * eyeHeight;
            const float Enough = 4f;                  // 이만큼 트여 있으면 그대로 둔다
            if (LookClear(eye, spawnYaw) >= Enough) return spawnYaw;

            float best = -1f, bestYaw = spawnYaw;
            for (int a = 0; a < 360; a += 15)
            {
                float clear = LookClear(eye, a);
                if (clear > best) { best = clear; bestYaw = a; }
            }
            Debug.Log($"[등장 컷신] 스폰 방향({spawnYaw:F0}°)이 {LookClear(eye, spawnYaw):F1}m 앞에서 막혀 " +
                      $"{bestYaw:F0}°({best:F1}m)로 돌려 인계합니다.");
            return bestYaw;
        }

        /// <summary>눈높이에서 그 방향으로 얼마나 멀리 보이는가.</summary>
        static float LookClear(Vector3 eye, float yaw)
        {
            const float Max = 20f;
            RaycastHit hit;
            return Physics.Raycast(eye, Quaternion.Euler(0f, yaw, 0f) * Vector3.forward, out hit, Max,
                                   Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                 ? hit.distance : Max;
        }

        /// <summary>
        /// 사방으로 레이를 쏴 <b>가장 트인 각</b>에서 찍기 시작하도록 궤도를 정하고, 천장 높이로
        /// 낙하 고도를 깎는다. 좁은 방에서도 컷신이 성립하게 만드는 유일한 장치다.
        /// </summary>
        void MeasureOrbit()
        {
            Vector3 focus = ground + Vector3.up * 1.0f;

            float best = -1f;
            for (int a = 0; a < 360; a += 15)
            {
                float v = VisibleDist(focus, a, DistFallWant, 0.45f);
                if (v > best) { best = v; thetaStart = a; }
            }
            distFall = Mathf.Clamp(best, DistMin, DistFallWant);
            distImpact = Mathf.Min(distFall, DistImpactWant);

            // 등 뒤(180°)로 돌아 들어가는 마무리는 <b>그쪽에서도 주인공이 보일 때만</b> 한다.
            // 안 보이면 시작 각 쪽에 머문다 — 어차피 마지막엔 눈높이로 수렴하므로 성립한다.
            float back = VisibleDist(focus, 180f, distImpact, 0.6f);
            thetaEnd = back >= DistMin + WallMargin ? 180f : thetaStart + Mathf.DeltaAngle(thetaStart, 180f) * 0.35f;

            RaycastHit up;
            float ceiling = Physics.Raycast(focus, Vector3.up, out up, FallHeightWant + 6f,
                                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                          ? up.distance : FallHeightWant + 6f;
            fallHeight = Mathf.Clamp(ceiling - 2f, 6f, FallHeightWant);

            Debug.Log($"[등장 컷신] 실측 — 바닥 y={floorY:F2} · 시작각 {thetaStart:F0}°(여유 {best:F1}m) · " +
                      $"마무리각 {thetaEnd:F0}° · 낙하 {fallHeight:F1}m(천장 {ceiling:F1}m)");
        }

        /// <summary>
        /// θ 방향에서 <b>주인공이 실제로 보이는</b> 최대 거리.
        ///
        /// <para>★ "벽까지 거리"를 재면 안 된다. 처음엔 focus에서 바깥으로 레이를 쏴 여유를 쟀는데,
        /// 아레나의 방은 <b>커다란 박스 콜라이더 하나</b>로 되어 있어서 레이가 그 안에서 출발하면
        /// 히트가 잡히지 않는다(유니티는 시작점이 콜라이더 내부면 그 콜라이더를 무시한다).
        /// 그래서 "사방이 14m 트여 있다"고 읽고는 카메라를 9m 밖에 세웠고, 그 자리는 방 <b>바깥</b>이라
        /// 착지 컷 내내 벽이 주인공을 가렸다(카메라→주인공 선을 그어 보니 매 프레임 막혀 있었다).</para>
        ///
        /// <para>그래서 재는 대상을 바꾼다 — 후보 위치에서 <b>주인공까지 선이 통하는가</b>. 이건 벽의
        /// 안팎·모양과 무관하게 우리가 정말 원하는 것(보이는가)을 그대로 묻는다.</para>
        /// </summary>
        float VisibleDist(Vector3 focus, float theta, float want, float camUp)
        {
            Vector3 d = Quaternion.Euler(0f, yaw0 + theta, 0f) * Vector3.forward;
            for (float dist = want; dist >= DistMin; dist -= 0.6f)
            {
                Vector3 p = focus + d * dist + Vector3.up * camUp;
                if (!Physics.Linecast(p, focus, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    return dist;
            }
            return DistMin;
        }

        static bool Skipped()
        {
            var kb = Keyboard.current;
            if (kb == null) return false;
            return kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame
                || kb.spaceKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame;
        }

        // ── 카메라 ───────────────────────────────────────────────────────
        /// <summary>
        /// 궤도는 <b>주인공 기준 극좌표</b>로 잡는다 — θ(각)·거리·높이 셋만 구간별로 이으면
        /// 카메라가 알아서 돈다. 월드 좌표로 키를 찍으면 스폰이 바뀔 때마다 다시 찍어야 한다.
        /// </summary>
        void DriveCamera(float time)
        {
            var vcam = main.GameplayVcam;
            if (vcam == null) return;

            float theta, dist, camUp, focusH, fov;
            Vector3 aim;

            float thetaImpact = thetaStart + Mathf.DeltaAngle(thetaStart, thetaEnd) * 0.12f;

            if (time < ImpactT)
            {
                // 낙하 — 카메라는 지상에 서서 떨어지는 사람을 올려다본다. 가까워질수록 뒤로 물러난다.
                float u = Ease(time / FallDur);
                theta  = Mathf.LerpAngle(thetaStart, thetaImpact, u);
                dist   = Mathf.Lerp(distFall, distImpact, u);
                camUp  = Mathf.Lerp(0.3f, 0.55f, u);
                focusH = 1.0f;
                fov    = Mathf.Lerp(FovFall, FovImpact, u);
                aim    = ActorPos(time) + Vector3.up * 0.9f;   // 사람을 계속 화면에 문다
            }
            else if (time < RiseT)
            {
                // 착지 정지 — 아주 조금만 밀어 넣는다(완전히 멈추면 화면이 죽는다).
                float u = Ease((time - ImpactT) / HoldDur);
                theta  = thetaImpact;
                dist   = Mathf.Lerp(distImpact, distImpact - 0.4f, u);
                camUp  = Mathf.Lerp(0.55f, 0.6f, u);
                focusH = Mathf.Lerp(1.0f, 0.85f, u);
                fov    = Mathf.Lerp(FovImpact, FovImpact - 2f, u);
                aim    = ground + Vector3.up * focusH;
            }
            else
            {
                // 기립 + 인계 — 트인 쪽을 따라 돌며 눈높이까지 올라선다.
                float u = Ease(Mathf.Clamp01((time - RiseT) / (RiseDur + HandDur)));
                theta  = Mathf.LerpAngle(thetaImpact, thetaEnd, u);
                dist   = Mathf.Lerp(distImpact - 0.4f, 0f, u);
                camUp  = Mathf.Lerp(0.6f, 0f, u);
                focusH = Mathf.Lerp(0.85f, eyeHeight, u);
                fov    = Mathf.Lerp(FovImpact - 2f, savedLens.FieldOfView, u);
                aim    = ground + Vector3.up * Mathf.Lerp(0.85f, eyeHeight, u);
            }

            Vector3 focus = ground + Vector3.up * focusH;
            // 매 프레임 가시선을 다시 본다 — 궤도가 도는 동안 벽이 새로 끼어들 수 있다.
            dist = VisibleDist(focus, theta, dist, camUp);
            Vector3 dir = Quaternion.Euler(0f, yaw0 + theta, 0f) * Vector3.forward;
            Vector3 pos = focus + dir * dist + Vector3.up * camUp;

            // ★ 시선 벡터가 <b>거의 0이거나 거의 수직</b>이면 LookRotation을 쓰면 안 된다.
            //   첫 시도에서 벽 클램프가 거리를 0까지 줄인 순간 카메라가 주인공 안에 들어갔고,
            //   그때 aim-pos가 거의 0이 되면서 방향이 폭주해 <b>벽만 비추는 프레임</b>이 나왔다.
            //   그런 구간은 조용히 최종 1인칭 방향으로 대체한다(어차피 다음 구간이 그리로 간다).
            Vector3 toAim = aim - pos;
            Vector3 flat = new Vector3(toAim.x, 0f, toAim.z);
            Quaternion rot = flat.sqrMagnitude > 0.01f
                ? Quaternion.LookRotation(toAim.normalized, Vector3.up)
                : Quaternion.Euler(0f, yaw0, 0f);

            // 마지막 구간은 <b>정확한 1인칭 pose로 수렴</b>시킨다. LookAt만 쓰면 거리가 0에
            // 가까워질 때 방향이 폭주하고, 끝나는 순간 Main이 잡는 pose와 어긋나 화면이 튄다.
            if (time > HandT)
            {
                float k = Ease(Mathf.Clamp01((time - HandT) / HandDur));
                Vector3 endPos = ground + Vector3.up * eyeHeight;
                Quaternion endRot = Quaternion.Euler(0f, yaw0, 0f);
                pos = Vector3.Lerp(pos, endPos, k);
                rot = Quaternion.Slerp(rot, endRot, k);
                fov = Mathf.Lerp(fov, savedLens.FieldOfView, k);
            }

            vcam.transform.SetPositionAndRotation(pos, rot);
            var lens = vcam.Lens;
            lens.FieldOfView = fov;
            vcam.Lens = lens;
        }

        /// <summary>
        /// 궤도 반지름을 <b>지형에 막히는 지점까지</b>로 줄인다. 스폰이 벽 앞이거나 좁은 통로면
        /// 9.5m짜리 궤도가 그대로 벽 속으로 들어가 화면이 통째로 검어진다 — 컷신은 맵마다
        /// 좌표를 확인해 줄 수 없으므로(코드로 궤도를 잡는 대가), 이 한 줄이 안전장치다.
        /// 몹·플레이어엔 콜라이더가 없어 이 레이는 지형만 친다.
        /// </summary>
        // ── 주인공 ───────────────────────────────────────────────────────
        /// <summary>시각 t에서의 발밑 위치. 낙하는 등가속(거리 ∝ 시간²)이라 끝에서 가장 빠르다.</summary>
        Vector3 ActorPos(float time)
        {
            if (time >= ImpactT) return ground;
            float u = Mathf.Clamp01(time / FallDur);
            float h = fallHeight * (1f - u * u);
            Vector3 fwd = Quaternion.Euler(0f, yaw0, 0f) * Vector3.forward;
            return ground - fwd * (FallBack * (1f - u)) + Vector3.up * h;
        }

        void DriveActor(float time)
        {
            if (actorRoot == null) return;

            // 카메라가 머리 안으로 들어가기 직전에 몸을 지운다 — 안 지우면 얼굴 내부가 보인다.
            if (time > HandT + HandDur * 0.72f)
            {
                if (actorRoot.gameObject.activeSelf) actorRoot.gameObject.SetActive(false);
                return;
            }

            float tilt = time < ImpactT ? Mathf.Lerp(-16f, 0f, Ease(time / FallDur)) : 0f;
            actorRoot.SetPositionAndRotation(ActorPos(time), Quaternion.Euler(tilt, yaw0, 0f));

            if (jumpClip == null || rig == null) return;

            jumpClip.SampleAnimation(rig.gameObject, PoseTime(time));
            ApplyKneel(KneelWeight(time));
            rig.localPosition = rigBasePos;
            rig.localRotation = rigBaseRot;

            // ── 몸을 실제 좌표에 못 박는다 ──────────────────────────────────
            // 트랜스폼을 되돌리는 것만으로는 <b>부족하다</b>. 휴머노이드 클립의 이동은 상당 부분이
            // <b>골반 뼈</b>에 실려 있어서, 루트를 원위치시켜도 몸은 포즈가 가진 만큼 밀려나 있다.
            // 게다가 이 리그는 120배 스케일이라 그 어긋남이 센티미터가 아니라 <b>미터</b>로 나온다 —
            // 실제로 착지 컷에서 인물이 화면 좌표 0.86(오른쪽 끝)까지 밀려나 벽 뒤로 들어갔다.
            // 그래서 포즈를 적용한 뒤 <b>뼈를 재서</b> 루트를 그만큼 되민다.
            Vector3 want = ActorPos(time);
            if (hipsBone != null)
            {
                Vector3 d = hipsBone.position - actorRoot.position;
                actorRoot.position += new Vector3(want.x - (actorRoot.position.x + d.x), 0f,
                                                  want.z - (actorRoot.position.z + d.z));
            }

            // 착지 이후엔 <b>발을 실제로 바닥에 붙인다</b>. 포즈마다 접지 높이가 다르고(측정: 착지
            // 0.06 · 기립 0.11) 그 차이가 그대로 "떠 보임"으로 나온다.
            if (time >= ImpactT && toeL != null && toeR != null)
            {
                // 무릎 꿇은 자세에선 <b>무릎이 발보다 낮다</b> — 발끝만 보고 맞추면 무릎이 바닥을
                // 뚫는다(실측: 무릎 0.02 vs 바닥 0.07). 접지 후보를 다 보고 제일 낮은 것을 올린다.
                float low = Mathf.Min(toeL.position.y, toeR.position.y);
                if (kneeL != null) low = Mathf.Min(low, kneeL.position.y + KneeRadius);
                actorRoot.position += Vector3.up * (floorY - low);
            }
        }

        /// <summary>
        /// 히어로 랜딩 자세를 얼마나 얹을지(0~1).
        ///
        /// 접지 직전에 <b>빠르게</b> 차오르고(0.12초), 착지·정지 구간 내내 1로 머문 뒤,
        /// 기립 앞부분에서 풀린다. 클립의 착지는 두 발로 쭈그리는 평범한 자세라 이 가중치가
        /// 0이면 레퍼런스의 "한 무릎 + 한 손 짚기"가 안 나온다.
        /// </summary>
        static float KneelWeight(float time)
        {
            const float Snap = 0.12f;
            if (time < ImpactT - Snap) return 0f;
            if (time < ImpactT) return Ease((time - (ImpactT - Snap)) / Snap);
            if (time < RiseT) return 1f;
            return 1f - Ease(Mathf.Clamp01((time - RiseT) / (RiseDur * 0.6f)));
        }

        /// <summary>착지 자세에서 골반을 추가로 내리는 양(m). 튜닝용 손잡이 — 기본은 손대지 않음.</summary>
        public static float LandExtraDrop = 0f;

        // ── 착지 자세 (2026-07-23 분리) ──────────────────────────────────
        // 예전엔 TitleActor.PoseValues를 <b>직접 읽어</b> 타이틀과 한 벌을 공유했다. 화면이
        // 둘인데 값이 하나면 한쪽을 고칠 때 다른 쪽이 딸려 바뀐다 — 실제로 타이틀 자세를
        // 다시 튜닝하자 컷신 착지가 어색해졌다. 그래서 <b>여기에 자기 값을 둔다</b>.
        //
        // 아래 값은 분리 시점에 컷신이 실제로 쓰고 있던 그 자세다(타이틀이 그 값이었을 때
        // 착지가 성립했으므로, 그 상태를 그대로 떠 왔다). 이제 타이틀을 아무리 바꿔도
        // 컷신은 그대로다. 착지만 따로 고치고 싶으면 이 배열만 만지면 된다.
        //
        // 근육 이름은 TitleActor.PoseMuscles와 <b>같은 순서</b>를 쓴다 — 이름 목록까지 복사하면
        // 두 벌이 되어 또 어긋나므로, 이름은 계속 공유하고 <b>값만</b> 분리한다.
        public static float[] LandValues =
        {
            // [착지 자세 갱신, 2026-07-23] 사람이 F7 패널로 직접 맞춘 값. 근육 값을 임의로
            // "정리"하지 말 것 — 0이 아닌 축은 전부 이유가 있다.

            // 왼다리 — 앞으로 내딛는 쪽(-0.39). 좌우로 벌리고(+0.51) 비틀어(-0.39) 발을
            //   바깥으로 눕힌다(발등 -0.50 · 발 비틀기 -0.82).
            //   앞뒤    좌우    비틀기   무릎    정강이비틀기  발등    발비틀기
            -0.39f,  0.51f, -0.39f, -0.47f,  0.00f, -0.50f, -0.82f,
            // 오른다리 — 뒤로 끝까지 뻗는 쪽(+1.00). 벌리고(+0.44) 크게 비틀어(+0.68) 딛는다.
             1.00f,  0.44f,  0.68f, -0.60f,  0.00f, -0.46f,  0.00f,
            // 몸통 — 착지 충격을 <b>가슴에서 끝까지 접어</b>(Chest -1.00) 받는다. 윗가슴도
            //   앞으로(-0.75) 말리고 가슴 좌우(+0.34)로 살짝 비스듬해진다.
            //   Spine   좌우    비틀기   Chest   좌우    비틀기  UpperChest 좌우 비틀기
            -0.24f,  0.00f,  0.00f, -1.00f,  0.34f,  0.00f, -0.75f,  0.00f,  0.00f,
            // 목·머리 — 접힌 상체에서 목만 들어(+0.50) 고개가 처지지 않게 한다.
             0.50f,  0.00f,  0.00f,  0.00f,  0.00f,  0.00f,
            // 왼팔 — 팔꿈치를 펴(0.79) 바닥을 짚는다.
             0.00f,  0.05f, -0.05f,  0.00f, -0.02f,  0.79f,  0.00f,  0.00f,  0.00f,
            // 오른팔 — 팔꿈치를 끝까지 펴(1.00) 뒤로 뻗어(-0.50) 균형을 잡는다.
             0.02f,  0.38f, -0.44f, -0.50f,  0.02f,  1.00f, -0.09f,  0.10f,  0.00f,
        };

        /// <summary>착지 시 골반 낙차(m). 타이틀의 KneelDrop과 분리된 자기 값이다.</summary>
        public static float LandKneelDrop = 0.52f;

        /// <summary>
        /// 착지 자세의 <b>전 근육 목표값</b>. 타이틀 자세(authoring된 축) + 나머지는 0.
        /// 튜닝 패널로 값이 바뀔 수 있으므로 매 프레임 다시 채운다(64칸이라 비용은 무시할 만하다).
        /// </summary>
        float[] kneelTarget;
        void EnsureKneelTarget()
        {
            if (kneelTarget == null || kneelTarget.Length != HumanTrait.MuscleCount)
                kneelTarget = new float[HumanTrait.MuscleCount];
            System.Array.Clear(kneelTarget, 0, kneelTarget.Length);

            var vals = LandValues;   // ★ 타이틀이 아니라 컷신 자기 값
            for (int i = 0; i < kneelMuscle.Length && i < vals.Length; i++)
            {
                int m = kneelMuscle[i];
                if (m < 0 || m >= kneelTarget.Length) continue;
                kneelTarget[m] = Mathf.Clamp(vals[i] + LandExtra(TitleActor.PoseMuscles[i]), -1f, 1f);
            }
        }

        /// <summary>
        /// 컷신 전용 근육 보정(타이틀 값 <b>위에</b> 더한다). 지금은 전부 0 — 손잡이만 남겨 둔다.
        ///
        /// <para>★ 값을 넣어 봤다가 되돌린 자리다. 짚는 손을 지면까지 내리려고 등·어깨를 더
        /// 눕혔더니(spine -0.15 · chest -0.30 · shoulder -0.55) 이미 깊게 튜닝된 값 위에 겹쳐서
        /// 몸이 <b>공처럼 접혔다</b>(렌더로 확인). 타이틀 자세는 렌더해 가며 맞춘 값이라 축 하나만
        /// 건드려도 균형이 무너진다 — 고칠 땐 반드시 한 축씩, 화면을 보면서 할 것.</para>
        /// </summary>
        static float LandExtra(string muscle)
        {
            switch (muscle)
            {
                default: return 0f;
            }
        }

        /// <summary>
        /// 타이틀에서 튜닝한 무릎 자세를 <b>근육 공간에서 섞어</b> 얹는다.
        ///
        /// <para>클립(뼈 트랜스폼)과 authoring 자세(근육값)는 표현이 달라서 그냥은 못 섞는다.
        /// 그래서 클립을 구운 <b>뒤에</b> 그 결과를 <c>GetHumanPose</c>로 근육값으로 되읽고,
        /// 거기에 무릎 자세를 가중치로 보간한 다음 다시 써 넣는다 — 낙하 자세에서 착지 자세로
        /// 자연스럽게 넘어가고, 일어설 땐 클립의 기립 동작으로 되돌아간다.</para>
        /// </summary>
        void ApplyKneel(float w)
        {
            if (poseHandler == null || kneelMuscle == null) return;

            // 칼날 각도(TitleActor.BladeTilt)는 여기서 쓰지 않는다 — 컷신은 칼을 아예 숨긴다.
            if (w <= 0.001f) return;

            poseHandler.GetHumanPose(ref humanPose);   // 방금 구운 클립 자세를 근육값으로 되읽는다
            if (humanPose.muscles == null) return;

            // ★ authoring된 축만 섞으면 <b>안 된다</b>. TitleActor는 매 프레임 <b>전 근육을 0으로 지우고</b>
            //   자기 값만 쓴다 — 즉 그 자세는 "나머지 축은 전부 0"을 전제로 맞춰진 값이다. 처음엔
            //   적어 둔 축만 보간했더니 클립의 나머지 축(허리 비틀림·팔 twist 등)이 그대로 남아
            //   몸이 공처럼 접힌 잡종 자세가 나왔다(렌더로 확인). 그래서 목표를 <b>전체 근육 벡터</b>로
            //   두고 통째로 보간한다 — w=1이면 타이틀과 완전히 같은 자세가 된다.
            EnsureKneelTarget();
            for (int m = 0; m < humanPose.muscles.Length && m < kneelTarget.Length; m++)
                humanPose.muscles[m] = Mathf.Clamp(
                    Mathf.Lerp(humanPose.muscles[m], kneelTarget[m], w), -1f, 1f);

            // 몸통 기준 회전도 바인드 쪽으로 되돌린다(같은 이유 — 자세가 그걸 전제로 맞춰져 있다).
            humanPose.bodyRotation = Quaternion.Slerp(humanPose.bodyRotation, poseBaseRot, w);
            // 무릎을 꿇으면 골반이 내려앉는다 — 근육만으로는 루트 높이가 안 바뀐다(TitleActor와 같은 처리).
            humanPose.bodyPosition -= Vector3.up * ((LandKneelDrop + LandExtraDrop) * w);
            poseHandler.SetHumanPose(ref humanPose);
        }

        /// <summary>클립 한 장 안에서 훑는 시점 — 공중 → (다리 뻗음) → 착지 → 눌림 → 기립.</summary>
        float PoseTime(float time)
        {
            float reachFrom = ImpactT - ReachDur;
            if (time < reachFrom) return Mathf.Lerp(PoseAir0, PoseAir1, Ease(time / Mathf.Max(0.01f, reachFrom)));
            if (time < ImpactT)   return Mathf.Lerp(PoseAir1, PoseLand, Ease((time - reachFrom) / ReachDur));
            if (time < RiseT)     return Mathf.Lerp(PoseLand, PoseSettle, Ease((time - ImpactT) / HoldDur));
            return Mathf.Lerp(PoseSettle, PoseStand, Ease((time - RiseT) / RiseDur));
        }

        void BuildActor()
        {
            var prefab = Resources.Load<GameObject>("PlayerGhost");
            if (prefab == null)
            {
                Debug.LogWarning("[등장 컷신] Resources/PlayerGhost 가 없어 카메라 연출만 재생합니다.");
                return;
            }

            var root = new GameObject("IntroActor");
            root.transform.SetParent(transform, false);
            actorRoot = root.transform;
            actorRoot.localScale = Vector3.one * ActorScale;   // 착지 컷신 캐릭터 크기(PlayerGhost 프리팹은 1)

            var body = Instantiate(prefab, actorRoot);
            body.name = "Actor";
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;

            // ★ 스킨드 메시 바운즈를 <b>매 프레임 다시 계산</b>하게 한다.
            //   Animator를 꺼 두면(포즈를 우리가 굽는 대가) 유니티가 바운즈를 갱신하지 않는다.
            //   그러면 주인공이 상공 16m에서 지상으로 내려와도 바운즈는 처음 자리에 남아 있고,
            //   카메라가 그 낡은 상자를 벗어나는 순간 <b>몸만 통째로 컬링돼 사라진다</b> —
            //   실제로 착지 컷에서 먼지는 보이는데 사람만 없는 화면이 나왔다(화면 좌표는 정중앙이었다).
            foreach (var smr in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.updateWhenOffscreen = true;

            animator = body.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
            {
                Debug.LogWarning("[등장 컷신] PlayerGhost에 휴머노이드 Animator가 없어 바인드 포즈로 떨어집니다.");
                animator = null;
            }
            else
            {
                // ★ Animator를 <b>재운다</b>. 포즈는 우리가 매 프레임 SampleAnimation으로 직접 굽는데,
                //   켜 두면 Animator가 쓴 포즈를 우리가 덮고 다음 프레임에 또 덮여 떨린다(TitleActor와 동일).
                animator.enabled = false;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                rig = animator.transform;
                rigBasePos = rig.localPosition;
                rigBaseRot = rig.localRotation;
                hipsBone = animator.GetBoneTransform(HumanBodyBones.Hips);
                kneeL = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
                toeL = animator.GetBoneTransform(HumanBodyBones.LeftToes)
                    ?? animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                toeR = animator.GetBoneTransform(HumanBodyBones.RightToes)
                    ?? animator.GetBoneTransform(HumanBodyBones.RightFoot);
                jumpClip = Resources.Load<AnimationClip>(ClipJump);
                if (jumpClip == null)
                    Debug.LogWarning($"[등장 컷신] Resources/{ClipJump} 이 없어 바인드 포즈로 재생합니다.");

                // 착지 자세는 <b>컷신 자기 값</b>(LandValues)이다. 근육 <b>이름 목록</b>만 타이틀과
                // 공유하고(순서를 맞추기 위해) 값은 분리돼 있다 — F7로 타이틀을 고쳐도 여기는 안 바뀐다.
                poseHandler = new HumanPoseHandler(animator.avatar, rig);
                poseHandler.GetHumanPose(ref humanPose);   // 바인드 자세 = 기준점
                poseBaseRot = humanPose.bodyRotation;
                kneelMuscle = new int[TitleActor.PoseMuscles.Length];
                for (int i = 0; i < kneelMuscle.Length; i++)
                    kneelMuscle[i] = System.Array.IndexOf(HumanTrait.MuscleName, TitleActor.PoseMuscles[i]);

                // 칼은 <b>숨긴다</b> — 등장 컷신은 맨몸으로 떨어져 착지하는 그림이다.
                // 착지 순간에만 지우면 한 컷 안에서 칼이 사라졌다 다시 나타나 보이므로 컷신 내내 끈다.
                // (1인칭 뷰모델 칼은 인계 시점에 HideViewmodel이 되살린다 — 그쪽은 별개다.)
                var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                var katana = hand != null ? hand.Find("GhostKatana") : null;
                if (katana != null) katana.gameObject.SetActive(false);
            }

            // 아레나 조명은 몹 기준으로 맞춰져 있어 사람 하나를 잡아 주지 않는다.
            // 컷신 동안만 키 + 림 두 장을 켠다(끝나면 같이 사라진다).
            MakeLight("Key", new Vector3(34f, yaw0 + 150f, 0f), new Color(0.86f, 0.92f, 1f), 1.1f);
            MakeLight("Rim", new Vector3(12f, yaw0 - 20f, 0f), new Color(1f, 0.55f, 0.22f), 1.4f);
        }

        void MakeLight(string name, Vector3 euler, Color color, float intensity)
        {
            var go = new GameObject("IntroLight_" + name);
            go.transform.SetParent(transform, false);
            go.transform.localRotation = Quaternion.Euler(euler);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = color;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
        }

        // ── 착지 ─────────────────────────────────────────────────────────
        void Impact()
        {
            impacted = true;
            flash = 1f;
            CombatAudio.CutsceneLanding();
            if (impulse != null) impulse.GenerateImpulseWithForce(LandImpulse);
            EmitDust();
        }

        void BuildDust()
        {
            var go = new GameObject("IntroDust");
            go.transform.SetParent(transform, false);
            go.transform.position = ground;
            dust = go.AddComponent<ParticleSystem>();

            var m = dust.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.16f);
            m.gravityModifier = 0.9f;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.useUnscaledTime = true;     // 컷신은 unscaled로 돈다 — 여기만 scaled면 박자가 어긋난다
            m.maxParticles = 400;
            m.playOnAwake = false;
            m.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1.6f, 1.5f, 1.4f), new Color(1.9f, 0.9f, 0.35f));

            var em = dust.emission; em.enabled = false;   // 수동 Emit만
            var sh = dust.shape;    sh.enabled = false;

            var col = dust.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var pr = dust.GetComponent<ParticleSystemRenderer>();
            pr.material = DustMaterial();   // 가산 + 둥근 알파 — 어두운 아레나에서 빛을 받은 먼지처럼 뜬다
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>
        /// 먼지 재질. 전선 스파크 재질(<see cref="WireMaterials"/>)을 그대로 빌려 썼더니 텍스처가 없어
        /// 입자가 <b>흰 네모</b>로 나왔다(착지 컷에 정사각형이 흩뿌려졌다). 가운데가 밝고 가장자리로
        /// 사라지는 원형 알파를 코드로 만들어 붙인다 — 에셋 없이 먼지처럼 보이게 하는 최소 조건이다.
        /// </summary>
        static Material dustMat;
        static Material DustMaterial()
        {
            if (dustMat != null) return dustMat;

            const int N = 32;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "IntroDustTex" };
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N - 0.5f, dy = (y + 0.5f) / N - 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;         // 0=중심, 1=가장자리
                    float a = Mathf.Clamp01(1f - r);
                    a *= a;                                              // 가장자리로 부드럽게 빠지게
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;

            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                  ?? Shader.Find("Particles/Standard Unlit")
                  ?? Shader.Find("Sprites/Default");
            dustMat = new Material(sh) { name = "IntroDustMat", mainTexture = tex };
            if (dustMat.HasProperty("_BaseMap")) dustMat.SetTexture("_BaseMap", tex);
            dustMat.SetOverrideTag("RenderType", "Transparent");
            if (dustMat.HasProperty("_Surface")) dustMat.SetFloat("_Surface", 1f);
            if (dustMat.HasProperty("_Blend")) dustMat.SetFloat("_Blend", 1f);   // Additive
            dustMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            dustMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            dustMat.SetInt("_ZWrite", 0);
            dustMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            dustMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return dustMat;
        }

        /// <summary>발밑에서 사방으로 퍼지는 고리 + 위로 튀는 파편.</summary>
        void EmitDust()
        {
            if (dust == null) return;
            const int Ring = 54;
            for (int i = 0; i < Ring; i++)
            {
                float a = (i / (float)Ring) * Mathf.PI * 2f + Random.Range(-0.05f, 0.05f);
                Vector3 outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float speed = Random.Range(4.5f, 8.5f);
                dust.Emit(new ParticleSystem.EmitParams
                {
                    position = ground + outward * 0.35f + Vector3.up * 0.06f,
                    velocity = outward * speed + Vector3.up * Random.Range(0.3f, 1.6f),
                    startLifetime = Random.Range(0.4f, 0.85f),
                    startSize = Random.Range(0.05f, 0.14f),
                }, 1);
            }
            for (int i = 0; i < 22; i++)   // 위로 솟는 파편 — 고리만 있으면 납작해 보인다
            {
                Vector3 v = new Vector3(Random.Range(-1.4f, 1.4f), Random.Range(4f, 8f), Random.Range(-1.4f, 1.4f));
                dust.Emit(new ParticleSystem.EmitParams
                {
                    position = ground + new Vector3(Random.Range(-0.4f, 0.4f), 0.1f, Random.Range(-0.4f, 0.4f)),
                    velocity = v,
                    startLifetime = Random.Range(0.5f, 1.0f),
                    startSize = Random.Range(0.03f, 0.09f),
                }, 1);
            }
        }

        // ── 끝내기 ───────────────────────────────────────────────────────
        void Finish()
        {
            if (finished) return;
            finished = true;

            // 시선 인계 — 컷신이 끝난 시선을 그대로 입력에 넘긴다(카메라가 되돌아 튀지 않게).
            if (main != null)
            {
                main.SetLookYaw(yaw0);
                main.SetLookPitch(0f);
                // 컷신이 세워 둔 바닥으로 sim 플레이어도 옮긴다 — 안 그러면 조작이 넘어오는
                // 순간 스폰 높이만큼 뚝 떨어지거나 바닥에 박힌 채 시작한다(실측: 7cm 어긋남).
                main.PlacePlayerAt(ground);
            }
            Restore();

            // 장막·레터박스는 이 프레임에 지우지 않는다 — 마지막 한 겹이 걷히면서 조작이 들어와야
            // "컷신이 끝났다"가 아니라 "컷신이 조작으로 이어졌다"로 읽힌다.
            veilFade = Mathf.Max(veilFade, 3.0f);
            Destroy(gameObject, 0.5f);
        }

        /// <summary>잠금·뷰모델·렌즈를 원래대로. Finish와 OnDestroy 양쪽에서 불려도 안전하다.</summary>
        void Restore()
        {
            Cutscene.Active = false;
            UiVisibility.SuppressedByMenu = false;

            if (viewmodel != null) { viewmodel.SetActive(true); viewmodel = null; }
            if (lensSaved && main != null && main.GameplayVcam != null)
            {
                main.GameplayVcam.Lens = savedLens;
                lensSaved = false;
            }
            if (actorRoot != null) { Destroy(actorRoot.gameObject); actorRoot = null; }
        }

        // ── UI ───────────────────────────────────────────────────────────
        void BuildUi()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 220;   // 타이틀(200)보다 위 — 잔상 없이 확실히 덮는다
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            barTop = NewImage("BarTop", new Color(0.02f, 0.025f, 0.03f, 1f));
            Bar(barTop.rectTransform, 1f);
            barBot = NewImage("BarBot", new Color(0.02f, 0.025f, 0.03f, 1f));
            Bar(barBot.rectTransform, 0f);

            titleText = NewText("Title", 34, TextAnchor.LowerLeft);
            SetRect(titleText, new Vector2(0f, 0f), new Vector2(120f, 132f), new Vector2(900f, 44f));
            titleText.text = "P R E   C O G";
            SetAlpha(titleText, 0f);

            placeText = NewText("Place", 20, TextAnchor.LowerLeft);
            SetRect(placeText, new Vector2(0f, 0f), new Vector2(124f, 104f), new Vector2(900f, 28f));
            placeText.text = "아 레 나  ·  강 하  완 료";
            placeText.color = new Color(0.42f, 0.92f, 1f, 0f);

            hintText = NewText("Hint", 16, TextAnchor.LowerRight);
            SetRect(hintText, new Vector2(1f, 0f), new Vector2(-120f, 108f), new Vector2(600f, 24f));
            hintText.text = "ENTER  건너뛰기";
            hintText.color = new Color(1f, 1f, 1f, 0f);

            loadingText = NewText("Loading", 22, TextAnchor.MiddleCenter);
            SetRect(loadingText, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 34f));
            loadingText.text = "아 레 나  생 성  중";
            loadingText.color = new Color(0.42f, 0.92f, 1f, 0.85f);

            // 섬광 → 장막 순서로 얹는다(장막이 제일 위여야 건너뛰기를 완전히 덮는다).
            flashImg = NewImage("Flash", new Color(1f, 0.96f, 0.9f, 0f));
            Stretch(flashImg.rectTransform);
            veilImg = NewImage("Veil", new Color(0f, 0f, 0f, 1f));
            Stretch(veilImg.rectTransform);
        }

        void DriveUi(float time)
        {
            // 레터박스는 낙하 중에 내려오고, 인계 구간에서 걷힌다.
            float openK = Ease(Mathf.Clamp01(time / 0.5f));
            float closeK = time > HandT ? Ease(Mathf.Clamp01((time - HandT) / HandDur)) : 0f;
            float bar = 96f * openK * (1f - closeK);
            barTop.rectTransform.sizeDelta = new Vector2(0f, bar);
            barBot.rectTransform.sizeDelta = new Vector2(0f, bar);

            // 글자는 착지 직후에 찍히고 기립이 끝날 때 빠진다 — 낙하 중엔 화면을 비워 둔다.
            float txt = Mathf.Clamp01((time - ImpactT - 0.1f) / 0.35f)
                      * (1f - Mathf.Clamp01((time - HandT) / (HandDur * 0.6f)));
            SetAlpha(titleText, txt);
            SetAlpha(placeText, txt * 0.8f);
            SetAlpha(hintText, (1f - closeK) * 0.34f);

            // 섬광은 <b>약하게</b>. 0.65로 넣었더니 착지 프레임이 통째로 하얘져서 정작 보여줘야 할
            // 웅크린 자세와 먼지 고리가 안 보였다 — 충격은 임펄스·먼지·소리가 이미 전달한다.
            var fc = flashImg.color;
            flashImg.color = new Color(fc.r, fc.g, fc.b, flash * flash * 0.22f);
        }

        void FadeVeil()
        {
            if (veilFade > 0f) veil = Mathf.Max(0f, veil - Mathf.Min(Time.unscaledDeltaTime, 0.05f) * veilFade);
            var c = veilImg.color;
            veilImg.color = new Color(c.r, c.g, c.b, veil);
        }

        static void SetAlpha(Graphic g, float a)
        {
            var c = g.color;
            g.color = new Color(c.r, c.g, c.b, a);
        }

        /// <summary>부드럽게 붙는 이징(양끝이 느리다). 컷신 전 구간이 이 하나를 쓴다.</summary>
        static float Ease(float k)
        {
            k = Mathf.Clamp01(k);
            return k * k * (3f - 2f * k);
        }

        Image NewImage(string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.color = color;
            return img;
        }

        Text NewText(string name, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(transform, false);
            var t2 = go.GetComponent<Text>();
            t2.raycastTarget = false;
            t2.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t2.fontSize = size;
            t2.fontStyle = FontStyle.Bold;
            t2.alignment = anchor;
            t2.horizontalOverflow = HorizontalWrapMode.Overflow;
            t2.verticalOverflow = VerticalWrapMode.Overflow;
            t2.color = Color.white;
            return t2;
        }

        /// <summary>화면 모서리 기준 배치 — 피벗을 앵커와 같게 둬야 offset이 "그 모서리에서 얼마"가 된다.</summary>
        static void SetRect(Graphic g, Vector2 anchor, Vector2 offset, Vector2 size)
        {
            var rt = (RectTransform)g.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = offset;
            rt.sizeDelta = size;
        }

        /// <summary>레터박스 한 장 — 가로는 화면을 꽉 채우고, 두께(sizeDelta.y)만 연출이 몬다.</summary>
        static void Bar(RectTransform rt, float edge)
        {
            rt.anchorMin = new Vector2(0f, edge);
            rt.anchorMax = new Vector2(1f, edge);
            rt.pivot = new Vector2(0.5f, edge);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
