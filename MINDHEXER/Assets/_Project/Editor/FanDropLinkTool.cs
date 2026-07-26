using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// Fan마다 SpawnDrop 링크를 여러 개 자동 배치한다. (설계 §4-⑤)
    ///  · 곧바로 아래가 아니라 여러 방향·깊이로 착지점을 분산(단차 섞기) → 빠른 스폰에도 안 겹침.
    ///  · 착지점은 아래로 레이캐스트해 실제 바닥(콜라이더)에 스냅.
    ///  · 만든 링크를 FanSpawn.dropLinks에 등록. 몹은 스폰 순번 % 개수 로 골라 탄다.
    /// </summary>
    public class FanDropLinkTool : EditorWindow
    {
        int linksPerFan = 4;
        float spreadRadius = 6f;     // 착지점 수평 분산 반경
        float minDropDown = 3f;      // 최소 낙차(레이 시작 아래)
        float maxRayDown = 60f;      // 바닥 탐색 최대 거리
        bool clearExisting = true;   // 재실행 시 기존 링크 제거

        const string GroupSuffix = "_DropLinks";

        [MenuItem("Tools/스폰/Fan 드롭 링크 자동배치")]
        static void Open() => GetWindow<FanDropLinkTool>("Fan 드롭 링크").minSize = new Vector2(320, 260);

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "FanSpawn이 붙은 Fan들을 선택하고 실행하십시오.\n" +
                "각 Fan 아래로 SpawnDrop 링크를 분산 생성해 여러 착지점을 만듭니다.", MessageType.Info);

            linksPerFan  = EditorGUILayout.IntSlider("Fan당 링크 수", linksPerFan, 1, 8);
            spreadRadius = EditorGUILayout.Slider("착지 분산 반경(m)", spreadRadius, 0f, 15f);
            minDropDown  = EditorGUILayout.Slider("최소 낙차(m)", minDropDown, 0.5f, 10f);
            maxRayDown   = EditorGUILayout.Slider("바닥 탐색 최대(m)", maxRayDown, 5f, 100f);
            clearExisting = EditorGUILayout.Toggle("기존 드롭 링크 지우고 새로", clearExisting);

            int sel = 0;
            foreach (var go in Selection.gameObjects) if (go.GetComponent<FanSpawn>() != null) sel++;
            EditorGUILayout.LabelField("FanSpawn 붙은 선택", sel + "개");

            using (new EditorGUI.DisabledScope(sel == 0))
            {
                if (GUILayout.Button("드롭 링크 자동배치", GUILayout.Height(26)))
                    Place();
                if (GUILayout.Button("선택 Fan의 드롭 링크 삭제", GUILayout.Height(22)))
                    Clear();
            }
        }

        void Clear()
        {
            int fans = 0, removed = 0;
            foreach (var go in Selection.gameObjects)
            {
                var fs = go.GetComponent<FanSpawn>();
                if (fs == null) continue;
                fans++;

                foreach (var l in new List<TraversalLink>(fs.dropLinks))
                    if (l != null) { Undo.DestroyObjectImmediate(l.gameObject); removed++; }
                fs.dropLinks.Clear();

                // 리스트에서 빠진 고아 링크도 그룹째 정리한다.
                var group = fs.transform.Find(fs.name + GroupSuffix);
                if (group != null) Undo.DestroyObjectImmediate(group.gameObject);

                EditorUtility.SetDirty(fs);
            }
            Debug.Log($"[Fan 드롭 링크] Fan {fans}개에서 링크 {removed}개 삭제(그룹 포함).");
        }

        void Place()
        {
            int fans = 0, links = 0, failed = 0;
            foreach (var go in Selection.gameObjects)
            {
                var fs = go.GetComponent<FanSpawn>();
                if (fs == null) continue;
                fans++;

                if (clearExisting)
                {
                    foreach (var l in new List<TraversalLink>(fs.dropLinks))
                        if (l != null) Undo.DestroyObjectImmediate(l.gameObject);
                    fs.dropLinks.Clear();
                }

                Transform group = EnsureGroup(fs.transform, fs.name + GroupSuffix);
                Vector3 mouth = fs.Mouth;   // FanSpawn이 이미 팬 아래로 잡아둠

                // 후보를 넉넉히 돌려 '유효한(막히지 않은)' 링크만 linksPerFan개까지 채운다.
                int made = 0, candidates = Mathf.Max(linksPerFan * 3, 12);
                for (int i = 0; i < candidates && made < linksPerFan; i++)
                {
                    float ang = (i / (float)candidates) * Mathf.PI * 2f;
                    float rad = (i == 0) ? 0.4f : spreadRadius * (0.4f + 0.6f * ((i % 3) / 2f));
                    Vector3 offXZ = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);

                    if (!Physics.Raycast(mouth + offXZ, Vector3.down, out RaycastHit hit, maxRayDown,
                                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        continue;

                    var lgo = new GameObject($"{fs.name}_Drop{made}");
                    Undo.RegisterCreatedObjectUndo(lgo, "드롭 링크 생성");
                    lgo.transform.SetParent(group, false);
                    lgo.transform.position = mouth;                 // A = Fan 입(높은 쪽)
                    var link = Undo.AddComponent<TraversalLink>(lgo);
                    link.kind = TraversalLinkKind.SpawnDrop;
                    link.endOffset = lgo.transform.InverseTransformPoint(hit.point);  // B = 착지점(로컬)
                    link.clearanceAuto = false;
                    link.clearance = 0.2f;                          // 낙하 — 상승 아치 없음
                    link.slotsAuto = true;

                    if (link.IsBlocked) { Undo.DestroyObjectImmediate(lgo); continue; }   // 벽에 낀 착지 버림
                    fs.dropLinks.Add(link);
                    links++; made++;
                }
                if (made < linksPerFan) failed += linksPerFan - made;
                EditorUtility.SetDirty(fs);
            }
            Debug.Log($"[Fan 드롭 링크] Fan {fans}개 → 링크 {links}개 배치" +
                      (failed > 0 ? $" (바닥 못 찾아 스킵 {failed}개 — 반경·낙차 조정)" : "") +
                      ". SpawnDrop 종류로 생성됨.");
        }

        static Transform EnsureGroup(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (t != null) return t;
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "드롭 링크 그룹");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            return go.transform;
        }
    }
}
