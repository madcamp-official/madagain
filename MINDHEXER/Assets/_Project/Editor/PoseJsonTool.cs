using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using Game.View;   // PoseFile/PoseBone/PoseObject 는 런타임 쪽(PoseData.cs)에 있다

namespace Game.EditorTools
{
    /// <summary>
    /// 포즈를 JSON으로 내보내고 불러온다.
    ///   내보내기: 씬에서 잡은 자세 → Assets/_Project/Poses/pose_&lt;이름&gt;.json
    ///   불러오기: JSON → 씬에 적용
    ///
    /// 이 파일을 주고받으며 키프레임을 설계한다(사람이 포즈, 도구가 데이터).
    /// 뼈는 회전만 저장한다(위치는 변하지 않으므로). 칼·그립만 위치·크기까지 저장.
    /// </summary>
    public class PoseJsonTool : EditorWindow
    {
        public const string PoseDir = "Assets/_Project/Poses";
        static readonly string[] FullTrsNames = { "Katana", "Grip_R", "Grip_L", "IKTarget_R", "ElbowPole_R", "ElbowPole_L" };

        Transform root;
        string    poseName = "새포즈";
        Vector2   scroll;
        string[]  files = new string[0];

        // 고스트(참조 포즈 겹쳐보기)
        const string GhostPrefix = "__PoseGhost_";
        [Range(0f, 1f)] float ghostAlpha = 0.28f;
        Color ghostTint = new Color(0.35f, 0.75f, 1f);   // 옅은 하늘색

        [MenuItem("Tools/뷰모델/포즈 JSON")]
        static void Open() { GetWindow<PoseJsonTool>("포즈 JSON").minSize = new Vector2(420f, 420f); }

        void OnEnable() => Refresh();

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "씬의 자세를 JSON으로 저장/복원합니다.\n" +
                "저장한 파일을 넘겨주면 키프레임을 설계해 드립니다.", MessageType.Info);

            if (root == null) AutoDetect();
            root = (Transform)EditorGUILayout.ObjectField("뷰모델 루트", root, typeof(Transform), true);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("내보내기", EditorStyles.boldLabel);
            poseName = EditorGUILayout.TextField("포즈 이름", poseName);
            using (new EditorGUI.DisabledScope(root == null || string.IsNullOrWhiteSpace(poseName)))
                if (GUILayout.Button("현재 자세를 JSON으로 저장", GUILayout.Height(30f))) Export(poseName);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("저장된 포즈", EditorStyles.boldLabel);
                if (GUILayout.Button("새로고침", GUILayout.Width(70f))) Refresh();
                if (GUILayout.Button("폴더 열기", GUILayout.Width(70f))) EditorUtility.RevealInFinder(PoseDir);
            }

            // 고스트 설정
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("고스트", GUILayout.Width(46f));
                ghostAlpha = EditorGUILayout.Slider(ghostAlpha, 0.05f, 0.8f);
                ghostTint  = EditorGUILayout.ColorField(ghostTint, GUILayout.Width(50f));
                if (GUILayout.Button("전부 끄기", GUILayout.Width(70f))) ClearGhosts();
            }
            EditorGUILayout.LabelField("  체크하면 그 포즈가 반투명으로 겹쳐 보입니다(참조용)", EditorStyles.miniLabel);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var f in files)
            {
                string nm = Path.GetFileNameWithoutExtension(f).Substring("pose_".Length);
                using (new EditorGUILayout.HorizontalScope("box"))
                {
                    bool on  = FindGhost(nm) != null;
                    bool now = GUILayout.Toggle(on, "고스트", EditorStyles.miniButton, GUILayout.Width(52f));
                    if (now != on)
                    {
                        if (now) ShowGhost(f, nm); else RemoveGhost(nm);
                    }
                    EditorGUILayout.LabelField(nm);
                    using (new EditorGUI.DisabledScope(root == null))
                        if (GUILayout.Button("적용", GUILayout.Width(46f))) Import(f);
                    if (GUILayout.Button("삭제", GUILayout.Width(46f)))
                    {
                        if (EditorUtility.DisplayDialog("삭제", Path.GetFileName(f) + " 를 삭제할까요?", "삭제", "취소"))
                        { RemoveGhost(nm); File.Delete(f); AssetDatabase.Refresh(); Refresh(); break; }
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        void AutoDetect()
        {
            var cam = Camera.main;
            if (cam != null && cam.transform.childCount > 0) root = cam.transform.GetChild(0);
            if (root == null) { var go = GameObject.Find("KatanaViewmodel"); if (go != null) root = go.transform; }
        }

        void Refresh()
        {
            Directory.CreateDirectory(PoseDir);
            files = Directory.GetFiles(PoseDir, "pose_*.json");
            Array.Sort(files);
        }

        // ── 내보내기 ──
        void Export(string name)
        {
            var bones = new List<PoseBone>();
            var objs  = new List<PoseObject>();

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root) continue;
                string path = PathOf(t, root);
                if (Array.IndexOf(FullTrsNames, t.name) >= 0)
                {
                    objs.Add(new PoseObject
                    {
                        path  = path,
                        pos   = V3(t.localPosition),
                        euler = V3(t.localRotation.eulerAngles),
                        quat  = Q(t.localRotation),
                        scale = V3(t.localScale),
                    });
                }
                else
                {
                    bones.Add(new PoseBone
                    {
                        path  = path,
                        euler = V3(t.localRotation.eulerAngles),
                        quat  = Q(t.localRotation),
                    });
                }
            }

            // 컴포넌트 상태(IK 가중치·손가락 그립)도 함께 기록
            var states = new List<PoseComponentState>();
            foreach (var ik in root.GetComponentsInChildren<HandIK>(true))
                states.Add(new PoseComponentState { path = PathOf(ik.transform, root), type = "HandIK", weight = ik.weight });
            foreach (var fp in root.GetComponentsInChildren<FingerPoser>(true))
                states.Add(new PoseComponentState { path = PathOf(fp.transform, root), type = "FingerPoser", grip = fp.grip });

            var pf = new PoseFile
            {
                name      = name,
                created   = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                root      = root.name,
                rootPos   = V3(root.localPosition),   // 루트 배치까지 통째로 저장
                rootQuat  = Q(root.localRotation),
                rootScale = V3(root.localScale),
                bones     = bones.ToArray(),
                objects   = objs.ToArray(),
                states    = states.ToArray(),
            };

            Directory.CreateDirectory(PoseDir);
            string file = Path.Combine(PoseDir, $"pose_{Sanitize(name)}.json");
            File.WriteAllText(file, JsonUtility.ToJson(pf, true), System.Text.Encoding.UTF8);
            AssetDatabase.Refresh();
            Refresh();
            Debug.Log($"[포즈 JSON] 저장: {file}\n  뼈 {bones.Count}개 · 오브젝트 {objs.Count}개");
        }

        // ── 불러오기 ──
        public static void Apply(Transform root, PoseFile pf, out int ok, out int miss)
        {
            ok = 0; miss = 0;
            // 루트 배치 복원(저장돼 있으면)
            if (pf.rootPos != null && pf.rootPos.Length == 3)
            {
                Undo.RecordObject(root, "포즈 적용");
                root.localPosition = PoseMath.ToV3(pf.rootPos);
                if (pf.rootQuat  != null && pf.rootQuat.Length  == 4) root.localRotation = PoseMath.ToQ(pf.rootQuat);
                if (pf.rootScale != null && pf.rootScale.Length == 3) root.localScale    = PoseMath.ToV3(pf.rootScale);
            }
            if (pf.bones != null)
                foreach (var b in pf.bones)
                {
                    var t = root.Find(b.path);
                    if (t == null) { miss++; continue; }
                    Undo.RecordObject(t, "포즈 적용");
                    t.localRotation = ToQ(b.quat);
                    ok++;
                }
            if (pf.objects != null)
                foreach (var o in pf.objects)
                {
                    var t = root.Find(o.path);
                    if (t == null) { miss++; continue; }
                    Undo.RecordObject(t, "포즈 적용");
                    if (o.pos   != null && o.pos.Length   == 3) t.localPosition = ToV3(o.pos);
                    if (o.quat  != null && o.quat.Length  == 4) t.localRotation = ToQ(o.quat);
                    if (o.scale != null && o.scale.Length == 3) t.localScale    = ToV3(o.scale);
                    ok++;
                }
        }

        void Import(string file)
        {
            var pf = JsonUtility.FromJson<PoseFile>(File.ReadAllText(file, System.Text.Encoding.UTF8));

            // ★ 적용 전에 자세를 덮어쓰는 것들을 끈다.
            //   HandIK(weight>0)는 매 LateUpdate에 팔을 다시 풀고 손목을 그립 회전으로 덮어쓴다.
            //   이걸 안 끄면 불러온 포즈가 즉시 사라진다(손목이 이상하게 남는 원인).
            var offIk = new List<string>();
            foreach (var ik in root.GetComponentsInChildren<HandIK>(true))
                if (ik.weight > 0.001f)
                {
                    Undo.RecordObject(ik, "포즈 적용");
                    offIk.Add($"{ik.gameObject.name}({ik.weight:0.00})");
                    ik.weight = 0f;
                    EditorUtility.SetDirty(ik);
                }

            Apply(root, pf, out int ok, out int miss);

            // FingerPoser는 자기 rest 로 손가락을 되돌리므로, 불러온 손가락을 새 기준으로 잡아준다.
            foreach (var fp in root.GetComponentsInChildren<FingerPoser>(true))
            {
                Undo.RecordObject(fp, "포즈 적용");
                fp.Rebuild();                     // 방금 적용된 손가락 = 새 기준
                EditorUtility.SetDirty(fp);
            }

            // 저장돼 있던 컴포넌트 상태 반영(손가락 그립만. IK 가중치는 켜면 포즈를 덮으므로 로그로만 알림)
            string ikNote = "";
            if (pf.states != null)
                foreach (var s in pf.states)
                {
                    var t = root.Find(s.path);
                    if (t == null) continue;
                    if (s.type == "FingerPoser")
                    {
                        var fp = t.GetComponent<FingerPoser>();
                        if (fp != null) { Undo.RecordObject(fp, "포즈 적용"); fp.grip = s.grip; EditorUtility.SetDirty(fp); }
                    }
                    else if (s.type == "HandIK" && s.weight > 0.001f)
                        ikNote += $" {t.name}={s.weight:0.00}";
                }

            Debug.Log($"[포즈 JSON] 적용: {Path.GetFileName(file)} — 성공 {ok}개" + (miss > 0 ? $" · 못 찾음 {miss}개" : "") +
                      (offIk.Count > 0 ? $"\n  IK를 껐습니다: {string.Join(", ", offIk)}  (켜면 포즈가 다시 덮어써집니다)" : "") +
                      (ikNote.Length > 0 ? $"\n  저장 당시 IK 가중치:{ikNote}" : ""));
        }

        // ── 고스트(참조 포즈 겹쳐보기) ──
        static GameObject FindGhost(string name)
        {
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go.name == GhostPrefix + name && go.scene.IsValid()) return go;
            return null;
        }

        void ShowGhost(string file, string name)
        {
            if (root == null) { Debug.LogWarning("[고스트] 뷰모델 루트가 없습니다."); return; }
            RemoveGhost(name);

            var pf = JsonUtility.FromJson<PoseFile>(File.ReadAllText(file, System.Text.Encoding.UTF8));

            // 뷰모델을 통째로 복제 → 포즈 적용 → 반투명 재질
            var ghost = Instantiate(root.gameObject, root.parent);
            ghost.name = GhostPrefix + name;
            ghost.transform.SetLocalPositionAndRotation(root.localPosition, root.localRotation);
            ghost.transform.localScale = root.localScale;
            // 저장되지 않게 + 선택/클릭 방해 없게
            foreach (var t in ghost.GetComponentsInChildren<Transform>(true))
                t.gameObject.hideFlags = HideFlags.DontSave | HideFlags.NotEditable;

            // 자세를 바꿀 수 있는 컴포넌트 제거(IK·손가락·애니메이터가 고스트를 움직이면 안 됨)
            foreach (var c in ghost.GetComponentsInChildren<Animator>(true))      DestroyImmediate(c);
            foreach (var c in ghost.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(c);
            foreach (var c in ghost.GetComponentsInChildren<Collider>(true))      DestroyImmediate(c);

            Apply(ghost.transform, pf, out _, out _);

            var mat = GhostMaterial();
            foreach (var r in ghost.GetComponentsInChildren<Renderer>(true))
            {
                var arr = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < arr.Length; i++) arr[i] = mat;
                r.sharedMaterials = arr;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            SceneView.RepaintAll();
            Debug.Log($"[고스트] '{name}' 표시");
        }

        void RemoveGhost(string name)
        {
            var g = FindGhost(name);
            if (g != null) { DestroyImmediate(g); SceneView.RepaintAll(); }
        }

        void ClearGhosts()
        {
            int n = 0;
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go.name.StartsWith(GhostPrefix) && go.scene.IsValid()) { DestroyImmediate(go); n++; }
            SceneView.RepaintAll();
            Debug.Log($"[고스트] {n}개 제거");
        }

        /// <summary>반투명 unlit 재질(URP). 매번 새로 만들어 색·알파를 반영.</summary>
        Material GhostMaterial()
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var m = new Material(sh) { name = "PoseGhostMat", hideFlags = HideFlags.DontSave };
            var c = new Color(ghostTint.r, ghostTint.g, ghostTint.b, ghostAlpha);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.color = c;
            // 투명 설정
            m.SetOverrideTag("RenderType", "Transparent");
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return m;
        }

        // ── 유틸 ──
        public static string PathOf(Transform t, Transform root)
        {
            var sb = new System.Text.StringBuilder(t.name);
            for (Transform p = t.parent; p != null && p != root; p = p.parent) sb.Insert(0, p.name + "/");
            return sb.ToString();
        }
        static string Sanitize(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Trim();
        }
        static float[] V3(Vector3 v)     => new[] { v.x, v.y, v.z };
        static float[] Q(Quaternion q)   => new[] { q.x, q.y, q.z, q.w };
        public static Vector3    ToV3(float[] a) => PoseMath.ToV3(a);
        public static Quaternion ToQ(float[] a)  => PoseMath.ToQ(a);
    }
}
