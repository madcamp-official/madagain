using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// 맵 굽기 통합 창. 맵을 고칠 때마다 "무엇을 어떤 순서로 다시 구워야 하는지" 한 곳에 모은다.
    ///
    /// 순서가 중요하다 — 앞 단계 결과를 뒷 단계가 먹는다:
    ///   ① NavMesh   ← 콜라이더에서 구움 (평상시 몹 길찾기)
    ///   ② 층이동 마커 ← 콜라이더 레이캐스트로 가장자리·틈 탐지
    ///   ③ 예측 그래프 ← NavMesh + 마커에서 구움 (예측 전용 고정 그래프)
    /// 하나라도 낡으면 예측과 실제 플레이가 어긋난다.
    ///
    /// ①을 에디터에서 미리 구워두면 Play가 즉시 시작되고, ③도 Play 없이 구울 수 있다
    /// (이 프로젝트는 원래 Play 중에만 NavMesh가 존재했다).
    /// </summary>
    public class MapBakeTool : EditorWindow
    {
        const string BakedDir = "Assets/_Project/View/Nav/Baked";

        [MenuItem("Tools/맵 굽기", false, 0)]
        static void Open() => GetWindow<MapBakeTool>("맵 굽기").minSize = new Vector2(360, 340);

        void OnGUI()
        {
            var scene = EditorSceneManager.GetActiveScene();
            EditorGUILayout.LabelField("씬", scene.name, EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "맵(콜라이더)을 고쳤다면 ① → ② → ③ 순서로 다시 구우십시오.\n" +
                "순서를 지키지 않으면 예측과 실제 플레이가 어긋납니다.", MessageType.Info);

            DrawStatus();

            EditorGUILayout.Space(8);

            // ── ① NavMesh ──
            EditorGUILayout.LabelField("① NavMesh (평상시 길찾기)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("콜라이더에서 구움. 미리 구워두면 Play가 즉시 시작됩니다.", EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(Application.isPlaying))
                if (GUILayout.Button("① NavMesh 굽기", GUILayout.Height(26))) BakeNavMesh();
            if (Application.isPlaying)
                EditorGUILayout.HelpBox("Play 중에는 구울 수 없습니다. 정지 후 실행하십시오.", MessageType.Warning);

            EditorGUILayout.Space(6);

            // ── ② 마커 ──
            EditorGUILayout.LabelField("② 층이동 마커 (낙하·도약 지점)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("콜라이더를 훑어 가장자리·틈을 찾습니다. Play 불필요.", EditorStyles.miniLabel);
            if (GUILayout.Button("② 자동 배치 창 열기", GUILayout.Height(24)))
                EditorApplication.ExecuteMenuItem("Tools/층이동 링크/자동 배치 창 열기");

            EditorGUILayout.Space(6);

            // ── ③ 예측 그래프 ──
            EditorGUILayout.LabelField("③ 예측 그래프", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("NavMesh + 마커에서 구움. ①이 끝나 있어야 합니다.", EditorStyles.miniLabel);
            if (GUILayout.Button("③ 예측 그래프 굽기 창 열기", GUILayout.Height(24)))
                EditorApplication.ExecuteMenuItem("Tools/층이동 링크/예측 그래프 굽기");

            EditorGUILayout.Space(10);
            if (GUILayout.Button("플레이어 시작 지점 만들기 (PlayerSpawnPoint)"))
                CreatePlayerSpawn();

            EditorGUILayout.Space(6);
            using (new EditorGUI.DisabledScope(Application.isPlaying))
                if (GUILayout.Button("현재 씬 구운 데이터 지우기 (NavMesh + 예측 그래프)"))
                    ClearBaked();
        }

        // 상태 조회는 무겁다(삼각화·씬 전수검색) → 1초 간격으로만 갱신하고 캐시를 그린다.
        double statAt;
        int verts, markers;
        bool hasSpawn;
        string spawnName = "", graphInfo = "";
        bool hasGraph;

        void RefreshStatus()
        {
            var tri = NavMesh.CalculateTriangulation();
            verts = tri.vertices != null ? tri.vertices.Length : 0;
            markers = Object.FindObjectsByType<TraversalLink>(FindObjectsSortMode.None).Length;

            var sp = Object.FindFirstObjectByType<PlayerSpawnPoint>();
            hasSpawn = sp != null;
            spawnName = sp != null ? sp.name : "";

            string scene = EditorSceneManager.GetActiveScene().name;
            var graph = Resources.Load<PredictionGraphAsset>(PredictionGraphAsset.ResourceName(scene));
            hasGraph = graph != null && graph.HasData;
            graphInfo = hasGraph ? $"노드 {graph.nodeCount} · 링크 {graph.linkCount} ({graph.bakedAt})" : "";
        }

        void DrawStatus()
        {
            if (EditorApplication.timeSinceStartup - statAt > 1.0)
            { RefreshStatus(); statAt = EditorApplication.timeSinceStartup; }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("현재 상태", EditorStyles.boldLabel);
            Row("NavMesh 정점", verts > 0 ? verts.ToString() : "없음 ← ①을 실행하십시오", verts > 0);
            Row("층이동 마커", markers > 0 ? markers + "개" : "없음 ← ②를 실행하십시오", markers > 0);
            Row("예측 그래프", hasGraph ? graphInfo : "없음 ← ③을 실행하십시오 (없으면 예측이 엉뚱해집니다)", hasGraph);
            Row("플레이어 시작점", hasSpawn ? spawnName : "없음 ← 아래 버튼으로 생성 권장", hasSpawn);
        }

        static void Row(string label, string value, bool ok)
        {
            var style = new GUIStyle(EditorStyles.label)
            { normal = { textColor = ok ? new Color(0.35f, 0.8f, 0.4f) : new Color(0.9f, 0.6f, 0.2f) } };
            EditorGUILayout.LabelField(label, value, style);
        }

        // ── ① NavMesh 굽기 ──
        static void BakeNavMesh()
        {
            var scene = EditorSceneManager.GetActiveScene();

            var surface = Object.FindFirstObjectByType<NavMeshSurface>();
            if (surface == null)
            {
                var go = new GameObject("NavMeshSurface");
                surface = go.AddComponent<NavMeshSurface>();
                Undo.RegisterCreatedObjectUndo(go, "NavMeshSurface 생성");
            }
            // 콜라이더 기준으로 굽는다 — 아트 메시는 읽기 불가(isReadable=0)라 렌더 메시로는 못 굽는다.
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();

            // 구운 데이터를 에셋으로 저장해야 씬을 다시 열어도 유지된다.
            var data = surface.navMeshData;
            if (data != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(data)))
            {
                EnsureFolder(BakedDir);
                AssetDatabase.CreateAsset(data, $"{BakedDir}/NavMesh_{scene.name}.asset");
            }
            if (data != null) EditorUtility.SetDirty(data);
            EditorUtility.SetDirty(surface);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);

            var tri = NavMesh.CalculateTriangulation();
            int verts = tri.vertices != null ? tri.vertices.Length : 0;
            if (verts > 0) Debug.Log($"[맵 굽기] NavMesh 완료 — 정점 {verts}. 씬을 저장하십시오(Ctrl+S).");
            else Debug.LogError("[맵 굽기] NavMesh가 비었습니다 — 걸을 수 있는 콜라이더가 있는지 확인하십시오.");
        }

        // ── 구운 데이터 지우기 ──
        // 씬의 모든 NavMeshSurface 데이터를 비우고(컴포넌트는 남김), 이 씬 이름으로 저장된
        // NavMesh 에셋과 예측 그래프 에셋을 삭제한다. 층이동 마커(TraversalLink)는 건드리지 않는다.
        static void ClearBaked()
        {
            var scene = EditorSceneManager.GetActiveScene();
            string navPath   = $"{BakedDir}/NavMesh_{scene.name}.asset";
            string graphPath = $"Assets/_Project/View/Nav/Resources/{PredictionGraphAsset.ResourceName(scene.name)}.asset";

            if (!EditorUtility.DisplayDialog("구운 데이터 지우기",
                    $"씬 '{scene.name}'의 구운 데이터를 지웁니다.\n\n" +
                    "· 씬의 모든 NavMeshSurface 데이터 비우기 (컴포넌트는 남김)\n" +
                    $"· {navPath} 삭제\n" +
                    $"· {graphPath} 삭제\n\n" +
                    "층이동 마커(TraversalLink)는 지우지 않습니다.", "지우기", "취소")) return;

            int surfaces = 0;
            foreach (var s in Object.FindObjectsByType<NavMeshSurface>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (s.navMeshData == null) continue;
                s.RemoveData();          // 활성 NavMesh에서 내리고
                s.navMeshData = null;    // 씬이 든 참조도 비운다(에셋 삭제 후 Missing 방지)
                EditorUtility.SetDirty(s);
                surfaces++;
            }

            int assets = 0;
            if (AssetDatabase.LoadMainAssetAtPath(navPath)   != null && AssetDatabase.DeleteAsset(navPath))   assets++;
            if (AssetDatabase.LoadMainAssetAtPath(graphPath) != null && AssetDatabase.DeleteAsset(graphPath)) assets++;

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[맵 굽기] 구운 데이터 정리 — Surface {surfaces}개 비움, 에셋 {assets}개 삭제. 씬을 저장하십시오(Ctrl+S).");
        }

        static void CreatePlayerSpawn()
        {
            var existing = Object.FindFirstObjectByType<PlayerSpawnPoint>();
            if (existing != null)
            { Selection.activeGameObject = existing.gameObject; Debug.Log("[맵 굽기] 이미 있습니다 — 선택했습니다."); return; }

            var go = new GameObject("PlayerSpawnPoint");
            go.AddComponent<PlayerSpawnPoint>();
            var sv = SceneView.lastActiveSceneView;
            if (sv != null) { Vector3 p = sv.pivot; p.y = 0f; go.transform.position = p; }

            Undo.RegisterCreatedObjectUndo(go, "플레이어 시작 지점 생성");
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[맵 굽기] PlayerSpawnPoint 생성 — 위치와 회전(파란 축=바라보는 방향)을 맞추십시오.");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string[] parts = path.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
