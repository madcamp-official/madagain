using UnityEngine;
using UnityEditor;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// GuardPatrol 커스텀 인스펙터 — 웨이포인트를 N개 한 번에 만들고, 씬 뷰에서
    /// 각 점을 손잡이(Handle)로 직접 드래그해 옮길 수 있게 한다. 점들은 항상 순서대로
    /// 노란 선으로 이어 그린다(GuardPatrol.OnDrawGizmos가 이미 그리는 것과 별개로,
    /// 씬 뷰 상시 표시 + 손잡이는 여기서 담당).
    ///
    /// 웨이포인트는 경비병의 <b>자식이 아니라 형제</b>로 만든다 — 자식이면 경비병이 회전할 때
    /// 목표 지점도 같이 돌아버린다. 이름은 "GuardPatrolPath (경비병 이름)"인 빈 오브젝트 밑에
    /// 모아 하이어라키를 안 어지럽힌다.
    /// </summary>
    [CustomEditor(typeof(GuardPatrol))]
    public class GuardPatrolEditor : Editor
    {
        int _addCount = 4;
        float _spacing = 2.5f;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var patrol = (GuardPatrol)target;

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("웨이포인트 도구", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            _addCount = Mathf.Max(1, EditorGUILayout.IntField("추가할 개수", _addCount));
            _spacing = Mathf.Max(0.1f, EditorGUILayout.FloatField("간격(m)", _spacing));
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button($"현재 경로 끝에 {_addCount}개 추가 (경비병이 보는 방향으로)"))
                AddWaypoints(patrol, _addCount, _spacing);

            using (new EditorGUI.DisabledScope(patrol.waypoints == null || patrol.waypoints.Length == 0))
            {
                if (GUILayout.Button("웨이포인트 전부 지우기(오브젝트까지 삭제)"))
                    ClearWaypoints(patrol);
            }

            EditorGUILayout.HelpBox(
                "씬 뷰의 노란 점을 드래그해 위치를 옮기세요. 선은 순서대로 자동으로 이어집니다.",
                MessageType.Info);
        }

        static void AddWaypoints(GuardPatrol patrol, int count, float spacing)
        {
            var existing = patrol.waypoints;
            int startIndex = existing != null ? existing.Length : 0;

            Vector3 basePos = startIndex > 0 && existing[startIndex - 1] != null
                ? existing[startIndex - 1].position
                : patrol.transform.position;
            Vector3 dir = startIndex > 0 && existing.Length >= 2 && existing[startIndex - 1] != null && existing[startIndex - 2] != null
                ? (existing[startIndex - 1].position - existing[startIndex - 2].position).normalized
                : patrol.transform.forward;
            if (dir.sqrMagnitude < 1e-4f) dir = patrol.transform.forward;
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward; else dir.Normalize();

            Transform holder = FindOrCreateHolder(patrol);

            var list = new System.Collections.Generic.List<Transform>(existing ?? System.Array.Empty<Transform>());
            for (int i = 1; i <= count; i++)
            {
                var go = new GameObject($"WP_{startIndex + i}");
                Undo.RegisterCreatedObjectUndo(go, "웨이포인트 추가");
                go.transform.SetParent(holder, true);   // 계층 정리용일 뿐 — 월드 좌표는 그대로 유지(worldPositionStays=true)
                go.transform.position = basePos + dir * spacing * i;
                list.Add(go.transform);
            }

            Undo.RecordObject(patrol, "웨이포인트 배열 갱신");
            patrol.waypoints = list.ToArray();
            EditorUtility.SetDirty(patrol);
            EditorSceneManager_MarkDirty(patrol);
        }

        static void ClearWaypoints(GuardPatrol patrol)
        {
            if (patrol.waypoints == null) return;
            if (!EditorUtility.DisplayDialog("웨이포인트 삭제",
                $"웨이포인트 오브젝트 {patrol.waypoints.Length}개를 씬에서 완전히 삭제합니다.", "삭제", "취소"))
                return;

            foreach (var t in patrol.waypoints)
                if (t != null) Undo.DestroyObjectImmediate(t.gameObject);

            Undo.RecordObject(patrol, "웨이포인트 배열 비움");
            patrol.waypoints = System.Array.Empty<Transform>();
            EditorUtility.SetDirty(patrol);
            EditorSceneManager_MarkDirty(patrol);
        }

        static Transform FindOrCreateHolder(GuardPatrol patrol)
        {
            string name = $"[Path] {patrol.gameObject.name}";
            var existing = GameObject.Find(name);
            if (existing != null) return existing.transform;

            var holder = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(holder, "경로 폴더 생성");
            return holder.transform;
        }

        static void EditorSceneManager_MarkDirty(Component c)
            => UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(c.gameObject.scene);

        // ── 씬 뷰 손잡이 ──────────────────────────────────────────────────

        void OnSceneGUI()
        {
            var patrol = (GuardPatrol)target;
            if (patrol.waypoints == null) return;

            for (int i = 0; i < patrol.waypoints.Length; i++)
            {
                var t = patrol.waypoints[i];
                if (t == null) continue;

                EditorGUI.BeginChangeCheck();
                Vector3 newPos = Handles.PositionHandle(t.position, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(t, "웨이포인트 이동");
                    t.position = newPos;
                }

                Handles.Label(t.position + Vector3.up * 0.2f, $"{i}: {t.name}");
            }

            Handles.color = Color.yellow;
            for (int i = 1; i < patrol.waypoints.Length; i++)
            {
                if (patrol.waypoints[i] == null || patrol.waypoints[i - 1] == null) continue;
                Handles.DrawLine(patrol.waypoints[i - 1].position, patrol.waypoints[i].position);
            }
        }
    }
}
