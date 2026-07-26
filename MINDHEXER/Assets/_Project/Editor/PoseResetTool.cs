using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// 자세 리셋 — 씬의 뷰모델 뼈를 임포트된 fbx 원본(바인드) 자세로 되돌린다.
    ///
    /// 왜 필요한가: IK·수동 회전·손가락 복사 등이 누적되면 어떤 게 "기준"인지 알 수 없게 된다.
    ///   fbx 에셋에서 각 뼈의 원래 회전을 직접 읽어오므로, 씬을 아무리 망가뜨려도 항상 복구된다.
    ///
    /// ★ 리셋 전에 반드시 IK를 끄십시오. HandIK는 [ExecuteAlways]라 매 프레임 자세를
    ///   덮어쓰기 때문에, 켜둔 채로는 무엇을 고쳐도 즉시 되돌아갑니다.
    /// </summary>
    public class PoseResetTool : EditorWindow
    {
        enum Scope { 전체, 팔만, 손가락만 }

        Transform  root;      // 씬의 뷰모델 루트
        GameObject fbx;       // 원본 fbx 에셋
        Scope      scope = Scope.팔만;
        bool       alsoPositions;   // 회전뿐 아니라 위치까지(보통 불필요)

        [MenuItem("Tools/뷰모델/자세 리셋")]
        static void Open() => GetWindow<PoseResetTool>("자세 리셋").minSize = new Vector2(380f, 340f);

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "씬의 뼈 자세를 fbx 원본으로 되돌립니다.\n" +
                "★ 리셋 전에 'IK 전부 끄기'를 먼저 누르십시오. IK가 켜져 있으면 즉시 덮어씁니다.",
                MessageType.Warning);

            if (root == null || fbx == null) AutoDetect();
            root = (Transform)EditorGUILayout.ObjectField("뷰모델 루트", root, typeof(Transform), true);
            fbx  = (GameObject)EditorGUILayout.ObjectField("원본 fbx", fbx, typeof(GameObject), false);
            if (GUILayout.Button("자동 감지")) AutoDetect();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("① IK 제어", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("오른손 켜기",  GUILayout.Height(24f))) SetIk(1f, false);
                if (GUILayout.Button("오른손 끄기",  GUILayout.Height(24f))) SetIk(0f, false);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("왼손 켜기",   GUILayout.Height(24f))) SetIk(1f, true);
                if (GUILayout.Button("왼손 끄기",   GUILayout.Height(24f))) SetIk(0f, true);
            }
            if (GUILayout.Button("양손 전부 끄기 (조정 전 필수)", GUILayout.Height(26f))) { SetIk(0f, false); SetIk(0f, true); }
            ShowIkState();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("② 자세 리셋", EditorStyles.boldLabel);
            scope = (Scope)EditorGUILayout.EnumPopup("범위", scope);
            alsoPositions = EditorGUILayout.Toggle(
                new GUIContent("위치까지 리셋", "보통 뼈 위치는 안 변하므로 꺼두십시오"), alsoPositions);
            using (new EditorGUI.DisabledScope(root == null || fbx == null))
                if (GUILayout.Button("fbx 원본 자세로 리셋", GUILayout.Height(30f))) ResetPose();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("③ 기준 재캡처", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("  자세를 새로 잡은 뒤 눌러 IK·손가락의 기준을 갱신", EditorStyles.miniLabel);
            using (new EditorGUI.DisabledScope(root == null))
                if (GUILayout.Button("현재 자세를 기준으로 캡처 (IK + FingerPoser)")) Recapture();
        }

        void AutoDetect()
        {
            if (root == null)
            {
                var cam = Camera.main;
                if (cam != null && cam.transform.childCount > 0) root = cam.transform.GetChild(0);
                if (root == null) { var go = GameObject.Find("KatanaViewmodel"); if (go != null) root = go.transform; }
            }
            if (fbx == null && root != null)
            {
                // 씬 인스턴스의 원본 프리팹(fbx) 찾기
                var src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(root.gameObject);
                if (src != null) fbx = src.transform.root.gameObject;
                if (fbx == null)
                {
                    // 이름으로 검색(언팩된 경우)
                    foreach (var guid in AssetDatabase.FindAssets("t:Model Hooded_Mech"))
                    {
                        var p = AssetDatabase.GUIDToAssetPath(guid);
                        var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                        if (go != null) { fbx = go; break; }
                    }
                }
            }
        }

        static bool IsLeftIk(HandIK ik)
        {
            if (ik.end != null && ik.end.name.IndexOf("Left",  System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (ik.end != null && ik.end.name.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return ik.gameObject.name.EndsWith("_L");
        }

        void ShowIkState()
        {
            if (root == null) return;
            var iks = root.GetComponentsInChildren<HandIK>(true);
            if (iks.Length == 0) { EditorGUILayout.LabelField("  HandIK 없음", EditorStyles.miniLabel); return; }
            foreach (var ik in iks)
            {
                string side = IsLeftIk(ik) ? "왼손" : "오른손";
                string palm = ik.end != null && ik.target != null
                    ? $"  손바닥↔목표 {Vector3.Distance(ik.PalmWorld(), ik.target.position):0.000}m" : "";
                EditorGUILayout.LabelField(
                    $"  [{side}] {ik.gameObject.name} : weight {ik.weight:0.00}" +
                    (ik.IsOutOfReach() ? "  ★사정거리 밖" : "") + palm, EditorStyles.miniLabel);
            }
        }

        /// <summary>한쪽 IK만 weight 설정.</summary>
        void SetIk(float w, bool left)
        {
            if (root == null) return;
            int n = 0;
            foreach (var ik in root.GetComponentsInChildren<HandIK>(true))
            {
                if (IsLeftIk(ik) != left) continue;
                Undo.RecordObject(ik, "IK weight");
                ik.weight = w;
                EditorUtility.SetDirty(ik);
                n++;
            }
            Debug.Log($"[자세 리셋] {(left ? "왼손" : "오른손")} IK weight = {w} ({n}개)");
        }

        static bool IsFinger(string n) =>
            n.Contains("Thumb") || n.Contains("Index") || n.Contains("Middle") || n.Contains("Ring") || n.Contains("Pinky");

        static bool IsArm(string n) =>
            n.EndsWith("Shoulder") || n.EndsWith("Arm") || n.EndsWith("ForeArm") || n.EndsWith("Hand");

        bool InScope(string n)
        {
            switch (scope)
            {
                case Scope.팔만:      return IsArm(n) && !IsFinger(n);
                case Scope.손가락만:  return IsFinger(n);
                default:              return true;
            }
        }

        void ResetPose()
        {
            // fbx 원본의 이름 → 로컬 TRS 사전
            var src = new Dictionary<string, Transform>();
            foreach (var t in fbx.GetComponentsInChildren<Transform>(true))
                if (!src.ContainsKey(t.name)) src[t.name] = t;

            int applied = 0, missing = 0, skipped = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root) continue;
                if (!InScope(t.name)) { skipped++; continue; }
                if (!src.TryGetValue(t.name, out var s)) { missing++; continue; }

                Undo.RecordObject(t, "자세 리셋");
                t.localRotation = s.localRotation;
                if (alsoPositions) { t.localPosition = s.localPosition; t.localScale = s.localScale; }
                applied++;
            }
            Debug.Log($"[자세 리셋] {scope} — 적용 {applied}개 · 원본에 없음 {missing}개 · 범위 밖 {skipped}개\n" +
                      "  IK가 켜져 있으면 즉시 덮어써집니다. 껐는지 확인하십시오.");
        }

        void Recapture()
        {
            foreach (var ik in root.GetComponentsInChildren<HandIK>(true))
            {
                Undo.RecordObject(ik, "기준 캡처");
                ik.Capture();
                EditorUtility.SetDirty(ik);
            }
            foreach (var fp in root.GetComponentsInChildren<FingerPoser>(true))
            {
                Undo.RecordObject(fp, "기준 캡처");
                fp.Rebuild();
                EditorUtility.SetDirty(fp);
            }
            Debug.Log("[자세 리셋] IK·FingerPoser 기준을 현재 자세로 재캡처했습니다.");
        }
    }
}
