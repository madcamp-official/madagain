using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// 그레이박스를 에셋 프리팹으로 "타일링 교체"하는 도구.
    ///
    /// 핵심 원칙
    ///  · <b>에셋 크기를 절대 바꾸지 않는다.</b> 늘리는 대신 스케일 1 그대로 여러 개를 이어 붙인다.
    ///  · 타일을 그레이박스의 자식으로 넣지 <b>않는다</b> — 그레이박스는 크기를 localScale로 만들어서
    ///    (예: Wall = 60×20×0.5) 자식이 되면 프리팹이 60배로 늘어난다.
    ///    그래서 스케일 1인 별도 그룹(_Environment_asset/&lt;이름&gt;_Art) 아래에 넣는다.
    ///  · 원본 그레이박스는 <b>렌더러만 끄고 콜라이더는 남긴다</b> → 충돌·NavMesh·층이동 마커가 유지된다.
    ///    (NavMesh를 콜라이더 기준으로 굽도록 세팅해 뒀으므로 안 보여도 길찾기는 정상이다.)
    ///
    /// 프리팹 크기는 하드코딩하지 않고 렌더러 바운즈에서 산출한다(LOD0 기준, *_Collider 제외).
    /// </summary>
    public class GreyboxReplaceTool : EditorWindow
    {
        enum Original { HideRenderer, Keep, Delete }

        GameObject prefab;
        bool  singleMode;                 // true = 가운데 1개만
        int   faceAxis = 1;               // 0=X 1=Y 2=Z
        int   faceSign = 1;               // +1 / -1
        bool  overflow = true;            // 넘치게 깔기(겹침 허용) / 부족하게(틈)
        bool  alignToFace = true;         // 프리팹 +Y를 면 법선에 맞춰 세울지
        Vector3 rotEuler = Vector3.zero;  // 추가 회전(프리팹 자기 축 기준, 도)
        Vector3 tileScale = Vector3.one;  // 타일 스케일(1 = 원본 크기 유지)
        Vector3 nudge;                    // 미세 오프셋(월드)
        Original original = Original.HideRenderer;
        string rootName = "_Environment_asset";

        const int MaxTiles = 5000;        // 실수로 수만 개 깔리는 사고 방지

        static readonly string[] FaceNames = { "+X (오른쪽)", "-X (왼쪽)", "+Y (윗면)", "-Y (아랫면)", "+Z (앞면)", "-Z (뒷면)" };

        [MenuItem("Tools/에셋/그레이박스 → 에셋 교체")]
        static void Open() => GetWindow<GreyboxReplaceTool>("그레이박스 교체").minSize = new Vector2(340, 420);

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "씬에서 그레이박스를 선택하고 프리팹을 지정한 뒤 [교체]를 누르십시오.\n" +
                "프리팹은 크기 변경 없이 격자로 반복 배치됩니다.", MessageType.Info);

            int sel = Selection.gameObjects.Length;
            EditorGUILayout.LabelField("선택된 대상", sel > 0 ? $"{sel}개" : "없음",
                sel > 0 ? EditorStyles.label : EditorStyles.miniLabel);

            DrawPrefabPicker();
            if (prefab != null && PrefabBounds(prefab, out Bounds pb))
            {
                Vector3 s = Vector3.Scale(pb.size, tileScale);
                string extra = tileScale == Vector3.one ? "" : $"  (원본 {pb.size.x:0.00}×{pb.size.y:0.00}×{pb.size.z:0.00})";
                EditorGUILayout.LabelField(" ", $"크기 {s.x:0.00} × {s.y:0.00} × {s.z:0.00} m{extra}",
                                           EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(6);
            singleMode = EditorGUILayout.Toggle(new GUIContent("가운데 1개만", "기둥·컨테이너 같은 단일 오브젝트용"), singleMode);

            using (new EditorGUI.DisabledScope(singleMode))
            {
                int faceIdx = faceAxis * 2 + (faceSign > 0 ? 0 : 1);
                faceIdx = EditorGUILayout.Popup(new GUIContent("채울 면", "이 면 위에 타일을 깝니다"), faceIdx, FaceNames);
                faceAxis = faceIdx / 2;
                faceSign = (faceIdx % 2 == 0) ? 1 : -1;

                overflow = EditorGUILayout.Toggle(new GUIContent("넘치게 깔기", "끄면 부족하게 깔아 가장자리에 틈이 남습니다"), overflow);
            }

            EditorGUILayout.Space(4);
            alignToFace = EditorGUILayout.Toggle(new GUIContent("면에 맞춰 세우기",
                "켬: 프리팹의 +Y가 채울 면의 법선을 향하도록 눕힙니다(바닥→위, 벽→벽면 방향).\n" +
                "끔: 프리팹 원래 각도(그레이박스 회전)를 그대로 씁니다."), alignToFace);

            rotEuler = EditorGUILayout.Vector3Field(new GUIContent("추가 회전 X/Y/Z (도)",
                "프리팹 자기 축 기준으로 더 돌립니다"), rotEuler);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("X +90")) rotEuler.x = Mathf.Repeat(rotEuler.x + 90f, 360f);
                if (GUILayout.Button("Y +90")) rotEuler.y = Mathf.Repeat(rotEuler.y + 90f, 360f);
                if (GUILayout.Button("Z +90")) rotEuler.z = Mathf.Repeat(rotEuler.z + 90f, 360f);
                if (GUILayout.Button("초기화", GUILayout.Width(56))) rotEuler = Vector3.zero;
            }

            tileScale = EditorGUILayout.Vector3Field(new GUIContent("스케일",
                "1 = 원본 크기 유지(권장). 값을 바꾸면 타일 간격도 같이 계산됩니다."), tileScale);
            if (tileScale != Vector3.one)
                EditorGUILayout.HelpBox("스케일이 1이 아닙니다 — 텍스처 밀도가 늘어나/줄어 티가 날 수 있습니다.",
                                        MessageType.Warning);

            nudge = EditorGUILayout.Vector3Field("미세 오프셋", nudge);

            EditorGUILayout.Space(4);
            original = (Original)EditorGUILayout.EnumPopup(new GUIContent("원본 그레이박스",
                "렌더러만 끄기 = 콜리전·NavMesh 유지(권장)"), original);
            rootName = EditorGUILayout.TextField(new GUIContent("담을 그룹", "생성된 아트를 이 그룹 아래에 넣습니다"), rootName);

            EditorGUILayout.Space(10);
            using (new EditorGUI.DisabledScope(prefab == null || sel == 0))
                if (GUILayout.Button("교체", GUILayout.Height(30))) Replace();

            EditorGUILayout.Space(6);
            if (GUILayout.Button("원본 렌더러 전부 다시 켜기"))
                RestoreRenderers();
        }

        // ── 프리팹 선택 (Sci-Fi 에셋 폴더 안만 보여준다) ──
        const string PrefabDir = "Assets/Remesh Games/Sci-Fi Environment/Prefabs";
        string[] prefabNames;
        GameObject[] prefabObjs;
        string search = "";
        Vector2 pickScroll;
        float cellSize = 78f;
        float gridHeight = 480f;   // 썸네일 영역 세로 길이(창에서 조절)

        void RefreshPrefabList()
        {
            if (!AssetDatabase.IsValidFolder(PrefabDir))
            { prefabNames = new string[0]; prefabObjs = new GameObject[0]; return; }

            string[] paths = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir })
                .Select(AssetDatabase.GUIDToAssetPath).Distinct()
                .OrderBy(p => System.IO.Path.GetFileNameWithoutExtension(p)).ToArray();

            prefabNames = paths.Select(System.IO.Path.GetFileNameWithoutExtension).ToArray();
            prefabObjs  = paths.Select(AssetDatabase.LoadAssetAtPath<GameObject>).ToArray();
        }

        /// <summary>썸네일이 아직 생성 중이면 계속 다시 그려야 그림이 채워진다.</summary>
        void Update()
        {
            if (AssetPreview.IsLoadingAssetPreviews()) Repaint();
        }

        void DrawPrefabPicker()
        {
            if (prefabObjs == null) RefreshPrefabList();

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("검색", GUILayout.Width(34));
                search = EditorGUILayout.TextField(search);
                if (GUILayout.Button("✕", GUILayout.Width(22))) { search = ""; GUI.FocusControl(null); }
                if (GUILayout.Button("새로고침", GUILayout.Width(62))) RefreshPrefabList();
            }

            if (prefabObjs.Length == 0)
            { EditorGUILayout.HelpBox($"프리팹을 못 찾았습니다:\n{PrefabDir}", MessageType.Warning); return; }

            string q = search.Trim().ToLowerInvariant();
            var idx = new List<int>();
            for (int i = 0; i < prefabNames.Length; i++)
                if (q.Length == 0 || prefabNames[i].ToLowerInvariant().Contains(q)) idx.Add(i);

            EditorGUILayout.LabelField(
                prefab != null ? $"선택: {prefab.name}" : "선택: (없음) — 아래에서 고르십시오",
                EditorStyles.miniBoldLabel);

            if (idx.Count == 0)
            { EditorGUILayout.HelpBox("검색 결과가 없습니다.", MessageType.Info); return; }

            // ── 썸네일 그리드 ──
            var style = new GUIStyle(GUI.skin.button)
            {
                imagePosition = ImagePosition.ImageAbove,
                alignment = TextAnchor.LowerCenter,
                fontSize = 9,
                wordWrap = true,
                padding = new RectOffset(2, 2, 2, 2),
            };

            int cols = Mathf.Max(1, Mathf.FloorToInt((position.width - 26f) / (cellSize + 4f)));
            pickScroll = EditorGUILayout.BeginScrollView(pickScroll, GUILayout.Height(gridHeight));
            for (int k = 0; k < idx.Count; k++)
            {
                if (k % cols == 0) EditorGUILayout.BeginHorizontal();

                int i = idx[k];
                GameObject obj = prefabObjs[i];
                Texture tex = AssetPreview.GetAssetPreview(obj);
                if (tex == null) tex = AssetPreview.GetMiniThumbnail(obj);

                Color prev = GUI.backgroundColor;
                if (prefab == obj) GUI.backgroundColor = new Color(0.35f, 0.75f, 1f);
                if (GUILayout.Button(new GUIContent(prefabNames[i], tex, prefabNames[i]), style,
                                     GUILayout.Width(cellSize), GUILayout.Height(cellSize)))
                    prefab = (prefab == obj) ? null : obj;      // 다시 누르면 해제
                GUI.backgroundColor = prev;

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

        // ── 실행 ──
        void Replace()
        {
            if (!PrefabBounds(prefab, out Bounds pbRaw))
            { Debug.LogError("[교체] 프리팹에서 렌더 메시를 못 찾았습니다."); return; }

            Transform root = EnsureRoot(rootName);
            int totalTiles = 0, done = 0;

            foreach (var go in Selection.gameObjects)
            {
                var mf = go.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;   // 메시 없는 건 건너뜀

                Transform t = go.transform;
                Bounds mb = mf.sharedMesh.bounds;
                Vector3 sizeW = Vector3.Scale(mb.size, t.lossyScale);   // 그레이박스 실제 크기(월드)
                Vector3 centerW = t.TransformPoint(mb.center);
                Vector3[] axes = { t.right, t.up, t.forward };

                // 타일이 놓일 면과 그 법선
                Vector3 nrm = axes[faceAxis] * faceSign;
                Vector3 faceCenter = centerW + nrm * (sizeW[faceAxis] * 0.5f);

                // 타일 회전: (면에 맞춰 세우거나 / 그레이박스 각도 그대로) + 사용자 추가 회전(자기 축 기준)
                Quaternion baseRot = alignToFace ? Quaternion.FromToRotation(Vector3.up, nrm) : t.rotation;
                Quaternion rot = baseRot * Quaternion.Euler(rotEuler);
                Vector3 tx = rot * Vector3.right, tz = rot * Vector3.forward;
                Vector3 tup = rot * Vector3.up;   // 타일이 "위"로 삼는 방향(면에서 띄울 때 씀)

                var group = new GameObject(go.name + "_Art").transform;
                Undo.RegisterCreatedObjectUndo(group.gameObject, "그레이박스 교체");
                group.SetParent(root, true);
                group.localPosition = Vector3.zero;
                group.localRotation = Quaternion.identity;
                group.localScale = Vector3.one;                      // ★ 스케일 1 — 프리팹 왜곡 방지

                Vector3 sc = tileScale;
                if (singleMode)
                {
                    Place(group, faceCenter + nudge
                                 + tup * (-pbRaw.min.y * sc.y)
                                 - tx * (pbRaw.center.x * sc.x)
                                 - tz * (pbRaw.center.z * sc.z), rot);
                    totalTiles++; done++;
                }
                else
                {
                    // 면 위에서 타일 축(tx·tz) 방향으로 그레이박스가 차지하는 길이
                    int u = (faceAxis + 1) % 3, v = (faceAxis + 2) % 3;
                    float extX = Mathf.Abs(Vector3.Dot(axes[u], tx)) * sizeW[u]
                               + Mathf.Abs(Vector3.Dot(axes[v], tx)) * sizeW[v];
                    float extZ = Mathf.Abs(Vector3.Dot(axes[u], tz)) * sizeW[u]
                               + Mathf.Abs(Vector3.Dot(axes[v], tz)) * sizeW[v];

                    // 타일 간격 = 프리팹 크기 × 스케일 (스케일을 바꾸면 간격도 따라간다)
                    float sx = Mathf.Max(0.01f, pbRaw.size.x * sc.x), sz = Mathf.Max(0.01f, pbRaw.size.z * sc.z);
                    int nx = Count(extX / sx), nz = Count(extZ / sz);

                    if (nx * nz > MaxTiles)
                    {
                        Debug.LogError($"[교체] {go.name}: 타일 {nx * nz}개는 너무 많습니다(상한 {MaxTiles}). " +
                                       "더 큰 프리팹을 쓰거나 그레이박스를 나누십시오.");
                        Undo.DestroyObjectImmediate(group.gameObject);
                        continue;
                    }

                    for (int i = 0; i < nx; i++)
                    for (int j = 0; j < nz; j++)
                    {
                        float ox = (i + 0.5f) * sx - extX * 0.5f;
                        float oz = (j + 0.5f) * sz - extZ * 0.5f;
                        Vector3 pos = faceCenter + nudge
                                    + tx * (ox - pbRaw.center.x * sc.x)
                                    + tz * (oz - pbRaw.center.z * sc.z)
                                    + tup * (-pbRaw.min.y * sc.y);
                        Place(group, pos, rot);
                    }
                    totalTiles += nx * nz;
                    done++;
                }

                ApplyOriginal(go);
            }

            Debug.Log($"[교체] 대상 {done}개 → 타일 {totalTiles}개 생성. 그룹: {rootName}\n" +
                      "원본은 렌더러만 꺼졌다면 콜리전·NavMesh는 그대로입니다.");
        }

        void Place(Transform parent, Vector3 pos, Quaternion rot)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            if (inst == null) return;
            inst.transform.SetPositionAndRotation(pos, rot);
            inst.transform.localScale = tileScale;   // 기본 1 = 원본 크기 유지
            Undo.RegisterCreatedObjectUndo(inst, "그레이박스 교체");
        }

        int Count(float ratio) => Mathf.Max(1, overflow ? Mathf.CeilToInt(ratio - 1e-4f)
                                                       : Mathf.FloorToInt(ratio + 1e-4f));

        void ApplyOriginal(GameObject go)
        {
            switch (original)
            {
                case Original.HideRenderer:
                    foreach (var r in go.GetComponents<MeshRenderer>())
                        if (r.enabled) { Undo.RecordObject(r, "원본 렌더러 끄기"); r.enabled = false; }
                    break;
                case Original.Delete:
                    Undo.DestroyObjectImmediate(go);
                    break;
            }
        }

        static Transform EnsureRoot(string name)
        {
            var found = GameObject.Find(name);
            if (found != null) return found.transform;
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "아트 그룹 생성");
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            return go.transform;
        }

        static void RestoreRenderers()
        {
            int n = 0;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (!r.enabled) { Undo.RecordObject(r, "렌더러 복구"); r.enabled = true; n++; }
            Debug.Log($"[교체] 꺼져 있던 렌더러 {n}개를 다시 켰습니다.");
        }

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
                    Vector3 c = mb.center + Vector3.Scale(mb.extents, Corner(i));
                    Vector3 p = m.MultiplyPoint3x4(c);
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                    else bounds.Encapsulate(p);
                }
            }
            return any;
        }

        static Vector3 Corner(int i)
            => new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
    }
}
