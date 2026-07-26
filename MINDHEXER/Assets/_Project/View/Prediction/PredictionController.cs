using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using Game.Sim;
using Game.Prediction;

namespace Game.View
{
    // >>> [예측 세션 대폭 수정, 2026-07-18] 원래 이 파일은 core-controls(KJH)가 만든 버전으로,
    // RoutePreviewStub(같은 폴더, 지금도 그대로 남아있음)이 만든 가짜 루트(근접순/원거리순/
    // 스윕 휴리스틱, 이동만)를 보여주고 확정 시 그냥 로그만 찍고 닫혔다. 이번 세션에 실제
    // Game.Prediction 결과 연결 + Following(1인칭 실제 자동실행) + 정지/액션 잔상을 새로
    // 넣으면서 상당 부분을 고쳤다 — "원래 어땠는지"는 git에서 `git show ecb4d5b:PrecogPrototype/Assets/_Project/View/Prediction/PredictionController.cs`
    // 로 보거나, 같은 폴더의 RoutePreviewStub.cs(안 지움, 지금은 안 씀)를 참고하면 된다.
    // <<< [예측 세션 대폭 수정 — 아래 전체]
    /// <summary>
    /// 예측(예지) 연출 컨트롤러 — View 전용, 예측 봇과 독립.
    /// F: 정지 진입(시간 멈춤+흑백+3인칭) / 진입 중 F: 강조 후보 전환(모든 후보는 진입과
    /// 동시에 함께 애니메이션됨) / 마우스: 궤도 회전 / 좌클릭: 확정 / Esc: 취소.
    /// 확정하면 실제 플레이어가 기록된 궤적을 따라가되 액션 잔상마다 사용자가 직접 입력하고
    /// (Main.FixedUpdate가 TryConsumeFollowingInput을 통해 구동), 카메라는 1인칭으로 그
    /// 실제 위치·시선을 따라간다(Following). Preview 중 경로는 이동하며 페이드되는 트레일로
    /// 보여주고, 실제 행동이 시작되는 ActionEvent 틱마다 정지 잔상을 겹쳐 찍는다(고정 간격
    /// 아님 — 2026-07-20 변경, PREDICTION_CONTRACT.md §3.1.1의 "30틱 간격/선택 후보만" 서술과는
    /// 다름). 액션 이벤트는 ±8틱 Perfect, ±22틱 Good이며 미입력은 Miss로 직접 조작 전환한다.
    /// </summary>
    public class PredictionController
    {
        public enum State { Idle, Preview, Following }
        public State state = State.Idle;
        public bool Frozen => state != State.Idle;

        // >>> [자유 주행, 2026-07-22] Main이 "지금 조작권이 누구에게 있는가"를 묻는 두 프로퍼티.
        // 기존 Following은 기록 입력을 재생하므로 실시간 입력·시점을 통째로 막았지만(Frozen),
        // 자유 주행은 이동·시점의 소유권이 사용자에게 있으므로 막으면 안 된다.
        /// <summary>이동·시점의 소유권이 사용자에게 있는 방식으로 실행 중인가(자유 주행 등).</summary>
        public bool FreerunActive => state == State.Following && Mode.Active
                                     && Mode.Ownership == FollowInputOwnership.LiveInput;
        /// <summary>
        /// 지금 시선이 사용자 것인가. 자유 주행처럼 통째로 넘기는 방식뿐 아니라, 기록 재생
        /// 모드가 <b>슬로우 포켓 동안에만</b> 시선을 빌려주는 경우(모드 11)도 포함한다.
        /// Main이 자동 추종 카메라를 끌지 말지 이 값으로 정한다 — 둘이 동시에 켜지면 서로
        /// 카메라 포즈를 덮어쓴다.
        /// </summary>
        public bool UsesLiveLookCamera => state == State.Following && Mode.Active && Mode.AllowsLiveLook;
        /// <summary>실시간 입력 폴링·1인칭 시점 갱신을 막아야 하는가(Main.Update가 읽는다).</summary>
        public bool BlocksLiveInput => state == State.Preview
                                       || (state == State.Following && !UsesLiveLookCamera);
        // <<< [자유 주행 끝]

        // [보스 엔딩, 2026-07-23] 외부 연출(BossDeathDirector 슬로모)이 배속을 잠시 소유할 때 0 이상.
        // -1 = 소유 안 함(평소). UpdateRhythmTimeScale이 매 프레임 배속을 쓰므로 이 훅 없이는
        // 외부 슬로모가 즉시 1로 되돌려진다. Domain Reload 꺼짐 — 매 플레이 리셋을 명시적으로 한다.
        public static float CutsceneTimeScaleOverride = -1f;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCutsceneOverride() => CutsceneTimeScaleOverride = -1f;

        // [HUD 게이지, 2026-07-22] 예지 자원(0~1). HUD의 PREDICTION 다이얼이 이 값을 읽는다.
        // Idle 동안만 실시간으로 차오르고, 진입에 성공하면 소모된다.
        float charge = 1f;
        public float Charge01 => charge;
        /// <summary>지금 F가 먹는가(하한선 이상 찼는가).</summary>
        public bool ChargeUsable => charge >= PredictionConfig.ChargeMinToUse;
        /// <summary>가득 찼는가 — HUD가 READY 연출을 켜는 기준.</summary>
        public bool ChargeFull => charge >= 1f;
        /// <summary>지금 F를 누르면 몇 초를 내다보는가. HUD 다이얼이 이 값을 보여준다.</summary>
        public float ChargeSeconds => PredictionConfig.ChargeToSeconds(charge);

        Camera cam;
        Camera accentCamera;
        const int PredictionAccentLayer = 30;
        Transform camPose;   // Cinemachine vcam pose(여기 쓰면 Brain이 실제 카메라를 따라감). fx·칼 토글엔 cam 사용.
        readonly FreezeFx fx = new FreezeFx();

        List<PredictedRoute> routes = new List<PredictedRoute>();
        int selected;
        float orbitYaw, orbitPitch;   // 미리보기 3인칭 궤도(마우스)
        /// <summary>[2026-07-22] 미리보기 궤도 거리 — 마우스 휠로 조절한다. 예지 세션을 넘어
        /// 유지해서, 한 번 맞춰둔 거리를 매번 다시 맞추지 않아도 되게 한다.</summary>
        float orbitDist = PredictionConfig.CamDist;

        InputCmd[] followingControls;
        int followingIndex;
        RhythmJudge rhythmJudge;
        struct TimedRhythmInput
        {
            public PredictedActionType type;
            public float realTime;
        }

        readonly List<TimedRhythmInput> rhythmInputs = new List<TimedRhythmInput>(4);
        // [추적 방식 추상화, 2026-07-22] 예전엔 여기서 RhythmModeRuntime·PredictionFreerun을
        // 직접 new 하고 컨트롤러 곳곳에서 `if (freerun.Active)`로 갈랐다(15군데). 지금은
        // FollowModeRegistry가 상태 기계를 소유하고, 컨트롤러는 IFollowMode 훅으로만 묻는다
        // (FollowMode.cs). 모드 추가 시 이 파일을 고칠 일이 없게 하는 게 목적이다.
        readonly RhythmModeRuntime rhythmMode = FollowModeRegistry.Rhythm;
        /// <summary>지금 선택된 추적 방식. 숫자키로 바뀌므로 매번 레지스트리에서 읽는다.</summary>
        static IFollowMode Mode => FollowModeRegistry.Current;
        bool exitAfterFollowingStep;
        bool completedAfterFollowingStep;
        string rhythmFeedback = "";
        float rhythmFeedbackUntil;
        int rhythmSegmentEventIndex = -1;
        int rhythmSegmentStartTick;
        float rhythmSegmentStartRealTime;
        float rhythmSegmentDuration;
        float rhythmSegmentStartScale;
        float rhythmSegmentDecelStart;
        int rhythmWaitEventIndex = -1;
        float rhythmWaitStartRealTime;
        float cameraYawVelocity;
        PredictedRoute followingRoute;
        Texture2D rhythmRingTexture;
        Texture2D missGlitchTexture;
        float missGlitchStartedAt;
        float missGlitchUntil;
        // [보스 EMP, 2026-07-23] EMP로 F가 거부되거나 강제 종료된 순간의 화면 경고 표시 종료 시각.
        float empNoticeUntil;
        /// <summary>[2026-07-22] 1인칭 뷰모델(팔+칼) 렌더러 — ToggleViewmodel이 매번 다시 채운다.</summary>
        readonly List<Renderer> viewmodelRenderers = new List<Renderer>();
        readonly List<Renderer> collectBuffer = new List<Renderer>();
        readonly Dictionary<Renderer, Color> swordOriginalColors = new Dictionary<Renderer, Color>();
        readonly Dictionary<Transform, int> swordOriginalLayers = new Dictionary<Transform, int>();

        // ── 잔상 아바타 ──
        // PredictionController는 MonoBehaviour가 아니라 순수 C# 클래스(Main이 new로 생성)라
        // 인스펙터 슬롯이 없다. 그래서 프리팹은 Resources에서 "이름"으로 로드한다.
        //   → 아무 "Resources" 폴더에 "PlayerGhost"라는 프리팹을 두면 그걸 잔상 메시로 쓴다.
        //   → 없으면(null) 기존 기본 캡슐로 자동 폴백한다.
        const string GhostPrefabResource = "PlayerGhost";
        /// <summary>SkinnedMesh 잔상의 로컬 바운즈 한 변(리그 로컬 단위). 원본 메시가 2cm라
        /// 0.01 정도가 실제 크기 — 넉넉히 키워 프러스텀 컬링을 사실상 끄기 위한 값.</summary>
        const float GhostLocalBoundsSize = 2f;
        GameObject playerGhostPrefab;
        static bool ghostDiagLogged;   // 진단 로그 1회 제한
        // 아바타 피벗 높이 보정(m). 발이 피벗이면 0. 프리팹 루트 Transform으로도 조절 가능.
        float ghostHeightOffset = 0f;

        /// <summary>잔상 오브젝트의 월드 y 오프셋. 캡슐은 중심 피벗이라 반높이를 올리고, 아바타는 ghostHeightOffset으로 조절.</summary>
        float PivotYOffset => playerGhostPrefab != null ? ghostHeightOffset : SimConfig.PlayerHeight * 0.5f;

        // ── 잔상 포즈(움직이는 잔상) ──
        // [잔상 아바타 애니메이션, 2026-07-22] 잔상마다 Animator를 "재생"시키는 게 아니라,
        // Mixamo 클립의 특정 시각 한 프레임만 AnimationClip.SampleAnimation으로 구워 얼린다.
        // 이동 트레일은 경로 진행 거리로 위상을 만들어 잔상마다 다른 시각을 굽기 때문에,
        // 늘어선 잔상 전체가 하나의 걷기 사이클처럼 읽힌다. 액션 잔상은 종류별 클립의
        // 특징 시점 한 장(정지 스냅샷).
        // 전제: PlayerGhost 프리팹의 FBX 자식에 Animator + 휴머노이드 Avatar가 있어야 한다
        // (없으면 아래 rig가 안 잡혀서 전부 조용히 바인드 포즈로 폴백한다).
        AnimationClip ghostRunClip, ghostWalkClip, ghostJumpClip, ghostSlashClip;
        // [대시 포즈 클립 자작, 2026-07-22] Mixamo에 대시 클립이 없어서 방향별 대시 포즈를
        // 직접 구웠다 — Run 클립의 스트라이드 프레임에서 HumanPose(머슬)를 뽑아 손으로 수정한 뒤
        // 1프레임 휴머노이드 클립으로 저장한 것(Editor/GhostDashPoseBaker). "애니메이션"이 아니라
        // 정지 스냅샷이므로 대시 구간 내내 같은 포즈가 유지된다.
        AnimationClip ghostDashFwdClip, ghostDashBackClip, ghostDashLeftClip, ghostDashRightClip;
        // 런지 전용 클립은 아직 없다 — Resources에 "GhostLunge"를 넣으면 쓰고, 없으면 Slash로 대체.
        AnimationClip ghostLungeClip;

        sealed class GhostPoseRig
        {
            public Transform rigRoot;               // Animator가 붙은 GO(=FBX 루트)
            public Vector3 baseLocalPosition;
            public Quaternion baseLocalRotation;
            public AnimationClip lastClip;
            public float lastTime = float.NaN;
        }

        readonly Dictionary<Transform, GhostPoseRig> ghostRigs = new Dictionary<Transform, GhostPoseRig>();
        /// <summary>[자유 주행, 2026-07-22] 깨짐 연출이 스케일을 만지므로 생성 시 원래 크기를 기억한다.</summary>
        readonly Dictionary<Transform, Vector3> ghostBaseScale = new Dictionary<Transform, Vector3>();
        /// <summary>경로별 누적 이동 거리(path와 같은 인덱스). 이동 트레일 클립 위상 계산용.</summary>
        readonly List<float[]> routeDistances = new List<float[]>();

        LineRenderer domeLr;
        readonly List<LineRenderer> lines = new List<LineRenderer>();
        readonly Gradient previewLineGradient = new Gradient();
        readonly GradientColorKey[] previewLineColorKeys = new GradientColorKey[4];
        readonly GradientAlphaKey[] previewLineAlphaKeys =
        {
            new GradientAlphaKey(PredictionConfig.RouteAlphaSel, 0f),
            new GradientAlphaKey(PredictionConfig.RouteAlphaSel, 1f),
        };
        // [예측 세션 수정, 2026-07-20] 이전엔 routes[selected] 하나만 채우는 단일 풀이라
        // Preview 중 후보를 F로 순환해야만 다음 후보가 애니메이션됐다("하나씩 나가는" 문제).
        // 경로별 풀로 바꿔서 Preview 중엔 모든 후보가 동시에 표시되게 한다. Following 중엔
        // 실행 중인 selected 경로만 채워진다(그 외 경로 인덱스는 need=0으로 비어있음).
        readonly List<List<Transform>> ghostMarksByRoute = new List<List<Transform>>();    // 액션 지점 정지 잔상(가독성용)
        Transform startMarker;   // 시작 위치(=나) 표시 캡슐
        /// <summary>다음 노드 자리에 세우는 빛기둥(UpdateModeGuide). 위치는 모드가 정한다.</summary>
        Transform freerunBeacon;
        /// <summary>[모드 9] 3인칭에서 보여주는 실제 플레이어 본체(잔상과 같은 아바타 프리팹).
        /// 1인칭 모드에서는 만들지도 않는다.</summary>
        Transform playerBody;
        readonly List<Transform> revealGhosts = new List<Transform>();       // 경로별 이동 트레일 헤드
        readonly List<List<Transform>> revealAfterimagesByRoute = new List<List<Transform>>();
        // [잔상 페이드인, 2026-07-23] 각 분신이 처음 켜진 실시간 시각. 알파를 0→목표로 올리는 데 쓴다.
        // 숨을 때 제거해 재등장 시 다시 페이드한다. FinishEnter에서 새 예측마다 초기화.
        readonly Dictionary<Transform, float> afterimageFadeStart = new Dictionary<Transform, float>();
        // [예측 세션 수정, 2026-07-20] 매 프레임 new Gradient()를 만들면 GC 압박으로 프레임이
        // 튀어 트레일이 "한 번에 나타나는" 것처럼 보인다 — 선택/비선택 그라디언트를 경로당
        // 한 번만 만들어 캐싱한다(색은 경로 인덱스에 고정이라 재계산할 필요가 없다).
        // 예측 진입(F) 순간 GameObject.CreatePrimitive를 경로 3개 분량 한꺼번에 호출하면
        // 그 한 프레임이 크게 늘어져 reveal 진행률이 "점프"해 보인다 — 게임 시작 시 미리
        // 채워두고, 이후엔 아래 need > PreWarmCount인 드문 경우에만 추가 생성한다.
        // [버그 수정, 2026-07-20] 10이었지만 Full 설정(macroDepth 12)의 실제 경로는
        // actionMarkers/ghostFrames가 13~16개까지 나와 매번 풀이 모자랐다 — Preview 진입
        // 직후 첫 프레임마다 추가 CreatePrimitive가 실행되며 그 비용이 실시간 기준인
        // reveal 타이머(PreviewRevealSeconds)를 갉아먹어 "렉 걸렸다가 팟 하고 경로가
        // 그냥 뜨는" 현상으로 보였다. macroDepth 최대(ForDuration 5초 ≈ 20스텝)까지
        // 여유를 두고 24로 올린다.
        const int PreWarmMarksPerRoute = 24;
        float previewRevealStartRealTime;
        float previewRevealProgress;
        // [예측 세션 추가, 2026-07-21] Enter() 시점부터 재는 1인칭→3인칭 궤도 전환(pull-back)
        // 타이머 — PlaceCamera가 여기서부터 경과 시간을 읽어 위치/회전 이징에 쓴다.
        float enterTransitionStartRealTime;
        Vector3 enterStartCamPos;
        Quaternion enterStartCamRot;
        // [끊김 완화 A안, 2026-07-23] Enter는 연출만 즉시 켜고 무거운 예측 검색(Build)은
        // 다음 프레임 FinishEnter로 미룬다. 그 사이(이 플래그가 true인 동안) Preview는
        // 입력·표시 없이 대기한다.
        bool buildPending;

        public void Init(Camera camera, Transform pose)
        {
            cam = camera;
            camPose = pose;
            fx.Init();
            fx.EnableOnCamera(cam);
            SetupAccentCamera();
            // 잔상 아바타 프리팹 로드(있으면). PreWarmRoutePools보다 먼저 — 풀 생성 시 이 값을 참조한다.
            playerGhostPrefab = Resources.Load<GameObject>(GhostPrefabResource);
            if (playerGhostPrefab != null)
                Debug.Log($"[예측] 잔상 아바타 프리팹 '{GhostPrefabResource}' 로드됨 — 캡슐 대신 아바타로 표시.");
            else
                Debug.LogWarning($"[예측] Resources.Load(\"{GhostPrefabResource}\") 실패 — 프리팹을 못 찾아 기본 캡슐로 표시합니다. " +
                    "프리팹이 'Resources' 폴더 '직속'에 있고 이름이 정확히 'PlayerGhost'인지 확인하세요.");
            LoadGhostClips();
            domeLr = MakeDome();
            startMarker = MakeStartMarker();
            PreWarmRoutePools();
            SetVisible(false);
        }

        /// <summary>경로별 풀(고스트 마크, 이동 트레일)을 게임 시작 시점에 미리 만들어 둔다 —
        /// F를 눌러 Preview에 처음 들어가는 순간 CreatePrimitive 수십 번이 한 프레임에 몰려
        /// 프레임이 늘어지는 것을 막는다(트레일이 "점프"해 보이는 원인 중 하나).</summary>
        void PreWarmRoutePools()
        {
            int routeCount = PredictionConfig.RouteColors.Length;
            for (int ri = 0; ri < routeCount; ri++)
            {
                var ghostPool = new List<Transform>();
                for (int i = 0; i < PreWarmMarksPerRoute; i++)
                {
                    Transform g = MakeGhostMark(); g.gameObject.SetActive(false); ghostPool.Add(g);
                }
                ghostMarksByRoute.Add(ghostPool);

                if (ri != 0) continue; // 이동 잔상은 안전형 경로 하나만 생성한다.
                Transform ghost = MakeRevealGhost();
                ghost.gameObject.SetActive(false);
                revealGhosts.Add(ghost);
                var afterimages = new List<Transform>(PredictionConfig.PreviewAfterimageCount);
                for (int i = 0; i < PredictionConfig.PreviewAfterimageCount; i++)
                {
                    Transform afterimage = MakeRevealGhost();
                    afterimage.name = "PredictRevealAfterimage";
                    afterimage.gameObject.SetActive(false);
                    afterimages.Add(afterimage);
                }
                revealAfterimagesByRoute.Add(afterimages);
            }
        }

        void SetupAccentCamera()
        {
            if (cam == null || accentCamera != null) return;
            int accentMask = 1 << PredictionAccentLayer;
            cam.cullingMask &= ~accentMask;

            var go = new GameObject("PredictionAccentCamera");
            go.transform.SetParent(cam.transform, false);
            accentCamera = go.AddComponent<Camera>();
            accentCamera.CopyFrom(cam);
            accentCamera.cullingMask = accentMask;
            accentCamera.clearFlags = CameraClearFlags.Depth;
            accentCamera.depth = cam.depth + 1f;

            UniversalAdditionalCameraData baseData = cam.GetUniversalAdditionalCameraData();
            UniversalAdditionalCameraData overlayData = accentCamera.GetUniversalAdditionalCameraData();
            overlayData.renderType = CameraRenderType.Overlay;
            overlayData.renderPostProcessing = false;
            if (!baseData.cameraStack.Contains(accentCamera))
                baseData.cameraStack.Add(accentCamera);
        }

        /// <summary>
        /// Main.Update에서 매 프레임 호출.
        /// inputBlocked=true면 키·마우스 입력을 무시한다(개발 콘솔에 타이핑 중 등).
        /// 표시·카메라 갱신은 계속 돌아야 하므로 입력만 막는다.
        /// </summary>
        public void Tick(in SimWorld w, bool inputBlocked = false)
        {
            fx.Update();
            // 기본은 항상 원복 — 아래 Following 분기에서 모드가 원할 때만 다시 켠다.
            HitStop.Suppressed = false;
            UpdateRhythmTimeScale();

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null) return;

            // [보스 EMP, 2026-07-23] 보스의 레이저 충전~발사 동안(BossQuery.EmpActive) 예지가 무력화된다.
            // 사용 중(Preview/Following)에 보스가 충전을 시작하면 그 자리에서 강제 종료(충격파에 끊김).
            // 쿨(Recovery)·숨는 구간에는 예지를 쓸 수 있다.
            if (state != State.Idle && BossQuery.EmpActive(in w))
            {
                empNoticeUntil = Time.unscaledTime + 1.6f;
                Debug.LogWarning("[예측] 보스 EMP 충격파 — 예지가 교란되어 강제 종료됩니다.");
                Exit();
                return;
            }

            // 추적 방식 전환(숫자키)은 실행 중이 아닐 때만 — 도중에 바꾸면 진행 상태가 꼬인다.
            if (!inputBlocked && state != State.Following) FollowModeRegistry.PollSwitch(kb);

            if (state == State.Idle)
            {
                // 예지 자원 재충전 — 슬로모(timeScale)는 예측이 소유하므로 실시간으로 센다.
                if (charge < 1f)
                    charge = Mathf.Min(1f, charge + Time.unscaledDeltaTime / PredictionConfig.ChargeRechargeSeconds);
                if (!inputBlocked && kb.fKey.wasPressedThisFrame && ChargeUsable)
                {
                    // [보스 엔딩] 컷신·엔딩 연출 중엔 무시 — 보스가 죽어 EMP가 풀려도 예지가 열리면 안 됨.
                    if (Cutscene.Active || CutsceneTimeScaleOverride >= 0f) { }
                    // [보스 EMP] 교란 중엔 진입 거부 — 게이지는 소모하지 않고 경고만 띄운다.
                    else if (BossQuery.EmpActive(in w)) empNoticeUntil = Time.unscaledTime + 1.6f;
                    else Enter(in w);
                }
                return;
            }

            if (state == State.Following)
            {
                // [버그 수정, 2026-07-21] 예전엔 좌클릭도 "건너뛰기"로 잡아 Exit()시켰는데, 좌클릭은
                // 리듬 공격 입력(Attack)이기도 해서 화면의 "L-CLICK 공격" 프롬프트가 절대 먹지 않았다
                // — 누르는 순간 CaptureRhythmInputs에 도달하기 전에 예측이 종료됐다. 취소/건너뛰기는
                // Esc 전용으로 두고, 좌클릭은 그대로 리듬 입력으로 흘려보낸다.
                if (!inputBlocked && kb.escapeKey.wasPressedThisFrame)
                { Exit(); return; }   // 건너뛰기(취소)

                // [추적 방식 추상화, 2026-07-22] 모드 종류와 무관한 공통 갱신 — 모드가 실시간
                // 연출·종료 판정을 돌리고, 컨트롤러는 그 결과를 표시에만 반영한다.
                Mode.UpdateFrame(in w, cam);
                if (Mode.WantsExit) { Exit(); return; }
                ApplyModeCursor();
                // 히트스톱은 sim 틱을 통째로 건너뛴다 — 액션이 잦은 모드에서는 "적만
                // 얼어붙은" 것처럼 보이므로 모드가 원하면 끈다.
                HitStop.Suppressed = Mode.SuppressesHitStop;

                // 박자 입력은 기록 재생 + 판정기를 쓰는 방식(모드 1~5)에서만 받는다.
                // 자유 주행은 잔상에 닿아서, 클릭 체인은 잔상을 찍어서 액션이 나가므로 해당 없음.
                if (Mode.Ownership == FollowInputOwnership.RecordedReplay)
                {
                    rhythmMode.Update();
                    CaptureRhythmInputs(kb, mouse);
                }
                else if (!inputBlocked)
                {
                    // 판정기를 안 쓰는 모드는 자기 입력을 직접 받는다(난타의 A/B 등).
                    Mode.CaptureInput(kb, mouse, in w);
                }

                // 실제 이동·전투는 Main.FixedUpdate가 TryConsumeFollowingInput으로 구동한다.
                UpdateGhostMarks(in w);
                UpdateModeGuide(in w);
                UpdatePlayerBody(in w);
                return;
            }

            // ── Preview 중 ──
            // [끊김 완화 로딩 연출] Enter가 켠 3인칭 풀백을 유지하면서, 초록 물결(RadialInvertFx)을
            // 0→최대 톱니파로 <b>반복</b> 재생한다("스캔/로딩" 펄스). 이 반복이 끝나는 순간(잔상 뜨기
            // 직전) 무거운 Build를 돌려 그 프레임의 끊김을 펄스 뒤로 숨기고, FinishEnter가 잔상을
            // 페이드인으로 띄운다. 반복 동안은 입력·경로표시 없이 카메라·물결만 갱신한다.
            if (buildPending)
            {
                PlaceCamera(in w);   // 카메라 풀백(RadialInvert 반경은 아래서 반복 펄스로 덮어씀)
                float loopElapsed = Time.unscaledTime - enterTransitionStartRealTime;
                float phase = (loopElapsed % PredictionConfig.RipplePeriodSeconds)
                              / PredictionConfig.RipplePeriodSeconds;   // 0→1 반복
                RadialInvertFx.SetRadius(phase, PredictionConfig.RadialInvertMaxRadius);
                if (loopElapsed >= PredictionConfig.RippleLoopSeconds) FinishEnter(in w);
                return;
            }
            if (inputBlocked) return;
            if (kb.escapeKey.wasPressedThisFrame) { Exit(); return; }
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) { Confirm(in w); return; }
            if (kb.fKey.wasPressedThisFrame && routes.Count > 0)
            {
                // [예측 세션 수정, 2026-07-20] 모든 후보가 이미 동시에 애니메이션 중이라,
                // F는 강조 대상만 바꾼다 — 예전처럼 reveal 타이머를 리셋하면 나머지 두
                // 후보까지 처음부터 다시 스윕해서 "하나씩 나가는" 것처럼 보인다.
                selected = (selected + 1) % routes.Count;
                LogSelected();
            }

            if (mouse != null)   // 마우스로 플레이어 중심 궤도 회전
            {
                Vector2 md = mouse.delta.ReadValue();
                orbitYaw += md.x * PredictionConfig.OrbitSens;
                orbitPitch = Mathf.Clamp(orbitPitch - md.y * PredictionConfig.OrbitSens,
                                         PredictionConfig.OrbitPitchMin, PredictionConfig.OrbitPitchMax);

                // 휠 줌 — 한 칸이 보통 ±120이라 정규화해서 "노치 수"로 환산한다.
                float notches = mouse.scroll.ReadValue().y / 120f;
                if (Mathf.Abs(notches) > 0.001f)
                    orbitDist = Mathf.Clamp(
                        orbitDist - notches * PredictionConfig.CamZoomPerNotch,
                        PredictionConfig.CamDistMin, PredictionConfig.CamDistMax);
            }

            Render(in w);
            PlaceCamera(in w);
        }

        void Enter(in SimWorld w)
        {
            if (camPose != null)
            {
                enterStartCamPos = camPose.position;
                enterStartCamRot = camPose.rotation;
            }
            else
            {
                float p = Main.Instance != null ? Main.Instance.LookPitch : 0f;
                enterStartCamPos = w.player.pos + Vector3.up * PredictionConfig.CamLookY;
                enterStartCamRot = Quaternion.Euler(p, w.player.yaw, 0f);
            }

            // [끊김 완화 A안, 2026-07-23] 무거운 예측 검색(Build)은 다음 프레임(FinishEnter)으로
            // 미루고, 이 프레임에는 연출만 즉시 켠다 — 3인칭 전환·발동음·흑백·시간정지(state=Preview로
            // Main이 sim 틱을 멈춤). 발동 순간의 "부왕" 연출이 매끄럽게 나가고, Build 프레임의 끊김은
            // 이미 켜진 흑백/정지 화면 뒤로 숨는다. 실제 경로 표시는 FinishEnter가 채운다.
            state = State.Preview;
            buildPending = true;
            fx.SetExecution(false);
            fx.SetActive(true);
            PredictionAudio.Enter();    // 예지 발동음(산데비스탄) — 드론도 여기서 뜬다
            ToggleViewmodel(false);         // 3인칭이라 1인칭 칼 숨김
            orbitYaw = w.player.yaw; orbitPitch = PredictionConfig.OrbitPitchInit;   // 플레이어 뒤에서 시작
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;        // 마우스=궤도 회전
            // [끊김 완화 A안 개선] 전환(초록 퍼짐+3인칭 풀백) 타이머를 지금 시작하고 이 프레임에
            // 곧바로 한 번 그려, 1인칭에서부터 연출을 즉시 재생한다. 무거운 Build는 이 연출이
            // 끝까지 재생된 뒤(FinishEnter) 돈다 — 그래야 끊김이 정지된 연출 화면에 가려진다.
            enterTransitionStartRealTime = Time.unscaledTime;
            PlaceCamera(in w);
        }

        /// <summary>
        /// [끊김 완화 A안] Enter가 켠 연출 뒤, 다음 프레임에 실제 예측을 만들어 미리보기를 완성한다.
        /// 유효 경로가 없으면 이미 켠 연출을 원복하고 종료한다.
        /// </summary>
        void FinishEnter(in SimWorld w)
        {
            buildPending = false;

            // [진단, 2026-07-22] "미리보기가 공중 적을 안 노린다" 원인 특정용 — 원인을 잡으면
            // 이 줄과 AerialTargetingDiagnostic.cs를 함께 지운다.
            AerialTargetingDiagnostic.Report(in w, Main.Instance.Services);

            // 게이지가 곧 예측 지평 — 얼마나 찼는지가 몇 초를 내다볼지를 정한다.
            routes = RealRoutePreview.Build(in w, Main.Instance.Services, PredictionConfig.RouteColors, ChargeSeconds);
            if (routes.Count == 0)
            {
                Debug.LogWarning("[예측] 유효한 경로를 만들지 못해 미리보기를 취소했습니다.");
                Exit();   // Enter가 켠 연출(흑백·드론·3인칭·칼 숨김)을 원복
                return;
            }
            BuildRouteDistances();   // 이동 잔상 클립 위상(거리 기반) 계산용
            // PlanByProfile의 출력 순서나 일부 프로필의 재생 실패 여부와 무관하게
            // 공격형 대표 경로를 기본 강조/확정 대상으로 삼는다.
            selected = 0;
            for (int i = 0; i < routes.Count; i++)
            {
                if (routes[i].profileLabel != "공격형") continue;
                selected = i;
                break;
            }
            previewRevealProgress = 0f;
            afterimageFadeStart.Clear();   // [잔상 페이드인] 새 예측 — 분신들이 처음부터 다시 페이드인
            charge = PredictionConfig.ChargeAfterEnter;   // 경로 확보에 성공한 경우에만 소모
            SetVisible(true);
            BuildLines();
            previewRevealStartRealTime = Time.unscaledTime;
            Debug.Log($"[예측] 진입 — 사정권 {PredictionConfig.Range}, 루트 {routes.Count}개. (F 순환 · 좌클릭 확정 · Esc 취소)");
            LogSelected();
        }

        /// <summary>바깥에서 강제로 닫는다(사망 → 재시작 등). 정상 종료와 같은 경로를 타므로
        /// 배속·카메라·스폰잠금·1인칭 칼이 모두 원복된다.</summary>
        public void Cancel() => Exit();

        void Exit()
        {
            // 해제음 + 드론 내리기. Exit는 여러 경로에서 중복 호출될 수 있어 Idle이면 건너뛴다.
            if (state != State.Idle) PredictionAudio.Exit();
            Mode.End();   // 모드가 Active일 때만 실제로 정리한다(각 구현이 자체 가드)
            HitStop.Suppressed = false;
            buildPending = false;   // [끊김 완화 A안] 미완성 진입 중 종료돼도 대기 플래그를 남기지 않는다
            state = State.Idle;
            followingControls = null;
            followingRoute = null;
            followingIndex = 0;
            rhythmJudge = null;
            rhythmInputs.Clear();
            exitAfterFollowingStep = false;
            completedAfterFollowingStep = false;
            rhythmSegmentEventIndex = -1;
            rhythmWaitEventIndex = -1;
            previewRevealProgress = 0f;
            cameraYawVelocity = 0f;
            RestoreNormalTimeScale();
            if (Main.Instance != null) Main.Instance.SetPredictionSpawnLocked(false);
            fx.SetExecution(false);
            fx.SetActive(false);
            RadialInvertFx.SetRadius(0f, PredictionConfig.RadialInvertMaxRadius);   // 카메라와 함께 즉시 스냅
            SetSwordExecutionStyle(false);
            ToggleViewmodel(true);         // 1인칭 칼 복구
            SetVisible(false);
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        }

        void UpdateRhythmTimeScale()
        {
            // [보스 엔딩, 2026-07-23] 외부 연출이 배속을 소유 중이면 그 값을 그대로 쓰고 끝
            // — 아래 로직이 매 프레임 1로 되돌리는 것을 막는다(BossDeathDirector 슬로모).
            if (CutsceneTimeScaleOverride >= 0f)
            { Time.timeScale = CutsceneTimeScaleOverride; return; }

            float target = 1f;
            // [추적 방식 추상화, 2026-07-22] 배속을 모드가 직접 소유하는 방식들(자유 주행의
            // 처치 슬로모, 클릭 체인의 조준 포켓)은 아래 리듬 페이싱을 통째로 건너뛴다.
            // Time.timeScale에 실제로 쓰는 건 여전히 여기(컨트롤러)뿐이다.
            if (state == State.Following && Mode.Active && Mode.OwnsTimeScale)
            {
                Time.timeScale = Mode.TimeScale;
                return;
            }
            if (state == State.Following)
            {
                int pending = rhythmJudge != null ? rhythmJudge.FirstPendingIndex : -1;
                if (pending >= 0)
                {
                    if (pending != rhythmSegmentEventIndex)
                        BeginRhythmSegment();

                    float elapsedReal = Time.unscaledTime - rhythmSegmentStartRealTime;
                    float u = Mathf.Clamp01(elapsedReal / Mathf.Max(0.01f, rhythmSegmentDuration));
                    if (u >= rhythmSegmentDecelStart)
                    {
                        float span = Mathf.Max(
                            PredictionConfig.RhythmCurveMinSeconds, 1f - rhythmSegmentDecelStart);
                        float decelU = Mathf.Clamp01((u - rhythmSegmentDecelStart) / span);
                        float eased = 0.5f - 0.5f * Mathf.Cos(Mathf.PI * decelU);
                        target = Mathf.Lerp(
                            rhythmSegmentStartScale, PredictionConfig.RhythmMinTimeScale, eased);
                    }
                    else target = rhythmSegmentStartScale;

                    int eventTick = rhythmJudge.GetEvent(pending).tick;
                    target = ApplyMovementPacing(followingIndex, eventTick, target);
                }
                else
                {
                    // [예측 세션 수정, 2026-07-21] 판정 대상 액션이 하나도 없는 구간(적이 없거나
                    // 남은 액션을 모두 처리한 뒤)엔 이 블록 자체가 안 돌아서 걷기 가속·회전 감쇠가
                    // 전혀 적용되지 않고 내내 1배속이었다 — "적 없을 때 예측을 따라가면 밋밋하게
                    // 멈췄다 간다"는 피드백의 원인. eventTick을 충분히 멀리 잡아서(가짜 값) 아래
                    // ApplyMovementPacing이 항상 "판정 임박 아님" 분기(걷기 가속)를 타게 한다.
                    int farEventTick = followingIndex + PredictionConfig.RhythmApproachTicks + 1;
                    target = ApplyMovementPacing(followingIndex, farEventTick, target);
                }
            }
            Time.timeScale = target;
        }

        /// <summary>
        /// 이벤트-근접 감속 커브(target) 위에 틱별 이동/회전 페이싱을 얹는다. 대시·런지 트리거
        /// 직후 몇 틱은 커브를 무시하고 강제로 끌어올려("쫀득한" 스냅) 빠른 구간을 확실히
        /// 빠르게 만들고, 반대로 제자리 회전 구간은 커브에 감쇠를 곱해 더 느리게 만든다.
        /// </summary>
        float ApplyMovementPacing(int tick, int eventTick, float target)
        {
            if (followingRoute == null) return target;

            var markers = followingRoute.actionMarkers;
            for (int i = 0; i < markers.Count; i++)
            {
                PredictedActionType t = markers[i].type;
                bool burstTrigger = t == PredictedActionType.DashForward
                    || t == PredictedActionType.DashBackward
                    || t == PredictedActionType.DashLeft
                    || t == PredictedActionType.DashRight
                    || t == PredictedActionType.Lunge;
                if (!burstTrigger) continue;
                if (tick >= markers[i].tick && tick <= markers[i].tick + PredictionConfig.RhythmBurstTicks)
                    return PredictionConfig.RhythmBurstTimeScale;
            }

            var path = followingRoute.path;
            var yaw = followingRoute.yaw;
            bool turning = false;
            if (path != null && yaw != null && tick >= 0 && tick + 1 < path.Count && tick + 1 < yaw.Count)
            {
                float moveSpeed = Vector3.Distance(path[tick], path[tick + 1]) * SimConfig.TickRate;
                float yawDelta = Mathf.Abs(Mathf.DeltaAngle(yaw[tick], yaw[tick + 1]));
                turning = yawDelta > PredictionConfig.RhythmTurnYawDegPerTick
                    && moveSpeed < PredictionConfig.RhythmTurnMoveSpeedThreshold;
            }
            if (turning) return target * PredictionConfig.RhythmTurnTimeScale;

            // [예측 세션 추가, 2026-07-21] 다음 판정까지 남은 틱이 넉넉하면(=아직 순수 이동
            // 구간) 실시간 기반 이벤트-근접 감속 커브를 무시하고 최고 속도를 유지한다. 짧은
            // 액션들이 줄줄이 이어질 때 각 세그먼트가 자기 duration의 마지막 30%를 감속에
            // 쓰다 보니, 실제로는 다음 액션까지 한참 남았는데도 "멈췄다 가는" 느낌이 났다 —
            // 감속은 실제 판정 틱에 가까울 때(RhythmApproachTicks 이내)만 허용한다. 반응 여유는
            // 시간 배속과 무관하게 TryConsumeFollowingInput의 tick 기반 대기 게이트가 보장한다.
            // [예측 세션 수정, 2026-07-21] 걷는 구간 속도감을 더 키워달라는 피드백 — 이 "멀리
            // 남은 순수 이동" 구간은 RhythmMaxTimeScale(세그먼트 커브 상한, 판정 임박 구간에도
            // 쓰임)보다 한 단계 더 빠른 전용 상한(RhythmWalkTimeScale)을 쓴다.
            int ticksToEvent = eventTick - tick;
            if (ticksToEvent > PredictionConfig.RhythmApproachTicks)
                return PredictionConfig.RhythmWalkTimeScale;

            return target;
        }

        void BeginRhythmSegment()
        {
            rhythmSegmentEventIndex = rhythmJudge != null ? rhythmJudge.FirstPendingIndex : -1;
            rhythmSegmentStartTick = followingIndex;
            rhythmSegmentStartRealTime = Time.unscaledTime;
            rhythmSegmentDuration = PredictionConfig.RhythmNormalMinSeconds;
            if (rhythmSegmentEventIndex >= 0 && rhythmJudge != null)
                rhythmMode.OnBeatChanged(rhythmSegmentEventIndex,
                    rhythmJudge.GetEvent(rhythmSegmentEventIndex).type);
            if (rhythmSegmentEventIndex < 0)
            {
                rhythmSegmentStartScale = 1f;
                rhythmSegmentDecelStart = 1f;
                return;
            }
            if (rhythmSegmentEventIndex == 0)
            {
                // [예측 세션 추가, 2026-07-21] 경로 확정 직후 첫 박자는 강제 타이밍 없이 사용자가
                // 원하는 순간에 직접 시작한다 — 확정하자마자 판정이 들이닥치면 당황스럽다는
                // 피드백. duration/스케일은 오직 접근링 연출용이고(급하게 안 조이게 느슨한 값),
                // 실제 입력 마감(Miss)은 TryConsumeFollowingInput이 pending==0일 때 걸지 않는다.
                rhythmSegmentDuration = PredictionConfig.RhythmFirstBeatDisplaySeconds;
                rhythmSegmentStartScale = 1f;
                rhythmSegmentDecelStart = 1f;
                return;
            }

            int eventTick = rhythmJudge.GetEvent(rhythmSegmentEventIndex).tick;
            float simSeconds = Mathf.Max(0f,
                eventTick - rhythmSegmentStartTick) / (float)SimConfig.TickRate;
            bool combo = IsSamePositionCombo(rhythmSegmentEventIndex);
            rhythmSegmentDuration = combo
                ? Mathf.Clamp(simSeconds + PredictionConfig.RhythmComboReadPadding,
                    PredictionConfig.RhythmComboMinSeconds, PredictionConfig.RhythmComboMaxSeconds)
                : Mathf.Clamp(simSeconds + PredictionConfig.RhythmNormalReadPadding,
                    PredictionConfig.RhythmNormalMinSeconds, PredictionConfig.RhythmNormalMaxSeconds);
            rhythmSegmentDuration = Mathf.Max(rhythmSegmentDuration, simSeconds);

            // [예측 세션 수정, 2026-07-21] 이전엔 평균 속도가 정확히 이벤트 도착 시각과 맞아
            //떨어지게 역산했는데, 그러다 보니 "다음 액션까지 남은 시간이 짧다"고 계산되면
            // 세그먼트 전체가 감속 구간이 되어 순수 이동조차 늘 느릿했다. 클릭 판정 자체는
            // tick 기준으로 별도 처리되니(TryConsumeFollowingInput) 이 실시간 커브가 정확히
            // 안 맞아도 된다 — 그래서 항상 최고 속도로 시작해 마지막 구간(RhythmDecelStartFloor)
            // 에서만 판독 가능하도록 감속한다("다음 액션 나오는 순간까지 빠르게 걷기").
            rhythmSegmentStartScale = PredictionConfig.RhythmMaxTimeScale;
            rhythmSegmentDecelStart = PredictionConfig.RhythmDecelStartFloor;
        }

        bool IsSamePositionCombo(int currentIndex)
        {
            if (followingRoute == null || currentIndex <= 0
                || currentIndex >= followingRoute.actionMarkers.Count)
                return false;
            ActionMarker previous = followingRoute.actionMarkers[currentIndex - 1];
            ActionMarker current = followingRoute.actionMarkers[currentIndex];
            return current.tick - previous.tick <= PredictionConfig.RhythmComboMaxGapTicks
                   && Vector3.Distance(previous.position, current.position)
                      <= PredictionConfig.RhythmComboPositionRadius;
        }

        public void RestoreNormalTimeScale()
        {
            Time.timeScale = 1f;
        }

        void Confirm(in SimWorld w)
        {
            if (routes.Count == 0) { Exit(); return; }
            var r = routes[selected];
            if (r.controls == null || r.controls.Length == 0)
            {
                Debug.LogWarning("[예측] 이 루트엔 재생할 입력이 없음(스텁 데이터?) — 자동실행 없이 닫음.");
                Exit();
                return;
            }
            state = State.Following;
            followingRoute = r;
            followingControls = r.controls;
            followingIndex = 0;

            // [2026-07-22] 시작 위치 마커(=나 아바타)는 미리보기 전용 — 1인칭 실행 중엔 카메라가
            // 그 안에 들어가 화면 전체가 잔상 색으로 덮인다. 실행 진입 시 꺼서 시야를 확보한다.
            if (startMarker != null) startMarker.gameObject.SetActive(false);

            Mode.Begin(r, in w);

            // [추적 방식 추상화, 2026-07-22] RhythmJudge를 쓰지 않는 방식(자유 주행·클릭 체인)은
            // 아래 리듬 세팅을 통째로 건너뛰되, 카메라/연출 전환은 공통으로 쓴다.
            // 클릭 체인은 기록 재생을 쓰지만 판정기는 안 쓴다 — followingControls는 위에서 이미 잡혔다.
            if (Mode.Ownership != FollowInputOwnership.RecordedReplay)
            {
                rhythmJudge = null;
                rhythmInputs.Clear();
                exitAfterFollowingStep = false;
                completedAfterFollowingStep = false;
                rhythmFeedback = "";
                rhythmFeedbackUntil = 0f;
                rhythmSegmentEventIndex = -1;
                rhythmWaitEventIndex = -1;
                fx.SetExecution(false);
                if (Main.Instance != null) Main.Instance.SetPredictionSpawnLocked(true);
                if (camPose != null && Mode.Ownership == FollowInputOwnership.GatedReplay)
                    camPose.rotation = Quaternion.Euler(0f, w.player.yaw, 0f);
                cameraYawVelocity = 0f;
                ApplyFollowingVisuals();
                // 경로 수행 중엔 1인칭 뷰모델 복구 — 단 3인칭 유지 모드(9)는 계속 숨긴다.
                ToggleViewmodel(Mode.CameraMode == FollowCameraMode.FirstPerson);
                SetSwordExecutionStyle(true);
                ApplyModeCursor();
                Debug.Log($"[예측] 루트 {selected} 확정 — {Mode.Name} · {Mode.Hint} (Esc 취소)");
                return;
            }

            var events = new PredictedActionEvent[r.actionMarkers.Count];
            for (int i = 0; i < events.Length; i++)
            {
                ActionMarker marker = r.actionMarkers[i];
                events[i] = new PredictedActionEvent
                {
                    tick = marker.tick,
                    type = marker.type,
                    targetId = marker.targetId,
                };
            }
            rhythmJudge = new RhythmJudge(events);
            rhythmInputs.Clear();   // BeginRoute는 위의 Mode.Begin이 이미 호출했다
            exitAfterFollowingStep = false;
            completedAfterFollowingStep = false;
            rhythmFeedback = "";
            rhythmFeedbackUntil = 0f;
            BeginRhythmSegment();
            fx.SetExecution(false);
            if (Main.Instance != null) Main.Instance.SetPredictionSpawnLocked(true);
            // 3인칭 궤도 → 1인칭 전환 순간은 즉시 스냅(이후 Following 중 겨냥 변화만 서서히 따라간다).
            if (camPose != null) camPose.rotation = Quaternion.Euler(0f, w.player.yaw, 0f);
            cameraYawVelocity = 0f;
            ApplyFollowingVisuals();
            // 1인칭 복귀 — 칼도 다시 보이게. 단 3인칭 유지 모드(9)는 계속 숨긴다.
            ToggleViewmodel(Mode.CameraMode == FollowCameraMode.FirstPerson);
            SetSwordExecutionStyle(true);
            ApplyModeCursor();
            Debug.Log($"[예측] 루트 {selected} 확정 — 리듬 실행 ({r.seconds:0.0}초). " +
                      "Space 점프 · WASD 대시 · 좌클릭 공격 · 우클릭 런지 · Esc 취소");
        }

        void CaptureRhythmInputs(Keyboard kb, Mouse mouse)
        {
            if (kb.spaceKey.wasPressedThisFrame)
                AddRhythmInput(PredictedActionType.Jump);
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                AddRhythmInput(PredictedActionType.Attack);
            if (mouse != null && mouse.rightButton.wasPressedThisFrame)
                AddRhythmInput(PredictedActionType.Lunge);

            if (kb.wKey.wasPressedThisFrame)
                AddRhythmInput(PredictedActionType.DashForward);
            if (kb.sKey.wasPressedThisFrame)
                AddRhythmInput(PredictedActionType.DashBackward);
            if (kb.aKey.wasPressedThisFrame)
                AddRhythmInput(PredictedActionType.DashLeft);
            if (kb.dKey.wasPressedThisFrame)
                AddRhythmInput(PredictedActionType.DashRight);
        }

        void AddRhythmInput(PredictedActionType type)
        {
            // [리듬 재미 실험, 2026-07-22] 원시 입력을 곧바로 판정 큐에 넣지 않고 모드에 통과시킨다.
            // 연타 충전(Mash)·커맨드 중간 입력(Sequence)은 여기서 소비되고 판정에 올라가지 않는다.
            int pending = rhythmJudge != null ? rhythmJudge.FirstPendingIndex : -1;
            if (pending >= 0)
            {
                PredictedActionType expected = rhythmJudge.GetEvent(pending).type;
                if (!rhythmMode.ResolveInput(type, expected, out PredictedActionType resolved)) return;
                type = resolved;
            }

            rhythmInputs.Add(new TimedRhythmInput
            {
                type = type,
                realTime = Time.unscaledTime,
            });
        }

        /// <summary>
        /// Main.FixedUpdate 전용. Following 중이면 이번 틱에 실제로 넣을 기록된 입력을 반환하고
        /// true를 준다. 재생이 끝났으면(또는 Following이 아니면) false를 주고 — 재생이 끝나서
        /// false가 된 경우엔 자동으로 Idle로 돌아간다(Exit 호출).
        /// </summary>
        public bool TryConsumeFollowingInput(out InputCmd cmd)
            => TryConsumeFollowingInput(InputCmd.Empty, out cmd);

        /// <summary>
        /// [자유 주행, 2026-07-22] 위 메서드의 확장. <paramref name="live"/>는 이번 틱의 실제
        /// 사용자 입력이며 자유 주행에서만 쓰인다(그 외 모드는 기록 입력을 재생하므로 무시).
        /// </summary>
        public bool TryConsumeFollowingInput(in InputCmd live, out InputCmd cmd)
        {
            // [추적 방식 추상화, 2026-07-22] 입력 소유권에 따라 세 갈래로 갈린다. 기록 재생 +
            // RhythmJudge를 쓰는 방식(모드 1~5)만 아래 기존 경로를 그대로 탄다.
            if (state == State.Following && Mode.Active
                && Mode.Ownership == FollowInputOwnership.LiveInput)
            {
                // 이동·시점은 사용자 것 그대로, 그 위에 노드 발동만 얹는다.
                cmd = live;
                Mode.TryInject(in Main.Instance.World, ref cmd);
                followingIndex++;   // 잔상 페이드 등 틱 기반 표시가 계속 진행되도록.
                return true;
            }

            if (state == State.Following && Mode.Active
                && Mode.Ownership == FollowInputOwnership.GatedReplay)
            {
                if (followingControls == null || followingIndex >= followingControls.Length)
                { cmd = default; Exit(); return false; }

                if (!Mode.TryAdvanceReplay(followingIndex, in Main.Instance.World))
                {
                    // 모드가 재생을 붙잡았다. 중립 명령을 받을 수 있으면 재생 인덱스만 붙잡고
                    // sim은 계속 굴린다 — 적·투사체가 얼어붙지 않는다. 못 받으면 완전 정지.
                    if (Mode.TryGetHoldCommand(in Main.Instance.World, out InputCmd hold))
                    { cmd = hold; return true; }
                    cmd = default;
                    return false;
                }

                cmd = followingControls[followingIndex++];
                if (followingIndex >= followingControls.Length) completedAfterFollowingStep = true;
                return true;
            }

            if (state != State.Following || followingControls == null || followingIndex >= followingControls.Length)
            {
                cmd = default;
                if (state == State.Following) Exit();   // 재생 끝 — 정상 종료
                return false;
            }

            int tick = followingIndex;
            if (rhythmJudge != null)
            {
                int pending = rhythmJudge.FirstPendingIndex;
                if (pending >= 0)
                {
                    PredictedActionEvent evt = rhythmJudge.GetEvent(pending);
                    float targetRealTime = rhythmSegmentStartRealTime + rhythmSegmentDuration;
                    float earlyGoodSeconds = RhythmJudge.GoodWindowTicks / (float)SimConfig.TickRate;
                    // [예측 세션 수정, 2026-07-21] 실시간(Time.unscaledTime) 기준만으로 "아직 이르다"고
                    // 판단하면, 대시/런지 구간에서 timeScale을 확 끌어올리는 페이싱(RhythmBurstTimeScale)
                    // 때문에 실시간은 목표 시각에 못 미쳤는데 틱(tick)은 이미 evt.tick을 지나쳐버릴 수
                    // 있다 — 그러면 waitingAtEvent 게이트를 아예 안 타서 사용자가 클릭하기 전에
                    // 공격이 그냥 재생돼버린다(싱크 어긋남). tick < evt.tick을 반드시 같이 검사해서
                    // 틱이 이벤트에 도달한 순간부터는 무조건 입력 대기 분기로 넘어가게 한다.
                    if (tick < evt.tick && Time.unscaledTime < targetRealTime - earlyGoodSeconds
                        && rhythmInputs.Count == 0)
                    {
                        // 아직 표시상 Good 창 전이며 입력도 없으므로 평소 경로를 계속 재생한다.
                    }
                    else
                    {
                        bool waitingAtEvent = tick >= evt.tick;
                        int judgementTick = tick;
                        if (waitingAtEvent && rhythmWaitEventIndex != pending)
                        {
                            rhythmWaitEventIndex = pending;
                            rhythmWaitStartRealTime = Time.unscaledTime;
                        }

                        if (waitingAtEvent)
                        {
                            float waited = Time.unscaledTime - rhythmWaitStartRealTime;
                            int lateTicks = Mathf.Clamp(
                                Mathf.RoundToInt(waited / PredictionConfig.RhythmWaitGoodSeconds
                                                 * RhythmJudge.GoodWindowTicks),
                                0, RhythmJudge.GoodWindowTicks + 1);
                            judgementTick = evt.tick + lateTicks;
                        }
                        // [예측 세션 수정, 2026-07-21] 첫 박자(pending==0)는 시간 제한 없이 사용자가
                        // 직접 시작한다 — 아무리 오래 기다렸다 눌러도 정확히 evt.tick으로 제출해
                        // 항상 Perfect로 받아준다(일반 이벤트처럼 대기시간 기반 지각 판정을 적용하면
                        // GoodWindowTicks를 넘겨 영영 거부당할 수 있으므로 별도 처리).
                        bool firstBeat = pending == 0;
                        bool accepted = false;
                        for (int i = 0; i < rhythmInputs.Count; i++)
                        {
                            if (firstBeat && rhythmInputs[i].type != evt.type) continue;
                            int inputTick = firstBeat
                                ? evt.tick
                                : RhythmJudge.MapDisplayTimeToTick(
                                    rhythmInputs[i].realTime, targetRealTime, evt.tick,
                                    PredictionConfig.RhythmWaitGoodSeconds);
                            RhythmJudgement result = rhythmJudge.Submit(
                                rhythmInputs[i].type, inputTick);
                            if (result == RhythmJudgement.Perfect || result == RhythmJudgement.Good)
                            {
                                accepted = true;
                                rhythmMode.OnAccepted(result);
                                rhythmFeedback = result == RhythmJudgement.Perfect ? "PERFECT" : "GOOD";
                                rhythmFeedbackUntil = Time.unscaledTime + 0.45f;
                                CombatAudio.Hit();
                                Debug.Log($"[예측 리듬] {rhythmFeedback} — 사용자 입력으로 " +
                                          $"{evt.type} 실행 (tick {tick})");
                                break;
                            }
                        }
                        rhythmInputs.Clear();

                        if (!accepted)
                        {
                            if (!waitingAtEvent)
                                goto ConsumeFollowingControl;

                            if (firstBeat)
                            {
                                cmd = default;
                                return false;   // 첫 박자는 Miss 없이 계속 대기
                            }

                            int missed = rhythmJudge.CompleteTick(judgementTick);
                            if (missed >= 0)
                            {
                                rhythmFeedback = "MISS";
                                rhythmFeedbackUntil = Time.unscaledTime + 0.45f;
                                missGlitchStartedAt = Time.unscaledTime;
                                missGlitchUntil = missGlitchStartedAt + PredictionConfig.MissGlitchSeconds;
                                CombatAudio.PlayerHurt();
                                rhythmMode.OnMissed();
                                // [리듬 재미 실험, 2026-07-22] 모드에 따라 Miss의 무게가 다르다.
                                // Classic/Mash/Sequence는 기존대로 예지가 풀리고 직접 조작으로 넘어가지만,
                                // Freestyle/Highway는 리듬게임처럼 "곡이 계속 간다" — 콤보만 끊기고
                                // 그 액션은 예측대로 자동 실행되며 경로를 계속 따라간다.
                                if (rhythmMode.MissEndsFollowing)
                                {
                                    Debug.LogWarning("[예측 리듬] Miss — 액션을 실행하지 않고 직접 조작으로 전환");
                                    Exit();
                                    cmd = default;
                                    return false;
                                }
                                Debug.LogWarning("[예측 리듬] Miss — 콤보만 끊고 경로는 계속 진행");
                                goto ConsumeFollowingControl;
                            }
                            cmd = default;
                            return false;
                        }
                        rhythmWaitEventIndex = -1;
                    }
                }
            }

        ConsumeFollowingControl:
            cmd = followingControls[followingIndex++];
            if (followingIndex >= followingControls.Length)
                completedAfterFollowingStep = true;
            return true;
        }

        public void AfterFollowingStep()
        {
            if (state == State.Following && (exitAfterFollowingStep || completedAfterFollowingStep))
                Exit();
        }

        public void DrawRhythmHud()
        {
            if (UiVisibility.Skip) return;   // 콘솔 `ui off` / 임시 UI 숨김을 리듬 HUD도 따른다
            DrawMissGlitch();
            DrawEmpNotice();
            RhythmModeRuntime.DrawModeBadge(state == State.Following);
            // 박자 HUD 대신 모드 전용 HUD를 그리는 방식(자유 주행의 방향 안내, 클릭 체인의 조준점).
            if (Mode.Active && Mode.ReplacesDefaultHud)
            {
                Mode.DrawHud(in Main.Instance.World, cam);
                return;
            }
            bool feedbackActive = Time.unscaledTime < rhythmFeedbackUntil;
            if (state != State.Following || rhythmJudge == null)
            {
                if (feedbackActive) DrawRhythmFeedback();
                return;
            }
            int current = rhythmJudge.FirstPendingIndex;
            if (current < 0)
            {
                if (feedbackActive) DrawRhythmFeedback();
                return;
            }

            EnsureRhythmRingTexture();
            PredictedActionEvent evt = rhythmJudge.GetEvent(current);
            float progress = Mathf.Clamp01(
                (Time.unscaledTime - rhythmSegmentStartRealTime)
                / Mathf.Max(0.01f, rhythmSegmentDuration));
            float centerX = Screen.width * 0.5f;
            float centerY = Screen.height * 0.5f;

            // [리듬 재미 실험, 2026-07-22] Highway 모드는 접근링 대신 노트 레일 HUD를 쓴다.
            if (rhythmMode.ReplacesDefaultHud)
            {
                rhythmMode.DrawHighway(rhythmJudge, current, progress);
                DrawExecutionSpeedFx(progress);
                if (feedbackActive) DrawRhythmFeedback();
                return;
            }
            float targetSize = Mathf.Clamp(Screen.height * 0.16f, 105f, 150f);
            float approachSize = Mathf.Lerp(targetSize * 2.35f, targetSize, progress);

            Color oldColor = GUI.color;
            GUI.color = new Color(0.3f, 1f, 0.72f, 0.5f);
            GUI.DrawTexture(CenteredRect(centerX, centerY, targetSize), rhythmRingTexture);
            GUI.color = Color.Lerp(
                new Color(0.25f, 1f, 0.55f, 0.85f), Color.white, progress * progress);
            GUI.DrawTexture(CenteredRect(centerX, centerY, approachSize), rhythmRingTexture);
            GUI.color = oldColor;

            var centerStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.043f, 34f, 54f)),
                fontStyle = FontStyle.Bold,
                richText = true,
            };
            string prompt = progress >= 0.94f
                ? $"{ShortInputGuide(evt.type)}\n<size=22>NOW</size>"
                : ShortInputGuide(evt.type);
            GUI.Label(new Rect(centerX - 230f, centerY - 42f, 460f, 84f),
                $"<color=white>{prompt}</color>", centerStyle);

            DrawRhythmSequence(current, centerX, centerY);
            rhythmMode.DrawBeatOverlay(centerX, centerY, progress);
            DrawExecutionSpeedFx(progress);
            if (feedbackActive) DrawRhythmFeedback();
        }

        static Rect CenteredRect(float x, float y, float size)
            => new Rect(x - size * 0.5f, y - size * 0.5f, size, size);

        void EnsureRhythmRingTexture()
        {
            if (rhythmRingTexture != null) return;
            const int size = 128;
            rhythmRingTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "PredictionRhythmRing",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color32[size * size];
            Vector2 center = Vector2.one * ((size - 1) * 0.5f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float radius = Vector2.Distance(new Vector2(x, y), center) / (size * 0.5f);
                float alpha = Mathf.Clamp01(1f - Mathf.Abs(radius - 0.86f) / 0.055f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
            rhythmRingTexture.SetPixels32(pixels);
            rhythmRingTexture.Apply(false, true);
        }

        void DrawRhythmSequence(int current, float centerX, float centerY)
        {
            float sideOffset = Mathf.Clamp(Screen.width * 0.19f, 190f, 320f);
            float sideWidth = Mathf.Clamp(Screen.width * 0.16f, 150f, 230f);
            const float sideHeight = 82f;
            var sideStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.height * 0.03f, 24f, 36f)),
                fontStyle = FontStyle.Bold,
                richText = true,
            };

            Rect left = new Rect(centerX - sideOffset - sideWidth * 0.5f,
                centerY - sideHeight * 0.5f, sideWidth, sideHeight);
            Rect right = new Rect(centerX + sideOffset - sideWidth * 0.5f,
                centerY - sideHeight * 0.5f, sideWidth, sideHeight);
            Color old = GUI.color;
            GUI.color = new Color(0.08f, 0.18f, 0.16f, PredictionConfig.RhythmSidePromptAlpha);
            GUI.Box(left, GUIContent.none);
            GUI.Box(right, GUIContent.none);
            GUI.color = old;

            string previous = current > 0
                ? ShortInputGuide(rhythmJudge.GetEvent(current - 1).type)
                : "—";
            string next = current + 1 < rhythmJudge.Count
                ? ShortInputGuide(rhythmJudge.GetEvent(current + 1).type)
                : "—";
            GUI.Label(left,
                $"<size=15><color=#74A99A80>PREV</color></size>\n<color=#B7D8CE88>{previous}</color>",
                sideStyle);
            GUI.Label(right,
                $"<size=15><color=#74A99A80>NEXT</color></size>\n<color=#B7D8CE88>{next}</color>",
                sideStyle);

            string label = IsSamePositionCombo(current) ? "RAPID COMBO" : "NEXT BEAT";
            sideStyle.fontSize = 22;
            GUI.Label(new Rect(centerX - 180f, centerY - 116f, 360f, 34f),
                $"<color=#50FF9A>{label}</color>", sideStyle);
        }

        void DrawExecutionSpeedFx(float beatProgress)
        {
            float pulse = 0.55f + 0.45f * Mathf.Sin(
                (Time.unscaledTime * PredictionConfig.ExecutionSpeedLineRate + beatProgress)
                * Mathf.PI * 2f);
            Color old = GUI.color;
            GUI.color = new Color(0.25f, 1f, 0.72f,
                PredictionConfig.ExecutionSpeedLineAlpha * pulse);
            Texture2D white = Texture2D.whiteTexture;
            const int streaks = 9;
            for (int i = 0; i < streaks; i++)
            {
                float lane = (i + 0.5f) / streaks;
                float travel = Mathf.Repeat(
                    Time.unscaledTime * PredictionConfig.ExecutionSpeedLineRate + i * 0.137f, 1f);
                float y = Mathf.Lerp(Screen.height * 0.1f, Screen.height * 0.9f, lane);
                float width = Mathf.Lerp(42f, 150f, travel);
                float edgeInset = Mathf.Lerp(12f, Screen.width * 0.12f, travel);
                GUI.DrawTexture(new Rect(edgeInset, y, width, 2f), white);
                GUI.DrawTexture(new Rect(Screen.width - edgeInset - width, y, width, 2f), white);
            }
            GUI.color = old;
        }

        void DrawRhythmFeedback()
        {
            float width = Mathf.Min(980f, Screen.width - 24f);
            float x = (Screen.width - width) * 0.5f;
            float y = Mathf.Max(18f, Screen.height * 0.06f) + 218f;
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 68,
                fontStyle = FontStyle.Bold,
                richText = true,
            };
            string color = rhythmFeedback == "PERFECT" ? "#dfffff"
                : rhythmFeedback == "GOOD" ? "#40ffd8" : "#ff4038";
            GUI.Label(new Rect(x, y, width, 76f),
                $"<color={color}>{rhythmFeedback}</color>", style);
        }

        /// <summary>[보스 EMP, 2026-07-23] F 거부/강제 종료 순간의 짧은 화면 경고(깜빡이는 문구).</summary>
        void DrawEmpNotice()
        {
            if (Time.unscaledTime >= empNoticeUntil) return;
            float width = Mathf.Min(980f, Screen.width - 24f);
            float x = (Screen.width - width) * 0.5f;
            float y = Mathf.Max(18f, Screen.height * 0.06f) + 150f;
            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 40,
                fontStyle = FontStyle.Bold,
                richText = true,
            };
            float blink = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 14f));
            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, blink);
            GUI.Label(new Rect(x, y, width, 52f), "<color=#ff4038>예지 교란 — EMP</color>", style);
            GUI.color = old;
        }

        void DrawMissGlitch()
        {
            if (Time.unscaledTime >= missGlitchUntil) return;
            EnsureMissGlitchTexture();

            float elapsed = Time.unscaledTime - missGlitchStartedAt;
            float life = Mathf.Clamp01(elapsed / PredictionConfig.MissGlitchSeconds);
            float alpha = PredictionConfig.MissGlitchMaxAlpha
                * (1f - life) * (0.7f + 0.3f * Mathf.Abs(Mathf.Sin(elapsed * 95f)));
            Color previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            float offsetX = Mathf.Repeat(elapsed * 17.3f, 1f);
            float offsetY = Mathf.Repeat(elapsed * 29.7f, 1f);
            GUI.DrawTextureWithTexCoords(
                new Rect(0f, 0f, Screen.width, Screen.height),
                missGlitchTexture,
                new Rect(offsetX, offsetY, 3f, 3f));

            for (int i = 0; i < 7; i++)
            {
                float wave = Mathf.Repeat(elapsed * (31f + i * 3.7f) + i * 0.173f, 1f);
                float y = wave * Screen.height;
                float h = 4f + (i % 3) * 7f;
                float shift = Mathf.Sin(elapsed * 80f + i * 2.1f) * 0.18f;
                GUI.color = new Color(i % 2 == 0 ? 1f : 0.2f, 0.12f, 0.14f, alpha * 0.75f);
                GUI.DrawTextureWithTexCoords(
                    new Rect(0f, y, Screen.width, h),
                    missGlitchTexture,
                    new Rect(offsetX + shift, offsetY + i * 0.11f, 3f, 0.18f));
            }
            GUI.color = previous;
        }

        void EnsureMissGlitchTexture()
        {
            if (missGlitchTexture != null) return;
            const int size = 128;
            missGlitchTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "PredictionMissGlitch",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
            };
            var pixels = new Color32[size * size];
            var random = new System.Random(19770421);
            for (int i = 0; i < pixels.Length; i++)
            {
                byte value = (byte)random.Next(15, 256);
                byte pixelAlpha = (byte)random.Next(45, 210);
                pixels[i] = new Color32(value, value, value, pixelAlpha);
            }
            missGlitchTexture.SetPixels32(pixels);
            missGlitchTexture.Apply(false, true);
        }

        static string InputGuide(PredictedActionType type)
        {
            switch (type)
            {
                case PredictedActionType.Jump: return "[ SPACE ]  점프";
                case PredictedActionType.DashForward: return "[ W ]  전방 대시";
                case PredictedActionType.DashBackward: return "[ S ]  후방 대시";
                case PredictedActionType.DashLeft: return "[ A ]  좌측 대시";
                case PredictedActionType.DashRight: return "[ D ]  우측 대시";
                case PredictedActionType.Attack: return "[ 좌클릭 ]  공격";
                case PredictedActionType.Lunge: return "[ 우클릭 ]  런지";
                default: return type.ToString();
            }
        }

        static string ShortInputGuide(PredictedActionType type)
        {
            switch (type)
            {
                case PredictedActionType.Jump: return "SPACE";
                case PredictedActionType.DashForward: return "W";
                case PredictedActionType.DashBackward: return "S";
                case PredictedActionType.DashLeft: return "A";
                case PredictedActionType.DashRight: return "D";
                case PredictedActionType.Attack: return "L-CLICK";
                case PredictedActionType.Lunge: return "R-CLICK";
                default: return type.ToString();
            }
        }

        void LogSelected()
        {
            if (routes.Count == 0) return;
            var r = routes[selected];
            // 이제 점수 순위가 아니라 서로 다른 목적(안전형/기회형/공격형)의 대표 경로다 —
            // PredictedRoute.profileLabel(RealRoutePreview가 PlanByProfile에서 채움)을 그대로 표시.
            string nm = !string.IsNullOrEmpty(r.profileLabel) ? r.profileLabel : selected.ToString();
            Debug.Log($"[예측] 선택 루트 {selected}({nm}) — 처치 {r.kills.Count}, {r.seconds:0.0}초");
        }

        // ── 렌더 ──
        void Render(in SimWorld w)
        {
            UpdateDome(w.player.pos);
            previewRevealProgress = Mathf.Clamp01(
                (Time.unscaledTime - previewRevealStartRealTime)
                / Mathf.Max(0.01f, PredictionConfig.PreviewRevealSeconds));
            UpdatePreviewLines(previewRevealProgress);

            if (startMarker != null)   // 정지된 플레이어 위치 = 루트 시작점
            {
                // [2026-07-22] 캡슐에서 실제 아바타로 바뀌면서 피벗 기준이 달라졌다 —
                // 캡슐은 중심 피벗(반높이 보정)이지만 아바타는 발밑이라 PivotYOffset을 쓴다.
                startMarker.position = w.player.pos + Vector3.up * PivotYOffset;
                startMarker.rotation = Quaternion.Euler(0f, w.player.yaw, 0f);
                // 서 있는 포즈 한 장을 구워 바인드 포즈로 굳는 걸 막는다(잔상과 같은 방식).
                PoseGhostByTravel(startMarker, selected, 0);
                SetMarkColor(startMarker, PredictionConfig.StartMarkerColor);
            }

            UpdateRevealTrails(previewRevealProgress);
            UpdateGhostMarks(in w);
        }

        void PlaceCamera(in SimWorld w)
        {
            if (camPose == null) return;
            if (enterTransitionStartRealTime < 0f)
                enterTransitionStartRealTime = Time.unscaledTime;

            Vector3 pivot = w.player.pos + Vector3.up * PredictionConfig.CamLookY;
            Quaternion targetRot = Quaternion.Euler(orbitPitch, orbitYaw, 0f);
            Vector3 direction = -(targetRot * Vector3.forward);
            // [2026-07-22] 고정 CamDist에서 휠로 조절되는 orbitDist로 교체.
            float targetDistance = orbitDist;
            int collisionMask = Physics.DefaultRaycastLayers & ~(1 << PredictionAccentLayer);
            if (Physics.SphereCast(
                pivot,
                PredictionConfig.CamCollisionRadius,
                direction,
                out RaycastHit hit,
                orbitDist,
                collisionMask,
                QueryTriggerInteraction.Ignore))
            {
                targetDistance = Mathf.Clamp(
                    hit.distance - PredictionConfig.CamCollisionPadding,
                    PredictionConfig.CamCollisionMinDistance,
                    orbitDist);
            }

            Vector3 targetPos = pivot + direction * targetDistance;

            // 진입 시점 1인칭 카메라 위치·회전에서 3인칭 궤도 시점까지 위치와 회전을 동시에 부드럽게 이징
            float pullbackElapsed = Time.unscaledTime - enterTransitionStartRealTime;
            float pullbackT = Mathf.Clamp01(pullbackElapsed / PredictionConfig.EnterOrbitPullbackSeconds);
            float pullbackEased = 1f - (1f - pullbackT) * (1f - pullbackT) * (1f - pullbackT);

            camPose.position = Vector3.Lerp(enterStartCamPos, targetPos, pullbackEased);
            camPose.rotation = Quaternion.Slerp(enterStartCamRot, targetRot, pullbackEased);

            // 같은 진행률로 화면 중심에서 산데비스탄 펄스 레이어가 퍼짐
            RadialInvertFx.SetRadius(pullbackEased, PredictionConfig.RadialInvertMaxRadius);
        }

        /// <summary>
        /// Following 단계: 실제로 자동 실행 중인 플레이어의 위치·시선을 1인칭으로 따라간다.
        /// 회전은 즉시 스냅하지 않고 초당 최대 각도(FollowingCamTurnSpeed)로 제한해서 따라가게
        /// 한다 — 예측이 겨냥을 홱 바꿔도 화면이 순간 회전하지 않고, 지금 어느 쪽으로 도는지
        /// 눈으로 좇을 수 있다.
        /// </summary>
        public void UpdateFollowingCameraRenderPose(Vector3 position, float targetYaw)
        {
            if (state != State.Following || camPose == null) return;

            // [추적 방식 추상화, 2026-07-22] 모드가 시선을 지정하면 그쪽을 우선한다
            // (자석 주행이 노드에 닿는 순간 다음 노드로 돌려주는 경우).
            if (Mode.Active && Mode.TryGetCameraYaw(in Main.Instance.World, out float modeYaw))
                targetYaw = modeYaw;

            // [모드 9] 3인칭 유지 — 내 캐릭터가 예측대로 움직이는 걸 뒤에서 본다.
            if (Mode.Active && Mode.CameraMode == FollowCameraMode.ThirdPersonOrbit)
            {
                float orbit = Mathf.SmoothDampAngle(
                    camPose.eulerAngles.y, targetYaw, ref cameraYawVelocity,
                    PredictionConfig.ThirdPersonYawSmooth, PredictionConfig.FollowingCamTurnSpeed,
                    Time.unscaledDeltaTime);
                Quaternion rot = Quaternion.Euler(PredictionConfig.ThirdPersonPitch, orbit, 0f);
                Vector3 pivot = position + Vector3.up * PredictionConfig.ThirdPersonPivotY;
                Vector3 back = -(rot * Vector3.forward);

                // 벽에 파묻히지 않게 — Preview 궤도 카메라와 같은 규칙을 쓴다.
                float distance = PredictionConfig.ThirdPersonDistance;
                int mask = Physics.DefaultRaycastLayers & ~(1 << PredictionAccentLayer);
                if (Physics.SphereCast(pivot, PredictionConfig.CamCollisionRadius, back,
                                       out RaycastHit hit, distance, mask,
                                       QueryTriggerInteraction.Ignore))
                {
                    distance = Mathf.Clamp(hit.distance - PredictionConfig.CamCollisionPadding,
                                           PredictionConfig.CamCollisionMinDistance, distance);
                }

                camPose.position = pivot + back * distance;
                camPose.rotation = rot;
                return;
            }

            camPose.position = position + Vector3.up * PredictionConfig.CamLookY;
            float yaw1 = Mathf.SmoothDampAngle(
                camPose.eulerAngles.y, targetYaw, ref cameraYawVelocity,
                0.12f, PredictionConfig.FollowingCamTurnSpeed, Time.unscaledDeltaTime);
            camPose.rotation = Quaternion.Euler(0f, yaw1, 0f);
        }

        // ── 비주얼 요소 ──
        LineRenderer MakeDome()
        {
            var go = new GameObject("PredictDome");
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true; lr.loop = true;
            lr.widthMultiplier = PredictionConfig.DomeWidth;
            lr.positionCount = 64;
            lr.material = LineMat();
            lr.startColor = lr.endColor = PredictionConfig.DomeColor;
            lr.numCapVertices = 2;
            return lr;
        }

        void UpdateDome(Vector3 center)
        {
            int seg = domeLr.positionCount;
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                float rr = PredictionConfig.Range;
                domeLr.SetPosition(i, center + new Vector3(Mathf.Cos(a) * rr, 0.12f, Mathf.Sin(a) * rr));
            }
        }

        void BuildLines()
        {
            if (!PredictionConfig.ShowRoutePathLine) return;   // 경로 선 끔 — 아예 만들지 않는다
            while (lines.Count < routes.Count)
            {
                var go = new GameObject($"Route_{lines.Count}");
                go.layer = PredictionAccentLayer;
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace = true; lr.numCapVertices = 2; lr.numCornerVertices = 2;
                lr.material = LineMat();
                lines.Add(lr);
            }
            for (int i = 0; i < lines.Count; i++)
            {
                bool used = i == 0 && i < routes.Count;
                lines[i].gameObject.SetActive(used);
                if (!used) continue;
                lines[i].positionCount = 0;
                StyleLine(lines[i], PredictionConfig.RouteColors[0], true);
            }
        }

        void UpdatePreviewLines(float progress)
        {
            if (!PredictionConfig.ShowRoutePathLine) return;   // 경로 선 끔 — 잔상 행렬로 대체
            for (int i = 0; i < lines.Count; i++)
            {
                bool safetyRoute = i == 0 && i < routes.Count;
                lines[i].gameObject.SetActive(safetyRoute);
                if (safetyRoute)
                    SetPreviewLineWindow(lines[i], routes[i], progress);
            }
        }

        void SetPreviewLineWindow(LineRenderer line, PredictedRoute route, float progress)
        {
            float headProgress = Mathf.Clamp01(progress);
            SetLineReveal(line, route, headProgress);
            line.widthMultiplier = PredictionConfig.RouteWidthSel;
            ApplyPreviewLineGradient(line, headProgress);
        }

        void ApplyPreviewLineGradient(LineRenderer line, float progress)
        {
            float p = Mathf.Max(0.0001f, Mathf.Clamp01(progress));
            Color end = PreviewPathColor(p);
            previewLineColorKeys[0] = new GradientColorKey(PredictionConfig.PreviewPathGreen, 0f);
            if (p <= 1f / 3f)
            {
                previewLineColorKeys[1] = new GradientColorKey(
                    Color.Lerp(PredictionConfig.PreviewPathGreen, end, 1f / 3f), 1f / 3f);
                previewLineColorKeys[2] = new GradientColorKey(
                    Color.Lerp(PredictionConfig.PreviewPathGreen, end, 2f / 3f), 2f / 3f);
            }
            else if (p <= 2f / 3f)
            {
                float blueAt = (1f / 3f) / p;
                previewLineColorKeys[1] =
                    new GradientColorKey(PredictionConfig.PreviewPathTeal, blueAt);
                previewLineColorKeys[2] =
                    new GradientColorKey(Color.Lerp(PredictionConfig.PreviewPathTeal, end, 0.5f),
                        Mathf.Lerp(blueAt, 1f, 0.5f));
            }
            else
            {
                previewLineColorKeys[1] = new GradientColorKey(
                    PredictionConfig.PreviewPathTeal, (1f / 3f) / p);
                previewLineColorKeys[2] = new GradientColorKey(
                    PredictionConfig.PreviewPathBlue, (2f / 3f) / p);
            }
            previewLineColorKeys[3] = new GradientColorKey(end, 1f);
            previewLineGradient.SetKeys(previewLineColorKeys, previewLineAlphaKeys);
            line.colorGradient = previewLineGradient;
        }

        static void SetLineReveal(LineRenderer line, PredictedRoute route, float progress)
        {
            if (route == null || route.path.Count == 0)
            {
                line.positionCount = 0;
                return;
            }
            if (route.path.Count == 1)
            {
                line.positionCount = 1;
                line.SetPosition(0, route.path[0] + Vector3.up * 0.15f);
                return;
            }

            float scaled = Mathf.Clamp01(progress) * (route.path.Count - 1);
            int whole = Mathf.FloorToInt(scaled);
            float fraction = scaled - whole;
            int count = Mathf.Min(route.path.Count, whole + 2);
            line.positionCount = count;
            for (int i = 0; i <= whole && i < count; i++)
                line.SetPosition(i, route.path[i] + Vector3.up * 0.15f);
            if (whole + 1 < count)
            {
                Vector3 tip = Vector3.Lerp(route.path[whole], route.path[whole + 1], fraction);
                line.SetPosition(count - 1, tip + Vector3.up * 0.15f);
            }
        }

        static void StyleLine(LineRenderer lr, Color c, bool sel)
        {
            lr.widthMultiplier = sel ? PredictionConfig.RouteWidthSel : PredictionConfig.RouteWidthDim;
            Color col = sel ? c : c * PredictionConfig.RouteDimMul;
            col.a = sel ? PredictionConfig.RouteAlphaSel : PredictionConfig.RouteAlphaDim;   // 반투명 경로선
            lr.startColor = lr.endColor = col;
        }

        void ApplyFollowingVisuals()
        {
            for (int i = 0; i < lines.Count; i++)
            {
                bool chosen = i == selected && i < routes.Count;
                lines[i].gameObject.SetActive(chosen);
                if (chosen)
                {
                    // [예측 세션 수정, 2026-07-21] 이전엔 StyleLine으로 단색(ExecutionRouteColor,
                    // 초록)을 강제해서 Preview 때 보이던 그라데이션이 Following 진입 순간
                    // 사라져 보였다 — 경로 전체가 이미 공개된 상태(progress=1)의 프리뷰
                    // 그라데이션을 그대로 재사용해서 동일하게 보이게 한다.
                    SetLineReveal(lines[i], routes[i], 1f);
                    lines[i].widthMultiplier = PredictionConfig.RouteWidthSel;
                    ApplyPreviewLineGradient(lines[i], 1f);
                }
            }
            for (int i = 0; i < revealGhosts.Count; i++)
            {
                revealGhosts[i].gameObject.SetActive(false);
                if (i >= revealAfterimagesByRoute.Count) continue;
                for (int j = 0; j < revealAfterimagesByRoute[i].Count; j++)
                    revealAfterimagesByRoute[i][j].gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 예지 진입 시 "지금 나" 자리에 세우는 표식.
        ///
        /// [2026-07-22] 예전엔 캡슐 프리미티브였다 — 경로 잔상은 전부 실제 캐릭터 아바타인데
        /// 정작 출발점인 나만 캡슐이라 화면에서 튀었다. 잔상과 같은 <see cref="MakeGhostBody"/>
        /// 를 써서 같은 아바타로 세운다(프리팹이 없으면 예전처럼 캡슐로 자동 폴백).
        /// 색은 잔상과 구분되게 StartMarkerColor를 그대로 유지한다.
        /// </summary>
        Transform MakeStartMarker()
            => MakeGhostBody("PredictStart",
                new Vector3(SimConfig.PlayerRadius * 2f,
                            SimConfig.PlayerHeight * 0.5f,
                            SimConfig.PlayerRadius * 2f));

        /// <summary>아바타 프리팹이 설정돼 있으면 그걸 인스턴스화하고, 없으면 캡슐로 폴백.
        /// 두 경우 모두 Collider를 제거하고 모든 Renderer에 ghostMat을 덮어씌운다.</summary>
        Transform MakeGhostBody(string goName, Vector3 capsuleScale)
        {
            GameObject go;
            if (playerGhostPrefab != null)
            {
                go = Object.Instantiate(playerGhostPrefab);
                foreach (var col in go.GetComponentsInChildren<Collider>(true))
                    Object.Destroy(col);
                var mat = GhostMat();
                var renderers = go.GetComponentsInChildren<Renderer>(true);
                Bounds combined = new Bounds(go.transform.position, Vector3.zero);
                bool hasBounds = false;
                foreach (var rnd in renderers)
                {
                    // 서브메시가 여러 개여도 전부 같은 고스트 머티리얼로 교체
                    var mats = new Material[rnd.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                    rnd.materials = mats;
                    // 리깅(SkinnedMesh) 모델은 루트만 옮기면 바운즈가 원점에 남아 프러스텀 컬링돼
                    // 안 보일 수 있다.
                    // [잔상 밀도 상향, 2026-07-22] 예전엔 updateWhenOffscreen=true로 막았는데,
                    // 그건 "매 프레임 현재 포즈로 바운즈를 CPU 재계산"이라 잔상이 수백 개가 되면
                    // 프레임을 그대로 갉아먹는다. 잔상 포즈는 한 번 찍고 얼어붙으니 매 프레임
                    // 갱신할 이유가 없다 — 대신 로컬 바운즈를 넉넉히 키워 컬링만 사실상 끈다.
                    // (accent 카메라는 레이어 30 = 잔상만 렌더하므로 과다 바운즈의 부작용이 없다.)
                    if (rnd is SkinnedMeshRenderer smr)
                    {
                        smr.updateWhenOffscreen = false;
                        smr.localBounds = new Bounds(Vector3.zero, Vector3.one * GhostLocalBoundsSize);
                    }
                    if (!hasBounds) { combined = rnd.bounds; hasBounds = true; }
                    else combined.Encapsulate(rnd.bounds);
                }
                // ── 일회성 진단 로그: 렌더러 개수·크기·스케일을 실제로 찍어 원인을 특정한다 ──
                if (!ghostDiagLogged)
                {
                    ghostDiagLogged = true;
                    Debug.Log($"[예측/진단] 잔상 프리팹: 렌더러 {renderers.Length}개, " +
                        $"SkinnedMesh {System.Array.Exists(renderers, r => r is SkinnedMeshRenderer)}, " +
                        $"루트 스케일 {go.transform.lossyScale}, 월드 크기(size) {combined.size}, " +
                        $"활성 렌더러 {System.Array.TrueForAll(renderers, r => r.enabled)}");
                }
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Object.Destroy(go.GetComponent<Collider>());
                go.transform.localScale = capsuleScale;
                go.GetComponent<Renderer>().material = GhostMat();
            }
            go.name = goName;
            // ★ 잔상은 accent 카메라가 PredictionAccentLayer(30)만 렌더링해 보여준다.
            //   프리팹은 메시 렌더러가 자식에 있으므로 루트만 바꾸면 자식이 Default에 남아
            //   accent 카메라가 못 잡는다 → 아무것도 안 보인다. 자식까지 전부 레이어를 바꾼다.
            SetLayerRecursive(go.transform, PredictionAccentLayer);
            RegisterGhostRig(go.transform);
            // [자유 주행, 2026-07-22] 깨짐 연출이 스케일을 만지므로 원래 크기를 기억해둔다 —
            // 캡슐 폴백과 프리팹의 기본 스케일이 서로 달라서 Vector3.one으로 되돌리면 안 된다.
            ghostBaseScale[go.transform] = go.transform.localScale;
            return go.transform;
        }

        static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++)
                SetLayerRecursive(t.GetChild(i), layer);
        }

        // ── 잔상 포즈(움직이는 잔상) ──

        void LoadGhostClips()
        {
            ghostRunClip = Resources.Load<AnimationClip>("GhostRun");
            ghostWalkClip = Resources.Load<AnimationClip>("GhostWalk");
            ghostJumpClip = Resources.Load<AnimationClip>("GhostJump");
            ghostSlashClip = Resources.Load<AnimationClip>("GhostSlash");
            ghostDashFwdClip = Resources.Load<AnimationClip>("GhostDashForward");
            ghostDashBackClip = Resources.Load<AnimationClip>("GhostDashBackward");
            ghostDashLeftClip = Resources.Load<AnimationClip>("GhostDashLeft");
            ghostDashRightClip = Resources.Load<AnimationClip>("GhostDashRight");
            ghostLungeClip = Resources.Load<AnimationClip>("GhostLunge");
            // Run이 없으면 Walk로 폴백(둘 다 이동 사이클이라 위상 계산이 그대로 통한다).
            if (ghostRunClip == null) ghostRunClip = ghostWalkClip;
            // 방향별 대시 포즈가 없으면 Run으로 대체(예전 동작). 런지도 전용 포즈(GhostLunge)가
            // 없을 때만 Slash로 대체되는데, 정규화 구간이 0→1이라 이때는 베기 전체가 재생돼 어색하다
            // — 그런 상태면 Tools/예측/찌르기(런지) 잔상 포즈 굽기를 돌릴 것.
            if (ghostDashFwdClip == null) ghostDashFwdClip = ghostRunClip;
            if (ghostDashBackClip == null) ghostDashBackClip = ghostDashFwdClip;
            if (ghostDashLeftClip == null) ghostDashLeftClip = ghostDashFwdClip;
            if (ghostDashRightClip == null) ghostDashRightClip = ghostDashFwdClip;
            if (ghostLungeClip == null) ghostLungeClip = ghostSlashClip;
            if (ghostRunClip == null && ghostJumpClip == null && ghostSlashClip == null)
            {
                Debug.LogWarning("[예측] 잔상 애니 클립(GhostRun/GhostJump/GhostSlash)을 Resources에서 못 찾음 " +
                    "— 잔상은 바인드 포즈(정지)로 표시됩니다.");
                return;
            }
            Debug.Log($"[예측] 잔상 클립 로드 — Run:{(ghostRunClip != null ? ghostRunClip.name : "없음")} " +
                $"Jump:{(ghostJumpClip != null ? ghostJumpClip.name : "없음")} " +
                $"Slash:{(ghostSlashClip != null ? ghostSlashClip.name : "없음")}");
        }

        /// <summary>잔상 인스턴스의 Animator(=휴머노이드 샘플 대상)를 찾아 캐싱한다. 캡슐 폴백이거나
        /// 프리팹에 Animator/Avatar가 없으면 등록하지 않고, 그러면 PoseGhost가 조용히 무시된다.</summary>
        void RegisterGhostRig(Transform body)
        {
            var animator = body.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman) return;
            Transform rigRoot = animator.transform;
            ghostRigs[body] = new GhostPoseRig
            {
                rigRoot = rigRoot,
                baseLocalPosition = rigRoot.localPosition,
                baseLocalRotation = rigRoot.localRotation,
            };
        }

        /// <summary>잔상 하나에 클립의 특정 시각(초) 포즈를 굽는다. 같은 (클립, 양자화된 시각)이면
        /// 재샘플을 건너뛴다 — 정지 잔상은 사실상 한 번만 계산된다.</summary>
        void PoseGhost(Transform body, AnimationClip clip, float timeSeconds)
        {
            if (clip == null || body == null) return;
            GhostPoseRig rig;
            if (!ghostRigs.TryGetValue(body, out rig) || rig.rigRoot == null) return;

            float quantized = Mathf.Round(timeSeconds * PredictionConfig.GhostPoseSampleRate)
                / PredictionConfig.GhostPoseSampleRate;
            if (rig.lastClip == clip && rig.lastTime == quantized) return;
            rig.lastClip = clip;
            rig.lastTime = quantized;

            clip.SampleAnimation(rig.rigRoot.gameObject, quantized);
            // 휴머노이드 샘플은 루트 모션까지 대상 GO 트랜스폼에 얹는다 — 잔상의 월드 위치는
            // 경로(PlaceRevealBody/UpdateGhostMarks)가 결정하므로 위치만 원래대로 되돌린다.
            rig.rigRoot.localPosition = rig.baseLocalPosition;
            rig.rigRoot.localRotation = rig.baseLocalRotation;
            // 참고: 대시 포즈의 "몸통 눕힘"을 클립 RootQ에 담아보려 했으나 SampleAnimation이
            // 휴머노이드 클립의 루트 회전을 적용하지 않는다(실측). 그래서 눕힘은 클립이 아니라
            // 배치 회전(GhostPlacement.pitch/roll)이 담당한다 — PredictionConfig.GhostDash*Pitch/Roll.
        }

        /// <summary>이동 잔상 포즈 — 경로를 실제로 얼마나 걸어왔는지(누적 거리)로 달리기 클립의
        /// 위상을 정한다. 시간이 아니라 거리 기준이라 제자리 회전 구간에서는 다리가 멈춘다.</summary>
        void PoseGhostByTravel(Transform body, int routeIndex, float scaledIndex)
        {
            if (ghostRunClip == null) return;
            float distance = TravelDistanceAt(routeIndex, scaledIndex);
            float cycles = distance / Mathf.Max(0.01f, PredictionConfig.GhostRunStrideMeters);
            PoseGhost(body, ghostRunClip, Mathf.Repeat(cycles, 1f) * ghostRunClip.length);
        }

        /// <summary>액션 한 종류가 "몇 틱 동안, 어느 클립의 어느 구간을" 쓰는지.</summary>
        struct GhostActionPose
        {
            public AnimationClip clip;
            public int windowTicks;
            public float fromNormalized;
            public float toNormalized;
            public float pitchDegrees;   // 진행 방향으로 앞뒤로 눕는 각도(+ 앞 / − 뒤)
            public float endPitchDegrees;// 구간 끝의 눕힘 — 런지처럼 구간 내내 깊어지는 동작용
            public float rollDegrees;    // 옆으로 기우는 각도(옆 대시 — 얼굴은 정면 유지)
        }

        GhostActionPose ActionPoseFor(PredictedActionType type)
        {
            switch (type)
            {
                case PredictedActionType.Attack:
                    return new GhostActionPose
                    {
                        clip = ghostSlashClip,
                        windowTicks = PredictionConfig.GhostAttackWindowTicks,
                        fromNormalized = PredictionConfig.GhostAttackFromNormalized,
                        toNormalized = PredictionConfig.GhostAttackToNormalized,
                    };
                case PredictedActionType.Lunge:
                    return new GhostActionPose
                    {
                        clip = ghostLungeClip,
                        windowTicks = PredictionConfig.GhostLungeWindowTicks,
                        fromNormalized = PredictionConfig.GhostLungeFromNormalized,
                        toNormalized = PredictionConfig.GhostLungeToNormalized,
                        // 준비에서 꽂힘까지 상체가 점점 깊이 눕는다(포즈 클립과 짝).
                        pitchDegrees = PredictionConfig.GhostLungeFromPitch,
                        endPitchDegrees = PredictionConfig.GhostLungeToPitch,
                    };
                case PredictedActionType.Jump:
                    return new GhostActionPose
                    {
                        clip = ghostJumpClip,
                        windowTicks = PredictionConfig.GhostJumpWindowTicks,
                        fromNormalized = PredictionConfig.GhostJumpFromNormalized,
                        toNormalized = PredictionConfig.GhostJumpToNormalized,
                    };
                default:   // Dash 4종 — 방향별 자작 포즈(1프레임)라 구간 내내 같은 스냅샷
                {
                    var dash = new GhostActionPose
                    {
                        clip = DashClipFor(type),
                        windowTicks = PredictionConfig.GhostDashWindowTicks,
                        fromNormalized = 0f,
                        toNormalized = 0f,
                    };
                    switch (type)
                    {
                        case PredictedActionType.DashBackward:
                            dash.pitchDegrees = PredictionConfig.GhostDashBackwardPitch; break;
                        // 옆 대시는 pitch가 아니라 roll — 얼굴은 정면을 본 채 몸만 가는 쪽으로
                        // 기울어야 앞 대시와 구분된다(포즈 클립도 그 전제로 구워져 있다).
                        case PredictedActionType.DashLeft:
                            dash.rollDegrees = PredictionConfig.GhostDashSideRoll; break;
                        case PredictedActionType.DashRight:
                            dash.rollDegrees = -PredictionConfig.GhostDashSideRoll; break;
                        default:
                            dash.pitchDegrees = PredictionConfig.GhostDashForwardPitch; break;
                    }
                    dash.endPitchDegrees = dash.pitchDegrees;   // 대시는 구간 내내 같은 스냅샷
                    return dash;
                }
            }
        }

        AnimationClip DashClipFor(PredictedActionType type)
        {
            switch (type)
            {
                case PredictedActionType.DashBackward: return ghostDashBackClip;
                case PredictedActionType.DashLeft: return ghostDashLeftClip;
                case PredictedActionType.DashRight: return ghostDashRightClip;
                default: return ghostDashFwdClip;
            }
        }

        /// <summary>액션 포즈를 굽고 나온 배치 정보 — 기울기와 "이 잔상은 진행 방향이 아니라
        /// 플레이어 시선(yaw)을 봐야 한다"는 표시.</summary>
        struct GhostPlacement
        {
            public bool posed;
            public float pitchDegrees;
            public float rollDegrees;
            public bool useFacingYaw;
            public float facingYaw;
        }

        /// <summary>이 잔상이 서 있는 경로 위치(tickIndex)가 어떤 액션의 지속 구간 안이면 그
        /// 액션 포즈를 굽는다. 구간 안 여러 잔상이 각각 다른 시점을 굽기 때문에, 늘어선 걸 한
        /// 번에 보면 찌르기·공격 동작이 펼쳐지는 것처럼 보인다(대시는 1프레임 스냅샷이라 유지).
        ///
        /// 액션 잔상은 진행 방향이 아니라 <b>플레이어 시선(yaw)</b>을 향해야 한다 — 옆/뒤 대시
        /// 포즈는 "정면을 본 채 옆으로 밀어낸다"를 전제로 구워져 있어서, 평소처럼 이동 방향으로
        /// 돌려버리면 전부 앞 대시처럼 보인다.</summary>
        GhostPlacement TryPoseGhostByAction(Transform body, PredictedRoute route, float tickIndex)
        {
            var result = new GhostPlacement();
            if (route == null) return result;
            var markers = route.actionMarkers;
            // 구간이 겹치면(연속 콤보) 나중에 시작한 액션이 이긴다.
            for (int i = markers.Count - 1; i >= 0; i--)
            {
                GhostActionPose pose = ActionPoseFor(markers[i].type);
                if (pose.clip == null || pose.windowTicks <= 0) continue;
                float delta = tickIndex - markers[i].tick;
                if (delta < 0f || delta > pose.windowTicks) continue;
                float u = delta / pose.windowTicks;
                PoseGhost(body, pose.clip,
                    Mathf.Lerp(pose.fromNormalized, pose.toNormalized, u) * pose.clip.length);
                result.posed = true;
                result.pitchDegrees = Mathf.Lerp(pose.pitchDegrees, pose.endPitchDegrees, u);
                result.rollDegrees = pose.rollDegrees;
                result.useFacingYaw = true;
                result.facingYaw = FacingYawAt(route, tickIndex, markers[i].yaw);
                return result;
            }
            return result;
        }

        /// <summary>경로의 틱별 yaw에서 시선 각도를 뽑는다(비어 있으면 액션 마커의 yaw로 폴백).</summary>
        static float FacingYawAt(PredictedRoute route, float tickIndex, float fallbackYaw)
        {
            var yaws = route.yaw;
            if (yaws == null || yaws.Count == 0) return fallbackYaw;
            int from = Mathf.Clamp(Mathf.FloorToInt(tickIndex), 0, yaws.Count - 1);
            int to = Mathf.Min(from + 1, yaws.Count - 1);
            return Mathf.LerpAngle(yaws[from], yaws[to], Mathf.Clamp01(tickIndex - from));
        }

        /// <summary>액션 지점 정지 잔상 한 장 — 구간 중간쯤의 대표 포즈를 굽고, 그 포즈와
        /// 짝을 이루는 몸통 눕힘 회전을 돌려준다.</summary>
        Quaternion PoseGhostAtAction(Transform body, PredictedActionType type)
        {
            GhostActionPose pose = ActionPoseFor(type);
            if (pose.clip == null) return Quaternion.identity;
            const float representative = 0.45f;
            PoseGhost(body, pose.clip,
                Mathf.Lerp(pose.fromNormalized, pose.toNormalized, representative) * pose.clip.length);
            return Quaternion.Euler(
                Mathf.Lerp(pose.pitchDegrees, pose.endPitchDegrees, representative), 0f, pose.rollDegrees);
        }

        /// <summary>routes가 갱신될 때마다 경로별 누적 이동 거리를 미리 계산해 둔다(매 프레임
        /// 잔상 수십 개가 참조하므로 프레임 중 재계산은 피한다).</summary>
        void BuildRouteDistances()
        {
            routeDistances.Clear();
            for (int ri = 0; ri < routes.Count; ri++)
            {
                var path = routes[ri].path;
                var cumulative = new float[path.Count];
                for (int i = 1; i < path.Count; i++)
                    cumulative[i] = cumulative[i - 1] + Vector3.Distance(path[i - 1], path[i]);
                routeDistances.Add(cumulative);
            }
        }

        /// <summary>path 상의 (실수) 인덱스 위치까지의 누적 이동 거리(m).</summary>
        float TravelDistanceAt(int routeIndex, float scaledIndex)
        {
            if (routeIndex < 0 || routeIndex >= routeDistances.Count) return 0f;
            float[] cumulative = routeDistances[routeIndex];
            if (cumulative.Length == 0) return 0f;
            int from = Mathf.Clamp(Mathf.FloorToInt(scaledIndex), 0, cumulative.Length - 1);
            int to = Mathf.Min(from + 1, cumulative.Length - 1);
            return Mathf.Lerp(cumulative[from], cumulative[to], Mathf.Clamp01(scaledIndex - from));
        }

        /// <summary>TravelDistanceAt의 역함수 — 누적 거리로 path 상의 (실수) 인덱스를 찾는다.
        /// 남겨두는 분신을 "경로상 일정 거리마다" 찍기 위해 쓴다(이분 탐색, 누적 거리는 단조증가).</summary>
        float PathIndexAtDistance(int routeIndex, float distance)
        {
            if (routeIndex < 0 || routeIndex >= routeDistances.Count) return 0f;
            float[] cumulative = routeDistances[routeIndex];
            if (cumulative.Length < 2) return 0f;
            if (distance <= 0f) return 0f;
            if (distance >= cumulative[cumulative.Length - 1]) return cumulative.Length - 1;

            int low = 0, high = cumulative.Length - 1;
            while (high - low > 1)
            {
                int mid = (low + high) / 2;
                if (cumulative[mid] <= distance) low = mid; else high = mid;
            }
            float span = cumulative[high] - cumulative[low];
            return span > 1e-5f ? low + (distance - cumulative[low]) / span : low;
        }

        Transform MakeRevealGhost()
            => MakeGhostBody("PredictRevealGhost",
                new Vector3(SimConfig.PlayerRadius * 2.2f,
                            SimConfig.PlayerHeight * 0.55f,
                            SimConfig.PlayerRadius * 2.2f));
        // "잔상을 일정 간격으로 찍는" 대신 "움직이는 경로가 사라지는 잔상"을 만드는 부분 —
        // 캡슐이 경로를 따라 이동하고, 지나간 자리는 TrailRenderer가 알파 0으로 페이드되며 남긴다.

        /// <summary>Preview 중 모든 후보가 동시에 자기 경로를 따라 이동하며 트레일을 남긴다
        /// (goal: 후보가 하나씩 순차 재생되지 않고 동시에 나가야 함). 정지 스탬프(액션 지점)는
        /// UpdateGhostMarks가 따로 담당한다.</summary>
        void UpdateRevealTrails(float progress)
        {
            // 보통 PreWarmRoutePools가 이미 RouteColors.Length(3)개를 다 채워둬서 여기서 자랄 일은
            // 없다 — 후보 수가 그 이상으로 늘어나는 미래 변경에 대비한 안전망만 유지한다.
            int revealRouteCount = routes.Count > 0 ? 1 : 0;
            while (revealGhosts.Count < revealRouteCount)
            {
                revealGhosts.Add(MakeRevealGhost());
                var afterimages = new List<Transform>(PredictionConfig.PreviewAfterimageCount);
                for (int i = 0; i < PredictionConfig.PreviewAfterimageCount; i++)
                {
                    Transform afterimage = MakeRevealGhost();
                    afterimage.name = "PredictRevealAfterimage";
                    afterimages.Add(afterimage);
                }
                revealAfterimagesByRoute.Add(afterimages);
            }

            for (int ri = 0; ri < revealGhosts.Count; ri++)
            {
                Transform ghost = revealGhosts[ri];
                PredictedRoute route = ri < routes.Count ? routes[ri] : null;
                bool show = state == State.Preview && route != null && route.path.Count > 0;
                ghost.gameObject.SetActive(show);
                List<Transform> afterimages = revealAfterimagesByRoute[ri];
                if (!show)
                {
                    for (int i = 0; i < afterimages.Count; i++)
                        afterimages[i].gameObject.SetActive(false);
                    continue;
                }

                float scaled = Mathf.Clamp01(progress) * Mathf.Max(0, route.path.Count - 1);
                bool sel = ri == selected;
                Color color = PreviewPathColor(progress);
                color.a = sel ? 0.85f : 0.4f;
                GhostPlacement headPlacement = TryPoseGhostByAction(ghost, route, scaled);
                if (!headPlacement.posed) PoseGhostByTravel(ghost, ri, scaled);
                PlaceRevealBody(ghost, route, scaled, PivotYOffset, headPlacement);
                SetMarkColor(ghost, color);

                // [잔상 유지, 2026-07-22] 헤드를 따라다니는 꼬리가 아니라, 경로상 고정 지점에
                // 일정 거리(StepMeters)마다 한 번 찍히고 그대로 남는 분신들("투사주법"). 위치가
                // 진행률과 무관하게 고정이라 헤드가 지나가는 순간 켜지기만 하고 이후엔 움직이지도
                // 페이드되지도 않는다 — 포즈 샘플링도 각자 한 번씩만 일어난다(캐시 적중).
                float totalDistance = TravelDistanceAt(ri, route.path.Count - 1);
                float step = Mathf.Max(
                    PredictionConfig.PreviewAfterimageStepMeters,
                    totalDistance / Mathf.Max(1, afterimages.Count));
                float headDistance = TravelDistanceAt(ri, scaled);
                for (int i = 0; i < afterimages.Count; i++)
                {
                    float stampDistance = step * (i + 1);
                    Transform afterimage = afterimages[i];
                    // 헤드가 아직 그 지점을 지나지 않았거나, 경로 자체가 거기까지 안 가면 숨김.
                    bool afterVisible = stampDistance <= totalDistance && stampDistance <= headDistance;
                    afterimage.gameObject.SetActive(afterVisible);
                    if (!afterVisible) { afterimageFadeStart.Remove(afterimage); continue; }

                    // [잔상 페이드인] 이 분신이 처음 켜진 시각을 기록하고, 그로부터 경과에 따라 알파를 올린다.
                    if (!afterimageFadeStart.TryGetValue(afterimage, out float shownAt))
                    { shownAt = Time.unscaledTime; afterimageFadeStart[afterimage] = shownAt; }
                    float fadeIn = PredictionConfig.AfterimageFadeInSeconds > 0.001f
                        ? Mathf.Clamp01((Time.unscaledTime - shownAt) / PredictionConfig.AfterimageFadeInSeconds)
                        : 1f;

                    float afterScaled = PathIndexAtDistance(ri, stampDistance);
                    float afterProgress = route.path.Count > 1
                        ? afterScaled / (route.path.Count - 1)
                        : 0f;
                    // 이 지점이 대시·런지·공격·점프 구간 안이면 그 액션 포즈를(구간 내 위치에
                    // 맞는 시점으로), 아니면 평소처럼 이동 거리 기반 달리기 포즈를 굽는다.
                    GhostPlacement placement = TryPoseGhostByAction(afterimage, route, afterScaled);
                    if (!placement.posed) PoseGhostByTravel(afterimage, ri, afterScaled);
                    PlaceRevealBody(afterimage, route, afterScaled, PivotYOffset, placement);
                    Color afterColor = PreviewPathColor(afterProgress);
                    afterColor.a = PredictionConfig.PreviewAfterimageHeadAlpha
                        * (sel ? 1f : PredictionConfig.RouteDimMul)
                        * fadeIn;   // [잔상 페이드인] 등장 직후 0→목표로 알파 램프
                    SetMarkColor(afterimage, afterColor);
                }
            }
        }

        /// <summary>leanDegrees는 진행 방향으로 숙이는 각도 — 대시·런지 잔상이 "달리기"와
        /// 구분되게 몸을 던지는 각도를 준다(월드 기준, 로컬 X축 피치라 부호가 명확하다).</summary>
        static void PlaceRevealBody(
            Transform body, PredictedRoute route, float scaled, float yOffset,
            GhostPlacement placement = default(GhostPlacement))
        {
            int from = Mathf.Clamp(Mathf.FloorToInt(scaled), 0, route.path.Count - 1);
            int to = Mathf.Min(from + 1, route.path.Count - 1);
            float fraction = Mathf.Clamp01(scaled - from);
            body.position = Vector3.Lerp(route.path[from], route.path[to], fraction)
                + Vector3.up * yOffset;

            Quaternion facing;
            if (placement.useFacingYaw)
                facing = Quaternion.Euler(0f, placement.facingYaw, 0f);
            else
            {
                Vector3 direction = route.path[to] - route.path[from];
                if (direction.sqrMagnitude <= 1e-5f) return;   // 제자리면 이전 회전 유지
                facing = Quaternion.LookRotation(direction.normalized, Vector3.up);
            }
            body.rotation = facing
                * Quaternion.Euler(placement.pitchDegrees, 0f, placement.rollDegrees);
        }

        static Color PreviewPathColor(float progress)
        {
            float p = Mathf.Clamp01(progress);
            if (p < 1f / 3f)
                return Color.Lerp(
                    PredictionConfig.PreviewPathGreen,
                    PredictionConfig.PreviewPathTeal,
                    p * 3f);
            if (p < 2f / 3f)
                return Color.Lerp(
                    PredictionConfig.PreviewPathTeal,
                    PredictionConfig.PreviewPathBlue,
                    (p - 1f / 3f) * 3f);
            return Color.Lerp(
                PredictionConfig.PreviewPathBlue,
                PredictionConfig.PreviewPathNavy,
                (p - 2f / 3f) * 3f);
        }

        /// <summary>정지 잔상 — 고정 간격이 아니라 route.ghostFrames(RealRoutePreview가
        /// ActionEvent 틱에서 샘플링)의 위치·yaw를 그대로 가져와 풀링된 캡슐로 배치한다.
        /// Preview 중엔 모든 후보가 동시에 표시되고(경로별 풀), Following 중엔 실행 중인
        /// selected 경로만 표시된다.</summary>
        void UpdateGhostMarks(in SimWorld world)
        {
            while (ghostMarksByRoute.Count < routes.Count) ghostMarksByRoute.Add(new List<Transform>());

            for (int ri = 0; ri < ghostMarksByRoute.Count; ri++)
            {
                List<Transform> pool = ghostMarksByRoute[ri];
                PredictedRoute r = ri < routes.Count ? routes[ri] : null;
                bool routeVisible = r != null
                    && ((state == State.Preview && ri == 0)
                        || (state == State.Following && ri == selected));
                int need = routeVisible ? r.ghostFrames.Count : 0;
                while (pool.Count < need) pool.Add(MakeGhostMark());

                int revealTick = r != null && r.path.Count > 0
                    ? Mathf.RoundToInt(previewRevealProgress * (r.path.Count - 1))
                    : -1;
                for (int i = 0; i < pool.Count; i++)
                {
                    PredictedFrame f = i < need ? r.ghostFrames[i] : default;
                    // [2026-07-22] 실행(Following) 시작 시 첫 잔상(i==0)이 1인칭 카메라 바로 앞에
                    // 서서 시야를 통째로 가린다는 피드백 — 실행 중엔 첫 잔상을 숨긴다.
                    bool used = i < need && (state != State.Preview || f.tick <= revealTick)
                                && !(state == State.Following && i == 0);
                    pool[i].gameObject.SetActive(used);
                    if (!used) continue;
                    // ghostFrames는 actionMarkers와 같은 인덱스로 대응하되 마지막 프레임만
                    // 액션 없이 추가된다(PredictedRoute 주석) — tick이 같을 때만 액션 포즈를
                    // 쓰고, 그 외(마지막 프레임)는 이동 포즈로 굽는다.
                    Quaternion markTilt = Quaternion.identity;
                    if (i < r.actionMarkers.Count && r.actionMarkers[i].tick == f.tick)
                        markTilt = PoseGhostAtAction(pool[i], r.actionMarkers[i].type);
                    else
                        PoseGhostByTravel(pool[i], ri, f.tick);

                    // [자유 주행, 2026-07-22] 잔상이 곧 판정 대상이므로 좌표에 못 박아두지 않는다 —
                    // 대상이 있는 노드는 그 적을 따라가고, 발동한 노드는 부풀며 깨진다.
                    Vector3 markPosition = f.playerPosition;
                    float shatter = 0f;
                    bool modeOwnsVisual = false;
                    bool hasTint = false;
                    Color tint = Color.white;
                    if (Mode.Active && i < r.actionMarkers.Count
                        && Mode.TryGetNodeVisual(i, in world, out FollowNodeVisual visual))
                    {
                        if (!visual.visible)
                        { pool[i].gameObject.SetActive(false); continue; }
                        markPosition = visual.position;
                        shatter = visual.shatter;
                        hasTint = visual.hasTint;
                        tint = visual.tint;
                        modeOwnsVisual = true;
                    }

                    pool[i].position = markPosition + Vector3.up
                        * (PivotYOffset + shatter * PredictionConfig.FreerunShatterRise);
                    pool[i].rotation = Quaternion.Euler(0f, f.playerYaw, 0f) * markTilt;
                    if (modeOwnsVisual && ghostBaseScale.TryGetValue(pool[i], out Vector3 baseScale))
                        pool[i].localScale = baseScale
                            * Mathf.Lerp(1f, PredictionConfig.FreerunShatterScale, shatter);
                    // 모드가 색을 지정했으면(예: "다음에 칠 잔상"을 하얗게) 기본 규칙보다 우선한다.
                    SetMarkColor(pool[i], hasTint
                        ? tint
                        : GhostDisplayColor(r, ri, i, f, world.player.pos));
                }
            }
        }

        Color GhostDisplayColor(
            PredictedRoute route, int routeIndex, int ghostIndex,
            PredictedFrame frame, Vector3 playerPosition)
        {
            if (state == State.Following)
            {
                // [예측 세션 수정, 2026-07-21] 이전엔 임박 구간은 고정 초록, 지난 구간은
                // 무지개 순환색을 써서 Preview 때 보이던 경로 그라데이션과 완전히 달라
                // 보였다 — Preview와 같은 위치 기반 그라데이션(PreviewPathColor)을 그대로
                // 써서 "경로 나올 때처럼" 일관되게 만들고, 밝기/투명도만으로 임박·경과
                // 여부를 구분한다.
                float distance = Vector3.Distance(playerPosition, frame.playerPosition);
                float proximityFade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
                    PredictionConfig.ExecutionGhostFadeNear,
                    PredictionConfig.ExecutionGhostFadeFar,
                    distance));
                float pathProgress = route.path.Count > 1
                    ? frame.tick / (float)(route.path.Count - 1)
                    : 0f;
                Color c = PreviewPathColor(pathProgress);

                // [다음 잔상 강조, 2026-07-22] ghostFrames는 actionMarkers와 같은 인덱스로
                // 대응하므로(마지막 프레임만 액션 없이 추가됨 — PredictedRoute 주석), 판정
                // 대기 중인 이벤트 인덱스가 곧 "지금 가야 할 잔상"의 인덱스다. 예전엔 이걸
                // 틱 거리(|frame.tick - eventTick| <= 15)로 어림잡아서 콤보처럼 액션이 촘촘한
                // 구간에선 이미 지나간 잔상까지 같이 밝아져 목표가 흐려졌다.
                // [추적 방식 추상화, 2026-07-22] 판정 대기 인덱스 = "지금 가야 할 잔상".
                // RhythmJudge가 없는 방식(자유 주행·클릭 체인)은 모드가 자기 커서를 내주고,
                // 리듬 방식은 -1을 내줘서 아래 판정기 인덱스로 폴백한다(강조 규칙은 공유).
                int modeHighlight = Mode.Active ? Mode.HighlightIndex : -1;
                int pending = modeHighlight >= 0
                    ? modeHighlight
                    : (rhythmJudge != null ? rhythmJudge.FirstPendingIndex : -1);
                if (pending >= 0)
                {
                    if (ghostIndex == pending)
                    {
                        // 근접 페이드에 하한을 깔아 도착 직전에도 목표가 남아있게 한다.
                        float fade = Mathf.Max(proximityFade, PredictionConfig.GhostNextProximityFloor);
                        float pulse = Mathf.Sin(
                            Time.unscaledTime * PredictionConfig.GhostNextPulseHz * Mathf.PI * 2f);
                        c = Color.Lerp(c, Color.white, PredictionConfig.GhostNextWhiteBlend);
                        c.a = Mathf.Clamp01(PredictionConfig.GhostNextAlpha
                                            + PredictionConfig.GhostNextPulseAmplitude * pulse) * fade;
                        return c;
                    }
                    // [2026-07-22] 다음 표적(pending+1) 강조 제거 — 현재 표적과 다음 표적 원이
                    // 둘 다 떠서 헷갈린다는 피드백. 현재 표적(pending)만 강조하고, 다음 것은
                    // 아래 일반 경로 색으로 흐리게 둔다.
                }

                if (frame.tick < followingIndex)
                    c.a = PredictionConfig.ExecutionGhostAlpha * proximityFade;
                else
                    c.a = PredictionConfig.GhostFutureAlpha * proximityFade;
                return c;
            }

            // Preview: 경로 색으로 후보를 구분하고, 선택된 후보만 또렷하게 강조한다
            // (산데비스탄 어두운 그린 배경 위에서 잔상 실루엣이 뚜렷하게 시인성을 가짐).
            bool sel = routeIndex == selected;
            float previewPathProgress = route.path.Count > 1
                ? frame.tick / (float)(route.path.Count - 1)
                : 0f;
            Color previewColor = PreviewPathColor(previewPathProgress);
            if (sel)
            {
                // 선택 잔상은 밝기/화이트를 살짝 보강하고 알파를 높여 또렷하게 표출
                previewColor = Color.Lerp(previewColor, Color.white, 0.15f);
                previewColor.a = 0.72f;
            }
            else
            {
                previewColor.a = 0.32f;
                previewColor = new Color(
                    previewColor.r * PredictionConfig.RouteDimMul,
                    previewColor.g * PredictionConfig.RouteDimMul,
                    previewColor.b * PredictionConfig.RouteDimMul,
                    previewColor.a);
            }
            return previewColor;
        }

        /// <summary>
        /// [자유 주행, 2026-07-22] 다음에 가야 할 노드 자리에 세우는 빛기둥. 1인칭에서 잔상만으로는
        /// "어느 게 다음 것인지" 안 읽힌다는 피드백 대응 — 잔상 강조(색·맥동)는 가까이 와야
        /// 구분되지만 기둥은 멀리서도 보이고 지형에 가려도 위쪽이 삐져나온다.
        /// 화면 쪽 안내는 각 모드의 DrawHud가 따로 그린다.
        /// [추적 방식 추상화, 2026-07-22] 위치를 모드가 정한다 — 안 내주면 기둥을 숨긴다.
        /// </summary>
        void UpdateModeGuide(in SimWorld w)
        {
            if (!Mode.Active || !Mode.TryGetWorldGuide(in w, out Vector3 target))
            {
                if (freerunBeacon != null) freerunBeacon.gameObject.SetActive(false);
                return;
            }

            if (freerunBeacon == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.Destroy(go.GetComponent<Collider>());
                go.name = "PredictFreerunBeacon";
                go.GetComponent<Renderer>().material = GhostMat();
                SetLayerRecursive(go.transform, PredictionAccentLayer);
                freerunBeacon = go.transform;
            }

            // 실린더 프리미티브는 높이 2가 기본이라 원하는 높이의 절반을 스케일로 준다.
            freerunBeacon.gameObject.SetActive(true);
            freerunBeacon.localScale = new Vector3(
                PredictionConfig.FreerunBeaconRadius * 2f,
                PredictionConfig.FreerunBeaconHeight * 0.5f,
                PredictionConfig.FreerunBeaconRadius * 2f);
            freerunBeacon.position = target + Vector3.up * (PredictionConfig.FreerunBeaconHeight * 0.5f);
            freerunBeacon.rotation = Quaternion.identity;

            float pulse = 0.5f + 0.5f * Mathf.Sin(
                Time.unscaledTime * PredictionConfig.FreerunGuidePulseHz * Mathf.PI * 2f);
            Color c = PredictionConfig.FreerunBeaconColor;
            c.a *= Mathf.Lerp(0.55f, 1f, pulse);
            SetMarkColor(freerunBeacon, c);
        }

        /// <summary>
        /// [추적 방식 추상화, 2026-07-22] 커서 표시 여부를 모드가 정한다. 클릭 체인은 조준
        /// 포켓이 열린 동안만 커서를 풀어주고, 그 외 모드·상태에서는 예전처럼 잠가둔다
        /// (1인칭 시점이 마우스를 쓰므로 평소엔 반드시 잠겨 있어야 한다).
        /// </summary>
        /// <summary>
        /// [모드 9, 2026-07-22] 3인칭에서 보여줄 실제 플레이어 본체를 실제 위치에 세운다.
        /// 기존 3인칭 표시는 시작 지점의 캡슐(startMarker)뿐이라 "움직이는 나"가 화면에
        /// 없었다 — 잔상용 아바타 프리팹(PlayerGhost)을 그대로 재사용하되, 잔상처럼 반투명하게
        /// 두지 않고 불투명한 본체로 그린다.
        /// </summary>
        void UpdatePlayerBody(in SimWorld w)
        {
            bool want = state == State.Following && Mode.Active && Mode.ShowsPlayerBody;
            if (!want)
            {
                if (playerBody != null) playerBody.gameObject.SetActive(false);
                return;
            }

            if (playerBody == null)
            {
                playerBody = MakeGhostMark();
                playerBody.gameObject.name = "PredictPlayerBody";
            }

            playerBody.gameObject.SetActive(true);
            playerBody.position = w.player.pos + Vector3.up * PivotYOffset;
            playerBody.rotation = Quaternion.Euler(0f, w.player.yaw, 0f);
            // 지금 밟고 있는 경로 진행도로 걷기 포즈 위상을 만든다(잔상 트레일과 같은 규칙).
            PoseGhostByTravel(playerBody, selected, followingIndex);
            SetMarkColor(playerBody, PredictionConfig.ThirdPersonBodyColor);
        }

        void ApplyModeCursor()
        {
            bool visible = state == State.Following && Mode.Active && Mode.WantsCursorVisible;
            CursorLockMode want = visible ? CursorLockMode.None : CursorLockMode.Locked;
            if (Cursor.lockState != want) Cursor.lockState = want;
            if (Cursor.visible != visible) Cursor.visible = visible;
        }

        Transform MakeGhostMark()
            => MakeGhostBody("PredictGhostMark",
                new Vector3(SimConfig.PlayerRadius * 2f,
                            SimConfig.PlayerHeight * 0.5f,
                            SimConfig.PlayerRadius * 2f));

        static void SetMarkColor(Transform mark, Color color)
        {
            foreach (var rnd in mark.GetComponentsInChildren<Renderer>())
            {
                rnd.material.color = color;
                if (rnd.material.HasProperty("_BaseColor"))
                    rnd.material.SetColor("_BaseColor", color);
            }
        }

        void SetVisible(bool on)
        {
            if (domeLr != null) domeLr.gameObject.SetActive(on);
            for (int i = 0; i < lines.Count; i++) lines[i].gameObject.SetActive(on && i < routes.Count);
            for (int ri = 0; ri < ghostMarksByRoute.Count; ri++)
                for (int i = 0; i < ghostMarksByRoute[ri].Count; i++)
                    ghostMarksByRoute[ri][i].gameObject.SetActive(on);
            if (startMarker != null) startMarker.gameObject.SetActive(on);
            // 본체는 3인칭 모드에서만 UpdatePlayerBody가 다시 켠다.
            if (!on && playerBody != null) playerBody.gameObject.SetActive(false);
            // 빛기둥은 모드가 위치를 내줄 때만 UpdateModeGuide가 다시 켠다.
            if (freerunBeacon != null) freerunBeacon.gameObject.SetActive(false);
            for (int i = 0; i < revealGhosts.Count; i++)
            {
                revealGhosts[i].gameObject.SetActive(on && state == State.Preview);
                if (i >= revealAfterimagesByRoute.Count) continue;
                for (int j = 0; j < revealAfterimagesByRoute[i].Count; j++)
                    revealAfterimagesByRoute[i][j].gameObject.SetActive(on && state == State.Preview);
            }
        }

        /// <summary>
        /// 1인칭 뷰모델(팔 + 칼) 표시 토글.
        ///
        /// [버그 수정, 2026-07-22] 예전엔 <c>SwordPivot</c> 하나만 숨겼다. 그런데 실제 1인칭
        /// 리그는 <b>KatanaViewmodel</b>(팔까지 포함, ViewmodelCamera.FindViewmodel이 찾는 그것)
        /// 이라, 3인칭 미리보기로 빠져도 팔과 칼이 화면에 그대로 떠 있었다.
        ///
        /// GameObject.SetActive 대신 <b>Renderer.enabled</b>만 끈다 — 오브젝트를 비활성화하면
        /// ViewmodelCamera가 레이어를 다시 입힐 때 GameObject.Find로 못 찾고(비활성 제외),
        /// SwordView/PosePlayer의 참조도 끊긴다. 보이는 것만 끄는 게 안전하다.
        ///
        /// 런타임에 생성되는 리그라 매번 다시 훑는다(상태 전환에서만 호출되므로 비용 무시 가능).
        /// </summary>
        void ToggleViewmodel(bool show)
        {
            viewmodelRenderers.Clear();
            if (cam != null) CollectViewmodel(cam.transform.Find("SwordPivot"));

            Transform katana = cam != null ? cam.transform.Find("KatanaViewmodel") : null;
            if (katana == null)
            {
                GameObject go = GameObject.Find("KatanaViewmodel");
                if (go != null) katana = go.transform;
            }
            CollectViewmodel(katana);

            for (int i = 0; i < viewmodelRenderers.Count; i++)
            {
                if (viewmodelRenderers[i] == null) continue;
                viewmodelRenderers[i].enabled = show;
            }
        }

        void CollectViewmodel(Transform root)
        {
            if (root == null) return;
            root.GetComponentsInChildren(true, collectBuffer);
            viewmodelRenderers.AddRange(collectBuffer);
        }

        void SetSwordExecutionStyle(bool active)
        {
            if (cam == null) return;
            Transform sword = cam.transform.Find("SwordPivot");
            if (sword == null) return;
            Renderer[] renderers = sword.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                Material material = renderer.material;
                if (active)
                {
                    Transform current = renderer.transform;
                    if (!swordOriginalLayers.ContainsKey(current))
                        swordOriginalLayers.Add(current, current.gameObject.layer);
                    current.gameObject.layer = PredictionAccentLayer;
                    if (!swordOriginalColors.ContainsKey(renderer))
                        swordOriginalColors.Add(renderer, material.color);
                    material.color = PredictionConfig.ExecutionPlayerColor;
                    if (material.HasProperty("_BaseColor"))
                        material.SetColor("_BaseColor", PredictionConfig.ExecutionPlayerColor);
                    if (material.HasProperty("_EmissionColor"))
                    {
                        material.EnableKeyword("_EMISSION");
                        material.SetColor("_EmissionColor",
                            PredictionConfig.ExecutionPlayerColor * 2.2f);
                    }
                }
                else if (swordOriginalColors.TryGetValue(renderer, out Color original))
                {
                    if (swordOriginalLayers.TryGetValue(renderer.transform, out int originalLayer))
                        renderer.gameObject.layer = originalLayer;
                    material.color = original;
                    if (material.HasProperty("_BaseColor"))
                        material.SetColor("_BaseColor", original);
                    if (material.HasProperty("_EmissionColor"))
                        material.SetColor("_EmissionColor", Color.black);
                }
            }
            if (!active)
            {
                swordOriginalColors.Clear();
                swordOriginalLayers.Clear();
            }
        }

        // ── 머티리얼 ──
        static Material LineMat()
        {
            var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            var m = new Material(sh);
            // Unlit로 폴백된 경우 기본이 Opaque라 반투명 경로선의 알파가 무시된다 — 명시적으로 켠다.
            if (m.HasProperty("_Surface")) { m.SetFloat("_Surface", 1f); m.renderQueue = 3000; }
            return m;
        }

        static Material GhostMat()
        {
            var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            var m = new Material(sh);
            Color c = PredictionConfig.GhostColor;
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Surface")) { m.SetFloat("_Surface", 1f); m.renderQueue = 3000; }
            return m;
        }

        static Material SolidMat(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }
    }
}
