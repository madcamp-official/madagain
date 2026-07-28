using UnityEditor;
using UnityEngine;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// 레일 세트 authoring 툴 — 축·레일 길이·중앙을 씬에서 재서 채우고, 이동 범위를 숫자와 핸들
    /// 양쪽으로 조정한다. 칸 눈금을 그려 플릭이 어디에 떨어지는지 눈으로 확인할 수 있다. (§6.2)
    /// </summary>
    [CustomEditor(typeof(RailSet))]
    public class RailSetEditor : Editor
    {
        int _cells = 2;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var rs = (RailSet)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("툴", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("자식에서 Rails/Riders 찾기")) FindRoots(rs);
                if (GUILayout.Button("축 자동 산출")) DeriveAxis(rs);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("레일 길이 측정")) MeasureRailLength(rs);
                if (GUILayout.Button("중앙 재계산")) RecenterFromRails(rs);
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                _cells = EditorGUILayout.IntField("칸 수 ±", _cells);
                if (GUILayout.Button("범위를 ±N칸으로"))
                {
                    Undo.RecordObject(rs, "Rail range by cells");
                    rs.rangeMax = _cells * rs.railLength;
                    rs.rangeMin = -rs.rangeMax;
                    EditorUtility.SetDirty(rs);
                }
            }

            float span = rs.rangeMax - rs.rangeMin;
            float cells = rs.railLength > 0.0001f ? span / rs.railLength : 0f;
            EditorGUILayout.HelpBox(
                $"범위 폭 {span:0.###} (레일 {cells:0.##}칸)\n" +
                $"중앙 기준 {rs.rangeMin:0.###} ~ {rs.rangeMax:0.###}\n" +
                "플릭 1회 = 레일 1칸. 홀드는 연속 이동.",
                MessageType.Info);

            if (GUILayout.Button("라이더를 중앙으로")) MoveRider(rs, 0f);
            if (GUILayout.Button("라이더를 가장 가까운 칸에 정렬")) SnapRider(rs);

            if (rs.railRoot == null || rs.riderRoot == null)
                EditorGUILayout.HelpBox("railRoot / riderRoot 가 비어 있습니다. 위 버튼으로 찾거나 직접 물리세요.", MessageType.Warning);
        }

        // ── 툴 동작 ───────────────────────────────────────────────────────

        static void FindRoots(RailSet rs)
        {
            Undo.RecordObject(rs, "Find rail roots");
            foreach (Transform t in rs.transform)
            {
                if (rs.railRoot == null && t.name == "Rails") rs.railRoot = t;
                if (rs.riderRoot == null && t.name == "Riders") rs.riderRoot = t;
            }
            if (rs.referenceRail == null && rs.railRoot != null && rs.railRoot.childCount > 0)
                rs.referenceRail = rs.railRoot.GetChild(0);
            EditorUtility.SetDirty(rs);
        }

        /// <summary>레일들의 배치가 가장 길게 뻗은 로컬 축을 트랙 방향으로 잡는다.</summary>
        static void DeriveAxis(RailSet rs)
        {
            if (!TryLocalBounds(rs, out Bounds b)) return;
            Undo.RecordObject(rs, "Derive rail axis");
            Vector3 s = b.size;
            rs.axis = (s.x >= s.y && s.x >= s.z) ? Vector3.right
                    : (s.y >= s.z) ? Vector3.up : Vector3.forward;
            EditorUtility.SetDirty(rs);
        }

        /// <summary>기준 레일의 렌더러 크기를 축에 투영해 1칸 길이를 잰다(사용자가 스케일로 조정한 값 반영).</summary>
        static void MeasureRailLength(RailSet rs)
        {
            Transform r = rs.referenceRail;
            if (r == null && rs.railRoot != null && rs.railRoot.childCount > 0) r = rs.railRoot.GetChild(0);
            if (r == null) { Debug.LogWarning("[RailSet] 기준 레일이 없습니다."); return; }

            var rends = r.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) { Debug.LogWarning("[RailSet] 기준 레일에 Renderer가 없습니다."); return; }

            Bounds wb = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) wb.Encapsulate(rends[i].bounds);

            Vector3 axisW = rs.transform.TransformDirection(rs.AxisLocal).normalized;
            float worldLen = Mathf.Abs(Vector3.Dot(wb.size, new Vector3(Mathf.Abs(axisW.x), Mathf.Abs(axisW.y), Mathf.Abs(axisW.z))));
            float scale = Mathf.Max(0.0001f, Vector3.Scale(rs.transform.lossyScale, rs.AxisLocal).magnitude);

            Undo.RecordObject(rs, "Measure rail length");
            rs.railLength = worldLen / scale;
            EditorUtility.SetDirty(rs);
            Debug.Log($"[RailSet] 레일 1칸 = {rs.railLength:0.###} (월드 {worldLen:0.###})");
        }

        static void RecenterFromRails(RailSet rs)
        {
            if (!TryLocalBounds(rs, out Bounds b)) return;
            Undo.RecordObject(rs, "Recenter rail set");
            rs.center = b.center;
            EditorUtility.SetDirty(rs);
        }

        /// <summary>레일들의 바운즈를 RailSet 로컬 공간에서 계산.</summary>
        static bool TryLocalBounds(RailSet rs, out Bounds b)
        {
            b = default;
            if (rs.railRoot == null) { Debug.LogWarning("[RailSet] railRoot가 비어 있습니다."); return false; }

            var rends = rs.railRoot.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) { Debug.LogWarning("[RailSet] 레일에 Renderer가 없습니다."); return false; }

            bool first = true;
            foreach (var r in rends)
            {
                Vector3 c = rs.transform.InverseTransformPoint(r.bounds.center);
                Vector3 e = rs.transform.InverseTransformVector(r.bounds.extents);
                e = new Vector3(Mathf.Abs(e.x), Mathf.Abs(e.y), Mathf.Abs(e.z));
                var local = new Bounds(c, e * 2f);
                if (first) { b = local; first = false; }
                else b.Encapsulate(local);
            }
            return true;
        }

        static void MoveRider(RailSet rs, float offset)
        {
            if (rs.riderRoot == null) return;
            Undo.RecordObject(rs.riderRoot, "Move rider");
            Vector3 perp = rs.riderRoot.localPosition - rs.center;
            perp -= rs.AxisLocal * Vector3.Dot(perp, rs.AxisLocal);   // 축 성분만 교체, 나머지는 보존
            rs.riderRoot.localPosition = rs.center + perp + rs.AxisLocal * offset;
            EditorUtility.SetDirty(rs.riderRoot);
        }

        static void SnapRider(RailSet rs)
        {
            if (rs.riderRoot == null) return;
            float cur = Vector3.Dot(rs.riderRoot.localPosition - rs.center, rs.AxisLocal);
            MoveRider(rs, rs.NearestCell(cur));
        }

        // ── 씬 기즈모·핸들 ────────────────────────────────────────────────

        void OnSceneGUI()
        {
            var rs = (RailSet)target;
            Transform tr = rs.transform;

            Vector3 c = tr.TransformPoint(rs.center);
            Vector3 ax = tr.TransformDirection(rs.AxisLocal).normalized;
            float scale = Mathf.Max(0.0001f, Vector3.Scale(tr.lossyScale, rs.AxisLocal).magnitude);

            Vector3 a = c + ax * (rs.rangeMin * scale);
            Vector3 b = c + ax * (rs.rangeMax * scale);

            // 트랙 범위
            Handles.color = Color.cyan;
            Handles.DrawLine(a, b, 3f);

            // 중앙 마커
            Handles.color = Color.yellow;
            float hs = HandleUtility.GetHandleSize(c) * 0.12f;
            Handles.SphereHandleCap(0, c, Quaternion.identity, hs, EventType.Repaint);
            Handles.Label(c + Vector3.up * hs * 2f, "중앙");

            // 칸 눈금 — 플릭이 떨어지는 지점
            if (rs.railLength > 0.0001f)
            {
                Handles.color = new Color(1f, 1f, 1f, 0.6f);
                int lo = Mathf.CeilToInt(rs.rangeMin / rs.railLength);
                int hi = Mathf.FloorToInt(rs.rangeMax / rs.railLength);
                for (int i = lo; i <= hi; i++)
                {
                    Vector3 p = c + ax * (i * rs.railLength * scale);
                    Handles.SphereHandleCap(0, p, Quaternion.identity, HandleUtility.GetHandleSize(p) * 0.05f, EventType.Repaint);
                }
            }

            // 범위 양 끝을 드래그로 조정
            EditorGUI.BeginChangeCheck();
            Vector3 na = Handles.Slider(a, ax, HandleUtility.GetHandleSize(a) * 0.15f, Handles.ConeHandleCap, 0f);
            Vector3 nb = Handles.Slider(b, ax, HandleUtility.GetHandleSize(b) * 0.15f, Handles.ConeHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(rs, "Adjust rail range");
                rs.rangeMin = Vector3.Dot(na - c, ax) / scale;
                rs.rangeMax = Vector3.Dot(nb - c, ax) / scale;
                if (rs.rangeMin > 0f) rs.rangeMin = 0f;
                if (rs.rangeMax < 0f) rs.rangeMax = 0f;
                EditorUtility.SetDirty(rs);
            }
        }

        // ── 생성 메뉴 ─────────────────────────────────────────────────────

        [MenuItem("GameObject/MINDHEXER/레일 세트", false, 10)]
        static void CreateRailSet(MenuCommand cmd)
        {
            var root = new GameObject("RailSet");
            GameObjectUtility.SetParentAndAlign(root, cmd.context as GameObject);

            var rails = new GameObject("Rails");
            rails.transform.SetParent(root.transform, false);
            var riders = new GameObject("Riders");
            riders.transform.SetParent(root.transform, false);

            var rs = root.AddComponent<RailSet>();
            rs.railRoot = rails.transform;
            rs.riderRoot = riders.transform;

            var h = root.AddComponent<Hackable>();
            h.kind = HackableKind.RailCarrier;
            h.controlType = ControlType.ExternalControl;

            root.AddComponent<ControlAxisGizmo>();

            Undo.RegisterCreatedObjectUndo(root, "Create Rail Set");
            Selection.activeGameObject = root;
        }
    }
}
