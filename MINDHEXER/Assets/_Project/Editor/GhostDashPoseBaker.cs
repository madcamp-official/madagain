using UnityEngine;
using UnityEditor;

namespace Game.EditorTools
{
    /// <summary>
    /// 예측 잔상의 <b>방향별 대시 포즈</b> 클립을 굽는다.
    ///
    /// 배경: Mixamo 클립 중 대시/회피 모션이 프로젝트에 없어서, 앞/뒤/좌/우 대시 잔상이 전부
    /// 달리기 포즈로 보였다. 대시 잔상은 "움직이는 애니메이션"이 아니라 정지 스냅샷 한 장이면
    /// 충분하므로, Run 클립의 스트라이드 프레임에서 휴머노이드 머슬 값을 뽑아 손으로 수정한 뒤
    /// 1프레임짜리 휴머노이드 클립으로 저장한다. 휴머노이드 머슬 클립이라 아바타가 바뀌어도
    /// 리타게팅이 그대로 통한다.
    ///
    /// 결과물: Resources/GhostDashForward · GhostDashBackward · GhostDashLeft · GhostDashRight.
    /// PredictionController.LoadGhostClips가 이 이름으로 로드한다.
    ///
    /// 머슬 부호(이 리그에서 실측 확인함):
    ///   Front-Back  : − 앞 / + 뒤       In-Out : + 바깥
    ///   Left-Right  : − 왼쪽 / + 오른쪽  Down-Up: + 위
    ///   Lower Leg Stretch : + 폄 / − 굽힘
    /// 포즈를 다시 손보려면 아래 상수만 고치고 메뉴를 다시 실행하면 된다.
    /// </summary>
    public static class GhostDashPoseBaker
    {
        const string GhostPrefabResource = "PlayerGhost";
        const string BaseClipResource = "GhostRun";
        const string OutputDir = "Assets/_Project/Prefabs/Resources";
        /// <summary>베이스로 삼는 Run 클립 시각(초) — 앞뒤로 크게 벌어진 스트라이드 프레임.</summary>
        const float BaseClipTime = 0.13f;

        // 머슬 인덱스(HumanTrait.MuscleName 기준)
        const int SpineFB = 0, SpineLR = 1, ChestFB = 3, ChestLR = 4;
        const int NeckNod = 9, NeckTilt = 10, HeadNod = 12, HeadTilt = 13;
        const int LUpFB = 21, LUpIO = 22, LLowSt = 24, LFootUD = 26;
        const int RUpFB = 29, RUpIO = 30, RLowSt = 32, RFootUD = 34;
        const int LShoFB = 38, LArmUp = 39, LArmFB = 40, LFore = 42;
        const int RShoFB = 47, RArmUp = 48, RArmFB = 49, RFore = 51;

        [MenuItem("Tools/예측/대시 잔상 포즈 굽기")]
        public static void Bake()
        {
            var prefab = Resources.Load<GameObject>(GhostPrefabResource);
            var baseClip = Resources.Load<AnimationClip>(BaseClipResource);
            if (prefab == null || baseClip == null)
            {
                Debug.LogError($"[대시 포즈] Resources에서 '{GhostPrefabResource}' 또는 " +
                    $"'{BaseClipResource}'를 찾지 못했습니다.");
                return;
            }

            BakeOne(prefab, baseClip, "GhostDashForward");
            BakeOne(prefab, baseClip, "GhostDashBackward");
            BakeOne(prefab, baseClip, "GhostDashLeft");
            BakeOne(prefab, baseClip, "GhostDashRight");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[대시 포즈] 4종 굽기 완료 — " + OutputDir);
        }

        // ── 찌르기(런지) ─────────────────────────────────────────────
        // 대시와 달리 런지는 "한 장"이 아니라 <b>준비 → 꽂힘</b> 두 포즈를 담은 2키 클립이다.
        // PredictionController가 런지 구간(12틱)의 잔상마다 클립의 다른 시각을 굽기 때문에,
        // 블링크 경로에 늘어선 잔상을 한눈에 보면 찌르기가 펼쳐진다.
        //
        // 자세(레퍼런스 이미지 기준): 칼 쥔 오른팔을 앞으로 완전히 뻗고, 뒷다리(왼쪽)는 몸 축을
        // 따라 뒤로 길게 뻗어 밀어내고, 앞다리는 무릎을 접어 끌어올린다. 상체는 깊게 눕는다.
        const string LungeClipName = "GhostLunge";
        /// <summary>클립 길이(초). 잔상은 정규화 구간(0~1)으로 샘플하므로 값 자체는 상관없지만,
        /// 에디터에서 스크럽해 보기 좋게 잡는다.</summary>
        const float LungeClipLength = 0.4f;
        /// <summary>준비/꽂힘 시점의 상체 눕힘(도). 클립의 RootQ는 SampleAnimation이 무시하므로
        /// 실제 적용은 PredictionConfig.GhostLungeFromPitch/ToPitch가 한다 — 값을 바꾸면 둘 다.</summary>
        public const float LungePrepPitch = 18f, LungeHitPitch = 60f;

        [MenuItem("Tools/예측/찌르기(런지) 잔상 포즈 굽기")]
        public static void BakeLunge()
        {
            var prefab = Resources.Load<GameObject>(GhostPrefabResource);
            var baseClip = Resources.Load<AnimationClip>(BaseClipResource);
            if (prefab == null || baseClip == null)
            {
                Debug.LogError($"[찌르기 포즈] Resources에서 '{GhostPrefabResource}' 또는 " +
                    $"'{BaseClipResource}'를 찾지 못했습니다.");
                return;
            }

            GameObject inst = Object.Instantiate(prefab);
            try
            {
                var animator = inst.GetComponentInChildren<Animator>(true);
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                {
                    Debug.LogError("[찌르기 포즈] PlayerGhost 프리팹에 휴머노이드 Animator가 없습니다.");
                    return;
                }

                baseClip.SampleAnimation(animator.gameObject, BaseClipTime);
                var handler = new HumanPoseHandler(animator.avatar, animator.transform);
                var pose = new HumanPose();
                handler.GetHumanPose(ref pose);
                float bodyY = pose.bodyPosition.y;
                var baseMuscles = new float[pose.muscles.Length];
                for (int i = 0; i < baseMuscles.Length; i++) baseMuscles[i] = pose.muscles[i] * BaseMuscleBlend;
                handler.Dispose();

                var prep = (float[])baseMuscles.Clone(); ApplyLungePrep(prep);
                var hit = (float[])baseMuscles.Clone(); ApplyLungeHit(hit);
                WriteTwoKeyClip(prep, hit, bodyY, LungeClipName, LungeClipLength);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[찌르기 포즈] 굽기 완료 — {OutputDir}/{LungeClipName}.anim " +
                    $"(눕힘 {LungePrepPitch}°→{LungeHitPitch}°는 PredictionConfig가 적용)");
            }
            finally
            {
                Object.DestroyImmediate(inst);
            }
        }

        /// <summary>준비 — 몸을 웅크리고 칼 쥔 팔을 허리로 당겨 장전한다.</summary>
        static void ApplyLungePrep(float[] m)
        {
            m[RArmFB] = 0.10f; m[RArmUp] = -0.10f; m[RFore] = -0.85f; m[RShoFB] = 0.10f;
            m[LArmFB] = -0.35f; m[LArmUp] = 0.25f; m[LFore] = -0.55f; m[LShoFB] = -0.15f;
            m[LUpFB] = 0.35f; m[LLowSt] = -0.35f; m[LFootUD] = -0.30f;    // 뒷다리 접어 장전
            m[RUpFB] = -0.55f; m[RLowSt] = -0.60f; m[RFootUD] = 0.20f;    // 앞다리도 모아 접음
            m[SpineFB] = -0.10f; m[ChestFB] = -0.05f;
            m[NeckNod] = 0.35f; m[HeadNod] = 0.35f;
        }

        /// <summary>꽂힘 — 팔을 완전히 뻗고 뒷다리를 뒤로 길게 밀어낸 관통 순간.</summary>
        static void ApplyLungeHit(float[] m)
        {
            m[RArmFB] = -0.95f; m[RArmUp] = 0.20f; m[RFore] = 0.95f; m[RShoFB] = -0.50f;
            m[LArmFB] = 0.90f; m[LArmUp] = 0.30f; m[LFore] = -0.25f; m[LShoFB] = 0.30f;
            m[LUpFB] = 0.90f; m[LLowSt] = 1.00f; m[LFootUD] = -0.90f;     // 뒷다리 뒤로 최대
            m[RUpFB] = -0.90f; m[RLowSt] = -0.60f; m[RFootUD] = 0.15f;    // 앞무릎 끌어올림
            m[SpineFB] = 0.12f; m[ChestFB] = 0.12f;
            m[NeckNod] = 0.70f; m[HeadNod] = 0.65f;    // 깊이 눕어도 시선은 표적
        }

        // ── 포즈 확인용 ──────────────────────────────────────────────
        // 구운 포즈를 눈으로 보려면 씬에 실물로 세워두고 씬 뷰에서 돌려보는 게 제일 빠르다.
        // 미리보기 오브젝트는 HideFlags.DontSave라 씬에 저장되지 않고, 아래 "치우기"로 지운다.
        const string PreviewRootName = "__대시포즈_미리보기";

        [MenuItem("Tools/예측/대시 잔상 포즈 씬에 세우기")]
        public static void SpawnPreview()
        {
            ClearPreview();
            var prefab = Resources.Load<GameObject>(GhostPrefabResource);
            if (prefab == null)
            {
                Debug.LogError($"[대시 포즈] Resources에서 '{GhostPrefabResource}'를 찾지 못했습니다.");
                return;
            }

            var root = new GameObject(PreviewRootName) { hideFlags = HideFlags.DontSave };
            string[] clipNames = { "GhostDashForward", "GhostDashBackward", "GhostDashLeft", "GhostDashRight" };
            string[] labels = { "1_앞대시_W", "2_뒤대시_S", "3_왼대시_A", "4_오른대시_D" };
            // 실제 대시가 가는 방향(캐릭터는 넷 다 +Z를 본다 — 게임에서도 시선은 고정이고
            // 몸만 옆/뒤로 밀려나므로, 포즈가 이 화살표 방향과 맞는지 보면 된다).
            Vector3[] dashDirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };

            for (int i = 0; i < clipNames.Length; i++)
            {
                var clip = Resources.Load<AnimationClip>(clipNames[i]);
                var ghost = Object.Instantiate(prefab, root.transform);
                ghost.name = labels[i];
                ghost.transform.position = new Vector3(i * 2.5f, 0f, 0f);
                ghost.transform.rotation = Quaternion.identity;   // 전부 +Z를 본다
                var animator = ghost.GetComponentInChildren<Animator>(true);
                if (clip != null && animator != null)
                {
                    clip.SampleAnimation(animator.gameObject, 0f);
                    animator.transform.localPosition = Vector3.zero;
                    animator.transform.localRotation = Quaternion.identity;
                    // 런타임과 동일하게 몸통 눕힘까지 적용해야 실제로 보이는 모습이 된다
                    // (게임에서는 PredictionController가 배치 회전으로 준다).
                    ghost.transform.rotation = BodyTiltFor(clipNames[i]);
                }
                else Debug.LogWarning($"[대시 포즈] 클립 '{clipNames[i]}'을 못 찾아 바인드 포즈로 세웁니다.");
                foreach (var smr in ghost.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    smr.updateWhenOffscreen = true;   // 미리보기에서 컬링돼 사라지지 않게

                // 대시 방향 화살표(바닥에 눕힌 납작한 막대) — 포즈가 이 방향으로 밀려나 보여야 한다.
                // 잔상 본체는 눕혀지므로 화살표는 root에 붙여 월드 기준으로 고정한다.
                var arrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
                arrow.name = labels[i] + "_방향";
                arrow.transform.SetParent(root.transform, false);
                arrow.transform.localScale = new Vector3(0.12f, 0.03f, 1.4f);
                arrow.transform.position = ghost.transform.position + dashDirs[i] * 0.9f;
                arrow.transform.rotation = Quaternion.LookRotation(dashDirs[i], Vector3.up);
                Object.DestroyImmediate(arrow.GetComponent<Collider>());
            }

            Selection.activeGameObject = root;
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.FrameSelected();
            Debug.Log("[대시 포즈] 씬에 4종을 세웠습니다 — 씬 뷰에서 돌려보세요. " +
                "네 명 모두 +Z(파란 축)를 보고 있고, 발밑 막대가 각자 대시로 밀려나는 방향입니다. " +
                "확인이 끝나면 Tools/예측/대시 잔상 포즈 치우기.");
        }

        /// <summary>찌르기 구간을 게임에서 보이는 그대로(클립 시각 + 그 시각의 눕힘) 늘어세운다.
        /// 잔상 간격이 촘촘해서 실제로는 이 사이가 더 빽빽하게 채워진다.</summary>
        [MenuItem("Tools/예측/찌르기(런지) 잔상 포즈 씬에 세우기")]
        public static void SpawnLungePreview()
        {
            ClearPreview();
            var prefab = Resources.Load<GameObject>(GhostPrefabResource);
            var clip = Resources.Load<AnimationClip>(LungeClipName);
            if (prefab == null || clip == null)
            {
                Debug.LogError($"[찌르기 포즈] '{GhostPrefabResource}' 또는 '{LungeClipName}'을 " +
                    "Resources에서 찾지 못했습니다 — 먼저 굽기 메뉴를 실행하세요.");
                return;
            }

            var root = new GameObject(PreviewRootName) { hideFlags = HideFlags.DontSave };
            const int steps = 6;
            for (int i = 0; i < steps; i++)
            {
                float u = i / (float)(steps - 1);
                var ghost = Object.Instantiate(prefab, root.transform);
                ghost.name = $"런지_{i}_{Mathf.RoundToInt(u * 100f)}%";
                // 런지는 앞으로 블링크하므로 잔상이 진행 방향(+Z)으로 늘어선다.
                ghost.transform.position = new Vector3(0f, 0f, u * 5f);
                ghost.transform.rotation = Quaternion.Euler(Mathf.Lerp(LungePrepPitch, LungeHitPitch, u), 0f, 0f);
                var animator = ghost.GetComponentInChildren<Animator>(true);
                if (animator != null)
                {
                    clip.SampleAnimation(animator.gameObject, u * clip.length);
                    animator.transform.localPosition = Vector3.zero;
                }
                foreach (var smr in ghost.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    smr.updateWhenOffscreen = true;
            }

            Selection.activeGameObject = root;
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.FrameSelected();
            Debug.Log("[찌르기 포즈] 씬에 구간 6장을 세웠습니다(왼쪽=준비, 오른쪽=꽂힘). " +
                "확인이 끝나면 Tools/예측/대시 잔상 포즈 치우기.");
        }

        /// <summary>미리보기 캐릭터 이름 접두사. 루트를 지웠는데도 자식이 살아남는 경우(과거
        /// 스폰 버전이 남긴 고아 등)가 있어서, 루트뿐 아니라 이름으로도 훑어서 지운다.</summary>
        static readonly string[] PreviewNamePrefixes =
        {
            "1_앞대시", "2_뒤대시", "3_왼대시", "4_오른대시", "찌르기_",
        };

        [MenuItem("Tools/예측/대시 잔상 포즈 치우기")]
        public static void ClearPreview()
        {
            // 계층이 지워지면서 열거 결과가 흔들릴 수 있어 몇 번 훑는다.
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (var go in Object.FindObjectsByType<GameObject>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (go == null) continue;
                    if (go.name != PreviewRootName && !IsPreviewName(go.name)) continue;
                    Object.DestroyImmediate(go);
                }
            }
        }

        static bool IsPreviewName(string name)
        {
            foreach (string prefix in PreviewNamePrefixes)
                if (name.StartsWith(prefix)) return true;
            return false;
        }

        static void BakeOne(GameObject prefab, AnimationClip baseClip, string outputName)
        {
            GameObject inst = Object.Instantiate(prefab);
            try
            {
                var animator = inst.GetComponentInChildren<Animator>(true);
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                {
                    Debug.LogError("[대시 포즈] PlayerGhost 프리팹에 휴머노이드 Animator가 없습니다.");
                    return;
                }

                // 베이스 스트라이드 프레임의 머슬 값을 뽑아온다.
                baseClip.SampleAnimation(animator.gameObject, BaseClipTime);
                var handler = new HumanPoseHandler(animator.avatar, animator.transform);
                var pose = new HumanPose();
                handler.GetHumanPose(ref pose);
                float bodyY = pose.bodyPosition.y;
                // 베이스 스트라이드는 "잔향"만 남기고 거의 지운다 — 대시 포즈는 달리기와 완전히
                // 다른 자세라, 베이스를 그대로 두면 달리기 실루엣이 계속 비친다.
                for (int i = 0; i < pose.muscles.Length; i++) pose.muscles[i] *= BaseMuscleBlend;
                ApplyDashPose(pose.muscles, outputName);
                handler.Dispose();

                WriteClip(pose.muscles, bodyY, outputName);
            }
            finally
            {
                Object.DestroyImmediate(inst);
            }
        }

        /// <summary>베이스 스트라이드 잔향 비율. 0이면 완전 중립(웅크린 바인드 포즈)이라
        /// 오히려 어색해서, 아주 살짝만 남긴다.</summary>
        const float BaseMuscleBlend = 0.15f;

        /// <summary>방향별 대시 포즈를 덧씌우고, <b>몸통 전체 눕힘</b>(루트 회전)을 돌려준다.
        /// 베이스 프레임에서는 오른다리가 뒤, 왼다리가 앞이다.
        ///
        /// 레퍼런스(사용자 제공 컨셉아트 3장)의 핵심:
        ///  · 앞 대시 — 스프린트 발진. 몸을 깊게 눕히고 뒷다리를 몸 축을 따라 길게 뻗는다.
        ///  · 뒤 대시 — 몸을 뒤로 눕히고 두 다리를 앞으로 쭉 뻗어 땅을 밀어낸다.
        ///  · 옆 대시 — <b>얼굴은 정면을 본 채</b> 몸만 가는 쪽으로 기운다(=앞 대시와의 결정적 차이).
        ///    그래서 눕힘이 pitch가 아니라 roll이고, 목·머리는 반대로 세워 정면을 유지한다.</summary>
        /// <summary>포즈와 짝을 이루는 몸통 눕힘. 런타임에서는 PredictionController가
        /// 같은 값(PredictionConfig)으로 배치 회전에 적용한다 — 미리보기도 똑같이 보이게 여기서 참조.</summary>
        public static Quaternion BodyTiltFor(string outputName)
        {
            switch (outputName)
            {
                case "GhostDashBackward":
                    return Quaternion.Euler(Game.View.PredictionConfig.GhostDashBackwardPitch, 0f, 0f);
                case "GhostDashLeft":
                    return Quaternion.Euler(0f, 0f, Game.View.PredictionConfig.GhostDashSideRoll);
                case "GhostDashRight":
                    return Quaternion.Euler(0f, 0f, -Game.View.PredictionConfig.GhostDashSideRoll);
                default:
                    return Quaternion.Euler(Game.View.PredictionConfig.GhostDashForwardPitch, 0f, 0f);
            }
        }

        static void ApplyDashPose(float[] m, string outputName)
        {
            switch (outputName)
            {
                case "GhostDashForward":
                    // 뒷다리(오른쪽)는 눕은 몸통 축을 그대로 이어 뒤로 길게, 앞다리는 무릎을 끌어올림.
                    m[RUpFB] = 0.15f; m[RLowSt] = 0.90f; m[RFootUD] = -0.80f;
                    m[LUpFB] = -0.75f; m[LLowSt] = -0.55f; m[LFootUD] = 0.30f;
                    m[SpineFB] = 0.15f; m[ChestFB] = 0.10f;   // 눕힘은 루트가 하고 등은 살짝 젖힘
                    m[NeckNod] = 0.60f; m[HeadNod] = 0.60f;   // 숙여도 시선은 앞
                    m[RArmFB] = -0.85f; m[RArmUp] = 0.20f; m[RFore] = -0.15f;   // 앞으로 뻗은 팔
                    m[LArmFB] = 0.80f; m[LArmUp] = 0.10f; m[LFore] = -0.50f;
                    return;   // 눕힘(pitch 55°)은 BodyTiltFor / PredictionConfig가 담당

                case "GhostDashBackward":
                    // [피드백 반영, 2026-07-22] 예전엔 눕힘 -40°에 두 다리를 수평으로 뻗어서
                    // "뒤로 넘어지는" 그림이었다. 뒤 대시는 어디까지나 상체를 뒤로 힘주며 버티는
                    // 동작이므로, 눕힘을 -20°로 줄이고 두 발이 모두 땅 가까이 남게 한다.
                    m[LUpFB] = -0.40f; m[LLowSt] = 0.60f; m[LFootUD] = 0.25f;   // 앞다리: 앞으로 뻗어 디딤
                    m[RUpFB] = -0.15f; m[RLowSt] = -0.25f; m[RFootUD] = -0.05f; // 뒷다리: 몸 아래서 지지
                    m[SpineFB] = 0.25f; m[ChestFB] = 0.15f;    // 상체를 뒤로 힘주는 아치
                    m[NeckNod] = 0.20f; m[HeadNod] = 0.20f;
                    m[LArmFB] = -0.45f; m[RArmFB] = -0.35f;
                    m[LArmUp] = 0.20f; m[RArmUp] = 0.15f;
                    m[LFore] = -0.40f; m[RFore] = -0.40f;
                    return;   // 눕힘(pitch -20°)은 BodyTiltFor / PredictionConfig가 담당

                default:   // GhostDashLeft / GhostDashRight
                {
                    bool left = outputName == "GhostDashLeft";
                    float side = left ? 1f : -1f;   // roll 부호: +면 상체가 캐릭터 왼쪽(-X)으로 기운다
                    // 가는 쪽 다리가 lead(무릎 접어 끌어올림), 반대쪽이 땅을 밀어내는 push.
                    int leadUp = left ? LUpFB : RUpFB, pushUp = left ? RUpFB : LUpFB;
                    int leadIO = left ? LUpIO : RUpIO, pushIO = left ? RUpIO : LUpIO;
                    int leadLow = left ? LLowSt : RLowSt, pushLow = left ? RLowSt : LLowSt;
                    int pushFoot = left ? RFootUD : LFootUD;
                    int leadArmFB = left ? LArmFB : RArmFB, leadArmUp = left ? LArmUp : RArmUp;
                    int leadFore = left ? LFore : RFore;
                    int pushArmFB = left ? RArmFB : LArmFB, pushArmUp = left ? RArmUp : LArmUp;
                    int pushFore = left ? RFore : LFore, pushSho = left ? RShoFB : LShoFB;
                    // [피드백 반영, 2026-07-22] 예전엔 두 다리를 크게 벌려서(leadIO 0.55/pushIO 0.35)
                    // 뜬 다리가 디딤발에서 너무 떨어져 어색했다 — 좌우로 벌리지 말고 붙인다.
                    m[leadIO] = 0.15f; m[pushIO] = 0.15f;
                    m[leadUp] = -0.25f; m[pushUp] = 0.25f;
                    m[leadLow] = -0.60f;                       // 뜬 다리는 무릎을 접어 몸쪽으로
                    m[pushLow] = 0.55f; m[pushFoot] = -0.35f;  // 디딤발은 펴서 땅을 밀어냄
                    // 몸이 기운 만큼 척추·목·머리를 반대로 굽혀 얼굴을 세운다. 부호는 렌더로 실측:
                    // roll이 +(왼쪽)일 때 반대굽힘은 −쪽이다(이름과 반대이니 그냥 이 조합을 유지할 것).
                    // [피드백 반영] 예전엔 목·머리 반대굽힘이 0.60이라 머리가 대시 방향으로 같이
                    // 꺾여 보였다 — 0.90까지 올려 "아주 약간만" 꺾이게 한다.
                    m[SpineLR] = -side * 0.50f; m[ChestLR] = -side * 0.35f;
                    m[NeckTilt] = -side * 0.90f; m[HeadTilt] = -side * 0.90f;
                    m[NeckNod] = 0.15f; m[HeadNod] = 0.15f;
                    m[leadArmFB] = -0.35f; m[leadArmUp] = 0.40f; m[leadFore] = -0.25f;
                    // [피드백 반영] 디딤발 쪽 팔은 뒤로 뻗는 느낌(예전엔 양팔 다 앞이었다).
                    m[pushArmFB] = 0.40f; m[pushArmUp] = 0.20f; m[pushFore] = -0.20f;
                    m[pushSho] = 0.15f;
                    return;   // 기울임(roll ±30°)은 BodyTiltFor / PredictionConfig가 담당
                }
            }
        }

        /// <summary>머슬 값 배열을 1프레임 휴머노이드 클립으로 저장한다. 머슬 커브 + Root 커브를
        /// 모두 넣어야 isHumanMotion이 서고 SampleAnimation 리타게팅이 통한다.</summary>
        static void WriteClip(float[] muscles, float bodyY, string outputName)
        {
            var clip = new AnimationClip { name = outputName, frameRate = 60f };
            const float oneFrame = 1f / 60f;

            string[] muscleNames = HumanTrait.MuscleName;
            for (int i = 0; i < muscleNames.Length && i < muscles.Length; i++)
                SetConstant(clip, muscleNames[i], muscles[i], oneFrame);

            // 위치·회전은 런타임에서 경로와 배치 회전이 덮어쓰므로 원점·무회전으로 고정한다.
            // (몸통 눕힘을 RootQ에 담아보려 했으나 SampleAnimation이 휴머노이드 클립의 루트
            //  회전을 적용하지 않는다 — 그래서 눕힘은 PredictionConfig.GhostDash*Pitch/Roll이 맡는다.)
            SetConstant(clip, "RootT.x", 0f, oneFrame);
            SetConstant(clip, "RootT.y", bodyY, oneFrame);
            SetConstant(clip, "RootT.z", 0f, oneFrame);
            SetConstant(clip, "RootQ.x", 0f, oneFrame);
            SetConstant(clip, "RootQ.y", 0f, oneFrame);
            SetConstant(clip, "RootQ.z", 0f, oneFrame);
            SetConstant(clip, "RootQ.w", 1f, oneFrame);

            string path = $"{OutputDir}/{outputName}.anim";
            AssetDatabase.CreateAsset(clip, path);
            Debug.Log($"[대시 포즈] {path} (human={clip.isHumanMotion})");
        }

        /// <summary>두 포즈를 양 끝 키로 갖는 클립을 저장한다(중간 시각은 선형 보간 = 동작이 펼쳐진다).
        /// 눕힘은 클립에 담지 않는다 — SampleAnimation이 휴머노이드 클립의 RootQ를 적용하지 않으므로
        /// 실제 각도는 PredictionConfig 쪽 배치 회전이 준다.</summary>
        static void WriteTwoKeyClip(float[] from, float[] to, float bodyY, string outputName, float length)
        {
            var clip = new AnimationClip { name = outputName, frameRate = 60f };
            string[] muscleNames = HumanTrait.MuscleName;
            for (int i = 0; i < muscleNames.Length && i < from.Length; i++)
                SetLinear(clip, muscleNames[i], from[i], to[i], length);

            SetConstant(clip, "RootT.x", 0f, length);
            SetConstant(clip, "RootT.y", bodyY, length);
            SetConstant(clip, "RootT.z", 0f, length);
            SetConstant(clip, "RootQ.x", 0f, length);
            SetConstant(clip, "RootQ.y", 0f, length);
            SetConstant(clip, "RootQ.z", 0f, length);
            SetConstant(clip, "RootQ.w", 1f, length);

            string path = $"{OutputDir}/{outputName}.anim";
            AssetDatabase.CreateAsset(clip, path);
            Debug.Log($"[찌르기 포즈] {path} (human={clip.isHumanMotion}, len={clip.length:F2}s)");
        }

        /// <summary>양 끝 두 키를 직선으로 잇는다. AddKey의 기본 접선은 평평해서(ease in/out)
        /// 구간 가운데 잔상들이 한 포즈에 몰려 보이므로, 접선을 직접 직선으로 잡는다.</summary>
        static void SetLinear(AnimationClip clip, string property, float from, float to, float duration)
        {
            float slope = (to - from) / Mathf.Max(0.0001f, duration);
            var a = new Keyframe(0f, from, slope, slope);
            var b = new Keyframe(duration, to, slope, slope);
            clip.SetCurve("", typeof(Animator), property, new AnimationCurve(a, b));
        }

        static void SetConstant(AnimationClip clip, string property, float value, float duration)
        {
            var curve = new AnimationCurve();
            curve.AddKey(0f, value);
            curve.AddKey(duration, value);
            clip.SetCurve("", typeof(Animator), property, curve);
        }
    }
}
