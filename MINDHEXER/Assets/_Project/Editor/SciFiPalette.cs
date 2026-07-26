using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// 구매 에셋(Sci-Fi Environment) 배치 팔레트.
    /// 썸네일을 보고 클릭하면 씬 뷰 중앙에 배치된다. 회전·스케일·바닥정렬·그룹을 미리 정해둘 수 있다.
    ///
    /// ★ 콜라이더는 이 도구가 절대 건드리지 않는다.
    ///   이 에셋의 메시는 전부 isReadable=0 이라 MeshCollider로 쓰면 런타임 NavMesh 베이크에서
    ///   제외되고("does not allow read access") 빌드에서 깨진다. 콜리전은 프리팹에 Box/Capsule을
    ///   손으로 붙이는 방식으로 간다(자동 링크 배치·예측 그래프가 전부 콜라이더에서 출발한다).
    ///
    /// 배치 시 LODGroup이 없으면 LOD0/1/2를 찾아 연결한다(이 에셋엔 LODGroup이 하나도 없어서
    /// 전환이 안 되고 메시가 겹쳐 그려진다).
    /// </summary>
    public class SciFiPalette : EditorWindow
    {
        const string PrefabDir = "Assets/Remesh Games/Sci-Fi Environment/Prefabs";
        const string FavKey    = "SciFiPalette.Favorites";

        // 목록
        string[] names = new string[0];
        GameObject[] objs = new GameObject[0];
        string[] guids = new string[0];
        readonly HashSet<string> favs = new HashSet<string>();

        // 보기
        string search = "";
        bool favOnly;
        Vector2 scroll;
        float cellSize = 78f;
        float gridHeight = 480f;   // 기본 높이(슬라이더로 조절)

        // 배치 옵션
        Vector3 rotEuler = Vector3.zero;
        Vector3 scale = Vector3.one;
        bool groundAlign = true;
        string groupName = "_Environment_asset";

        [MenuItem("Tools/에셋/Sci-Fi 팔레트")]
        static void Open()
        {
            var w = GetWindow<SciFiPalette>("Sci-Fi 팔레트");
            w.minSize = new Vector2(300, 420);
            w.Show();
        }

        void OnEnable() { LoadFavs(); Refresh(); }

        void Update() { if (AssetPreview.IsLoadingAssetPreviews()) Repaint(); }

        void Refresh()
        {
            if (!AssetDatabase.IsValidFolder(PrefabDir))
            { names = new string[0]; objs = new GameObject[0]; guids = new string[0]; return; }

            string[] paths = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir })
                .Select(AssetDatabase.GUIDToAssetPath).Distinct()
                .OrderBy(p => System.IO.Path.GetFileNameWithoutExtension(p)).ToArray();

            names = paths.Select(System.IO.Path.GetFileNameWithoutExtension).ToArray();
            objs  = paths.Select(AssetDatabase.LoadAssetAtPath<GameObject>).ToArray();
            guids = paths.Select(AssetDatabase.AssetPathToGUID).ToArray();
        }

        void LoadFavs()
        {
            favs.Clear();
            foreach (string g in EditorPrefs.GetString(FavKey, "").Split(','))
                if (!string.IsNullOrEmpty(g)) favs.Add(g);
        }

        void SaveFavs() => EditorPrefs.SetString(FavKey, string.Join(",", favs));

        // ── UI ──
        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                search = GUILayout.TextField(search, EditorStyles.toolbarSearchField);
                if (GUILayout.Button("✕", EditorStyles.toolbarButton, GUILayout.Width(22)))
                { search = ""; GUI.FocusControl(null); }
                favOnly = GUILayout.Toggle(favOnly, "★만", EditorStyles.toolbarButton, GUILayout.Width(44));
                if (GUILayout.Button("새로고침", EditorStyles.toolbarButton, GUILayout.Width(58))) Refresh();
            }

            if (objs.Length == 0)
            { EditorGUILayout.HelpBox($"프리팹을 못 찾았습니다:\n{PrefabDir}", MessageType.Warning); return; }

            DrawGrid();
            DrawOptions();
        }

        void DrawGrid()
        {
            string q = search.Trim().ToLowerInvariant();
            var idx = new List<int>();
            for (int i = 0; i < names.Length; i++)
                if ((!favOnly || favs.Contains(guids[i])) &&
                    (q.Length == 0 || names[i].ToLowerInvariant().Contains(q)))
                    idx.Add(i);

            // 즐겨찾기를 앞으로
            idx = idx.OrderByDescending(i => favs.Contains(guids[i])).ThenBy(i => names[i]).ToList();

            EditorGUILayout.LabelField($"{idx.Count} / {names.Length}개   ·   썸네일 클릭 = 배치",
                                       EditorStyles.miniLabel);

            if (idx.Count == 0)
            {
                EditorGUILayout.HelpBox(favOnly ? "즐겨찾기가 없습니다. ☆를 눌러 등록하십시오."
                                                : "검색 결과가 없습니다.", MessageType.Info);
                return;
            }

            var imgStyle = new GUIStyle(GUI.skin.button)
            {
                imagePosition = ImagePosition.ImageAbove,
                alignment = TextAnchor.LowerCenter,
                fontSize = 9, wordWrap = true,
                padding = new RectOffset(2, 2, 2, 2),
            };

            int cols = Mathf.Max(1, Mathf.FloorToInt((position.width - 26f) / (cellSize + 6f)));
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(gridHeight));
            for (int k = 0; k < idx.Count; k++)
            {
                if (k % cols == 0) EditorGUILayout.BeginHorizontal();

                int i = idx[k];
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(cellSize)))
                {
                    Texture tex = AssetPreview.GetAssetPreview(objs[i]);
                    if (tex == null) tex = AssetPreview.GetMiniThumbnail(objs[i]);

                    if (GUILayout.Button(new GUIContent(names[i], tex, names[i]), imgStyle,
                                         GUILayout.Width(cellSize), GUILayout.Height(cellSize)))
                        Place(objs[i]);

                    bool isFav = favs.Contains(guids[i]);
                    if (GUILayout.Button(isFav ? "★" : "☆", EditorStyles.miniButton,
                                         GUILayout.Width(cellSize), GUILayout.Height(15)))
                    {
                        if (isFav) favs.Remove(guids[i]); else favs.Add(guids[i]);
                        SaveFavs();
                    }
                }

                if (k % cols == cols - 1 || k == idx.Count - 1) EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("썸네일", GUILayout.Width(42));
                cellSize = GUILayout.HorizontalSlider(cellSize, 48f, 160f);
                EditorGUILayout.LabelField($"{cellSize:0}", GUILayout.Width(28));
                EditorGUILayout.LabelField("높이", GUILayout.Width(30));
                gridHeight = GUILayout.HorizontalSlider(gridHeight, 120f, 1400f);
                EditorGUILayout.LabelField($"{gridHeight:0}", GUILayout.Width(32));
            }
        }

        void DrawOptions()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("배치 옵션", EditorStyles.boldLabel);

            rotEuler = EditorGUILayout.Vector3Field(new GUIContent("회전 X/Y/Z (도)",
                "배치할 때 적용할 회전"), rotEuler);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("X +90")) rotEuler.x = Mathf.Repeat(rotEuler.x + 90f, 360f);
                if (GUILayout.Button("Y +90")) rotEuler.y = Mathf.Repeat(rotEuler.y + 90f, 360f);
                if (GUILayout.Button("Z +90")) rotEuler.z = Mathf.Repeat(rotEuler.z + 90f, 360f);
                if (GUILayout.Button("초기화", GUILayout.Width(56))) rotEuler = Vector3.zero;
            }

            scale = EditorGUILayout.Vector3Field(new GUIContent("스케일",
                "1 = 원본 크기 유지(권장). 바꾸면 텍스처 밀도가 왜곡됩니다."), scale);
            if (scale != Vector3.one)
                EditorGUILayout.HelpBox("스케일이 1이 아닙니다 — 텍스처가 늘어나 티가 날 수 있습니다.",
                                        MessageType.Warning);

            groundAlign = EditorGUILayout.Toggle(new GUIContent("바닥에 맞춰 놓기",
                "프리팹 밑면이 배치 지점 높이에 오도록 내립니다(원점이 중심인 에셋이 반쯤 묻히는 것 방지)"),
                groundAlign);

            groupName = EditorGUILayout.TextField(new GUIContent("담을 그룹",
                "비우면 씬 최상단에 배치됩니다"), groupName);
        }

        // ── 배치 ──
        void Place(GameObject src)
        {
            if (src == null) return;

            Transform parent = string.IsNullOrWhiteSpace(groupName) ? null : EnsureGroup(groupName.Trim());
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src, parent);
            if (go == null) return;

            Quaternion rot = Quaternion.Euler(rotEuler);
            Vector3 pos = SpawnPos();

            // 회전·스케일까지 반영한 바운즈의 최저점을 배치 높이에 맞춘다
            if (groundAlign && PrefabBounds(src, out Bounds b))
                pos.y -= TransformedBounds(b, rot, scale).min.y;

            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = scale;

            string lod = SetupLODGroup(go);
            Undo.RegisterCreatedObjectUndo(go, "에셋 배치");
            Selection.activeGameObject = go;
            Debug.Log($"[에셋] {go.name} 배치 — {lod}");
        }

        static Transform EnsureGroup(string name)
        {
            var found = GameObject.Find(name);
            if (found != null) return found.transform;
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "그룹 생성");
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            return go.transform;
        }

        [MenuItem("Tools/에셋/선택 오브젝트 LODGroup 연결")]
        static void SetupSelection()
        {
            if (Selection.gameObjects.Length == 0)
            { Debug.LogWarning("[에셋] 선택된 오브젝트가 없습니다."); return; }
            foreach (var go in Selection.gameObjects)
                Debug.Log($"[에셋] {go.name} — {SetupLODGroup(go)}");
        }

        /// <summary>LOD0/1/2 렌더러를 모아 LODGroup을 만든다. 이미 있으면 건드리지 않는다.</summary>
        static string SetupLODGroup(GameObject root)
        {
            if (root.GetComponentInChildren<LODGroup>(true) != null) return "LODGroup 이미 있음(유지)";

            var byLevel = new Dictionary<int, List<Renderer>>();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                string n = r.gameObject.name.ToLowerInvariant();
                if (n.Contains("_collider")) continue;
                int at = n.IndexOf("_lod");
                if (at < 0) continue;
                string digits = new string(n.Substring(at + 4).TakeWhile(char.IsDigit).ToArray());
                int lvl;
                if (digits.Length == 0 || !int.TryParse(digits, out lvl)) continue;
                if (!byLevel.ContainsKey(lvl)) byLevel[lvl] = new List<Renderer>();
                byLevel[lvl].Add(r);
            }
            if (byLevel.Count < 2) return "LOD 단계 부족 — LODGroup 생성 안 함";

            int[] levels = byLevel.Keys.OrderBy(k => k).ToArray();
            var lods = new LOD[levels.Length];
            for (int i = 0; i < levels.Length; i++)
            {
                Renderer[] rends = byLevel[levels[i]].ToArray();
                foreach (var r in rends)
                    if (!r.enabled) { Undo.RecordObject(r, "LOD 렌더러 활성"); r.enabled = true; }
                lods[i] = new LOD(TransitionHeight(i, levels.Length), rends);
            }

            var group = Undo.AddComponent<LODGroup>(root);
            group.SetLODs(lods);
            group.RecalculateBounds();
            return $"LODGroup 생성 (LOD {levels.Length}단계)";
        }

        static float TransitionHeight(int i, int count)
        {
            if (count == 3) return new[] { 0.5f, 0.15f, 0.01f }[i];
            if (count == 2) return new[] { 0.4f, 0.01f }[i];
            return Mathf.Max(0.01f, 0.6f / (i + 1));
        }

        // ── 바운즈 ──
        /// <summary>프리팹의 렌더 바운즈(스케일 1 기준). LOD0만, *_Collider 제외.</summary>
        static bool PrefabBounds(GameObject prefab, out Bounds bounds)
        {
            bounds = default(Bounds);
            bool any = false;
            Matrix4x4 toLocal = prefab.transform.worldToLocalMatrix;

            foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                string n = r.gameObject.name.ToLowerInvariant();
                if (n.Contains("_collider")) continue;
                if (n.Contains("_lod") && !n.Contains("_lod0")) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                Bounds mb = mf.sharedMesh.bounds;
                Matrix4x4 m = toLocal * r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, Corner(i)));
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                    else bounds.Encapsulate(p);
                }
            }
            return any;
        }

        /// <summary>회전·스케일을 적용한 뒤의 AABB.</summary>
        static Bounds TransformedBounds(Bounds b, Quaternion rot, Vector3 scl)
        {
            var m = Matrix4x4.TRS(Vector3.zero, rot, scl);
            Bounds outB = new Bounds(m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, Corner(0))), Vector3.zero);
            for (int i = 1; i < 8; i++)
                outB.Encapsulate(m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, Corner(i))));
            return outB;
        }

        static Vector3 Corner(int i)
            => new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);

        static Vector3 SpawnPos()
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv != null) { Vector3 p = sv.pivot; p.y = 0f; return p; }
            return Vector3.zero;
        }
    }
}
