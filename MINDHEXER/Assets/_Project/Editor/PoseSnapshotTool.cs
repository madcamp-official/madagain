using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// 포즈 스냅샷 — 현재 팔·손가락·칼·그립·씬뷰 카메라를 이름 붙여 저장하고 언제든 복원한다.
    /// 키포즈를 모아두고 불러가며 Animation 창에 찍는 용도.
    /// </summary>
    public class PoseSnapshotTool : EditorWindow
    {
        Transform     root;      // 뷰모델 루트
        PoseSnapshot  asset;
        string        newName = "새 포즈";
        Vector2       scroll;

        // 복원 시 무엇을 되돌릴지
        bool applyBones = true, applyKatana = true, applyGrips = true, applyCam = true, applyIk = true;

        [MenuItem("Tools/뷰모델/포즈 스냅샷")]
        static void Open() => GetWindow<PoseSnapshotTool>("포즈 스냅샷").minSize = new Vector2(380f, 460f);

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "현재 자세를 이름 붙여 저장해 두고 언제든 복원합니다.\n" +
                "뼈(어깨·팔꿈치·손목·손가락) · 칼 · 그립 · 씬뷰 카메라를 함께 담습니다.", MessageType.Info);

            // 대상
            if (root == null) AutoDetect();
            root  = (Transform)EditorGUILayout.ObjectField("뷰모델 루트", root, typeof(Transform), true);
            asset = (PoseSnapshot)EditorGUILayout.ObjectField("스냅샷 에셋", asset, typeof(PoseSnapshot), false);
            if (GUILayout.Button("자동 감지 / 에셋 만들기")) { AutoDetect(); EnsureAsset(); }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(root == null || asset == null))
            {
                EditorGUILayout.LabelField("저장", EditorStyles.boldLabel);
                newName = EditorGUILayout.TextField("이름", newName);
                if (GUILayout.Button("현재 자세를 이 이름으로 저장", GUILayout.Height(28f))) Save(newName);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("복원 대상", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                applyBones  = GUILayout.Toggle(applyBones,  "뼈",   EditorStyles.miniButtonLeft);
                applyKatana = GUILayout.Toggle(applyKatana, "칼",   EditorStyles.miniButtonMid);
                applyGrips  = GUILayout.Toggle(applyGrips,  "그립", EditorStyles.miniButtonMid);
                applyIk     = GUILayout.Toggle(applyIk,     "IK",   EditorStyles.miniButtonMid);
                applyCam    = GUILayout.Toggle(applyCam,    "카메라", EditorStyles.miniButtonRight);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("저장된 포즈", EditorStyles.boldLabel);
            if (asset == null) { EditorGUILayout.LabelField("  에셋 없음"); return; }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int i = 0; i < asset.slots.Count; i++)
            {
                var s = asset.slots[i];
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        string n = EditorGUILayout.TextField(s.name);
                        if (n != s.name) { Undo.RecordObject(asset, "이름 변경"); s.name = n; EditorUtility.SetDirty(asset); }
                        if (GUILayout.Button("복원", GUILayout.Width(48f))) Restore(s);
                        if (GUILayout.Button("덮어쓰기", GUILayout.Width(66f))) { Overwrite(s); }
                        if (GUILayout.Button("×", GUILayout.Width(22f)))
                        {
                            Undo.RecordObject(asset, "슬롯 삭제");
                            asset.slots.RemoveAt(i); EditorUtility.SetDirty(asset);
                            AssetDatabase.SaveAssets(); break;
                        }
                    }
                    EditorGUILayout.LabelField(
                        $"  뼈 {(s.bonePaths != null ? s.bonePaths.Length : 0)}개" +
                        (s.hasKatana ? " · 칼" : "") + (s.hasGripR ? " · GripR" : "") + (s.hasGripL ? " · GripL" : "") +
                        (s.hasCam ? " · 카메라" : ""), EditorStyles.miniLabel);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        void AutoDetect()
        {
            if (root == null)
            {
                var cam = Camera.main;
                if (cam != null && cam.transform.childCount > 0) root = cam.transform.GetChild(0);
                if (root == null)
                {
                    var go = GameObject.Find("KatanaViewmodel");
                    if (go != null) root = go.transform;
                }
            }
            if (asset == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:PoseSnapshot");
                if (guids.Length > 0)
                    asset = AssetDatabase.LoadAssetAtPath<PoseSnapshot>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }
        }

        void EnsureAsset()
        {
            if (asset != null) return;
            EnsureFolder("Assets/_Project/Prefabs/Viewmodel");
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/_Project/Prefabs/Viewmodel/PoseSnapshot.asset");
            asset = ScriptableObject.CreateInstance<PoseSnapshot>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            Debug.Log($"[포즈 스냅샷] 에셋 생성: {path}");
        }

        // ── 저장 ──
        void Save(string name)
        {
            var slot = new PoseSlot { name = string.IsNullOrWhiteSpace(name) ? "포즈" : name };
            Fill(slot);
            Undo.RecordObject(asset, "포즈 저장");
            asset.slots.Add(slot);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log($"[포즈 스냅샷] '{slot.name}' 저장 — 뼈 {slot.bonePaths.Length}개");
        }

        void Overwrite(PoseSlot slot)
        {
            string keep = slot.name;
            Undo.RecordObject(asset, "포즈 덮어쓰기");
            Fill(slot);
            slot.name = keep;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log($"[포즈 스냅샷] '{slot.name}' 덮어씀");
        }

        void Fill(PoseSlot slot)
        {
            // 뼈 — 루트 하위 전체
            var paths = new List<string>();
            var rots  = new List<Quaternion>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root) continue;
                paths.Add(PathOf(t, root));
                rots.Add(t.localRotation);
            }
            slot.bonePaths = paths.ToArray();
            slot.boneRots  = rots.ToArray();

            // 칼 / 그립
            Transform katana = FindIn(root, "Katana"), gr = FindIn(root, "Grip_R"), gl = FindIn(root, "Grip_L");
            slot.hasKatana = katana != null;
            if (katana != null) { slot.katanaPos = katana.localPosition; slot.katanaRot = katana.localRotation; slot.katanaScale = katana.localScale; }
            slot.hasGripR = gr != null;
            if (gr != null) { slot.gripRPos = gr.localPosition; slot.gripRRot = gr.localRotation; }
            slot.hasGripL = gl != null;
            if (gl != null) { slot.gripLPos = gl.localPosition; slot.gripLRot = gl.localRotation; }

            // IK / 손가락
            slot.hasIk = false;
            foreach (var ik in root.GetComponentsInChildren<HandIK>(true))
            {
                slot.hasIk = true;
                if (ik.gameObject.name.EndsWith("_L")) slot.ikWeightL = ik.weight; else slot.ikWeightR = ik.weight;
            }
            foreach (var fp in root.GetComponentsInChildren<FingerPoser>(true))
            {
                if (fp.gameObject.name.Contains("Left")) slot.fingerGripL = fp.grip; else slot.fingerGripR = fp.grip;
            }

            // 씬 뷰 카메라
            var sv = SceneView.lastActiveSceneView;
            slot.hasCam = sv != null;
            if (sv != null) { slot.camPivot = sv.pivot; slot.camRot = sv.rotation; slot.camSize = sv.size; }
        }

        // ── 복원 ──
        void Restore(PoseSlot s)
        {
            if (applyBones && s.bonePaths != null)
            {
                int ok = 0, miss = 0;
                for (int i = 0; i < s.bonePaths.Length; i++)
                {
                    var t = root.Find(s.bonePaths[i]);
                    if (t == null) { miss++; continue; }
                    Undo.RecordObject(t, "포즈 복원");
                    t.localRotation = s.boneRots[i];
                    ok++;
                }
                if (miss > 0) Debug.LogWarning($"[포즈 스냅샷] 뼈 {miss}개를 못 찾았습니다(이름·구조가 바뀌었을 수 있음). 적용 {ok}개");
            }

            Transform katana = FindIn(root, "Katana"), gr = FindIn(root, "Grip_R"), gl = FindIn(root, "Grip_L");
            if (applyKatana && s.hasKatana && katana != null)
            {
                Undo.RecordObject(katana, "포즈 복원");
                katana.localPosition = s.katanaPos; katana.localRotation = s.katanaRot; katana.localScale = s.katanaScale;
            }
            if (applyGrips)
            {
                if (s.hasGripR && gr != null) { Undo.RecordObject(gr, "포즈 복원"); gr.localPosition = s.gripRPos; gr.localRotation = s.gripRRot; }
                if (s.hasGripL && gl != null) { Undo.RecordObject(gl, "포즈 복원"); gl.localPosition = s.gripLPos; gl.localRotation = s.gripLRot; }
            }
            if (applyIk && s.hasIk)
            {
                foreach (var ik in root.GetComponentsInChildren<HandIK>(true))
                {
                    Undo.RecordObject(ik, "포즈 복원");
                    ik.weight = ik.gameObject.name.EndsWith("_L") ? s.ikWeightL : s.ikWeightR;
                }
                foreach (var fp in root.GetComponentsInChildren<FingerPoser>(true))
                {
                    Undo.RecordObject(fp, "포즈 복원");
                    fp.grip = fp.gameObject.name.Contains("Left") ? s.fingerGripL : s.fingerGripR;
                }
            }
            if (applyCam && s.hasCam)
            {
                var sv = SceneView.lastActiveSceneView;
                if (sv != null) { sv.LookAt(s.camPivot, s.camRot, s.camSize); sv.Repaint(); }
            }
            Debug.Log($"[포즈 스냅샷] '{s.name}' 복원");
        }

        // ── 유틸 ──
        static string PathOf(Transform t, Transform root)
        {
            var sb = new System.Text.StringBuilder(t.name);
            for (Transform p = t.parent; p != null && p != root; p = p.parent)
                sb.Insert(0, p.name + "/");
            return sb.ToString();
        }

        static Transform FindIn(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf   = System.IO.Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
