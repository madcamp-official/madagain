using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// 신체 파츠를 <b>팔</b>과 <b>나머지</b>로 자동 분류한다.
    /// 설계: `docs/KJH/design/플레이어_신체표현_설계.md` §5.4
    ///
    /// <para><b>왜 이름으로 못 하나</b>: AI 생성 모델이라 파츠 이름이 <c>tripo_part_21_037</c> 식이라
    /// 아무 의미가 없다. 그래서 각 파츠의 <b>본 가중치</b>를 보고 "이 메시가 주로 어느 뼈에 묶였나"로 판정한다.</para>
    ///
    /// <para><b>메시를 자르지 않는다</b> — <see cref="ViewmodelArmTool"/>(삼각형 단위 절단)과 다르다.
    /// 여기서는 파츠를 <b>목록으로 나누기만</b> 하고, 실제 표시는 SetActive로 한다. 되돌릴 수 있다.</para>
    ///
    /// 결과는 <see cref="PlayerBodyParts"/>에 채워진다.
    /// </summary>
    public class BodyPartTaggerTool : EditorWindow
    {
        PlayerBodyParts target;
        float armThreshold = 0.5f;
        bool  forearmOnly = true;
        bool  includeShoulder = true;
        bool  includeClavicle;
        Vector2 scroll;

        // 미리보기 결과
        readonly List<Renderer> _arm = new List<Renderer>();
        readonly List<Renderer> _body = new List<Renderer>();
        readonly Dictionary<Renderer, float> _score = new Dictionary<Renderer, float>();
        bool _previewed;

        [MenuItem("Tools/뷰모델/신체 파츠 분류")]
        static void Open() => GetWindow<BodyPartTaggerTool>("파츠 분류").minSize = new Vector2(420f, 460f);

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "각 파츠가 어느 뼈에 묶였는지(본 가중치)로 팔/나머지를 나눕니다.\n" +
                "메시를 자르지 않고 목록만 나누므로 언제든 되돌릴 수 있습니다.", MessageType.Info);

            if (target == null && Selection.activeGameObject != null)
                target = Selection.activeGameObject.GetComponentInChildren<PlayerBodyParts>(true);

            target = (PlayerBodyParts)EditorGUILayout.ObjectField("대상", target, typeof(PlayerBodyParts), true);

            if (target == null)
            {
                EditorGUILayout.HelpBox(
                    "PlayerBodyParts 컴포넌트가 붙은 오브젝트를 지정하십시오.\n" +
                    "없으면 아래 버튼으로 모델 루트에 붙일 수 있습니다.", MessageType.Warning);
                if (Selection.activeGameObject != null &&
                    GUILayout.Button($"'{Selection.activeGameObject.name}' 에 PlayerBodyParts 붙이기"))
                {
                    var p = Undo.AddComponent<PlayerBodyParts>(Selection.activeGameObject);
                    p.AutoFindBones();
                    target = p;
                }
                return;
            }

            EditorGUILayout.Space();
            armThreshold = EditorGUILayout.Slider(
                new GUIContent("팔 판정 임계값", "파츠의 팔 뼈 가중치 비율이 이 값 이상이면 팔로 본다"),
                armThreshold, 0.05f, 0.95f);
            forearmOnly = EditorGUILayout.Toggle(
                new GUIContent("팔꿈치 아래만", "전완·손·손가락만 남긴다. 위팔·어깨·쇄골은 전부 끈다.\n" +
                                              "어깨가 없으면 카메라 뒤 지오메트리가 없어져 근평면·벽 관통 문제가 함께 사라진다."),
                forearmOnly);

            using (new EditorGUI.DisabledScope(forearmOnly))
            {
                includeShoulder = EditorGUILayout.Toggle(
                    new GUIContent("어깨 포함", "끄면 위팔부터. '팔꿈치 아래만'이 켜져 있으면 무의미하다"), includeShoulder);
                includeClavicle = EditorGUILayout.Toggle(
                    new GUIContent("쇄골 포함", "켜면 가슴 위쪽까지 딸려올 수 있다"), includeClavicle);
            }

            if (forearmOnly)
                EditorGUILayout.HelpBox(
                    "팔꿈치 단면이 보이면 임계값을 올려 전완 위쪽 파츠를 더 떨어내십시오.\n" +
                    "근평면(0.01)은 이제 아무것도 잘라 주지 않습니다 — 손가락을 살리려 낮춘 값입니다.",
                    MessageType.None);

            EditorGUILayout.Space();
            if (GUILayout.Button("분류 미리보기", GUILayout.Height(28f))) Analyze();

            if (_previewed)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"팔 {_arm.Count}개 · 나머지 {_body.Count}개", EditorStyles.boldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("팔만 보기")) Preview(true);
                    if (GUILayout.Button("전체 보기")) Preview(false);
                }

                using (new EditorGUI.DisabledScope(_arm.Count == 0))
                    if (GUILayout.Button("이 분류를 저장", GUILayout.Height(30f))) Save();

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("경계 근처 파츠 (임계값 ±0.2)", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("  애매한 것만 보여줍니다. 클릭하면 씬에서 선택됩니다.", EditorStyles.miniLabel);

                scroll = EditorGUILayout.BeginScrollView(scroll);
                foreach (var kv in _score.OrderByDescending(k => k.Value))
                {
                    if (Mathf.Abs(kv.Value - armThreshold) > 0.2f) continue;
                    using (new EditorGUILayout.HorizontalScope("box"))
                    {
                        EditorGUILayout.LabelField($"{kv.Value:0.00}", GUILayout.Width(44f));
                        EditorGUILayout.LabelField(kv.Key != null ? kv.Key.name : "(없음)");
                        if (GUILayout.Button("선택", GUILayout.Width(46f)))
                            Selection.activeGameObject = kv.Key.gameObject;
                    }
                }
                EditorGUILayout.EndScrollView();
            }

            if (target.IsTagged)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(
                    $"저장된 분류: 팔 {target.armParts.Count}개 · 나머지 {target.bodyParts.Count}개",
                    EditorStyles.miniLabel);
            }
        }

        /// <summary>이 뼈가 팔 계열인가. 우리 리그는 R_/L_ 접두사를 쓴다.</summary>
        bool IsArmBone(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return false;
            string n = raw.ToLowerInvariant();

            if (IsHandBone(n)) return true;

            // ★ 팔꿈치 아래만 — 전완(forearm)과 그 트위스트, 손·손가락만 남긴다.
            //   'upperarm'도 "arm"을 포함하므로 문자열만 보면 못 가른다. forearm을 먼저 걸러야 한다.
            if (n.Contains("forearm")) return true;
            if (forearmOnly) return false;

            if (n.Contains("clavicle")) return includeClavicle;
            if (n.Contains("shoulder")) return includeShoulder;

            // upperarm·twist 전부 "arm"으로 잡힌다.
            return n.Contains("arm");
        }

        /// <summary>손목 이하(손·손가락). '팔꿈치 아래만' 모드에서도 항상 남는다.</summary>
        static bool IsHandBone(string n) =>
               n.Contains("hand")
            || n.Contains("thumb") || n.Contains("index")
            || n.Contains("middle")|| n.Contains("ring")
            || n.Contains("pinky");

        void Analyze()
        {
            _arm.Clear(); _body.Clear(); _score.Clear();
            _previewed = true;

            var renderers = target.GetComponentsInChildren<Renderer>(true);
            int noWeight = 0;

            foreach (var r in renderers)
            {
                float score = ArmScore(r, out bool measurable);
                if (!measurable) noWeight++;
                _score[r] = score;
                if (score >= armThreshold) _arm.Add(r); else _body.Add(r);
            }

            Debug.Log($"[파츠 분류] 팔 {_arm.Count}개 · 나머지 {_body.Count}개" +
                      (noWeight > 0 ? $"  (가중치를 못 읽은 파츠 {noWeight}개 — 위치로 추정함)" : ""));
        }

        /// <summary>
        /// 이 렌더러가 얼마나 "팔"인가 (0~1).
        /// 스킨드면 본 가중치 합, 아니면 부모 뼈 이름으로 판정한다.
        /// </summary>
        float ArmScore(Renderer r, out bool measurable)
        {
            measurable = false;
            var smr = r as SkinnedMeshRenderer;

            if (smr != null && smr.sharedMesh != null && smr.bones != null && smr.bones.Length > 0)
            {
                var bw = smr.sharedMesh.boneWeights;
                if (bw != null && bw.Length > 0)
                {
                    measurable = true;
                    var isArm = new bool[smr.bones.Length];
                    for (int i = 0; i < smr.bones.Length; i++)
                        isArm[i] = smr.bones[i] != null && IsArmBone(smr.bones[i].name);

                    float armSum = 0f, total = 0f;
                    foreach (var w in bw)
                    {
                        if (isArm[w.boneIndex0]) armSum += w.weight0;
                        if (isArm[w.boneIndex1]) armSum += w.weight1;
                        if (isArm[w.boneIndex2]) armSum += w.weight2;
                        if (isArm[w.boneIndex3]) armSum += w.weight3;
                        total += w.weight0 + w.weight1 + w.weight2 + w.weight3;
                    }
                    return total > 1e-5f ? armSum / total : 0f;
                }
            }

            // 스킨드가 아니면 부모 계층에 팔 뼈가 있는지로 본다.
            for (Transform t = r.transform; t != null; t = t.parent)
                if (IsArmBone(t.name)) return 1f;
            return 0f;
        }

        /// <summary>씬에서 즉시 확인 — 저장하지 않고 켜고 끄기만 한다.</summary>
        void Preview(bool armOnly)
        {
            foreach (var r in _arm)  if (r != null) r.gameObject.SetActive(true);
            foreach (var r in _body) if (r != null) r.gameObject.SetActive(!armOnly);
            SceneView.RepaintAll();
        }

        void Save()
        {
            Undo.RecordObject(target, "파츠 분류 저장");
            target.armParts = new List<Renderer>(_arm);
            target.bodyParts = new List<Renderer>(_body);
            target.AutoFindBones();
            EditorUtility.SetDirty(target);
            Debug.Log($"[파츠 분류] 저장 — 팔 {_arm.Count}개 · 나머지 {_body.Count}개" +
                      (target.rightHand != null ? $"  (R_Hand 찾음)" : "  ★R_Hand를 못 찾았습니다"));
        }
    }
}
