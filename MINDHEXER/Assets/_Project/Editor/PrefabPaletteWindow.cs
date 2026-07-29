using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// Hackable·TallCity 프리팹을 훑어보고 클릭 한 번으로 씬에 배치하는 팔레트 창.
    ///
    /// <para>두 카테고리를 명확히 분리한다 — Hackable(해킹 가능한 기믹: 경비병, CCTV, 터렛,
    /// 레일·피스톤·유압프레스 등 외부조종물)과 TallCity(배경용 사이버펑크 도시 애셋: 벽·바닥·
    /// 다리·전선 등). 폴더를 실제로 훑어서 목록을 만들기 때문에 새 프리팹을 추가해도 코드를
    /// 안 건드리고 "새로고침" 버튼만 누르면 바로 뜬다.</para>
    ///
    /// <para>배치 위치는 씬 뷰가 열려 있으면 그 피벗(카메라가 보고 있는 지점)에, 없으면
    /// 원점에 놓는다. 배치 직후 바로 선택되므로 이동 툴로 이어서 옮기면 된다.</para>
    /// </summary>
    public class PrefabPaletteWindow : EditorWindow
    {
        const string HackableRoot = "Assets/_Project/Prefabs/Hackables";
        const string TallCityRoot = "Assets/_Project/Prefabs/TallCity";

        enum Category { Hackable, TallCity }

        class Entry
        {
            public string path;
            public string name;
            public string group;   // 폴더 기준 소분류 (ExternalControl, Wall, Ground ...)
            public GameObject asset;
        }

        Category _category = Category.Hackable;
        string _search = "";
        Vector2 _scroll;
        readonly List<Entry> _hackables = new List<Entry>();
        readonly List<Entry> _tallCity = new List<Entry>();
        readonly Dictionary<string, bool> _groupOpen = new Dictionary<string, bool>();

        const float ThumbSize = 64f;
        const int Columns = 4;

        [MenuItem("Tools/프리팹 팔레트/열기")]
        public static void Open()
        {
            var win = GetWindow<PrefabPaletteWindow>("프리팹 팔레트");
            win.minSize = new Vector2(280f, 360f);
            win.Rescan();
        }

        void OnEnable() => Rescan();

        void Rescan()
        {
            _hackables.Clear();
            _tallCity.Clear();
            ScanInto(HackableRoot, _hackables);
            ScanInto(TallCityRoot, _tallCity);
        }

        static void ScanInto(string root, List<Entry> into)
        {
            if (!AssetDatabase.IsValidFolder(root)) return;

            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { root });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) continue;

                string relDir = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? root;
                string group = relDir == root ? "(기본)" : relDir.Substring(root.Length + 1);
                // TallCity는 실제 프리팹이 .../TallCity/Prefab/Wall/... 처럼 한 단계 더 깊으므로,
                // "Prefab/Wall" 같은 표시보다 마지막 폴더 이름 하나만 보여주는 편이 읽기 좋다.
                int lastSlash = group.LastIndexOf('/');
                if (lastSlash >= 0) group = group.Substring(lastSlash + 1);

                into.Add(new Entry { path = path, name = asset.name, group = group, asset = asset });
            }
            into.Sort((a, b) =>
            {
                int g = string.Compare(a.group, b.group, System.StringComparison.OrdinalIgnoreCase);
                return g != 0 ? g : string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase);
            });
        }

        void OnGUI()
        {
            DrawToolbar();
            EditorGUILayout.Space(4f);

            var list = _category == Category.Hackable ? _hackables : _tallCity;
            var filtered = string.IsNullOrEmpty(_search)
                ? list
                : list.Where(e => e.name.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            if (filtered.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    string.IsNullOrEmpty(_search) ? "프리팹이 없습니다." : "검색 결과가 없습니다.",
                    MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var group in filtered.Select(e => e.group).Distinct())
                DrawGroup(group, filtered.Where(e => e.group == group).ToList());
            EditorGUILayout.EndScrollView();
        }

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            var newCategory = (Category)GUILayout.Toolbar((int)_category,
                new[] { "Hackable", "TallCity" }, EditorStyles.toolbarButton, GUILayout.Width(180f));
            if (newCategory != _category) { _category = newCategory; _search = ""; GUI.FocusControl(null); }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("새로고침", EditorStyles.toolbarButton, GUILayout.Width(70f))) Rescan();
            EditorGUILayout.EndHorizontal();

            _search = EditorGUILayout.TextField("검색", _search);
        }

        void DrawGroup(string group, List<Entry> entries)
        {
            if (!_groupOpen.TryGetValue(group, out bool open)) { open = true; _groupOpen[group] = open; }

            open = EditorGUILayout.Foldout(open, $"{group} ({entries.Count})", true);
            _groupOpen[group] = open;
            if (!open) return;

            EditorGUILayout.BeginVertical(GUI.skin.box);
            for (int i = 0; i < entries.Count; i += Columns)
            {
                EditorGUILayout.BeginHorizontal();
                for (int c = 0; c < Columns; c++)
                {
                    int idx = i + c;
                    if (idx >= entries.Count) { GUILayout.FlexibleSpace(); break; }
                    DrawEntry(entries[idx]);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        void DrawEntry(Entry entry)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(ThumbSize + 12f));

            var preview = AssetPreview.GetAssetPreview(entry.asset) ?? AssetPreview.GetMiniThumbnail(entry.asset);
            var rect = GUILayoutUtility.GetRect(ThumbSize, ThumbSize, GUILayout.Width(ThumbSize));
            if (GUI.Button(rect, preview != null ? new GUIContent(preview) : new GUIContent("…")))
                PlaceInScene(entry);
            if (AssetPreview.IsLoadingAssetPreview(entry.asset.GetEntityId())) Repaint();

            var labelStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, alignment = TextAnchor.UpperCenter };
            EditorGUILayout.LabelField(entry.name, labelStyle, GUILayout.Width(ThumbSize + 12f));

            EditorGUILayout.EndVertical();
        }

        static void PlaceInScene(Entry entry)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(entry.asset);
            if (instance == null) return;

            Undo.RegisterCreatedObjectUndo(instance, "팔레트에서 배치: " + entry.name);
            instance.transform.position = SpawnPoint();

            Selection.activeGameObject = instance;
            EditorGUIUtility.PingObject(instance);
            EditorSceneManager.MarkSceneDirty(instance.scene);
        }

        static Vector3 SpawnPoint()
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null) return Vector3.zero;

            // 씬 뷰가 보고 있는 피벗 지점 — 바닥 위에 놓이도록 y만 0으로 스냅한다.
            var p = view.pivot;
            p.y = 0f;
            return p;
        }
    }
}
