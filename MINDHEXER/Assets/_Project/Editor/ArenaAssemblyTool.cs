using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// 마스터 씬 조립 보조.
    ///
    /// 아레나 씬은 각자 <b>완전한 플레이 씬</b>(Main·카메라·게임플레이 시스템 포함)이라 그냥 못 합친다.
    /// 이 도구는 씬을 <b>시스템(부트스트랩)</b>과 <b>콘텐츠(지형·웨이브·방)</b>로 갈라:
    ///   ② 콘텐츠만 프리팹으로 뽑고 (여러 아레나를 마스터에 끼워 넣을 조각),
    ///   ③ 시스템만 남긴 마스터 씬 뼈대를 만든다.
    ///
    /// 먼저 ①로 무엇이 시스템/콘텐츠로 분류되는지 확인(안 바꿈)한 뒤 ②·③을 쓰는 걸 권장.
    /// </summary>
    static class ArenaAssemblyTool
    {
        const string ContentDir = "Assets/_Project/Prefabs/Arenas/Content";
        const string ScenesDir  = "Assets/_Project/Scenes";
        const string MasterName = "Level_Main";

        // 시스템(부트스트랩)으로 취급할 루트 이름(정확히 일치). 콘텐츠 프리팹엔 안 넣고, 마스터엔 남긴다.
        static readonly string[] BootstrapExact =
        {
            "Main", "[Main]", "_Cameras", "_Gameplay", "GameplayVCam",
            "PlayerAnchor", "Directional Light", "EventSystem",
        };

        /// <summary>이 루트가 "시스템"인가(= 콘텐츠 프리팹에서 제외, 마스터에 유지).</summary>
        static bool IsBootstrap(GameObject go)
        {
            string n = go.name;
            if (n.StartsWith("["))          return true;   // [CombatHud] 같은 매니저
            if (n.StartsWith("Global Volume") || n.StartsWith("Post")) return true;
            foreach (var b in BootstrapExact) if (n == b) return true;
            if (go.GetComponentInChildren<Camera>(true) != null) return true;   // 카메라 리그
            return false;
        }

        static void Classify(Scene sc, out List<GameObject> boot, out List<GameObject> content)
        {
            boot = new List<GameObject>();
            content = new List<GameObject>();
            foreach (var go in sc.GetRootGameObjects())
                (IsBootstrap(go) ? boot : content).Add(go);
        }

        // ── ① 분류 미리보기 (안 바꿈) ───────────────────────────────────────────
        [MenuItem("Tools/Arena 조립/① 분류 미리보기 (안전)")]
        static void Preview()
        {
            var sc = SceneManager.GetActiveScene();
            Classify(sc, out var boot, out var content);
            Debug.Log(
                $"[Arena 조립] 씬 '{sc.name}' 분류\n" +
                $"■ 시스템(마스터에 유지 / 콘텐츠 제외) [{boot.Count}]: {string.Join(", ", boot.Select(g => g.name))}\n" +
                $"■ 콘텐츠(프리팹화 대상)        [{content.Count}]: {string.Join(", ", content.Select(g => g.name))}");
        }

        // ── ② 현재 씬 → 콘텐츠 프리팹 ────────────────────────────────────────────
        [MenuItem("Tools/Arena 조립/② 현재 씬 → 콘텐츠 프리팹")]
        static void MakeContentPrefab()
        {
            var sc = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(sc.path))
            { EditorUtility.DisplayDialog("Arena 조립", "먼저 아레나 씬을 열고 저장해 주세요.", "확인"); return; }

            Classify(sc, out var boot, out var content);
            if (content.Count == 0)
            { EditorUtility.DisplayDialog("Arena 조립", "콘텐츠로 분류된 루트가 없습니다.\n① 미리보기로 분류를 확인하세요.", "확인"); return; }

            string rootName = sc.name + "_Content";
            if (!EditorUtility.DisplayDialog("콘텐츠 프리팹화",
                    $"씬 '{sc.name}'의 콘텐츠 {content.Count}개를 '{rootName}' 하나로 묶어 프리팹으로 만듭니다.\n\n" +
                    $"묶을 콘텐츠: {string.Join(", ", content.Select(g => g.name))}\n\n" +
                    $"제외(시스템): {string.Join(", ", boot.Select(g => g.name))}\n\n" +
                    "씬에서는 콘텐츠가 이 루트 밑으로 모여 '프리팹 인스턴스'가 됩니다(원본 배치 유지).",
                    "진행", "취소")) return;

            EnsureFolder(ContentDir);

            var rootGo = new GameObject(rootName);
            Undo.RegisterCreatedObjectUndo(rootGo, "콘텐츠 루트 생성");
            // 월드 위치 유지하며 콘텐츠를 새 루트 밑으로 모은다.
            foreach (var c in content)
                Undo.SetTransformParent(c.transform, rootGo.transform, "콘텐츠 모으기");

            string path = AssetDatabase.GenerateUniqueAssetPath($"{ContentDir}/{rootName}.prefab");
            var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(rootGo, path, InteractionMode.UserAction);
            EditorSceneManager.MarkSceneDirty(sc);

            Debug.Log($"[Arena 조립] 콘텐츠 프리팹 생성 → {path}\n" +
                      $"이 씬을 저장하면 '시스템 + {rootName}(프리팹 인스턴스)' 상태가 됩니다.", prefab);
            EditorGUIUtility.PingObject(prefab);
        }

        // ── ③ 현재 씬 → 마스터 뼈대 복제 (시스템만) ─────────────────────────────
        [MenuItem("Tools/Arena 조립/③ 현재 씬 → 마스터 뼈대 복제 (시스템만)")]
        static void MakeMasterSkeleton()
        {
            var sc = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(sc.path))
            { EditorUtility.DisplayDialog("Arena 조립", "먼저 시스템을 갖춘 아레나 씬을 열고 저장해 주세요.", "확인"); return; }

            if (!EditorUtility.DisplayDialog("마스터 뼈대 생성",
                    $"현재 씬 '{sc.name}'을 복제해 '{ScenesDir}/{MasterName}.unity'를 만들고,\n" +
                    "콘텐츠(지형·웨이브 등)를 지워 시스템(Main·카메라·게임플레이)만 남깁니다.\n\n" +
                    "여기에 ②에서 만든 콘텐츠 프리팹들을 끼워 넣으면 됩니다.\n\n" +
                    "⚠ 시스템이 콘텐츠 오브젝트를 참조했다면 그 참조는 끊깁니다(수동 재배선 필요할 수 있음).",
                    "진행", "취소")) return;

            EnsureFolder(ScenesDir);
            EditorSceneManager.SaveScene(sc);   // 원본 먼저 저장(안전)

            string masterPath = AssetDatabase.GenerateUniqueAssetPath($"{ScenesDir}/{MasterName}.unity");
            if (!AssetDatabase.CopyAsset(sc.path, masterPath))
            { EditorUtility.DisplayDialog("Arena 조립", "씬 복제에 실패했습니다.", "확인"); return; }
            AssetDatabase.Refresh();

            var master = EditorSceneManager.OpenScene(masterPath, OpenSceneMode.Single);
            Classify(master, out var boot, out var content);
            // 이름은 파괴 전에 캡처해 둔다 — 파괴 후 g.name 접근은 MissingReferenceException.
            string contentNames = string.Join(", ", content.Select(g => g.name));
            foreach (var c in content) Object.DestroyImmediate(c);
            EditorSceneManager.MarkSceneDirty(master);
            EditorSceneManager.SaveScene(master);

            Debug.Log($"[Arena 조립] 마스터 뼈대 생성 → {masterPath}\n" +
                      $"남긴 시스템 [{boot.Count}]: {string.Join(", ", boot.Select(g => g.name))}\n" +
                      $"지운 콘텐츠 [{content.Count}]: {contentNames}\n" +
                      "이제 이 씬에 콘텐츠 프리팹들을 드래그해 배치하세요.");
        }

        // ── ④ 시스템 이식 (다른 씬 → 현재 씬) ─────────────────────────────────
        // 마스터에 시스템(Main·매니저 등)이 없거나 빠졌을 때, 검증된 아레나 씬에서 가져온다.
        // 원본 씬은 Additive로 열어 오브젝트를 옮긴 뒤 저장 없이 닫으므로 원본은 변하지 않는다.
        [MenuItem("Tools/Arena 조립/④ 시스템 이식 (다른 씬에서 가져오기)")]
        static void ImportSystem()
        {
            var target = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(target.path))
            { EditorUtility.DisplayDialog("Arena 조립", "먼저 대상 씬(마스터)을 열고 저장해 주세요.", "확인"); return; }

            string abs = EditorUtility.OpenFilePanel("시스템 원본 씬 선택",
                                                     "Assets/_Project/Prefabs/Arenas", "unity");
            if (string.IsNullOrEmpty(abs)) return;
            string src = FileUtil.GetProjectRelativePath(abs);
            if (string.IsNullOrEmpty(src))
            { EditorUtility.DisplayDialog("Arena 조립", "프로젝트 안의 씬만 선택할 수 있습니다.", "확인"); return; }
            if (src == target.path)
            { EditorUtility.DisplayDialog("Arena 조립", "원본과 대상이 같은 씬입니다.", "확인"); return; }

            var srcScene = EditorSceneManager.OpenScene(src, OpenSceneMode.Additive);
            try
            {
                Classify(srcScene, out var boot, out _);
                var existing = new HashSet<string>(target.GetRootGameObjects().Select(g => g.name));

                var moved = new List<string>();
                var skipped = new List<string>();
                foreach (var go in boot)
                {
                    if (existing.Contains(go.name)) { skipped.Add(go.name); continue; }
                    Undo.MoveGameObjectToScene(go, target, "시스템 이식");
                    moved.Add(go.name);
                }
                EditorSceneManager.MarkSceneDirty(target);
                Debug.Log($"[Arena 조립] 시스템 이식 — '{srcScene.name}' → '{target.name}'\n" +
                          $"가져옴 [{moved.Count}]: {string.Join(", ", moved)}\n" +
                          $"이미 있어 건너뜀 [{skipped.Count}]: {string.Join(", ", skipped)}\n" +
                          "⚠ 가져온 시스템이 원본 씬의 콘텐츠를 참조했다면 그 참조는 끊깁니다 — " +
                          "Main·게임플레이 인스펙터에서 Missing 필드를 점검하십시오. 이상 없으면 씬을 저장하십시오.");
            }
            finally
            {
                EditorSceneManager.CloseScene(srcScene, true);   // 저장 안 함 → 원본 무손상
            }
        }

        // ── ⑤ 선택한 콘텐츠 프리팹에 방 컴포넌트 심기 ──────────────────────────
        // Project 창에서 Arena_N_Content 프리팹들을 선택하고 실행:
        // 루트에 BoxCollider(존, isTrigger, 지형 바운드로 초기화) + ArenaRoom + ArenaLights를 심는다.
        // 이미 있는 컴포넌트는 건너뛴다(재실행 안전). 존 크기는 이후 씬에서 손 조정 권장.
        [MenuItem("Tools/Arena 조립/⑤ 선택한 콘텐츠 프리팹에 방 컴포넌트 심기")]
        static void InstallRoomComponents()
        {
            var paths = Selection.objects.OfType<GameObject>()
                .Select(AssetDatabase.GetAssetPath)
                .Where(p => !string.IsNullOrEmpty(p) && p.EndsWith(".prefab"))
                .Distinct().ToList();
            if (paths.Count == 0)
            { EditorUtility.DisplayDialog("Arena 조립",
                  "Project 창에서 콘텐츠 프리팹(Arena_N_Content)을 선택하고 실행해 주세요.", "확인"); return; }

            var report = new System.Text.StringBuilder();
            foreach (string path in paths)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    report.AppendLine($"{System.IO.Path.GetFileNameWithoutExtension(path)}: {Install(root)}");
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            Debug.Log("[Arena 조립] 방 컴포넌트 심기\n" + report);
        }

        static string Install(GameObject root)
        {
            var added = new List<string>();

            var box = root.GetComponent<BoxCollider>();
            if (box == null)
            {
                // 존 초기 크기 = 지형 전체 바운드(렌더러 우선, 없으면 콜라이더)
                Bounds b = default; bool has = false;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                { if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds); }
                if (!has)
                    foreach (var c in root.GetComponentsInChildren<Collider>(true))
                    { if (!has) { b = c.bounds; has = true; } else b.Encapsulate(c.bounds); }

                box = root.AddComponent<BoxCollider>();
                box.isTrigger = true;
                if (has)
                {
                    box.center = root.transform.InverseTransformPoint(b.center);
                    box.size   = b.size;
                }
                added.Add("BoxCollider(존)");
            }

            if (root.GetComponent<ArenaRoom>() == null)
            {
                var room = root.AddComponent<ArenaRoom>();
                room.runner = root.GetComponentInChildren<WaveRunner>(true);
                room.zone   = box;
                added.Add(room.runner != null ? "ArenaRoom" : "ArenaRoom(⚠ WaveRunner 없음 — 추가 필요)");
            }

            if (root.GetComponent<ArenaLights>() == null)
            { root.AddComponent<ArenaLights>(); added.Add("ArenaLights"); }

            return added.Count > 0 ? "추가 → " + string.Join(", ", added) : "이미 완비(변경 없음)";
        }

        static void EnsureFolder(string dir)
        {
            if (AssetDatabase.IsValidFolder(dir)) return;
            var parts = dir.Split('/');
            string cur = parts[0];   // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
