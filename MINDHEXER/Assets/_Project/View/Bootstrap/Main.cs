using UnityEngine;
using Unity.Cinemachine;
using Game.Sim;
using Game.Bridge;

namespace Game.View
{
    /// <summary>
    /// 심장. 빈 GameObject에 이 컴포넌트 하나만 붙이면 동작한다.
    /// Start: 맵·NavMesh·서비스·월드 구성. Update: 입력·뷰·1인칭 카메라. FixedUpdate: SimStep.
    /// 전투 없음(뼈대) — 이동·지형충돌·적 길찾기·겹침밀치기만.
    /// </summary>
    public class Main : MonoBehaviour
    {
        [SerializeField] float eyeHeight = 1.25f;   // 키 1.4375(1.15×1.25)에 맞춰 시점 상향
        [SerializeField] float gameplayFov = 0f;   // 0=씬 카메라 FOV 상속, >0=강제(둠식 넓은 시야는 90~100)
        public bool useSceneGeometry;   // true=씬 지형(Synty) 사용, false=코드 큐브맵

        // 전투 뷰(HUD·카메라고정·히트스톱)가 sim 상태를 읽는 최소 접근자 (읽기 전용)
        public static Main Instance { get; private set; }
        public ref readonly SimWorld World => ref world;
        /// <summary>직전 틱의 월드. 뷰가 "틱당 실제 이동량"을 재는 데 쓴다(프레임레이트 무관).</summary>
        public ref readonly SimWorld PrevWorld => ref prevWorld;
        public Camera Cam => cam;
        /// <summary>1인칭 눈높이. 등장 컷신이 카메라를 정확히 이 높이로 수렴시켜야 인계가 안 튄다.</summary>
        public float EyeHeight => eyeHeight;
        public CinemachineCamera GameplayVcam => gameplayVcam;   // 연출(FOV킥·Impulse)이 vcam을 건드리게

        // >>> [예측 세션 추가, 2026-07-18] Services/SpawnEnemyNear/ClearAllEnemies는 원래 없던
        // 접근자다. 예측 쪽(PredictionPreview.cs, RealRoutePreview.cs)이 실제 SimServices와
        // 디버그용 적 스폰/제거가 필요해서 추가함 — 전부 읽기 전용이거나 디버그 전용이라
        // 기존 게임 루프 동작에는 영향 없음.
        public SimServices Services => graphServices;   // 예측/following = 그래프(포크·결정론)

        /// <summary>디버그용: 지정 위치 근처에 적 1마리 소환(예측 미리보기 시나리오 설정용).</summary>
        public void SpawnEnemyNear(Vector3 pos) => world.AddEnemy(pos);

        /// <summary>디버그용: 살아있는 적 전부 제거(예측 미리보기 시나리오 리셋용).</summary>
        public void ClearAllEnemies() => world.enemyCount = 0;

        /// <summary>디버그용: 몹 뷰를 전부 버려 다음 프레임에 새로 만들게 한다(파손 변형 교체 반영).</summary>
        public void RebuildEnemyViews() => views.InvalidateViews();

        /// <summary>웨이브 런타임용: 지정 위치·조합으로 스폰하고 부여된 적 id를 돌려준다(-1 = 실패).</summary>
        public int SpawnEnemyAt(Vector3 pos, CombatType combat, MobilityType mobility, SizeClass size)
        {
            int id = world.nextEnemyId;   // AddEnemy가 이 값을 쓰고 증가시킨다
            return world.AddEnemy(pos, combat, mobility, size) ? id : -1;
        }

        /// <summary>
        /// 웨이브 배관용: 스폰과 동시에 **펄스**(초기 속도)를 주고 발사 상태로 만든다(설계 §4).
        /// 발사 중엔 AI·공격이 멈추고 탄도로 날아가며, 지상몹은 착지 시 · 공중몹은 타이머로 해제된다.
        /// </summary>
        public int SpawnEnemyLaunched(Vector3 pos, CombatType combat, MobilityType mobility, SizeClass size,
                                      Vector3 launchVel)
        {
            int id = SpawnEnemyAt(pos, combat, mobility, size);
            if (id < 0) return -1;
            for (int i = 0; i < world.enemyCount; i++)
                if (world.enemies[i].id == id)
                {
                    world.enemies[i].vel = launchVel;
                    world.enemies[i].launchTicks = 1;   // >0 = 발사 중
                    world.enemies[i].grounded = false;
                    break;
                }
            return id;
        }

        /// <summary>웨이브 배관용: 스폰 위치(팬 아래 입)에서 <b>즉시 SpawnDrop 링크를 타고 낙하</b>한다.
        /// 펄스 폐기 — 지상·돌진몹은 이 경로로 수직 낙하한다(공중몹은 SpawnEnemyLaunched로 약한 펄스).</summary>
        public int SpawnEnemyDropping(Vector3 pos, CombatType combat, MobilityType mobility, SizeClass size,
                                      Vector3 landing, float clearance, float gravity)
        {
            int id = SpawnEnemyAt(pos, combat, mobility, size);
            if (id < 0) return -1;
            for (int i = 0; i < world.enemyCount; i++)
                if (world.enemies[i].id == id)
                { EnemyMovement.BeginSpawnDrop(ref world.enemies[i], landing, clearance, gravity); break; }
            return id;
        }

        /// <summary>주어진 id들 중 살아있는 적 수 — 웨이브별 생존 카운트용(Sim 수정 없이 웨이브 소속 추적).</summary>
        public int AliveCountAmong(System.Collections.Generic.HashSet<int> ids)
        {
            if (ids == null || ids.Count == 0) return 0;
            int n = 0;
            for (int i = 0; i < world.enemyCount; i++)
                if (world.enemies[i].alive && ids.Contains(world.enemies[i].id)) n++;
            return n;
        }
        // <<< [예측 세션 추가 끝]

        // 화면연출(칼등치기 카메라 고정)이 끝날 때 최종 시선을 되돌려 써서 원복 방지.
        // yaw는 sim 입력(cmd.yaw)에도 쓰이므로 여기 하나로 시점·조준이 동기화된다.
        public void SetLookYaw(float yaw) => input.Yaw = yaw;
        public void SetLookPitch(float pitch) => input.Pitch = pitch;
        /// <summary>예지(F) 충전량 0~1. HUD의 PREDICTION 다이얼이 읽는다.</summary>
        public float PredictionCharge01 => prediction.Charge01;
        public float LookYaw   => input.Yaw;
        public float LookPitch => input.Pitch;
        public void SetPredictionSpawnLocked(bool locked) => world.spawnLocked = locked;
        /// <summary>예측(미리보기·실행 포함)이 진행 중인가. 연출(런지 타깃 윤곽 등)이 예측 중엔
        /// 표시를 끄기 위해 읽는다.</summary>
        public bool PredictionActive => prediction.Frozen;
        /// <summary>몹 뷰 접근(런지 타깃 윤곽 연출 등 순수 연출이 읽는다).</summary>
        public EntityViews Views => views;

        /// <summary>컷신 종료 시 플레이어를 현재 시선(yaw) 정면으로 dist만큼 이동(봉인 박스 탈출).
        /// 스크립트 텔레포트라 결정론과 무관(예측 중엔 컷신을 트리거하지 않음). prevWorld도 맞춰 보간 튐 방지.</summary>
        public void AdvancePlayerForward(float dist)
        {
            float yr = input.Yaw * Mathf.Deg2Rad;
            Vector3 fwd = new Vector3(Mathf.Sin(yr), 0f, Mathf.Cos(yr));
            world.player.pos += fwd * dist;
            prevWorld = Snapshot.Clone(in world);
        }

        /// <summary>등장 컷신 인계용: 플레이어를 지정 좌표에 놓는다(컷신이 실측한 바닥에 착지시키려고).
        /// AdvancePlayerForward와 같은 이유로 결정론과 무관하다 — 컷신 중엔 sim이 멈춰 있다.</summary>
        public void PlacePlayerAt(Vector3 pos)
        {
            world.player.pos = pos;
            prevWorld = Snapshot.Clone(in world);
        }

        /// <summary>플레이어가 죽었는가(hp 0). 사망 화면이 이걸 본다 — sim은 hp를 0에서 멈출 뿐
        /// 스스로 아무것도 하지 않으므로, "죽었다"의 처리는 전부 뷰 쪽에 있다.</summary>
        public bool PlayerDead => world.player.combat.hp <= 0;

        /// <summary>
        /// 판을 처음 상태로 되돌린다(사망 후 재시작).
        ///
        /// <para>★ 씬을 <b>다시 로드하지 않는다</b>. 이 프로젝트의 모든 시스템(Main·HUD·연출)은
        /// <c>RuntimeInitializeOnLoadMethod</c>로 붙는데 그건 플레이 세션당 <b>한 번만</b> 돈다 —
        /// 씬을 다시 로드하면 그 오브젝트들이 통째로 사라진 뒤 아무도 다시 만들어 주지 않는다.
        /// 그래서 월드 상태만 제자리에서 갈아 끼운다. 맵·NavMesh·그래프는 그대로 재사용하므로
        /// 굽기 대기(수 초)도 없다.</para>
        /// </summary>
        public void RestartRun()
        {
            prediction.Cancel();          // 예측이 켜진 채 죽었을 수 있다(배속·카메라·스폰잠금 원복)
            HitStop.FrozenTicks = 0;

            world = SimWorld.Create();
            world.mapVersion = restartMapVersion;
            world.player = PlayerSim.Spawn(restartSpawn);
            prevWorld = Snapshot.Clone(in world);

            input.Yaw = restartYaw;
            input.Pitch = 0f;
            spawnTimer = SimConfig.SpawnIntervalTicks;   // 첫 틱부터 다시 소환
            nextSpawn = 0;
            fixedAccum = 0f;

            // 씬을 다시 로드하지 않으므로 MonoBehaviour가 들고 있는 런 진행도도 직접 초기화한다.
            // 방에 연결되지 않은 개발용 WaveRunner도 있을 수 있어 둘 다 훑는다.
            foreach (var runner in FindObjectsByType<WaveRunner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                runner.ResetProgress();
            foreach (var room in FindObjectsByType<ArenaRoom>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                room.ResetProgress();
            foreach (var gate in FindObjectsByType<ArenaGate>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                gate.ResetToStartState();
            foreach (var fan in FindObjectsByType<FanSpawnActor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                fan.ResetToStartState();

            views.InvalidateViews();      // 적 뷰를 버려 새 월드 기준으로 다시 만들게 한다

            // ★ sim만 리셋하면 웨이브·아레나·게이트·보스소환기 등이 마지막 상태로 남는다.
            //   IRunResettable을 구현한 모든 컴포넌트를 한 번에 되돌린다 — 컴포넌트가 자기 리셋을
            //   소유하므로, 새 컴포넌트를 추가해도 이 목록을 고칠 필요가 없다(누락 불가).
            //   비활성 오브젝트까지 포함해 놓친 아레나가 없게 한다.
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (mb is IRunResettable r) r.ResetForRestart();
        }

        /// <summary>플레이어 체력을 최대로 — 아레나 클리어(ArenaRoom.OnUnlock) 시 호출.</summary>
        public void HealPlayerFull()
        {
            world.player.combat.hp = CombatConfig.PlayerMaxHp;
        }

        SimWorld world, prevWorld;
        // 재시작이 되돌릴 초기값 — Start에서 한 번 잡아 둔다(맵을 다시 만들지 않기 위해).
        Vector3 restartSpawn;
        float restartYaw;
        int restartMapVersion;
        readonly InputReader input = new InputReader();
        readonly EntityViews views = new EntityViews();
        readonly PredictionController prediction = new PredictionController();
        SimServices services;        // 평상시 = 런타임 NavMesh
        SimServices graphServices;   // 예측 검색·following 재생 = 고정 그래프
        Camera cam;
        CinemachineCamera gameplayVcam;   // 1인칭·예측·컷신 모두 이 vcam pose에 씀 → Brain이 실제 카메라 구동(+Impulse)
        Transform xrRig;                  // VR 모드에서만: 카메라의 부모 리그 루트. 매 프레임 플레이어 위치로 옮기고, 회전은 XR(머리)이 소유
        DevConsole console;
        MapSpawnConfig spawnConfig;   // 씬에 있으면 그 맵의 스폰지점·종류 세팅을 사용
        float fixedAccum;

        System.Collections.Generic.List<Vector3> spawnPoints;
        int spawnTimer, nextSpawn;

        // 개발 콘솔 훅
        public bool AutoSpawn = true;                 // 기본 on. 씬 세팅 있으면 그 값으로 덮음
        const float DevSpawnDistance = 6f;            // 콘솔 소환 위치 = 플레이어 정면 이 거리
        // 콘솔뿐 아니라 개발 패널(F1~F4)이 열려 있어도 입력을 막는다.
        // 안 그러면 슬라이더를 클릭할 때마다 그 클릭이 공격 입력으로도 들어간다.
        bool ConsoleOpen => (console != null && console.IsOpen) || DevPanels.AnyOpen;

        void Start()
        {
            Instance = this;
            Time.fixedDeltaTime = SimConfig.TickDelta;

            // 아레나 클리어(출구 열림) 시 플레이어 풀피 충전 — 각 ArenaRoom의 클리어 이벤트를 구독.
            foreach (var room in FindObjectsByType<ArenaRoom>(FindObjectsSortMode.None))
                room.OnUnlock += HealPlayerFull;

            Vector3 refPoint = Camera.main != null ? Camera.main.transform.position : Vector3.zero;

            MapResult map = useSceneGeometry ? MapBuilder.BuildFromScene(refPoint) : MapBuilder.BuildCubes();
            ArenaMapBake predictionMap = map.predictionMap;
            if (predictionMap == null || predictionMap.nodes == null || predictionMap.nodes.Length == 0)
            {
                predictionMap = GraphPathfinder.CreateArenaBake();
                Debug.LogWarning("[Prediction] ArenaMapAuthoring이 없어 내장 아레나 그래프를 사용합니다. 씬 지형 변경 시 authoring과 mapVersion을 갱신하세요.");
            }

            // 하이브리드: 평상시 = 런타임 NavMesh(연속 경로·나비 매끄러움).
            // 예측 검색·following 재생 = 고정 그래프(포크·결정론). 둘 다 EnemyMovement가 그대로 씀.
            var collision = new PhysicsCollision(Physics.DefaultRaycastLayers);
            // 전선(파손 연출)도 같은 마스크로 지형만 친다. 몹·플레이어엔 콜라이더가 없으므로
            // 이 마스크만으로 "지형에만 걸리고 캐릭터는 무시"가 자동으로 성립한다.
            DanglingWire.SetTerrainMask(Physics.DefaultRaycastLayers);
            var navPathfinder = new NavMeshPathfinder();
            ValidatePredictionMapAgainstNavMesh(predictionMap, navPathfinder);

            // 층이동 마커 Bake — 씬의 TraversalLink를 NavMeshLink(평상시) + 그래프 링크(예측)로 굽는다.
            // 같은 소스에서 양쪽을 만들기 때문에 평상시·예측의 층이동이 구조적으로 어긋나지 않는다.
            var markers = FindObjectsByType<TraversalLink>(FindObjectsSortMode.None);

            // 예측 그래프: 이 씬용으로 구운 에셋이 있으면 그걸 쓰고, 없으면 코드 그래프로 폴백.
            // 폴백 덕분에 SampleScene(하드코딩 그래프)은 굽지 않아도 그대로 동작한다.
            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            var graphAsset = Resources.Load<PredictionGraphAsset>(PredictionGraphAsset.ResourceName(sceneName));
            GraphPathfinder graphPathfinder;
            if (graphAsset != null && graphAsset.HasData)
            {
                graphPathfinder = GraphPathfinder.FromBake(graphAsset.ToBake());
                Debug.Log($"[예측 그래프] '{sceneName}' 구운 그래프 사용 — 노드 {graphAsset.nodeCount} · 링크 {graphAsset.linkCount} " +
                          $"(구운 시각 {graphAsset.bakedAt}, 마커 {graphAsset.markerCount}개)");
            }
            else
            {
                graphPathfinder = GraphPathfinder.CreateArena();
                Debug.LogWarning($"[예측 그래프] '{sceneName}'용 구운 그래프가 없어 코드 그래프(SampleScene 전용)로 폴백합니다.\n" +
                                 "  이 씬에서 예측을 쓰려면 Tools/층이동 링크/예측 그래프 굽기 를 실행하십시오.");
            }
            if (markers.Length > 0)
            {
                var baked = TraversalBaker.Bake(markers, System.Array.Empty<ArenaNavNode>());
                navPathfinder.links = baked.ToArray();   // 코너 좌표 매칭으로 진입 판정
                Debug.Log($"[층이동] 마커 {markers.Length}개 → 링크 {baked.Count}개 구움");
            }

            services      = new SimServices(collision, navPathfinder);
            graphServices = new SimServices(collision, graphPathfinder);

            world = SimWorld.Create();
            world.mapVersion = predictionMap.mapVersion;
            world.player = PlayerSim.Spawn(map.playerSpawn);
            spawnPoints = map.spawns;
            spawnTimer = SimConfig.SpawnIntervalTicks;   // 첫 틱부터 소환 시작

            restartSpawn = map.playerSpawn;
            restartYaw = map.playerYaw;
            restartMapVersion = predictionMap.mapVersion;

            prevWorld = Snapshot.Clone(in world);

            views.Init();
            SetupCamera();
            prediction.Init(cam, gameplayVcam.transform);
            console = gameObject.AddComponent<DevConsole>();   // ` 개발 콘솔(몹 소환 등)
            ReloadSpawnConfig();   // 씬에 MapSpawnConfig 있으면 그 맵의 스폰 세팅 채택
            input.Yaw = map.playerYaw;   // 씬의 PlayerSpawnPoint 방향(없으면 남쪽 180)
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            Debug.Log($"[Main] 시작. 스폰지점 {spawnPoints.Count}개, {SimConfig.SpawnIntervalTicks}틱마다 소환(최대 {SimConfig.SpawnCap}).\n" +
                      "  WASD 이동 · 마우스 시점 · Space 더블점프 · Shift+WASD 4방향 대시 · 좌클릭 평타 · 우클릭 런지 · F 예측 · F1 튜닝 · Esc 커서");
        }

        static void ValidatePredictionMapAgainstNavMesh(ArenaMapBake bake, NavMeshPathfinder runtimePathfinder)
        {
            int offMesh = 0;
            for (int i = 0; i < bake.nodes.Length; i++)
            {
                Vector3 authored = bake.nodes[i].position;
                if (!runtimePathfinder.ClampToWalkable(authored, 1.5f, out Vector3 sampled) ||
                    Vector3.Distance(authored, sampled) > 1.5f)
                    offMesh++;
            }
            if (offMesh > 0)
                Debug.LogWarning($"[Prediction] mapVersion {bake.mapVersion}: 그래프 노드 {offMesh}/{bake.nodes.Length}개가 런타임 NavMesh와 맞지 않습니다. ArenaMapAuthoring을 다시 베이크하세요.");
        }

        void Update()
        {
            // ★ 시선을 내가 소유하지 않는 구간에서는 마우스 시점을 동결한다(버튼 버퍼는 계속 받음).
            //   · 히트스톱  — sim 틱을 건너뛰므로 그동안의 마우스 이동이 쌓였다가 풀릴 때 튄다
            //   · 찌르기 락온 — CombatCamera가 시선을 덮어쓰는데 그 위에 델타가 계속 더해진다
            //   동결을 안 하면 "얼음이 풀리는 순간 그동안 휘두른 양이 한꺼번에" 반영돼 뚝 끊긴다.
            input.FreezeLook = HitStop.FrozenTicks > 0
                            || world.player.combat.lungePhase != CombatConfig.LgNone
                            || world.player.combat.gloryPhase != CombatConfig.GlNone;

            // 콘솔 열림 중엔 게임 입력·시점 정지(sim은 계속 돌아 소환한 몹 관찰 가능). 컷신 중엔 조작 잠금.
            // [자유 주행, 2026-07-22] 예전엔 prediction.Frozen(=Preview·Following 모두)이면 폴링을
            // 막았다 — 기록 입력을 재생하는 모드에선 맞지만, 자유 주행은 이동·시점의 소유권이
            // 사용자에게 있어서 막으면 조작이 통째로 죽는다. BlocksLiveInput이 그 구분을 안다.
            if (!prediction.BlocksLiveInput && !ConsoleOpen && !Cutscene.Active) input.PollFrame();

            // 정지 아닐 때: F 감시 / 정지 중: 루트 표시·탑다운 카메라.
            // 콘솔에 타이핑 중이면 입력만 차단(표시·카메라는 계속) — 'f' 타이핑에 예지가 발동하던 버그.
            prediction.Tick(in world, ConsoleOpen);

            fixedAccum += Time.deltaTime;
            float alpha = Mathf.Clamp01(fixedAccum / Time.fixedDeltaTime);
            // [2026-07-22] 미리보기 중엔 sim이 안 도므로(아래 FixedUpdate가 Preview에서 early-return)
            // 몹 애니메이터도 얼린다 — 정지 화면에서 몹 다리만 계속 걷던 버그 수정.
            EntityViews.SimFrozen = prediction.state == PredictionController.State.Preview;
            views.Sync(in world, in prevWorld, alpha);
            // 시선이 사용자 것인 동안에는 자동 추종 카메라를 쓰지 않는다 — 자유 주행처럼
            // 통째로 넘긴 경우뿐 아니라, 슬로우 포켓 동안만 빌려준 경우(모드 11)도 포함이다.
            // 둘이 동시에 켜지면 같은 vcam pose를 서로 덮어쓴다.
            if (prediction.state == PredictionController.State.Following
                && !prediction.UsesLiveLookCamera
                && views.PlayerAnchor != null)
                prediction.UpdateFollowingCameraRenderPose(
                    views.PlayerAnchor.position, views.PlayerAnchor.eulerAngles.y);

            // 정지 중엔 예측 컨트롤러가 카메라를 잡는다(탑다운). 아닐 때만 1인칭. 콘솔·컷신 중엔 시점 고정
            // (컷신 중엔 CinemachineTrack이 컷신 vcam을 잡으므로 게임플레이 vcam pose를 덮지 않는다).
            if (!prediction.BlocksLiveInput && !ConsoleOpen && !Cutscene.Active)
            {
                if (VrMode.Enabled)
                {
                    // VR: 리그 루트만 플레이어 위치로 옮긴다. 시점 회전은 XR(머리)이 소유하므로 카메라를 안 건드린다.
                    //     리그 yaw는 스폰 방향(몸통)으로 두고, 머리는 그 위에 상대적으로 돈다.
                    if (xrRig != null && views.PlayerAnchor != null)
                    {
                        xrRig.position = views.PlayerAnchor.position + Vector3.up * eyeHeight;
                        xrRig.rotation = Quaternion.Euler(0f, input.Yaw, 0f);
                    }
                }
                else if (gameplayVcam != null && views.PlayerAnchor != null)
                {
                    // 실제 카메라가 아니라 vcam pose에 쓴다 — Brain이 이걸 따라가며 Impulse(쉐이킹)를 얹는다.
                    gameplayVcam.transform.position = views.PlayerAnchor.position + Vector3.up * eyeHeight;
                    gameplayVcam.transform.rotation = Quaternion.Euler(input.Pitch, input.Yaw, 0f);
                }
                if (input.EscapePressed()) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            }
        }

        void OnGUI()
        {
            if (VrMode.Enabled) return;   // IMGUI는 스테레오 변환 불가 → VR에선 화면 디버그 HUD를 그리지 않는다
            int previousDepth = GUI.depth;
            GUI.depth = -200;
            try
            {
                prediction.DrawRhythmHud();
            }
            finally
            {
                GUI.depth = previousDepth;
            }
        }

        void OnDisable()
        {
            prediction.RestoreNormalTimeScale();
        }

        /// <summary>
        /// 몹 절차 애니메이션(시선 추적 등)은 여기서 적용한다.
        /// Animator는 Update 이후·LateUpdate 이전에 클립을 써 넣으므로,
        /// Update에서 본을 돌리면 그 직후 전부 덮어써진다. 반드시 LateUpdate여야 한다.
        /// </summary>
        void LateUpdate()
        {
            if (Cutscene.Active) return;
            views.LateSync(in world, PlayerAimPoint());
        }

        /// <summary>
        /// 몹이 바라볼 지점 = <b>플레이어 눈높이</b>.
        /// 카메라와 같은 높이여야 "눈을 마주친다"가 성립한다(가슴을 보면 고개를 숙여 노려보는 느낌이 죽는다).
        /// </summary>
        Vector3 PlayerAimPoint()
            => world.player.pos + Vector3.up * eyeHeight;

        void FixedUpdate()
        {
            // >>> [예측 세션 변경, 2026-07-18] 원래 코드는 아래 두 줄이었다:
            //   if (prediction.Frozen) return;   // 예측 정지 중엔 sim·소환 멈춤
            //   prevWorld = Snapshot.Clone(in world);
            //   InputCmd cmd = input.Consume();
            // 즉 예측 관련 상태(Preview든 Following이든)면 통째로 멈췄었다. 확정한 경로를
            // 실제 플레이어가 자동으로 수행하게 하려면(Following) 시뮬레이션을 계속 돌리되
            // 실시간 입력 대신 기록된 입력을 넣어야 해서, Preview/Following을 분리했다.
            // 되돌리려면: 아래 if/else 블록을 지우고 위 두 줄로 교체 + SpawnTick() 조건도 제거.
            if (prediction.state == PredictionController.State.Preview) return;   // 미리보기 중엔 정지

            // 컷신 중엔 sim 완전 정지(플레이어 이동·공격·소환 불가). 카메라는 Timeline이 잡는다.
            if (Cutscene.Active) return;

            // 히트스톱(A안): 얼린 틱만큼 sim 전진을 건너뛴다(입력 소비 전에 return → 기록 입력 보존).
            // timeScale은 건드리지 않으므로 뷰 연출(파티클·셰이크·화면효과)은 계속 재생된다.
            if (HitStop.FrozenTicks > 0) { HitStop.FrozenTicks--; return; }

            InputCmd cmd;
            if (prediction.state == PredictionController.State.Following)
            {
                // 확정된 예측 경로를 실제로 재생 — 기록된 입력을 그대로 넣는다. 재생이 끝나면
                // TryConsumeFollowingInput이 false를 주면서 자동으로 Idle로 돌아간다.
                // [자유 주행, 2026-07-22] 이 모드에서만 실시간 입력을 함께 넘긴다 — 이동은
                // 사용자 것 그대로 쓰고 예측은 액션 노드만 얹는다.
                InputCmd live = prediction.FreerunActive ? input.Consume() : InputCmd.Empty;
                if (!prediction.TryConsumeFollowingInput(in live, out cmd)) return;
            }
            else if (ConsoleOpen)
            {
                cmd = InputCmd.Empty;
                cmd.yaw = input.Yaw;
                cmd.pitch = input.Pitch;
                // 개발 패널이 열려 있어도 자동 콤보 테스트는 공격을 넣을 수 있게 한다
                if (DevInput.ConsumeAttack()) cmd.attack = true;
            }
            else
            {
                cmd = input.Consume();
                if (DevInput.ConsumeAttack()) cmd.attack = true;
            }

            prevWorld = Snapshot.Clone(in world);
            // following(확정 경로 재생)은 예측과 동일하게 그래프로 돌려야 예측 결과와 일치. 평소는 NavMesh.
            SimServices step = prediction.state == PredictionController.State.Following ? graphServices : services;
            SimStep.Run(ref world, in cmd, in step);
            fixedAccum = 0f;

            // 확정 경로 재생 중엔 예측이 가정한 대로(spawnLocked) 새 적 소환을 잠근다 —
            // 아니면 예측이 못 본 적이 재생 중에 끼어들어 결과가 어긋난다.
            if (prediction.state != PredictionController.State.Following)
                SpawnTick();
            if (prediction.state == PredictionController.State.Following)
                prediction.AfterFollowingStep();
            // <<< [예측 세션 변경 끝]

            // 고정 그래프 층이동 시작 감지
            for (int i = 0; i < world.enemyCount; i++)
            {
                if (prevWorld.enemyCount > i &&
                    prevWorld.enemies[i].traversalPhase == TraversalPhase.None &&
                    world.enemies[i].traversalPhase == TraversalPhase.Pause)
                    Debug.Log($"[층이동] 적 {i} {world.enemies[i].activeMoveKind} 시작 (tick {world.tick})");
            }
        }

        /// <summary>
        /// 일정 간격으로 스폰. 씬 세팅(MapSpawnConfig)이 있으면 지점별 지정 종류로,
        /// 없으면 맵 기본 스폰지점 + 3축 분포(폴백)로 순번대로 한 마리씩(최대치까지).
        /// </summary>
        void SpawnTick()
        {
            if (!AutoSpawn) return;   // 콘솔에서 autospawn off 하면 멈춤

            int interval = spawnConfig != null ? spawnConfig.intervalTicks : SimConfig.SpawnIntervalTicks;
            int configuredCap = spawnConfig != null ? spawnConfig.cap : SimConfig.SpawnCap;
            int cap = Mathf.Min(configuredCap, SimConfig.SpawnCap);

            spawnTimer++;
            if (spawnTimer < interval) return;
            if (world.AliveCount() >= cap) return;   // 죽은 적 제외 → 처치하면 재스폰
            spawnTimer = 0;

            if (spawnConfig != null && spawnConfig.entries != null && spawnConfig.entries.Length > 0)
            {
                SpawnEntry e = spawnConfig.entries[nextSpawn % spawnConfig.entries.Length];
                var (c, m, s) = SimWorld.ExperimentalAutoSpawn(nextSpawn);
                nextSpawn++;
                if (e.point == null) return;
                world.AddEnemy(e.point.position, c, m, s);   // 실험 분포, entries는 위치만 사용
            }
            else if (spawnPoints != null && spawnPoints.Count > 0)
            {
                var (c, m, s) = SimWorld.ExperimentalAutoSpawn(nextSpawn);
                world.AddEnemy(spawnPoints[nextSpawn % spawnPoints.Count], c, m, s);
                nextSpawn++;
            }
        }

        /// <summary>씬의 MapSpawnConfig를 다시 찾아 채택(Inspector 수정 후 콘솔 reload용).</summary>
        public void ReloadSpawnConfig()
        {
            spawnConfig = FindFirstObjectByType<MapSpawnConfig>();
            if (spawnConfig != null) AutoSpawn = spawnConfig.autoSpawnOnStart;
        }

        // ── 개발 콘솔 훅 ──
        /// <summary>플레이어 정면에 지정 조합 몹 한 마리 소환.</summary>
        public void DevSpawn(CombatType combat, MobilityType mobility, SizeClass size)
        {
            if (world.AliveCount() >= SimConfig.SpawnCap)
            {
                Debug.LogWarning($"[Spawn] 동시 몬스터 상한 {SimConfig.SpawnCap}마리 — 추가 소환을 거부합니다.");
                return;
            }
            float yr = input.Yaw * Mathf.Deg2Rad;
            Vector3 fwd = new Vector3(Mathf.Sin(yr), 0f, Mathf.Cos(yr));
            Vector3 at = world.player.pos + fwd * DevSpawnDistance;
            world.AddEnemy(at, combat, mobility, size);
        }

        public void DevClear() => world.DevClearEnemies();
        public int AliveEnemyCount() => world.AliveCount();

        void SetupCamera()
        {
            cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                go.tag = "MainCamera";
                cam = go.AddComponent<Camera>();
            }
            cam.nearClipPlane = 0.1f;   // 벽면 최소거리(≈0.25) 안쪽 → 벽에 붙어도 뒤가 안 잘림

            // 안티앨리어싱(SMAA) — 계단·흐릿한 테두리 완화. 뷰모델 오버레이 카메라도 같은 값을 쓴다.
            var camData = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>()
                       ?? cam.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            camData.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            if (VrMode.Enabled)
            {
                // VR 경로 — XR(Cardboard)이 카메라 자세(머리 회전)를 소유한다.
                //  · CinemachineBrain을 붙이지 않는다: 붙이면 vcam pose로 카메라 transform을 덮어써
                //    XR 헤드트래킹과 매 프레임 싸운다.
                //  · 카메라를 리그 루트의 자식으로 두고, 리그 루트만 플레이어 위치로 옮긴다(Update).
                //    카메라의 로컬 자세는 XR이 머리 트래킹으로 채운다.
                var rig = new GameObject("[XR Rig]");
                xrRig = rig.transform;
                cam.transform.SetParent(xrRig, false);
                cam.transform.localPosition = Vector3.zero;
                cam.transform.localRotation = Quaternion.identity;

                // prediction.Init 등이 참조하는 gameplayVcam은 만들되(비어 있어도 무해) 카메라는 구동하지 않는다.
                var vgoVr = new GameObject("GameplayVCam");
                gameplayVcam = vgoVr.AddComponent<CinemachineCamera>();

                // ScreenSpace HUD를 머리 앞 World-Space 패널로 변환(양안 렌더). 늦게 뜨는 HUD도 매 프레임 처리.
                var hudGo = new GameObject("[VrHudSpace]");
                var hud = hudGo.AddComponent<VrHudSpace>();
                hud.head = cam.transform;
                return;
            }

            // Cinemachine: Brain이 vcam pose를 따라 실제 카메라를 움직인다. 1인칭·예측·컷신을 vcam으로 통일.
            var brain = cam.GetComponent<CinemachineBrain>() ?? cam.gameObject.AddComponent<CinemachineBrain>();
            // FPS 게임플레이 카메라는 블렌드 금지 → 진입/전환 시 즉시 컷(잠깐 확대돼 보이는 블렌드 인 제거).
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);

            // ★ 갱신 시점을 LateUpdate로 못 박는다.
            //   기본값 SmartUpdate는 "타깃이 어떻게 움직이는가"를 보고 FixedUpdate/LateUpdate를
            //   자동으로 고르는데, 우리 vcam은 Main.Update(프레임)에서 pose를 쓰므로 FixedUpdate로
            //   샘플링되면 프레임률(가변)과 틱(고정 60Hz)이 어긋나 화면이 끊겨 보인다.
            //   FOV 킥처럼 프레임 단위로 변하는 값은 이 어긋남이 특히 크게 드러난다.
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;
            brain.BlendUpdateMethod = CinemachineBrain.BrainUpdateMethods.LateUpdate;

            var vgo = new GameObject("GameplayVCam");
            gameplayVcam = vgo.AddComponent<CinemachineCamera>();
            var lens = LensSettings.FromCamera(cam);            // 원래 카메라 렌즈(FOV·클립) 상속 → 이관 후 화면 변화 방지
            if (gameplayFov > 0f) lens.FieldOfView = gameplayFov;
            gameplayVcam.Lens = lens;
            vgo.AddComponent<CinemachineImpulseListener>();   // 타격 쉐이킹(Impulse) 수신 — 발생은 Phase 3
        }

        void OnDrawGizmos()
        {
            if (Application.isPlaying) SimGizmos.Draw(in world);
        }
    }

    /// <summary>Play 누르면 자동 실행. 씬에 Main이 없으면 하나 만든다(수동 추가해도 중복 안 됨).</summary>
    public static class AutoBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() => EnsureMain();

        /// <summary>
        /// 씬의 <see cref="Main"/>을 돌려준다. 없으면 여기서 만든다.
        ///
        /// <para><b>왜 공개 함수인가</b> — 타이틀 화면은 <c>Main.Start()</c>가 <b>돌기 전에</b>
        /// Main을 꺼야 한다(Start에서 NavMesh를 굽느라 몇 초가 걸리고, 그동안 화면이 검다).
        /// 그런데 타이틀의 부트도 <see cref="Boot"/>와 같은 AfterSceneLoad라 둘 중 누가 먼저
        /// 도는지 유니티가 보장하지 않는다. 양쪽이 이 함수를 거치면 순서가 어떻든
        /// <b>Main은 정확히 하나</b>고, 먼저 도는 쪽이 만들고 나중 쪽은 그걸 받는다.</para>
        /// </summary>
        public static Main EnsureMain()
        {
            var existing = Object.FindFirstObjectByType<Main>();
            if (existing != null) return existing;

            var main = new GameObject("[Main]").AddComponent<Main>();
            // SampleScene = 코드 큐브맵, 그 외(Demo 등) = 씬 지형 사용
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            main.useSceneGeometry = scene != "SampleScene";
            return main;
        }
    }
}
