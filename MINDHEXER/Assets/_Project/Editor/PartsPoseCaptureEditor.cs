using UnityEditor;
using UnityEngine;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// <see cref="PartsPoser"/> 인스펙터 — 지금 자세를 이름 붙여 애셋에 굽는다.
    ///
    /// <para>작업 흐름: 파츠를 손으로 잡아 원하는 모양을 만들고 → 이름을 넣고 → [현재 자세 캡처].
    /// <b>끝나면 반드시 [홈으로 복원] 후 저장할 것</b> — 안 하면 찌그러진 자세가 원본이 된다
    /// (보스 머리 쪽에서 실제로 겪은 사고다).</para>
    /// </summary>
    [CustomEditor(typeof(PartsPoser))]
    public class PartsPoseCaptureEditor : Editor
    {
        string _name = "찌그러짐";

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var p = (PartsPoser)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("자세 캡처", EditorStyles.boldLabel);

            if (p.pose == null)
            {
                EditorGUILayout.HelpBox(
                    "자세 애셋이 없습니다. Create ▸ MINDHEXER ▸ 파츠 자세 모음 으로 만들어 물리십시오.",
                    MessageType.Warning);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _name = EditorGUILayout.TextField("자세 이름", _name);
                if (GUILayout.Button("현재 자세 캡처", GUILayout.Width(110)))
                {
                    Undo.RecordObject(p.pose, "Capture parts pose");
                    p.Collect();
                    p.Capture(_name);
                    EditorUtility.SetDirty(p.pose);
                    AssetDatabase.SaveAssets();
                }
            }

            // 홈이 먼저 있어야 보간의 기준이 생긴다.
            if (p.pose.Home == null)
                EditorGUILayout.HelpBox(
                    "홈 자세가 없습니다. 파츠를 건드리기 전에 먼저 이름 \"홈\"으로 캡처하십시오 " +
                    "— 첫 자세가 홈이라는 규약이고, 되돌릴 안전망입니다.",
                    MessageType.Error);
            else
                EditorGUILayout.LabelField($"홈 파츠 {p.pose.Home.parts.Count}개 · 자세 {p.pose.snapshots.Count}종");

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("홈으로 복원"))
                {
                    Undo.RecordObject(p.transform, "Reset parts pose");
                    p.Collect();
                    p.ResetToHome();
                }
                if (GUILayout.Button("파츠 다시 수집"))
                    p.Collect();
            }

            EditorGUILayout.HelpBox(
                "확인이 끝나면 previewSnapshot을 비우고 [홈으로 복원] 후 저장하십시오. " +
                "찌그러진 자세가 원본으로 굳으면 되돌릴 방법이 없습니다.",
                MessageType.Info);
        }
    }
}
