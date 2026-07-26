using UnityEngine;
using UnityEditor;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// 칼 그립 & 손 IK 설정 도구.
    ///   ① 칼 자루에 그립 지점 생성 → ② 칼을 손에 자동 정렬 → ③ IK 설치 → 미세 조정 → ④ 기본 그립 저장
    ///
    /// 자동 정렬은 "완벽"이 아니라 "쓸 만한 출발점"을 만든다. 최종 위치·각도는 눈으로 다듬는다.
    /// </summary>
    public class GripSetupTool : EditorWindow
    {
        Transform hand;        // mixamorig:RightHand
        Transform forearm;     // mixamorig:RightForeArm
        Transform upperArm;    // mixamorig:RightArm
        Transform katana;      // 칼 오브젝트(손 자식)
        Transform grip;        // 칼 위의 그립 지점(오른손)

        // 왼손(IK로 칼을 따라감)
        Transform lHand, lForearm, lUpperArm, gripL;

        GripPreset preset;     // 기본 그립 프리셋 에셋
        Vector2 scroll;

        bool flipHandle;       // 자루가 반대편이면
        [Range(0f, 0.4f)] float gripAlongBlade = 0.10f;   // 자루 끝에서 날 쪽으로 얼마나

        // 기본 그립 저장값
        Vector3 savedPos; Quaternion savedRot; bool hasSaved;

        [MenuItem("Tools/뷰모델/칼 그립 & 손 IK 설정")]
        static void Open() => GetWindow<GripSetupTool>("칼 그립 & IK").minSize = new Vector2(380f, 420f);

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "칼을 손에 쥐게 하고, 칼을 움직이면 팔이 따라오게(IK) 설정합니다.\n" +
                "①→②→③ 순서로 누른 뒤 눈으로 다듬고 ④로 저장하십시오.", MessageType.Info);

            if (GUILayout.Button("자동 감지 (선택한 뷰모델에서 찾기)")) AutoDetect();

            EditorGUILayout.Space();
            upperArm = (Transform)EditorGUILayout.ObjectField("위팔 (RightArm)",     upperArm, typeof(Transform), true);
            forearm  = (Transform)EditorGUILayout.ObjectField("아래팔 (RightForeArm)", forearm,  typeof(Transform), true);
            hand     = (Transform)EditorGUILayout.ObjectField("손 (RightHand)",      hand,     typeof(Transform), true);
            katana   = (Transform)EditorGUILayout.ObjectField("칼",                  katana,   typeof(Transform), true);
            grip     = (Transform)EditorGUILayout.ObjectField("그립 지점",            grip,     typeof(Transform), true);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("① 그립 지점", EditorStyles.boldLabel);
            flipHandle      = EditorGUILayout.Toggle(new GUIContent("자루가 반대편", "생성된 지점이 칼끝에 붙으면 켜십시오"), flipHandle);
            gripAlongBlade  = EditorGUILayout.Slider(new GUIContent("자루에서의 위치", "0=자루 끝, 클수록 날 쪽"), gripAlongBlade, 0f, 0.4f);
            using (new EditorGUI.DisabledScope(katana == null))
                if (GUILayout.Button("① 칼 자루에 그립 지점 만들기")) MakeGrip();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("② 자동 정렬", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(katana == null || grip == null || hand == null || forearm == null))
                if (GUILayout.Button("② 칼을 손에 자동으로 쥐게 정렬", GUILayout.Height(26f))) AutoAlign();
            EditorGUILayout.LabelField("  → 이후 칼을 직접 움직여 미세 조정하십시오", EditorStyles.miniLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("③ IK", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(upperArm == null || forearm == null || hand == null || grip == null))
                if (GUILayout.Button("③ 손 IK 설치 (칼을 움직이면 팔이 따라옴)")) InstallIK();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("④ 왼손 (IK로 칼을 따라감)", EditorStyles.boldLabel);
            lUpperArm = (Transform)EditorGUILayout.ObjectField("왼 위팔",   lUpperArm, typeof(Transform), true);
            lForearm  = (Transform)EditorGUILayout.ObjectField("왼 아래팔", lForearm,  typeof(Transform), true);
            lHand     = (Transform)EditorGUILayout.ObjectField("왼손",      lHand,     typeof(Transform), true);
            gripL     = (Transform)EditorGUILayout.ObjectField("Grip_L",    gripL,     typeof(Transform), true);
            using (new EditorGUI.DisabledScope(katana == null || grip == null || lHand == null || lForearm == null || lUpperArm == null))
                if (GUILayout.Button("④ 왼손 자동 설정 (Grip_L + IK + 손가락)", GUILayout.Height(26f))) SetupLeft();
            EditorGUILayout.LabelField("  → 왼손 IK는 스윙 중에도 켜둡니다(칼을 계속 따라가야 함)", EditorStyles.miniLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("⑤ 기본 그립 프리셋", EditorStyles.boldLabel);
            preset = (GripPreset)EditorGUILayout.ObjectField("프리셋 에셋", preset, typeof(GripPreset), false);
            using (new EditorGUI.DisabledScope(katana == null))
            {
                if (GUILayout.Button("현재 그립을 프리셋으로 저장", GUILayout.Height(24f))) SavePreset();
                using (new EditorGUI.DisabledScope(preset == null))
                    if (GUILayout.Button("프리셋 불러오기 (원상복구)")) LoadPreset();
            }
            EditorGUILayout.LabelField("  저장: 칼오프셋 · Grip_L · 양손 손가락  (팔 포즈는 클립 몫)", EditorStyles.miniLabel);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("⑥ 팔 포즈 → Idle 클립", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(upperArm == null))
                if (GUILayout.Button("현재 팔 포즈를 Katana_Idle 클립에 굽기")) BakeIdle();
            EditorGUILayout.LabelField("  IK로 자세를 잡은 뒤 누르십시오. 이후 IK weight=0", EditorStyles.miniLabel);

            // 도달 경고
            if (upperArm != null && forearm != null && hand != null && grip != null)
            {
                float reach = Vector3.Distance(upperArm.position, forearm.position) + Vector3.Distance(forearm.position, hand.position);
                float dist  = Vector3.Distance(upperArm.position, grip.position);
                if (dist > reach)
                    EditorGUILayout.HelpBox($"그립이 팔 길이를 넘습니다 ({dist:0.00} > {reach:0.00}). 칼을 몸쪽으로 당기십시오.", MessageType.Warning);
            }
        }

        void AutoDetect()
        {
            var root = Selection.activeGameObject;
            if (root == null) { Debug.LogWarning("[그립] 뷰모델 루트를 선택한 뒤 누르십시오."); return; }

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                if (upperArm  == null && n.EndsWith("RightArm"))     upperArm  = t;
                if (forearm   == null && n.EndsWith("RightForeArm")) forearm   = t;
                if (hand      == null && n.EndsWith("RightHand"))    hand      = t;
                if (lUpperArm == null && n.EndsWith("LeftArm"))      lUpperArm = t;
                if (lForearm  == null && n.EndsWith("LeftForeArm"))  lForearm  = t;
                if (lHand     == null && n.EndsWith("LeftHand"))     lHand     = t;
            }
            // 칼 = 손 자식 중 렌더러를 가진 것
            if (katana == null && hand != null)
                foreach (Transform c in hand)
                    if (c.GetComponentInChildren<Renderer>() != null) { katana = c; break; }
            if (grip == null && katana != null)
            {
                var g = katana.Find("Grip_R");
                if (g != null) grip = g;
            }
            Debug.Log($"[그립] 자동 감지 — 위팔:{(upperArm?upperArm.name:"X")} 아래팔:{(forearm?forearm.name:"X")} 손:{(hand?hand.name:"X")} 칼:{(katana?katana.name:"X")}");
        }

        /// <summary>칼 메시의 긴 축에서 자루 쪽 끝을 추정해 그립 지점을 만든다.</summary>
        void MakeGrip()
        {
            var rends = katana.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) { Debug.LogError("[그립] 칼에 렌더러가 없습니다."); return; }

            // 칼 로컬 기준 바운드
            Bounds lb = default; bool init = false;
            foreach (var r in rends)
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                // 메시 로컬 → 칼 로컬
                Vector3 c = katana.InverseTransformPoint(mf.transform.TransformPoint(mb.center));
                Vector3 e = katana.InverseTransformVector(mf.transform.TransformVector(mb.extents));
                var b = new Bounds(c, new Vector3(Mathf.Abs(e.x), Mathf.Abs(e.y), Mathf.Abs(e.z)) * 2f);
                if (!init) { lb = b; init = true; } else lb.Encapsulate(b);
            }
            if (!init) { Debug.LogError("[그립] 메시를 못 읽었습니다."); return; }

            // 긴 축 = 날 방향
            Vector3 ex = lb.extents;
            Vector3 axis = ex.x >= ex.y && ex.x >= ex.z ? Vector3.right : ex.y >= ex.z ? Vector3.up : Vector3.forward;
            float half = Vector3.Scale(ex, axis).magnitude;
            float sign = flipHandle ? 1f : -1f;                       // 자루 쪽 끝
            Vector3 local = lb.center + axis * (sign * half * (1f - gripAlongBlade));

            var t = katana.Find("Grip_R");
            if (t == null)
            {
                var go = new GameObject("Grip_R");
                Undo.RegisterCreatedObjectUndo(go, "그립 지점 생성");
                t = go.transform;
                t.SetParent(katana, false);
            }
            t.localPosition = local;
            t.localRotation = Quaternion.identity;
            grip = t;
            Selection.activeGameObject = t.gameObject;
            Debug.Log($"[그립] 그립 지점 생성 — 칼 로컬 {local:F3} (자루 반대면 '자루가 반대편' 체크)");
        }

        /// <summary>그립이 손에 오고, 날이 아래팔 연장선 방향을 보도록 칼을 정렬.</summary>
        void AutoAlign()
        {
            Undo.RecordObject(katana, "칼 자동 정렬");

            // 날 방향(칼 로컬 긴 축)을 월드로
            Vector3 ex = LocalExtents();
            Vector3 axisLocal = ex.x >= ex.y && ex.x >= ex.z ? Vector3.right : ex.y >= ex.z ? Vector3.up : Vector3.forward;
            Vector3 bladeWorld = katana.TransformDirection(axisLocal).normalized;
            if (flipHandle) bladeWorld = -bladeWorld;

            // 목표 방향 = 아래팔 → 손 연장선(주먹에서 칼이 뻗어 나오는 자세)
            Vector3 want = (hand.position - forearm.position).normalized;
            if (want.sqrMagnitude < 1e-6f) want = hand.forward;

            katana.rotation = Quaternion.FromToRotation(bladeWorld, want) * katana.rotation;
            katana.position += hand.position - grip.position;   // 그립을 손 위치로

            Debug.Log("[그립] 자동 정렬 완료 — 칼을 직접 움직여 손아귀에 정확히 맞추십시오.");
        }

        Vector3 LocalExtents()
        {
            Bounds lb = default; bool init = false;
            foreach (var r in katana.GetComponentsInChildren<Renderer>())
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                Vector3 c = katana.InverseTransformPoint(mf.transform.TransformPoint(mb.center));
                Vector3 e = katana.InverseTransformVector(mf.transform.TransformVector(mb.extents));
                var b = new Bounds(c, new Vector3(Mathf.Abs(e.x), Mathf.Abs(e.y), Mathf.Abs(e.z)) * 2f);
                if (!init) { lb = b; init = true; } else lb.Encapsulate(b);
            }
            return lb.extents;
        }

        void InstallIK()
        {
            var root = hand.root;
            var ik = root.GetComponentInChildren<HandIK>();
            if (ik == null)
            {
                var go = new GameObject("HandIK_R");
                Undo.RegisterCreatedObjectUndo(go, "손 IK 설치");
                go.transform.SetParent(root, false);
                ik = go.AddComponent<HandIK>();
            }
            ik.upper = upperArm; ik.lower = forearm; ik.end = hand; ik.target = grip;

            // 팔꿈치 방향 기준점: 현재 팔꿈치에서 바깥·아래로 조금
            var poleT = root.Find("ElbowPole_R");
            if (poleT == null)
            {
                var pg = new GameObject("ElbowPole_R");
                Undo.RegisterCreatedObjectUndo(pg, "팔꿈치 기준점");
                poleT = pg.transform;
                poleT.SetParent(root, false);
            }
            poleT.position = forearm.position + (forearm.position - (upperArm.position + hand.position) * 0.5f).normalized * 0.5f;
            ik.pole = poleT;

            EditorUtility.SetDirty(ik);
            Selection.activeGameObject = ik.gameObject;
            Debug.Log("[그립] 손 IK 설치 완료. 이제 칼을 움직이면 팔이 따라옵니다.\n" +
                      "  팔꿈치가 이상한 방향이면 ElbowPole_R 을 옮기십시오. IK를 끄려면 weight=0.");
        }

        /// <summary>왼손: Grip_L 생성 + 왼팔 IK + 팔꿈치 기준점 + 손가락 포저를 한 번에.</summary>
        void SetupLeft()
        {
            // Grip_L — 오른손 그립에서 자루를 따라 조금 아래(칼끝 반대)로
            if (gripL == null) gripL = katana.Find("Grip_L");
            if (gripL == null)
            {
                var go = new GameObject("Grip_L");
                Undo.RegisterCreatedObjectUndo(go, "왼손 그립 지점");
                gripL = go.transform;
                gripL.SetParent(katana, false);
                // 그립 방향(칼 긴 축)으로 자루 끝 쪽으로 이동
                Vector3 ex = LocalExtents();
                Vector3 axis = ex.x >= ex.y && ex.x >= ex.z ? Vector3.right : ex.y >= ex.z ? Vector3.up : Vector3.forward;
                float step = Vector3.Scale(ex, axis).magnitude * 0.12f;
                gripL.localPosition = grip.localPosition + axis * (flipHandle ? step : -step);
                gripL.localRotation = grip.localRotation;
            }

            // 왼팔 IK
            Transform root = lHand.root;
            var ikGo = root.Find("HandIK_L");
            HandIK ik;
            if (ikGo == null)
            {
                var go = new GameObject("HandIK_L");
                Undo.RegisterCreatedObjectUndo(go, "왼손 IK");
                go.transform.SetParent(root, false);
                ik = go.AddComponent<HandIK>();
            }
            else ik = ikGo.GetComponent<HandIK>() ?? ikGo.gameObject.AddComponent<HandIK>();

            ik.upper = lUpperArm; ik.lower = lForearm; ik.end = lHand; ik.target = gripL;
            ik.weight = 1f; ik.matchRotation = true;   // 왼손은 항상 켜둠

            var poleT = root.Find("ElbowPole_L");
            if (poleT == null)
            {
                var pg = new GameObject("ElbowPole_L");
                Undo.RegisterCreatedObjectUndo(pg, "왼 팔꿈치 기준점");
                poleT = pg.transform;
                poleT.SetParent(root, false);
            }
            poleT.position = lForearm.position + (lForearm.position - (lUpperArm.position + lHand.position) * 0.5f).normalized * 0.5f;
            ik.pole = poleT;
            EditorUtility.SetDirty(ik);

            // 왼손 손가락 포저
            if (lHand.GetComponent<FingerPoser>() == null)
            {
                var fp = Undo.AddComponent<FingerPoser>(lHand.gameObject);
                fp.handRoot = lHand;
            }

            Selection.activeGameObject = gripL.gameObject;
            Debug.Log("[그립] 왼손 설정 완료 — Grip_L 을 자루 위 원하는 위치로 옮기고, 회전으로 손목각을 맞추십시오.\n" +
                      "  왼손 FingerPoser 로 손가락을 감싼 뒤 '현재 포즈를 기준으로 고정'.");
        }

        // ── 프리셋 ──
        void SavePreset()
        {
            if (preset == null)
            {
                EnsureFolder("Assets/_Project/Prefabs/Viewmodel");
                string path = AssetDatabase.GenerateUniqueAssetPath("Assets/_Project/Prefabs/Viewmodel/GripPreset.asset");
                preset = ScriptableObject.CreateInstance<GripPreset>();
                AssetDatabase.CreateAsset(preset, path);
            }
            Undo.RecordObject(preset, "기본 그립 저장");

            preset.hasKatana = katana != null;
            if (katana != null)
            {
                preset.katanaLocalPos   = katana.localPosition;
                preset.katanaLocalRot   = katana.localRotation;
                preset.katanaLocalScale = katana.localScale;
            }
            preset.hasGripL = gripL != null;
            if (gripL != null) { preset.gripLLocalPos = gripL.localPosition; preset.gripLLocalRot = gripL.localRotation; }

            var fpR = hand  != null ? hand.GetComponent<FingerPoser>()  : null;
            var fpL = lHand != null ? lHand.GetComponent<FingerPoser>() : null;
            preset.rightFingerRest = fpR != null ? fpR.ExportRest() : null;
            preset.leftFingerRest  = fpL != null ? fpL.ExportRest() : null;

            EditorUtility.SetDirty(preset);
            AssetDatabase.SaveAssets();
            Debug.Log($"[그립] 프리셋 저장 — 칼:{preset.hasKatana} GripL:{preset.hasGripL} " +
                      $"오른손가락:{preset.HasRightFingers} 왼손가락:{preset.HasLeftFingers}\n  {AssetDatabase.GetAssetPath(preset)}");
        }

        void LoadPreset()
        {
            if (preset.hasKatana && katana != null)
            {
                Undo.RecordObject(katana, "그립 복원");
                katana.localPosition = preset.katanaLocalPos;
                katana.localRotation = preset.katanaLocalRot;
                katana.localScale    = preset.katanaLocalScale;
            }
            if (preset.hasGripL && gripL != null)
            {
                Undo.RecordObject(gripL, "그립 복원");
                gripL.localPosition = preset.gripLLocalPos;
                gripL.localRotation = preset.gripLLocalRot;
            }
            var fpR = hand  != null ? hand.GetComponent<FingerPoser>()  : null;
            var fpL = lHand != null ? lHand.GetComponent<FingerPoser>() : null;
            if (fpR != null && preset.HasRightFingers) fpR.ImportRest(preset.rightFingerRest);
            if (fpL != null && preset.HasLeftFingers)  fpL.ImportRest(preset.leftFingerRest);
            Debug.Log("[그립] 프리셋 복원 완료");
        }

        /// <summary>IK로 잡은 현재 팔 포즈를 Katana_Idle 클립 프레임 0에 회전 키로 굽는다.</summary>
        void BakeIdle()
        {
            const string clipPath = "Assets/_Project/Prefabs/Katana/Katana_Idle.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null) { Debug.LogError($"[그립] Idle 클립 없음: {clipPath}"); return; }

            var animator = hand.GetComponentInParent<Animator>();
            if (animator == null) { Debug.LogError("[그립] Animator를 못 찾았습니다(뷰모델 루트에 있어야 합니다)."); return; }
            Transform animRoot = animator.transform;

            var bones = new System.Collections.Generic.List<Transform> { upperArm, forearm, hand };
            if (upperArm != null && upperArm.parent != null && upperArm.parent.name.EndsWith("Shoulder"))
                bones.Insert(0, upperArm.parent);

            Undo.RecordObject(clip, "Idle 포즈 굽기");
            int n = 0;
            foreach (var b in bones)
            {
                if (b == null) continue;
                string path = AnimationUtility.CalculateTransformPath(b, animRoot);
                Quaternion q = b.localRotation;
                SetConst(clip, path, "localRotation.x", q.x);
                SetConst(clip, path, "localRotation.y", q.y);
                SetConst(clip, path, "localRotation.z", q.z);
                SetConst(clip, path, "localRotation.w", q.w);
                n++;
            }
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();
            Debug.Log($"[그립] Idle 클립에 팔 포즈 {n}개 뼈를 구웠습니다.\n  이제 HandIK_R 의 weight를 0으로 두십시오(왼손 IK는 유지).");
        }

        static void SetConst(AnimationClip clip, string path, string prop, float v)
        {
            var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), prop);
            AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0f, 0f, v));
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf   = System.IO.Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        void SaveBase()
        {
            savedPos = katana.localPosition; savedRot = katana.localRotation; hasSaved = true;
            Debug.Log($"[그립] 칼 오프셋 임시 저장 — pos {savedPos:F4} rot {savedRot.eulerAngles:F1}  (영구 저장은 ⑤ 프리셋)");
        }

        void RestoreBase()
        {
            Undo.RecordObject(katana, "기본 그립 복원");
            katana.localPosition = savedPos; katana.localRotation = savedRot;
        }
    }
}
