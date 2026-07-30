using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// MINDHEXER 게임 씬 부트스트랩. 이식된 Precog <c>Main.cs</c>(결정론 예측 시뮬레이션 기반)를
    /// 대체하는 **우리 진입점**이다. 씬에 [GameBoot] 하나만 두면 1인칭 카메라 + 해킹 시스템이 구성된다.
    /// 예측·결정론 World/Snapshot에 의존하지 않는 **실시간 게임 부트**.
    ///
    /// VR 이식: <see cref="VrMode"/>.Enabled면 XR 리그(머리 회전=Cardboard 소유) + World-Space HUD를
    /// 구성한다 — Precog Main.cs의 VR 카메라 경로를 이 부트로 옮긴 것(예측/vcam 의존은 제거).
    /// Precog Main은 GameBoot 존재 시 자동부팅을 건너뛴다(Main.cs Boot 가드).
    /// (docs/KJH/decisions/0002-precog-purge.md)
    /// </summary>
    [DisallowMultipleComponent]
    public class GameBoot : MonoBehaviour
    {
        [Header("플레이어 시작")]
        [Tooltip("플레이어(카메라/리그) 시작 위치.")]
        public Vector3 startPosition = new Vector3(0f, 0f, -6f);
        [Tooltip("눈높이(카메라 Y 오프셋).")]
        public float eyeHeight = 1.6f;
        [Tooltip("PC 모드에서 시작 시 마우스 커서를 잠글지.")]
        public bool lockCursor = true;

        [Header("렌더 거리 (모바일 VR 부하의 최대 변수)")]
        [Tooltip("카메라 far 평면(m). 0 이하면 씬 값을 그대로 둔다.\n" +
                 "★ 스테이지 3개를 한 씬에 이어 붙였기 때문에 far가 크면 <b>지금 있는 스테이지가 아닌 것까지</b> " +
                 "통째로 그린다. 스테이지 간격이 113.5m라, 100이면 옆 스테이지가 거의 통째로 잘린다.\n" +
                 "먼 배경이 잘려 보이면 늘리되, 배경만 따로 살리려면 아래 레이어별 거리를 쓸 것.\n" +
                 "플레이 중에는 콘솔 `far <거리>`로 바꿔 가며 Stats를 비교할 수 있다.")]
        public float farClip = 40f;

        [System.Serializable]
        public struct LayerCullRule
        {
            [Tooltip("레이어 이름. 없는 이름은 조용히 무시된다.")]
            public string layerName;

            [Tooltip("이 레이어를 그릴 최대 거리(m). 0이면 farClip을 그대로 쓴다.")]
            public float distance;
        }

        [Tooltip("레이어별 컬링 거리. 구조물은 멀리, 잔장식은 가깝게 잘라 부하를 줄인다.\n" +
                 "비워 두면 전부 farClip을 쓴다.")]
        public LayerCullRule[] layerCullRules = new LayerCullRule[0];

        [Header("프레임")]
        [Tooltip("목표 프레임레이트. ⚠️ Unity는 Android에서 이 값을 지정하지 않으면 배터리 절약을 위해 " +
                 "30으로 묶는다 — 실기에서 정확히 30.0fps에 고정되는 것으로 확인했다. VR에서 30fps는 " +
                 "멀미를 유발하므로 반드시 올려야 한다. 0 이하면 건드리지 않는다.")]
        public int targetFrameRate = 60;

        Camera _cam;

        void Awake()
        {
            ApplyFrameRate();
            _cam = EnsureCamera();
            SetupRig();
        }

        /// <summary>
        /// 통합 리그 — PC와 VR이 <b>같은 몸</b>을 쓴다. 차이는 카메라에 붙는 시점 드라이버 하나뿐.
        ///
        /// <code>
        /// [PlayerBody]       ← CC + 이동·중력·밀림감지 + AutoTraversal + MantleRig. 회전하지 않는다.
        ///  └ [Head]          ← 시점 소유(PC=MouseLook / VR=TrackedPoseDriver RotationOnly)
        ///                      + VR 눈높이(VrTuning)
        ///     └ Main Camera  ← 연출 소유: MotionFeel(롤·상하 킥·흡수 지연) + HackDriver(조준=정면)
        ///        └ Viewmodel ← 팔 소유: ViewmodelMotion (있을 때만)
        /// </code>
        ///
        /// <para><b>왜 층을 나누나</b> — 연출과 시점이 같은 트랜스폼을 쓰면 둘 중 하나가 절대 대입을
        /// 하는 순간 다른 쪽이 지워진다. 실제로 <see cref="ViewmodelMotion"/>이 카메라를 뷰모델로
        /// 오인해 매 LateUpdate마다 덮어썼고, PC에선 시점 회전이 VR에선 눈높이가 죽었다.
        /// 층을 나누면 그 사고가 <b>구조적으로 불가능</b>해진다.</para>
        ///
        /// <para><b>★ 왜 시점이 위, 연출이 아래인가 (순서가 중요하다)</b> — 롤은 <b>시선 축</b>을 돌아야
        /// 한다. 연출을 시점 <i>위</i>에 두면 롤이 "몸의 앞 축"을 도는 게 되어, 옆을 볼수록 롤이
        /// 피치로 변질된다(yaw 90°·roll 20°에서 시선이 19° 들림 — 실측). 그래서 연출은 반드시
        /// 시점 드라이버 <b>아래</b>에 있어야 한다.</para>
        ///
        /// <para>부수 효과로 <b>VR에서도 롤 연출이 걸린다</b> — 전에는 롤을 합성해 주는 게
        /// <see cref="MouseLook"/>뿐이라 VR엔 아예 없었다(멀미 때문에 <c>vrRollScale=0</c> 기본값은
        /// 유지 — 이제는 '없는 것'이 아니라 '꺼둔 것'이다).</para>
        ///
        /// <para><b>트랜스폼 속성별 소유자 (하나씩만)</b>
        /// <list type="bullet">
        /// <item>[PlayerBody] 위치 — CharacterController</item>
        /// <item>[Head] 로컬회전 — MouseLook(PC) / TrackedPoseDriver(VR)</item>
        /// <item>[Head] 로컬위치 — VrTuning(VR 눈높이). PC는 0 고정.</item>
        /// <item>Main Camera 위치·로컬회전 — MotionFeel(연출 오프셋·롤)</item>
        /// <item>Viewmodel — ViewmodelMotion</item>
        /// </list></para>
        ///
        /// <para>몸 원점 = 눈높이. 기존 이동·등반 코드 전부가 "트랜스폼 위치 = 눈"을 전제하므로
        /// 그 규약을 유지한다(발 위치는 CC의 center −0.7이 만든다). 카메라 로컬은 0 — VR에서
        /// TrackedPoseDriver가 RotationOnly라 위치를 안 건드려 눈높이가 꺼지지 않는다.</para>
        ///
        /// <para>이동 기준 = 카메라 수평 forward("조이스틱 앞 = 보는 방향" — VR 표준 로코모션이자
        /// PC의 원래 동작). 예전 VR 전용 [XR Rig]+FreeLookController 경로는 삭제 — 몸이 없어서
        /// 충돌·중력 없이 벽을 뚫고 다녔고 눈높이도 TPD가 덮어써 땅이 꺼져 보였다.</para>
        /// </summary>
        /// <summary>
        /// 카메라 아래에 1인칭 뷰모델을 둔다. 이미 있으면(작업 씬처럼 손으로 배치한 경우) 그대로 쓴다.
        ///
        /// <para>프리팹은 <c>Resources/PlayerViewmodel</c>이다 — 이 컴포넌트가 런타임에 리그를
        /// 만들므로 씬에서 참조를 물릴 수가 없어 이름으로 찾는다. 프리팹 루트의 로컬 TRS
        /// (위치·180° 회전·스케일 2)가 곧 1인칭 구도라 <c>worldPositionStays: false</c>로 붙여
        /// 그 값을 살린다.</para>
        /// </summary>
        void EnsureViewmodel(Transform camT)
        {
            if (camT == null) return;
            if (camT.Find(ViewmodelCamera.ViewmodelRootName) != null) return;   // 이미 있음

            var pf = Resources.Load<GameObject>("PlayerViewmodel");
            if (pf == null)
            {
                Debug.LogWarning("[GameBoot] Resources/PlayerViewmodel 프리팹이 없어 1인칭 팔·거미가 안 나옵니다.");
                return;
            }
            var go = Instantiate(pf, camT, false);
            go.name = ViewmodelCamera.ViewmodelRootName;   // ViewmodelCamera·ViewmodelMotion이 이름으로 찾는다
        }

        void SetupRig()
        {
            var body = new GameObject("[PlayerBody]");
            body.transform.position = startPosition + Vector3.up * eyeHeight;

            // 시점 전용 트랜스폼 — 회전 소유자(MouseLook/TrackedPoseDriver)가 여기만 쓴다.
            // 몸 위치를 건드리면 CC와 싸우므로 시점은 여기까지만 내려온다.
            var head = new GameObject("[Head]");
            head.transform.SetParent(body.transform, false);

            // 카메라 = 연출 전용. 시점보다 아래에 있어야 롤이 '시선 축'을 돈다(클래스 주석 ★).
            _cam.transform.SetParent(head.transform, false);
            _cam.transform.localPosition = Vector3.zero;
            _cam.transform.localRotation = Quaternion.identity;
            var feel = _cam.GetComponent<MotionFeel>() ?? _cam.gameObject.AddComponent<MotionFeel>();
            feel.ownsRotation = true;   // 시점은 부모가 가지므로 이 트랜스폼의 회전은 연출 몫이다

            // ★ 1인칭 뷰모델(팔·손 IK·손가락·펫 거미)을 카메라 아래에 스폰한다.
            //   여태 이 리그는 ViewmodelStudio 씬에만 손으로 배치돼 있어서, 실제 스테이지에서
            //   플레이하면 <b>팔도 거미도 없었다</b>. 여기서 만들어야 전 씬에 한 번에 적용된다.
            //   MantleRig.Resolve()가 자기 하위에서 HandIK/FingerPoser를 찾으므로 배선은 자동이다
            //   (뷰모델은 [PlayerBody]/[Head]/Main Camera 아래라 하위에 들어온다).
            EnsureViewmodel(_cam.transform);

            // 몸 — MotionFeel(자식)이 먼저 있어야 Awake 캐시가 잡힌다.
            if (body.GetComponent<MantleRig>() == null) body.AddComponent<MantleRig>();
            var fpp = body.GetComponent<FirstPersonPlayer>() ?? body.AddComponent<FirstPersonPlayer>();
            fpp.view = _cam.transform;
            if (body.GetComponent<AutoTraversal>() == null) body.AddComponent<AutoTraversal>();

            // 해킹 — 조준 = 카메라 정면. [RequireComponent(HackContext)]라 HackContext도 함께 붙는다.
            if (_cam.GetComponent<HackDriver>() == null) _cam.gameObject.AddComponent<HackDriver>();

            // 조명 — 카메라에 붙인다([Head]가 아니다). MotionFeel의 킥·롤에 빛이 같이 흔들려야
            // 손전등을 들고 뛰는 감각이 난다. PC/VR 값은 리그가 모드별로 따로 들고 있다.
            if (_cam.GetComponent<PlayerLightRig>() == null) _cam.gameObject.AddComponent<PlayerLightRig>();

            if (VrMode.Enabled)
            {
                AttachHeadPoseDriver(head.transform);   // RotationOnly — 위치는 몸이 소유

                // ScreenSpace HUD → 머리 앞 World-Space 패널(양안 렌더).
                var hud = new GameObject("[VrHudSpace]").AddComponent<VrHudSpace>();
                hud.head = _cam.transform;

                // VR 튜닝 — 눈높이·HUD·렌더스케일 JSON 저장/로드.
                // 눈높이는 [Head]에 건다 — 카메라 로컬위치는 MotionFeel(연출)이 쓰므로 겹치면 안 된다.
                var tuning = gameObject.AddComponent<VrTuning>();
                tuning.head = head.transform;
                tuning.hud = hud;
                tuning.eyeBase = eyeHeight;   // 몸 원점이 이미 눈높이 — [Head] 로컬은 차이만 받는다
            }
            else
            {
                head.AddComponent<MouseLook>();
                if (body.GetComponent<MoveTuningPanel>() == null) body.AddComponent<MoveTuningPanel>();
                if (lockCursor) Cursor.lockState = CursorLockMode.Locked;
            }

            AuditCameraOwnership(head.transform);
        }

        /// <summary>
        /// 카메라 회전 소유자가 정확히 하나인지 확인하고, 아니면 경고한다.
        ///
        /// <para><b>왜 필요한가</b> — 이 규칙이 깨졌을 때의 증상이 원인과 전혀 안 닮았다.
        /// 실제로 <see cref="ViewmodelMotion"/>이 카메라를 뷰모델로 오인했을 때, PC에선
        /// "마우스를 움직여도 화면이 안 돎", VR에선 "머리는 도는데 눈높이가 죽음"으로 나타났다.
        /// 원인을 찾는 데 오래 걸렸으므로, 다음엔 콘솔 첫 줄에서 바로 보이게 한다.</para>
        ///
        /// <para>VR 실기에선 콘솔을 못 보므로 <see cref="VrStatsHud"/>에도 같은 사실이 실린다.</para>
        /// </summary>
        void AuditCameraOwnership(Transform head)
        {
            if (_cam == null || head == null) return;

            string owner = VrMode.Enabled ? "TrackedPoseDriver(VR)" : "MouseLook(PC)";
            bool ok = VrMode.Enabled
                ? head.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>() != null
                : head.GetComponent<MouseLook>() != null;

            if (!ok)
                Debug.LogError($"[GameBoot] 시점 소유자({owner})가 [Head]에 없습니다 — 화면이 안 돕니다.", head);

            // 시점과 연출이 같은 트랜스폼에 겹치면 롤이 시선 축을 못 돈다(옆을 볼 때 피치로 변질).
            if (_cam.GetComponent<MouseLook>() != null ||
                _cam.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>() != null)
                Debug.LogError("[GameBoot] 시점 드라이버가 카메라에 직접 붙었습니다 — 연출(MotionFeel)과 " +
                               "같은 트랜스폼이라 롤이 어긋납니다. [Head]로 옮기십시오.", _cam);

            // 카메라를 뷰모델로 잡아 회전을 덮어쓰는 사고의 재발 감지.
            var vm = FindFirstObjectByType<ViewmodelMotion>();
            if (vm != null && vm.enabled && vm.RootIsCamera)
                Debug.LogError("[GameBoot] ViewmodelMotion이 카메라를 뷰모델 루트로 잡았습니다 — " +
                               "시점이 죽습니다. Main Camera 아래 'Viewmodel' 오브젝트를 확인하십시오.", vm);

            Debug.Log($"[GameBoot] 리그 구성 완료 — 시점 소유자={owner}, 연출=[CamRig]/MotionFeel, " +
                      $"뷰모델={(vm != null ? "있음" : "없음")}");
        }

        /// <summary>
        /// 머리 포즈를 카메라에 적용한다.
        ///
        /// <para><b>왜 필요한가</b> — 구형 VR은 유니티가 메인 카메라에 머리 포즈를 자동 적용했지만,
        /// 신형 XR SDK는 그러지 않는다. 그래서 <b>스플래시 화면은 회전하는데 게임은 안 도는</b>
        /// 증상이 나온다(실기 확인) — 트래킹은 살아 있고 카메라가 그 값을 안 받는 것이다.</para>
        ///
        /// <para>바인딩 경로는 Cardboard 공식 샘플(HelloCardboard)의 카메라 설정에서 그대로 가져왔다.
        /// 추측하면 틀린다.</para>
        /// </summary>
        void AttachHeadPoseDriver(Transform head)
        {
            if (head == null) return;
            if (head.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>() != null) return;

            var tpd = head.gameObject.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            // RotationOnly — 위치까지 받으면 Cardboard(3DoF)의 거의-0 위치가 카메라 로컬을 덮어써
            // 눈높이가 지워지고 땅이 꺼져 보인다(실기 확인). 위치는 [PlayerBody]가 소유한다.
            tpd.trackingType = UnityEngine.InputSystem.XR.TrackedPoseDriver.TrackingType.RotationOnly;
            // Update + BeforeRender — 렌더 직전에 한 번 더 갱신해야 머리 지연이 최소가 된다.
            tpd.updateType = UnityEngine.InputSystem.XR.TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;

            var rot = new UnityEngine.InputSystem.InputAction(
                "HMD Rotation", UnityEngine.InputSystem.InputActionType.Value, "<XRHMD>/centerEyeRotation");
            rot.Enable();
            tpd.rotationInput = new UnityEngine.InputSystem.InputActionProperty(rot);

            Debug.Log("[GameBoot] 머리 포즈 드라이버 부착 (<XRHMD>/centerEyeRotation, RotationOnly)");
        }

        /// <summary>
        /// 프레임 상한 해제. vSync가 켜져 있으면 <c>targetFrameRate</c>가 무시되므로 함께 끈다.
        /// (Android 기본 품질 레벨은 이미 <c>vSyncCount 0</c>이지만, 레벨이 바뀌어도 안 흔들리게 명시한다.)
        /// </summary>
        void ApplyFrameRate()
        {
            if (targetFrameRate <= 0) return;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = targetFrameRate;
        }

        /// <summary>
        /// 레이어별 컬링 거리를 적용한다. 이름으로 찾으므로 레이어가 없으면 조용히 건너뛴다 —
        /// 프로젝트마다 레이어 구성이 달라도 터지지 않는다.
        ///
        /// <para><b>왜 레이어별인가</b>: <see cref="farClip"/> 하나로 자르면 "가까이서 보여야 할 장식"과
        /// "멀리 보여야 할 배경"이 같은 거리에서 잘린다. 장식은 60m면 충분하고 배경은 멀리 보여야 한다.</para>
        /// </summary>
        void ApplyLayerCullDistances(Camera c)
        {
            if (layerCullRules == null || layerCullRules.Length == 0) return;

            float[] d = c.layerCullDistances;   // 32칸. 0 = farClip 그대로 쓴다는 뜻
            bool any = false;
            for (int i = 0; i < layerCullRules.Length; i++)
            {
                int layer = LayerMask.NameToLayer(layerCullRules[i].layerName);
                if (layer < 0) continue;        // 없는 레이어는 무시 — 실수로 터뜨리지 않는다
                d[layer] = Mathf.Max(0f, layerCullRules[i].distance);
                any = true;
            }
            if (!any) return;

            c.layerCullDistances = d;
            c.layerCullSpherical = true;        // 평면이 아니라 구 기준 — 옆을 볼 때 갑자기 사라지지 않는다
        }

        Camera EnsureCamera()
        {
            var c = Camera.main;
            if (c == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                c = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }
            c.nearClipPlane = 0.1f;
            if (farClip > 0f) c.farClipPlane = farClip;
            ApplyLayerCullDistances(c);

            var camData = c.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>()
                       ?? c.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            camData.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            return c;
        }

    }
}
