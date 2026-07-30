using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.View
{
    /// <summary>
    /// Title 씬을 프로그램적으로 조립·저장하는 에디터 툴. (메뉴: MINDHEXER ▸ Build ▸ Title Scene)
    ///
    /// MCP 없이도 재현 가능하고 리뷰 가능하도록, 씬 YAML을 수기로 쓰지 않고 실제 Unity API로 빌드한다.
    /// 구성물(다크 톤 + 후광 rim + 얼굴 필 + MegaRobot + 13:12 카메라 + 헤드트래킹 + ScreenSpace UI +
    /// 섬광/페이드 연출 + PLAY/QUIT 배선)을 모두 만들고 Assets/_Project/Scenes/Title.unity에 저장한 뒤
    /// Build Settings에 Title을 추가한다. (지침: Title 외 다른 씬/설정은 건드리지 않음)
    /// </summary>
    public static class TitleSceneBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/Title.unity";
        const string RobotPrefab = "Assets/_Project/Prefabs/Hackables/Boss/MegaRobot.prefab";
        const string VolumeAsset = "Assets/_Project/Scenes/TitleVolume.asset";

        /// <summary>
        /// 로봇 Y 회전(도). 실제로 확인한 결과 MegaRobot의 모델 정면은 +Z라,
        /// -Z에서 바라보는 카메라를 마주보려면 180°가 맞다. (0으로 두면 뒷모습이 보인다)
        /// </summary>
        const float RobotYaw = 180f;

        /// <summary>
        /// 로봇 스케일. 원본 프리팹이 1m급이라 화면을 채우려면 카메라가 바짝 붙어야 했고,
        /// 그만큼 메시·노멀맵이 확대돼 거칠게 보였다. 크게 세우고 멀리서 잡는다.
        /// </summary>
        const float RobotScale = 50f;

        /// <summary>후광(rim) 세기. "그림자가 대부분, 윤곽만 희미하게"가 목표라 낮게 잡는다.</summary>
        const float RimIntensity = 4f;

        /// <summary>후광이 머리 뒤로 물러나는 거리 = 로봇 키 × 이 비율. 광원↔머리 거리가 이 값으로 확정된다.</summary>
        const float RimBackOffRatio = 0.30f;

        /// <summary>후광 사거리 = 로봇 키 × 이 비율. 물러난 거리(0.30)의 두 배라 머리에 확실히 닿는다.</summary>
        const float RimRangeRatio = 0.60f;

        /// <summary>
        /// 후광 콘 각도(도). <b>확산을 막는 건 사거리가 아니라 이 콘이다.</b>
        /// 광원이 머리 뒤 0.30×키 지점에 있으므로, 55°면 머리에서 반경 약 0.16×키만 밝아지고
        /// 그보다 아래(허리 등)는 콘 밖이라 빛이 전혀 닿지 않는다.
        /// </summary>
        const float RimConeAngle = 55f;

        [MenuItem("MINDHEXER/Build/Title Scene")]
        public static void Build()
        {
            if (!EditorUtility.DisplayDialog("Build Title Scene",
                    "Title.unity를 새로 조립해 덮어씁니다.\n(다른 씬/설정은 건드리지 않습니다)\n계속할까요?",
                    "빌드", "취소"))
                return;
            BuildScene();
        }

        /// <summary>다이얼로그 없이 Title 씬을 조립·저장한다. (메뉴/자동빌드 공용)</summary>
        public static void BuildScene()
        {
            // 열려 있던 다른 씬의 미저장 변경을 잃지 않도록 먼저 저장 기회를 준다.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ── 다크 환경(암전 톤) ─────────────────────────────────────────────
            // 앰비언트를 완전한 검정으로 둔다. 아주 약한 앰비언트라도 모든 면을 균일하게 들어올려
            // 빛이 닿지 않는 하반신까지 실루엣으로 떠오르게 만든다. 0이어야 "어둠에 잠긴다".
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.fog = false;
            RenderSettings.skybox = null;

            // ── 로봇: 프리팹 인스턴스(월드 중앙 고정) ─────────────────────────
            Bounds bounds = new Bounds(new Vector3(0f, 1f, 0f), new Vector3(1f, 2f, 1f));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPrefab);
            GameObject robot = null;
            if (prefab != null)
            {
                robot = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                robot.transform.position = Vector3.zero;
                // MegaRobot의 모델 정면은 -Z다. 카메라도 -Z쪽(z<0)에서 +Z를 보므로 회전 0이 정면.
                // (직전 초안은 180°를 줘서 뒷모습이 보였다. 다시 뒤돌면 이 값만 180으로 바꾸면 된다.)
                robot.transform.rotation = Quaternion.Euler(0f, RobotYaw, 0f);   // 카메라를 마주보게
                robot.transform.localScale = Vector3.one * RobotScale;
                bounds = ComputeBounds(robot);   // 스케일 적용 뒤에 재야 카메라·조명이 함께 커진다
            }
            else
            {
                Debug.LogWarning($"[TitleSceneBuilder] MegaRobot 프리팹을 찾지 못했습니다: {RobotPrefab} " +
                                 "— 로봇 없이 나머지를 조립합니다.");
            }

            // 머리 지점(근사): 바운즈 상단 근처.
            Vector3 head = new Vector3(bounds.center.x, bounds.max.y - bounds.size.y * 0.12f, bounds.center.z);

            // ── 카메라: 원거리 정면, 13:12, 헤드트래킹 ─────────────────────────
            // 멀리서 비추는 프레이밍: 약간 망원(FOV↓)으로 압축감을 주고, 로봇 전신 + 위아래 여유를
            // 담도록 거리를 크게 잡아 근접 압박감을 없앤다.
            const float fov = 24f;
            float framedHeight = bounds.size.y * 1.55f;                // 전신 + 여유
            float dist = Mathf.Max(1.5f, (framedHeight * 0.5f) / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad));
            float aimY = bounds.center.y + bounds.size.y * 0.08f;      // 초점을 상반신/머리 쪽으로 살짝 위
            Vector3 aim = new Vector3(bounds.center.x, aimY, bounds.center.z);

            // Unity 기본 카메라 관례(-Z에서 +Z를 봄)를 따른다. 로봇 뒷모습이 보이면 로봇을 Y로 180° 돌리면 된다.
            Vector3 camPos = new Vector3(bounds.center.x, aimY, bounds.min.z - dist);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            // 배경도 완전한 검정. 배경이 조금이라도 밝으면 빛 안 받는 하반신이 '더 검은 덩어리'로
            // 오려낸 듯 보인다. 배경과 같은 값이어야 경계 없이 어둠에 녹아든다.
            cam.backgroundColor = Color.black;
            cam.fieldOfView = fov;
            // 클립 평면도 피사체 크기에 맞춘다. 로봇이 커지면 near를 같이 키워야 뎁스 정밀도가 유지된다.
            cam.nearClipPlane = Mathf.Max(0.05f, bounds.size.y * 0.02f);
            cam.farClipPlane = Mathf.Max(1000f, dist * 5f);
            cam.allowHDR = true;    // 블룸/톤매핑 품질
            cam.allowMSAA = true;
            camGo.transform.position = camPos;
            camGo.transform.LookAt(aim);
            camGo.AddComponent<AudioListener>();
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var headLook = camGo.AddComponent<TitleHeadLook>();
            // 시차는 씬 스케일에 비례해야 보인다(로봇이 클수록 카메라도 멀어지므로 절대값 고정이면 무의미).
            headLook.parallaxAmount = bounds.size.y * 0.06f;
            // 13:12는 카메라 뷰포트 축소가 아니라 오버레이 검은 바로 만든다(아래 BuildLetterbox) →
            // 카메라는 전체 화면 풀해상도로 렌더해 화질을 유지한다.

            // ── 후광(rim) 라이트: 머리 뒤에서 좁은 콘으로 쏘는 스포트라이트 ────────
            //
            // 포인트 라이트를 쓰면 '사거리' 하나가 도달 거리와 확산 범위를 동시에 결정한다.
            // 하반신을 어둡게 하려고 사거리를 줄이면 머리에도 빛이 닿지 않아 모델이 통째로 사라진다.
            // (실제로 그랬다 — 광원을 bounds.max.z 뒤에 뒀는데 그건 팔·다리가 뻗은 지점이라
            //  몸통 중심선의 머리까지는 사거리보다 멀었다.)
            //
            // 스포트라이트는 확산을 '콘'이 막아주므로 사거리를 넉넉히 줄 수 있다.
            // 위치를 bounds.max.z가 아니라 bounds.center.z 기준으로 잡아, 광원↔머리 거리가
            // 로봇 형상과 무관하게 RimBackOffRatio로 확정된다.
            var rimGo = new GameObject("Rim Light");
            var rim = rimGo.AddComponent<Light>();
            rim.type = LightType.Spot;
            rim.color = new Color(0.95f, 0.97f, 1f, 1f);
            rim.intensity = RimIntensity;
            rim.shadows = LightShadows.None;
            rim.range = bounds.size.y * RimRangeRatio;
            rim.spotAngle = RimConeAngle;
            rim.innerSpotAngle = RimConeAngle * 0.55f;

            rimGo.transform.position = new Vector3(
                head.x,
                head.y + bounds.size.y * 0.02f,
                bounds.center.z + bounds.size.y * RimBackOffRatio);   // 머리 '뒤'(카메라 반대편)
            rimGo.transform.LookAt(head);                             // 머리를 정확히 겨눈다

            // ── 얼굴 필 라이트: 정면 근접에서 얼굴을 비춘다. 평소 0, PLAY 섬광 때만(TitleIntro 제어). ─
            var fillGo = new GameObject("Face Fill Light");
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Spot;
            fill.color = new Color(1f, 0.98f, 0.95f, 1f);
            fill.intensity = 0f;
            fill.range = Mathf.Max(3f, dist);
            fill.spotAngle = 16f;      // 좁게 → 얼굴만 도려내듯 비춘다(몸통까지 밝아지지 않게)
            fill.innerSpotAngle = 7f;
            fill.shadows = LightShadows.None;
            Vector3 frontDir = (camPos - head).normalized;             // 머리→카메라 방향(정면)
            fillGo.transform.position = head + frontDir * (dist * 0.35f) + Vector3.up * (bounds.size.y * 0.03f);
            fillGo.transform.LookAt(head);

            // ── URP Volume(후광 글로우/비네트) — API 편차 대비 try/catch ────────
            TryBuildVolume();

            // ── UI: 로고(상단 중앙) + PLAY/QUIT(하단 중앙). ScreenSpaceCamera라 13:12 뷰포트 안에 든다 ─
            var titleSystem = new GameObject("TitleSystem");
            var menu = titleSystem.AddComponent<TitleMenu>();
            var intro = titleSystem.AddComponent<TitleIntro>();
            menu.intro = intro;
            menu.introSceneName = "Intro";
            intro.faceFill = fill;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvasGo = new GameObject("Title Canvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            // ScreenSpaceOverlay = 전체 화면 해상도로 선명하게 그린다(카메라 포스트/레터박스 뷰포트 영향 없음).
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1300f, 1200f);   // 13:12
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // 로고 — 크고 또렷하게 + 외곽선/그림자로 어둠 위 대비.
            var logo = MakeText(canvasGo.transform, "Logo", "MINDHEXER", font, 150, FontStyle.Bold, Color.white);
            AnchorTopCenter(logo.rectTransform, new Vector2(1240f, 240f), -140f);
            AddTextPolish(logo);

            // 버튼 — 크게 + 간격 넓게.
            var play = MakeButton(canvasGo.transform, "PlayButton", "PLAY", font);
            AnchorBottomCenter(play.rt, new Vector2(440f, 112f), 330f);
            var quit = MakeButton(canvasGo.transform, "QuitButton", "QUIT", font);
            AnchorBottomCenter(quit.rt, new Vector2(440f, 112f), 190f);

            UnityEditor.Events.UnityEventTools.AddPersistentListener(play.button.onClick,
                new UnityEngine.Events.UnityAction(menu.Play));
            UnityEditor.Events.UnityEventTools.AddPersistentListener(quit.button.onClick,
                new UnityEngine.Events.UnityAction(menu.Quit));

            // ── 페이드/섬광 오버레이(전체화면 검정, 레터박스 밖까지 덮음) ───────
            var fadeGo = new GameObject("Fade Overlay", typeof(RectTransform));
            var fadeCanvas = fadeGo.AddComponent<Canvas>();
            fadeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            fadeCanvas.sortingOrder = 100;
            var fadeGroup = fadeGo.AddComponent<CanvasGroup>();
            fadeGroup.alpha = 1f;
            fadeGroup.blocksRaycasts = false;
            fadeGroup.interactable = false;
            var fadeImgGo = new GameObject("Black", typeof(RectTransform));
            fadeImgGo.transform.SetParent(fadeGo.transform, false);
            var fadeImg = fadeImgGo.AddComponent<Image>();
            fadeImg.color = Color.black;
            Stretch(fadeImg.rectTransform);
            intro.fadeOverlay = fadeGroup;

            // ── 레터박스 검은 바(전체화면 오버레이, CanvasScaler 없음 = 픽셀 공간). UI 뒤, 페이드 앞. ─
            var lbGo = new GameObject("Letterbox", typeof(RectTransform));
            var lbCanvas = lbGo.AddComponent<Canvas>();
            lbCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            lbCanvas.sortingOrder = -50;                 // UI 콘텐츠(0)보다 뒤, 카메라 렌더 위
            var bars = lbGo.AddComponent<LetterboxBars>();
            bars.targetWidth = 13f; bars.targetHeight = 12f;
            bars.left = MakeBar(lbGo.transform, "BarLeft");
            bars.right = MakeBar(lbGo.transform, "BarRight");
            bars.top = MakeBar(lbGo.transform, "BarTop");
            bars.bottom = MakeBar(lbGo.transform, "BarBottom");

            // ── EventSystem(새 Input System UI 모듈) ───────────────────────────
            if (Object.FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }

            // ── 저장 + Build Settings에 Title 추가 ─────────────────────────────
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ScenePath));
            bool ok = EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log(ok
                ? $"[TitleSceneBuilder] Title 씬 빌드 완료 → {ScenePath}\n" +
                  $"  · 로봇 Y 회전 {RobotYaw}° (정면), 스케일 ×{RobotScale}. 뒤돌아 보이면 RobotYaw만 180으로.\n" +
                  $"  · 후광 rim {RimIntensity} / range = 키×{RimRangeRatio} — 머리 주변만 은은하게. 얼굴 섬광은 PLAY 때만.\n" +
                  "  · 13:12는 오버레이 검은 바로 처리(카메라는 풀해상도 렌더).\n" +
                  "  · 3D 공간 검증: MINDHEXER ▸ Build ▸ Title 3D 검증(마우스 드래그로 궤도 회전).\n" +
                  "  · PLAY 대상 'Intro' 씬은 아직 없어 안전 처리됨(추가되면 자동 이동)."
                : "[TitleSceneBuilder] 씬 저장 실패");

            if (ok) EditorSceneManager.OpenScene(ScenePath);
        }

        // ── 헬퍼 ──────────────────────────────────────────────────────────────

        static Bounds ComputeBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs == null || rs.Length == 0)
                return new Bounds(new Vector3(0f, 1f, 0f), new Vector3(1f, 2f, 1f));
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        static Text MakeText(Transform parent, string name, string content, Font font,
                             int size, FontStyle style, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.text = content;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        struct ButtonRefs { public Button button; public RectTransform rt; }

        static ButtonRefs MakeButton(Transform parent, string name, string label, Font font)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.08f);   // 어두운 반투명 판
            var button = go.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0.08f);
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.22f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0.35f);
            colors.selectedColor = new Color(1f, 1f, 1f, 0.16f);
            button.colors = colors;

            var text = MakeText(go.transform, "Label", label, font, 52, FontStyle.Bold, Color.white);
            Stretch(text.rectTransform);
            AddTextPolish(text);

            return new ButtonRefs { button = button, rt = (RectTransform)go.transform };
        }

        // 어둠 위 텍스트 가독성: 외곽선 + 그림자.
        static void AddTextPolish(Text t)
        {
            var outline = t.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);
            var shadow = t.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
            shadow.effectDistance = new Vector2(3f, -5f);
        }

        static void AnchorTopCenter(RectTransform rt, Vector2 size, float y)
        {
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = size;
            rt.anchoredPosition = new Vector2(0f, y);
        }

        static void AnchorBottomCenter(RectTransform rt, Vector2 size, float y)
        {
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = new Vector2(0f, y);
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static RectTransform MakeBar(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = Color.black;
            img.raycastTarget = false;
            return (RectTransform)go.transform;
        }

        static void TryBuildVolume()
        {
            try
            {
                var profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeAsset);

                // 후광이 흰 덩어리로 번지지 않도록 threshold를 올리고 intensity를 낮춘다.
                var bloom = profile.Add<Bloom>(true);
                bloom.active = true;
                bloom.threshold.overrideState = true; bloom.threshold.value = 1.15f;
                bloom.intensity.overrideState = true; bloom.intensity.value = 0.35f;
                bloom.scatter.overrideState = true; bloom.scatter.value = 0.62f;
                bloom.clamp.overrideState = true; bloom.clamp.value = 6f;
                bloom.tint.overrideState = true; bloom.tint.value = new Color(0.86f, 0.9f, 1f, 1f);
                // 고품질 필터링: 어두운 화면에서 블룸 계단현상(밴딩)이 눈에 띄던 것을 없앤다.
                bloom.highQualityFiltering.overrideState = true; bloom.highQualityFiltering.value = true;

                var vig = profile.Add<Vignette>(true);
                vig.active = true;
                vig.intensity.overrideState = true; vig.intensity.value = 0.62f;
                vig.smoothness.overrideState = true; vig.smoothness.value = 0.75f;

                var tone = profile.Add<Tonemapping>(true);
                tone.active = true;
                tone.mode.overrideState = true; tone.mode.value = TonemappingMode.Neutral;

                // VolumeProfile.Add()가 만든 VolumeComponent는 별도의 ScriptableObject다.
                // .asset의 서브에셋으로 등록하지 않으면 저장 시 전부 null로 날아가고
                // (components: [{fileID: 0}, ...]) 블룸/비네트/톤매핑이 통째로 사라진다.
                foreach (var comp in profile.components)
                {
                    if (comp == null) continue;
                    comp.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
                    if (!AssetDatabase.IsSubAsset(comp))
                        AssetDatabase.AddObjectToAsset(comp, profile);
                    EditorUtility.SetDirty(comp);
                }
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);

                var volGo = new GameObject("Global Volume");
                var vol = volGo.AddComponent<Volume>();
                vol.isGlobal = true;
                vol.sharedProfile = profile;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[TitleSceneBuilder] URP Volume 생성 건너뜀(핵심 씬은 정상): " + e.Message);
            }
        }

        static void AddToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(s => s.path == path)) return;
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[TitleSceneBuilder] Build Settings에 Title 추가: {path}");
        }
    }

    /// <summary>
    /// Title 씬을 열었을 때 비어 있으면 "지금 조립?" 대화창을 띄운다 — 숨은 메뉴를 못 찾아
    /// 빈 타이틀이 뜨는 문제를 막는다. 이미 조립돼 있으면(TitleMenu 존재) 절대 건드리지 않는다.
    /// </summary>
    [InitializeOnLoad]
    static class TitleSceneAutoBuild
    {
        static TitleSceneAutoBuild()
        {
            EditorSceneManager.sceneOpened -= OnOpened;
            EditorSceneManager.sceneOpened += OnOpened;
            // 재컴파일 시 Title이 이미 열려 있으면 sceneOpened가 안 뜨므로 활성 씬도 검사.
            EditorApplication.delayCall += CheckActive;
        }

        static void CheckActive()
        {
            if (Application.isPlaying) return;
            var s = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (s.IsValid() && s.name == "Title" && !HasTitleContent(s)) Prompt();
        }

        static void OnOpened(UnityEngine.SceneManagement.Scene scene, OpenSceneMode mode)
        {
            if (mode != OpenSceneMode.Single || Application.isPlaying) return;
            if (scene.name != "Title" || HasTitleContent(scene)) return;
            Prompt();
        }

        static void Prompt()
        {
            if (EditorUtility.DisplayDialog("Title 씬 조립",
                    "Title 씬이 비어 있습니다.\n지금 타이틀 화면을 조립할까요?\n(로봇·조명·로고·PLAY/QUIT·연출)",
                    "조립", "나중에"))
            {
                // 콜백 안에서 즉시 씬을 바꾸지 않도록 다음 프레임에 실행.
                EditorApplication.delayCall += TitleSceneBuilder.BuildScene;
            }
        }

        static bool HasTitleContent(UnityEngine.SceneManagement.Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
                if (root.GetComponentInChildren<TitleMenu>(true) != null) return true;
            return false;
        }
    }
}
