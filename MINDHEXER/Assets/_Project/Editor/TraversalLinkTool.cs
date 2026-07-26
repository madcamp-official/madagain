using UnityEditor;
using UnityEngine;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// 층이동 링크 배치 도구. 맵 부속(MapPartsTool)과 같은 방식 — 씬 카메라 앞에 생성 + Undo + 즉시 선택.
    /// 종류 3종을 메뉴에서 바로 고를 수 있게 해, 저작자가 규칙을 외울 필요 없이 이름만 보고 놓게 한다.
    /// </summary>
    public static class TraversalLinkTool
    {
        [MenuItem("Tools/층이동 링크/① 일반 (하강·상승 모두 전 지상몹)", false, 0)]
        static void CreateNormal() => Create(TraversalLinkKind.Normal, "TraversalLink_일반");

        [MenuItem("Tools/층이동 링크/② 상승제한 (상승은 Traversal만)", false, 1)]
        static void CreateAscendRestricted() => Create(TraversalLinkKind.AscendRestricted, "TraversalLink_상승제한");

        [MenuItem("Tools/층이동 링크/③ 하강전용 (아무도 못 올라옴)", false, 2)]
        static void CreateDescendOnly() => Create(TraversalLinkKind.DescendOnly, "TraversalLink_하강전용");

        [MenuItem("Tools/층이동 링크/선택 링크 끝점 바닥에 스냅", false, 20)]
        static void SnapSelected()
        {
            int n = 0;
            foreach (var go in Selection.gameObjects)
            {
                var link = go.GetComponent<TraversalLink>();
                if (link == null) continue;
                Undo.RecordObject(link, "끝점 바닥 스냅");
                Undo.RecordObject(link.transform, "끝점 바닥 스냅");
                Vector3 a = SnapDown(link.PointA);
                Vector3 b = SnapDown(link.PointB);
                link.transform.position = a;
                link.endOffset = link.transform.InverseTransformPoint(b);
                EditorUtility.SetDirty(link);
                n++;
            }
            Debug.Log($"[층이동 링크] 끝점 바닥 스냅: {n}개");
        }

        [MenuItem("Tools/층이동 링크/NavMesh Area 생성 (Leap · LeapTraversal)", false, 30)]
        static void EnsureAreas()
        {
            // 몹별 게이팅은 NavMesh Area로 이뤄진다. Area가 없으면 코드가 0(Walkable)으로 폴백해
            // "상승제한 링크인데 아무나 다 지나가는" 상태가 된다.
            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset");
            if (asset == null || asset.Length == 0)
            { Debug.LogError("[층이동] NavMeshAreas.asset을 찾지 못했습니다."); return; }

            var so = new SerializedObject(asset[0]);
            var areas = so.FindProperty("areas");
            if (areas == null) { Debug.LogError("[층이동] areas 속성을 찾지 못했습니다."); return; }

            int made = 0;
            foreach (string want in new[] { "Leap", "LeapTraversal" })
            {
                bool exists = false;
                for (int i = 0; i < areas.arraySize; i++)
                    if (areas.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == want)
                    { exists = true; break; }
                if (exists) continue;

                // 3번부터가 커스텀 슬롯(0~2는 Walkable/NotWalkable/Jump 예약)
                for (int i = 3; i < areas.arraySize; i++)
                {
                    var el = areas.GetArrayElementAtIndex(i);
                    var nameProp = el.FindPropertyRelative("name");
                    if (!string.IsNullOrEmpty(nameProp.stringValue)) continue;
                    nameProp.stringValue = want;
                    el.FindPropertyRelative("cost").floatValue = 1f;
                    made++;
                    break;
                }
            }
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();

            var names = UnityEngine.AI.NavMesh.GetAreaNames();
            Debug.Log($"[층이동] NavMesh Area 정리 완료(신규 {made}개). 현재 Area: {string.Join(", ", names)}\n" +
                      "  이제 상승제한 링크가 Traversal 특성 몹에게만 열립니다. 링크를 다시 Bake(Play 재시작)하십시오.");
        }

        [MenuItem("Tools/층이동 링크/NavMesh 시각화 켜기·끄기", false, 40)]
        static void ToggleNavMeshView()
        {
            var view = Object.FindFirstObjectByType<NavMeshDebugView>();
            if (view != null)
            {
                Undo.DestroyObjectImmediate(view.gameObject);
                Debug.Log("[층이동] NavMesh 시각화 껐습니다.");
                return;
            }
            var go = new GameObject("[NavMeshDebugView]");
            go.AddComponent<NavMeshDebugView>();
            Undo.RegisterCreatedObjectUndo(go, "NavMesh 시각화 켜기");
            Selection.activeGameObject = go;
            Debug.Log("[층이동] NavMesh 시각화 켰습니다. Scene 뷰 Gizmos가 켜져 있어야 보입니다.\n" +
                      "  파랑=Walkable, 노랑=Jump, 그 외 색=커스텀 Area(Leap 등).\n" +
                      "  마커 끝점: 초록=면 위 / 빨강=면 밖(주황 선이 가장 가까운 면까지의 거리).");
        }

        [MenuItem("Tools/층이동 링크/씬의 링크 요약 출력", false, 21)]
        static void Summary()
        {
            var links = Object.FindObjectsByType<TraversalLink>(FindObjectsSortMode.None);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[층이동 링크] 씬에 {links.Length}개");
            foreach (var l in links)
                sb.AppendLine($"  {l.name}: {l.kind} 길이={l.Length:0.00}m " +
                              $"clearance={l.EffectiveClearance:0.00} 주저={l.PauseTicks}틱 멈칫={l.RecoverTicks}틱 " +
                              $"비행={l.DescendArc.flightTicks}틱(하강)");
            Debug.Log(sb.ToString());
        }

        // ── 생성 ──
        static void Create(TraversalLinkKind kind, string name)
        {
            var go = new GameObject(name);
            var link = go.AddComponent<TraversalLink>();
            link.kind = kind;

            Vector3 origin = SpawnPos();
            // 기본 형태: 앞으로 6m, 아래로 3m 내려가는 하강 링크. 바닥이 있으면 스냅.
            go.transform.position = SnapDown(origin);
            Vector3 endWorld = SnapDown(origin + new Vector3(0f, -3f, 6f));
            link.endOffset = go.transform.InverseTransformPoint(endWorld);

            Undo.RegisterCreatedObjectUndo(go, $"{name} 생성");
            Selection.activeGameObject = go;
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        /// <summary>씬 뷰 카메라가 보는 지점(맵 부속과 동일 관례). 씬 뷰가 없으면 원점.</summary>
        static Vector3 SpawnPos()
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null || sv.camera == null) return Vector3.zero;
            return sv.pivot;
        }

        /// <summary>위에서 아래로 레이를 쏴 바닥에 붙인다. 못 맞으면 원래 위치.</summary>
        static Vector3 SnapDown(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 5f, Vector3.down, out var hit, 50f))
                return hit.point;
            return p;
        }
    }
}
