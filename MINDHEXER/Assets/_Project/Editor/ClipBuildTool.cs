using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using Game.View;   // PoseFile 등은 런타임 쪽(PoseData.cs)

namespace Game.EditorTools
{
    // ── 애니메이션 명세 JSON ──────────────────────────────────────
    [Serializable] public class AnimKey
    {
        public int    frame;          // 프레임 번호
        public string pose;           // 포즈 이름 (pose_<이름>.json)
        public string ease = "auto";  // auto | flat | linear | step
    }

    [Serializable] public class AnimSpec
    {
        public string    clip;           // 만들 .anim 에셋 경로
        public int       frameRate = 60;
        public AnimKey[] keys;
    }

    /// <summary>
    /// 애니메이션 명세 JSON을 읽어 AnimationClip을 생성한다.
    ///
    /// 흐름: 포즈들을 JSON으로 저장 → 명세 JSON(어떤 포즈를 몇 프레임에, 어떻게 이을지) → 이 도구로 빌드.
    ///
    /// ease(키마다 지정):
    ///   auto   — 부드럽게 (기본)
    ///   flat   — 그 지점에서 멈춤 (예비동작 정점)
    ///   linear — 직선. 앞뒤 키 간격이 좁으면 급가속이 된다(타격 구간)
    ///   step   — 계단식(즉시 전환)
    ///
    /// 회전은 쿼터니언 4채널로 굽는다. 부호가 뒤집히면 한 바퀴 도는 현상이 생기므로
    /// 연속 키의 쿼터니언을 같은 반구로 맞춘다(아래 EnsureShortestPath).
    /// </summary>
    public class ClipBuildTool : EditorWindow
    {
        Transform root;
        string    specPath = "";
        Vector2   scroll;
        string[]  specs = new string[0];

        [MenuItem("Tools/뷰모델/클립 빌드")]
        static void Open() { GetWindow<ClipBuildTool>("클립 빌드").minSize = new Vector2(460f, 380f); }

        void OnEnable() => Refresh();

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "애니메이션 명세 JSON(anim_*.json)을 읽어 .anim 클립을 생성합니다.\n" +
                "명세에 적힌 포즈(pose_*.json)들을 키프레임으로 굽습니다.", MessageType.Info);

            if (root == null) AutoDetect();
            root = (Transform)EditorGUILayout.ObjectField("뷰모델 루트", root, typeof(Transform), true);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("명세 파일", EditorStyles.boldLabel);
                if (GUILayout.Button("새로고침", GUILayout.Width(70f))) Refresh();
                if (GUILayout.Button("폴더 열기", GUILayout.Width(70f))) EditorUtility.RevealInFinder(PoseJsonTool.PoseDir);
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var f in specs)
            {
                using (new EditorGUILayout.HorizontalScope("box"))
                {
                    EditorGUILayout.LabelField(Path.GetFileNameWithoutExtension(f));
                    using (new EditorGUI.DisabledScope(root == null))
                        if (GUILayout.Button("빌드", GUILayout.Width(60f))) Build(f);
                }
            }
            EditorGUILayout.EndScrollView();

            if (specs.Length == 0)
                EditorGUILayout.HelpBox($"{PoseJsonTool.PoseDir} 에 anim_*.json 이 없습니다.", MessageType.None);
        }

        void AutoDetect()
        {
            var cam = Camera.main;
            if (cam != null && cam.transform.childCount > 0) root = cam.transform.GetChild(0);
            if (root == null) { var go = GameObject.Find("KatanaViewmodel"); if (go != null) root = go.transform; }
        }

        void Refresh()
        {
            Directory.CreateDirectory(PoseJsonTool.PoseDir);
            specs = Directory.GetFiles(PoseJsonTool.PoseDir, "anim_*.json");
            Array.Sort(specs);
        }

        // ── 빌드 ──
        void Build(string specFile)
        {
            var spec = JsonUtility.FromJson<AnimSpec>(File.ReadAllText(specFile, System.Text.Encoding.UTF8));
            if (spec == null || spec.keys == null || spec.keys.Length == 0) { Debug.LogError("[클립 빌드] 명세가 비었습니다."); return; }

            // 포즈 로드
            var poses = new List<PoseFile>();
            foreach (var k in spec.keys)
            {
                string pf = Path.Combine(PoseJsonTool.PoseDir, $"pose_{k.pose}.json");
                if (!File.Exists(pf)) { Debug.LogError($"[클립 빌드] 포즈 없음: {pf}"); return; }
                poses.Add(JsonUtility.FromJson<PoseFile>(File.ReadAllText(pf, System.Text.Encoding.UTF8)));
            }

            float fr = Mathf.Max(1, spec.frameRate);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(spec.clip);
            bool isNew = clip == null;
            if (isNew) { clip = new AnimationClip(); }
            clip.frameRate = fr;
            clip.ClearCurves();

            // 경로별로 회전 커브 4채널 + (오브젝트면) 위치·크기 커브
            var rotByPath   = new Dictionary<string, List<(float t, Quaternion q)>>();
            var posByPath   = new Dictionary<string, List<(float t, Vector3 v)>>();
            var scaleByPath = new Dictionary<string, List<(float t, Vector3 v)>>();

            for (int i = 0; i < spec.keys.Length; i++)
            {
                float t = spec.keys[i].frame / fr;
                var p = poses[i];
                if (p.bones != null)
                    foreach (var b in p.bones) Add(rotByPath, b.path, t, PoseJsonTool.ToQ(b.quat));
                if (p.objects != null)
                    foreach (var o in p.objects)
                    {
                        if (o.quat  != null && o.quat.Length  == 4) Add(rotByPath,   o.path, t, PoseJsonTool.ToQ(o.quat));
                        if (o.pos   != null && o.pos.Length   == 3) Add(posByPath,   o.path, t, PoseJsonTool.ToV3(o.pos));
                        if (o.scale != null && o.scale.Length == 3) Add(scaleByPath, o.path, t, PoseJsonTool.ToV3(o.scale));
                    }
            }

            int curves = 0;
            foreach (var kv in rotByPath)
            {
                var list = kv.Value;
                EnsureShortestPath(list);   // 부호 뒤집힘 제거(한 바퀴 도는 현상 방지)
                for (int c = 0; c < 4; c++)
                {
                    var curve = new AnimationCurve();
                    for (int i = 0; i < list.Count; i++)
                    {
                        var q = list[i].q;
                        float v = c == 0 ? q.x : c == 1 ? q.y : c == 2 ? q.z : q.w;
                        curve.AddKey(new Keyframe(list[i].t, v));
                    }
                    ApplyEase(curve, spec.keys);
                    string prop = c == 0 ? "localRotation.x" : c == 1 ? "localRotation.y" : c == 2 ? "localRotation.z" : "localRotation.w";
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(kv.Key, typeof(Transform), prop), curve);
                    curves++;
                }
            }
            curves += WriteVec(clip, posByPath,   spec.keys, "localPosition");
            curves += WriteVec(clip, scaleByPath, spec.keys, "localScale");

            // 저장
            string dir = Path.GetDirectoryName(spec.clip).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(dir)) Directory.CreateDirectory(dir);
            if (isNew) AssetDatabase.CreateAsset(clip, spec.clip);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            int lastFrame = spec.keys[spec.keys.Length - 1].frame;
            Debug.Log($"[클립 빌드] {(isNew ? "생성" : "갱신")}: {spec.clip}\n" +
                      $"  키 {spec.keys.Length}개 · 커브 {curves}개 · 길이 {lastFrame}프레임 ({lastFrame / fr:0.000}초 @{fr}fps)");
            Selection.activeObject = clip;
        }

        static int WriteVec(AnimationClip clip, Dictionary<string, List<(float t, Vector3 v)>> src, AnimKey[] keys, string prop)
        {
            int n = 0;
            foreach (var kv in src)
                for (int c = 0; c < 3; c++)
                {
                    var curve = new AnimationCurve();
                    foreach (var e in kv.Value)
                        curve.AddKey(new Keyframe(e.t, c == 0 ? e.v.x : c == 1 ? e.v.y : e.v.z));
                    ApplyEase(curve, keys);
                    AnimationUtility.SetEditorCurve(clip,
                        EditorCurveBinding.FloatCurve(kv.Key, typeof(Transform), $"{prop}.{(c == 0 ? "x" : c == 1 ? "y" : "z")}"), curve);
                    n++;
                }
            return n;
        }

        static void Add<T>(Dictionary<string, List<(float, T)>> d, string path, float t, T v)
        {
            if (!d.TryGetValue(path, out var l)) { l = new List<(float, T)>(); d[path] = l; }
            l.Add((t, v));
        }

        /// <summary>연속 쿼터니언의 부호를 맞춰 최단 경로로 보간되게 한다.</summary>
        static void EnsureShortestPath(List<(float t, Quaternion q)> list)
        {
            for (int i = 1; i < list.Count; i++)
            {
                var prev = list[i - 1].q; var cur = list[i].q;
                if (Quaternion.Dot(prev, cur) < 0f)
                    list[i] = (list[i].t, new Quaternion(-cur.x, -cur.y, -cur.z, -cur.w));
            }
        }

        /// <summary>키마다 ease 지정에 따라 탄젠트를 설정.</summary>
        static void ApplyEase(AnimationCurve curve, AnimKey[] keys)
        {
            int n = Mathf.Min(curve.length, keys.Length);
            for (int i = 0; i < n; i++)
            {
                string e = (keys[i].ease ?? "auto").ToLowerInvariant();
                switch (e)
                {
                    case "flat":
                    {
                        var k = curve[i]; k.inTangent = 0f; k.outTangent = 0f; curve.MoveKey(i, k);
                        AnimationUtility.SetKeyLeftTangentMode (curve, i, AnimationUtility.TangentMode.Free);
                        AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Free);
                        break;
                    }
                    case "linear":
                        AnimationUtility.SetKeyLeftTangentMode (curve, i, AnimationUtility.TangentMode.Linear);
                        AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                        break;
                    case "step":
                    case "constant":
                        AnimationUtility.SetKeyLeftTangentMode (curve, i, AnimationUtility.TangentMode.Constant);
                        AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
                        break;
                    default:
                        AnimationUtility.SetKeyLeftTangentMode (curve, i, AnimationUtility.TangentMode.ClampedAuto);
                        AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                        break;
                }
            }
        }
    }
}
