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
    /// <para>세 카테고리를 명확히 분리한다 — Hackable(해킹 가능한 기믹: 외부 조종물인 레일·피스톤·
    /// 유압프레스·터렛과 빙의 대상인 경비병·로봇팔), TallCity(배경용 사이버펑크 도시 애셋: 벽·바닥·
    /// 다리·전선 등), Sci-Fi(Remesh 실내 환경: 콘솔·파이프·플랫폼 등). 폴더를 실제로 훑어서
    /// 목록을 만들기 때문에 새 프리팹을 추가해도 코드를 안 건드리고 "새로고침"만 누르면 바로 뜬다.</para>
    ///
    /// <para><b>보류 그룹</b>(<c>Hackables/Deferred/</c>) — 설계에서 이번 범위 제외로 정한 것들
    /// (CCTV·회전 장치, 기초_설계안 §6.1). 지우면 되살릴 근거가 사라지므로 <b>남겨 두되 눈에 띄게
    /// 구분</b>한다: 목록 <b>맨 아래</b>로 내리고, 기본 접힘 + 경고 문구를 단다. 실수로 배치하는
    /// 사고를 막는 게 목적이지 막아 놓는 게 목적이 아니라, 누르면 배치는 그대로 된다.</para>
    ///
    /// <para>배치 위치는 씬 뷰가 열려 있으면 그 피벗(카메라가 보고 있는 지점)에, 없으면
    /// 원점에 놓는다. 배치 직후 바로 선택되므로 이동 툴로 이어서 옮기면 된다.</para>
    /// </summary>
    public class PrefabPaletteWindow : EditorWindow
    {
        const string HackableRoot = "Assets/_Project/Prefabs/Hackables";
        const string TallCityRoot = "Assets/_Project/Prefabs/TallCity";
        // Sci-Fi는 원본 패키지 폴더가 아니라 <b>우리 프로젝트로 옮겨 온 사본</b>을 가리킨다.
        // 원본을 그대로 참조하면 재질을 우리 흑백 규격으로 바꿀 수 없다(패키지 갱신 시 되돌아간다).
        const string SciFiRoot    = "Assets/_Project/Prefabs/SCi";

        enum Category { Hackable, TallCity, SciFi }

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
        readonly List<Entry> _sciFi = new List<Entry>();
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
            _sciFi.Clear();
            ScanInto(HackableRoot, _hackables);
            ScanInto(TallCityRoot, _tallCity);
            ScanInto(SciFiRoot, _sciFi);
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
                // 보류 그룹은 항상 맨 아래 — 목록 위쪽은 '지금 쓰는 것'만 남긴다.
                int da = IsDeferred(a.group) ? 1 : 0, db = IsDeferred(b.group) ? 1 : 0;
                if (da != db) return da - db;
                int g = string.Compare(a.group, b.group, System.StringComparison.OrdinalIgnoreCase);
                return g != 0 ? g : string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase);
            });
        }

        void OnGUI()
        {
            DrawToolbar();
            EditorGUILayout.Space(4f);

            var list = _category == Category.Hackable ? _hackables
                     : _category == Category.TallCity ? _tallCity
                     : _sciFi;
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
                new[] { "Hackable", "TallCity", "Sci-Fi" }, EditorStyles.toolbarButton, GUILayout.Width(270f));
            if (newCategory != _category) { _category = newCategory; _search = ""; GUI.FocusControl(null); }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("새로고침", EditorStyles.toolbarButton, GUILayout.Width(70f))) Rescan();
            EditorGUILayout.EndHorizontal();

            _search = EditorGUILayout.TextField("검색", _search);
        }

        /// <summary>설계에서 이번 범위 제외로 정한 것들이 모인 폴더인가 (기초_설계안 §6.1).</summary>
        const string DeferredGroup = "Deferred";
        static bool IsDeferred(string group)
            => string.Equals(group, DeferredGroup, System.StringComparison.OrdinalIgnoreCase);

        void DrawGroup(string group, List<Entry> entries)
        {
            bool deferred = IsDeferred(group);

            // 보류는 기본 접힘 — 평소엔 존재만 보이고 목록을 차지하지 않는다.
            if (!_groupOpen.TryGetValue(group, out bool open)) { open = !deferred; _groupOpen[group] = open; }

            string title = deferred ? $"보류 — 배치하지 말 것 ({entries.Count})" : $"{group} ({entries.Count})";
            open = EditorGUILayout.Foldout(open, title, true);
            _groupOpen[group] = open;
            if (!open) return;

            if (deferred)
                EditorGUILayout.HelpBox(
                    "설계에서 이번 범위 제외로 정한 것들입니다 (기초_설계안 §6.1 — CCTV·회전 장치).\n" +
                    "지우지 않고 남겨 둔 것이라 배치 자체는 됩니다. 되살리기로 정한 게 아니면 쓰지 마십시오.",
                    MessageType.Warning);

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
