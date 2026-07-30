using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// <see cref="BossHeadCrush"/> 인스펙터에 <b>자세 캡처 버튼</b>을 붙인다.
    ///
    /// <para><b>작업 흐름</b>
    /// <list type="number">
    /// <item>Tools ▸ 보스 ▸ 머리 판때기 강체로 변환 (1회)</item>
    /// <item>변환 직후 <b>[홈 캡처]</b> — 원래 자세를 안전망으로 저장</item>
    /// <item>씬에서 판때기를 직접 잡고 찌그러진 모양을 만든 뒤 <b>[최종 찌그러짐 캡처]</b></item>
    /// <item>더 눌러 완전히 납작하게 만든 뒤 <b>[완전 납작 캡처]</b></item>
    /// <item><b>[홈으로 복원]</b>으로 원래 자세로 되돌린 다음 저장</item>
    /// </list></para>
    ///
    /// <para>캡처는 <see cref="BossHeadCrush.headRoot"/> 아래 <c>MeshRenderer</c>를 가진 자식들의
    /// <b>로컬</b> pos·rot·scale을 이름과 함께 기록한다. 이름으로 매칭하므로 파츠가 늘거나 줄어도
    /// 나머지는 그대로 살아 있다.</para>
    /// </summary>
    [CustomEditor(typeof(BossHeadCrush))]
    public class BossHeadCrushCaptureEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var t = (BossHeadCrush)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("자세 캡처", EditorStyles.boldLabel);

            if (t.pose == null)
            {
                EditorGUILayout.HelpBox(
                    "자세 애셋(BossHeadCrushPose)이 비어 있습니다.\n" +
                    "Project 창에서 우클릭 → Create ▸ MINDHEXER ▸ 보스 머리 찌그러짐 자세 로 만들어 연결하십시오.",
                    MessageType.Warning);
                return;
            }

            var parts = Collect(t);
            EditorGUILayout.LabelField($"대상 판때기 {parts.Count}개" +
                (parts.Count == 0 ? "  ← 강체 변환을 먼저 하십시오" : ""));

            if (parts.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "Head 아래에 MeshRenderer가 없습니다.\n" +
                    "스킨드 메시는 자기 Transform을 무시하므로 손으로 자세를 잡을 수 없습니다.\n" +
                    "Tools ▸ 보스 ▸ 머리 판때기 강체로 변환 을 먼저 실행하십시오.",
                    MessageType.Error);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("홈 캡처")) Capture(t, parts, t.pose.home, "홈");
                if (GUILayout.Button("최종 찌그러짐 캡처")) Capture(t, parts, t.pose.crush, "최종 찌그러짐");
                if (GUILayout.Button("완전 납작 캡처")) Capture(t, parts, t.pose.flat, "완전 납작");
            }

            EditorGUILayout.LabelField($"저장됨 — 홈 {t.pose.home.Count} / 찌그러짐 {t.pose.crush.Count} / 납작 {t.pose.flat.Count}");

            EditorGUILayout.Space();
            if (GUILayout.Button("홈으로 복원 (자세 되돌리기)")) Restore(t, parts);

            EditorGUILayout.HelpBox(
                "판때기를 만진 채 프리팹을 저장하면 그 자세가 원본이 됩니다.\n" +
                "작업이 끝나면 반드시 [홈으로 복원] 후 저장하십시오.",
                MessageType.Info);
        }

        static List<Transform> Collect(BossHeadCrush t)
        {
            var list = new List<Transform>();
            Transform root = t.headRoot;
            if (root == null)
                foreach (var tr in t.GetComponentsInChildren<Transform>(true))
                    if (tr.name == "Head") { root = tr; break; }
            if (root == null) return list;

            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
                list.Add(mr.transform);
            return list;
        }

        static void Capture(BossHeadCrush t, List<Transform> parts,
                            List<BossHeadCrushPose.PartPose> into, string label)
        {
            Undo.RecordObject(t.pose, "자세 캡처");
            into.Clear();
            foreach (var p in parts)
                into.Add(new BossHeadCrushPose.PartPose
                {
                    name = p.name,
                    pos = p.localPosition,
                    rot = p.localRotation,
                    scale = p.localScale,
                });
            EditorUtility.SetDirty(t.pose);
            AssetDatabase.SaveAssets();
            Debug.Log($"[머리 찌그러짐] '{label}' 자세 {into.Count}개 캡처 → {t.pose.name}");
        }

        static void Restore(BossHeadCrush t, List<Transform> parts)
        {
            if (t.pose.home.Count == 0)
            {
                EditorUtility.DisplayDialog("홈으로 복원", "홈 자세가 캡처돼 있지 않습니다.", "확인");
                return;
            }
            int n = 0;
            foreach (var p in parts)
            {
                var h = BossHeadCrushPose.Find(t.pose.home, p.name);
                if (h == null) continue;
                Undo.RecordObject(p, "홈으로 복원");
                p.localPosition = h.pos;
                p.localRotation = h.rot;
                p.localScale = h.scale;
                n++;
            }
            Debug.Log($"[머리 찌그러짐] 홈으로 복원 {n}개");
        }
    }
}
