using UnityEditor;
using UnityEngine;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// 레일 세트 authoring 툴 — 축·1칸 길이를 씬에서 재고, 이동 범위를 숫자·핸들로 잡는다.
    /// 칸 눈금으로 플릭이 떨어지는 지점을, 양 끝 고스트로 "끝까지 갔을 때 레일 끝이 보이는지"를
    /// 눈으로 확인한다. (기초_설계안 §6.2 / 레일_세트_설계)
    ///
    /// ★ 기준은 <b>앵커</b>(지금 놓인 자리). 편집 중 세트를 옮기면 앵커도 같이 옮겨진다.
    ///   그래서 "세트를 칸에 정렬" 같은 버튼은 의미가 없어 두지 않는다(항상 offset=0에서 시작).
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

            if (GUILayout.Button("레일 1칸 길이 측정")) MeasureRailLength(rs);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                _cells = EditorGUILayout.IntField("칸 수 ±", _cells);
                if (GUILayout.Button("범위를 ±N칸으로"))
                {
                    Undo.RecordObject(rs, "Rail range by steps");
                    rs.rangeMax = _cells * rs.stepTarget;
                    rs.rangeMin = -rs.rangeMax;
                    EditorUtility.SetDirty(rs);
                }
            }

            float span = rs.rangeMax - rs.rangeMin;
            EditorGUILayout.HelpBox(
                $"이동 폭 {span:0.###}   (앵커 기준 {rs.rangeMin:0.###} ~ {rs.rangeMax:0.###})\n" +
                $"눈금 {rs.Grid.Count}개 — 플릭 1회 = 눈금 한 칸. 홀드는 연속 크립.\n" +
                DescribeSteps(rs),
                MessageType.Info);

            WarnShortTail(rs);

            // 레일이 짧으면 끝까지 갔을 때 레일 끝이 시야에 드러난다.
            if (TryRailLengthAlongAxis(rs, out float railTotal))
            {
                if (railTotal < span - 1e-3f)
                    EditorGUILayout.HelpBox(
                        $"레일 총 길이 {railTotal:0.###} < 이동 폭 {span:0.###} — 끝까지 가면 레일 끝이 드러납니다.\n" +
                        "레일 칸을 더 붙이거나 범위를 줄이십시오. (보이는 구간까지 감안하면 더 길어야 합니다)",
                        MessageType.Warning);
                else
                    EditorGUILayout.LabelField($"레일 총 길이 {railTotal:0.###} (여유 {railTotal - span:0.###})");
            }

            if (rs.railRoot == null || rs.riderRoot == null)
                EditorGUILayout.HelpBox("railRoot / riderRoot 가 비어 있습니다. 위 버튼으로 찾거나 직접 물리세요.", MessageType.Warning);

            if (rs.railRoot != null && rs.railRoot.GetComponentInChildren<Collider>() != null)
                EditorGUILayout.HelpBox(
                    "레일에 콜라이더가 있습니다. 세트가 통째로 미끄러지므로 레일 콜라이더는 벽을 뚫고 지나갑니다.\n" +
                    "레일은 렌더러만 두고, 콜라이더는 라이더(벽·발판)에만 두십시오.",
                    MessageType.Warning);
        }

        /// <summary>실제로 만들어진 칸 크기를 알려 준다. 가운데는 항상 목표 간격이고 양 끝만 다르다.</summary>
        static string DescribeSteps(RailSet rs)
        {
            var g = rs.Grid;
            if (g.Count < 2) return "눈금이 없습니다 — 이동 범위가 0입니다.";
            return $"목표 간격 {rs.stepTarget:0.###} · 양 끝 칸 " +
                   $"{g[1] - g[0]:0.###} / {g[g.Count - 1] - g[g.Count - 2]:0.###}";
        }

        /// <summary>끝 자투리가 너무 짧으면 그 한 번의 플릭이 '안 움직인 것처럼' 보인다.</summary>
        static void WarnShortTail(RailSet rs)
        {
            var g = rs.Grid;
            if (g.Count < 2) return;

            float t = Mathf.Max(1e-4f, rs.stepTarget);
            float worst = Mathf.Min(g[1] - g[0], g[g.Count - 1] - g[g.Count - 2]);
            if (worst >= t * 0.5f) return;

            EditorGUILayout.HelpBox(
                $"끝 칸이 {worst:0.###}로 목표 간격 {t:0.###}의 절반보다 짧습니다 — 그 한 번의 플릭은 " +
                "거의 안 움직인 것처럼 보입니다.\n" +
                "tailMergeRatio를 올려 직전 칸에 합치거나, 범위 끝을 목표 간격의 배수에 가깝게 끄십시오.",
                MessageType.Warning);
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

        /// <summary>레일 배치가 가장 길게 뻗은 로컬 축을 트랙 방향으로 잡는다.</summary>
        static void DeriveAxis(RailSet rs)
        {
            if (!TryLocalBounds(rs, out Bounds b)) return;
            Undo.RecordObject(rs, "Derive rail axis");
            Vector3 s = b.size;
            rs.axis = (s.x >= s.y && s.x >= s.z) ? Vector3.right
                    : (s.y >= s.z) ? Vector3.up : Vector3.forward;
            EditorUtility.SetDirty(rs);
        }

        /// <summary>
        /// 기준 레일의 월드 크기를 축에 투영해 1칸 길이를 잰다.
        /// ★ 세트가 <b>부모 공간</b>에서 움직이므로 부모 스케일로 환산한다(자기 스케일이 아니다).
        /// </summary>
        static void MeasureRailLength(RailSet rs)
        {
            Transform r = rs.referenceRail;
            if (r == null && rs.railRoot != null && rs.railRoot.childCount > 0) r = rs.railRoot.GetChild(0);
            if (r == null) { Debug.LogWarning("[RailSet] 기준 레일이 없습니다."); return; }

            if (!TryWorldExtentAlongAxis(rs, r, out float worldLen))
            { Debug.LogWarning("[RailSet] 기준 레일에 Renderer가 없습니다."); return; }

            Undo.RecordObject(rs, "Measure rail length");
            rs.stepTarget = worldLen / rs.ParentScaleAlongAxis;
            EditorUtility.SetDirty(rs);
            Debug.Log($"[RailSet] 목표 간격을 레일 1칸에 맞춤 = {rs.stepTarget:0.###} (월드 {worldLen:0.###})");
        }

        /// <summary>레일 전체가 축 방향으로 차지하는 길이(부모 공간 단위).</summary>
        static bool TryRailLengthAlongAxis(RailSet rs, out float len)
        {
            len = 0f;
            if (rs.railRoot == null) return false;
            if (!TryWorldExtentAlongAxis(rs, rs.railRoot, out float worldLen)) return false;
            len = worldLen / rs.ParentScaleAlongAxis;
            return true;
        }

        static bool TryWorldExtentAlongAxis(RailSet rs, Transform root, out float worldLen)
        {
            worldLen = 0f;
            var rends = root.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return false;

            Bounds wb = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) wb.Encapsulate(rends[i].bounds);

            Vector3 a = rs.transform.TransformDirection(rs.AxisLocal).normalized;
            worldLen = Mathf.Abs(wb.size.x * a.x) + Mathf.Abs(wb.size.y * a.y) + Mathf.Abs(wb.size.z * a.z);
            return true;
        }

        /// <summary>레일 바운즈를 RailSet 로컬 공간에서 계산(축 산출용).</summary>
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

        // ── 씬 기즈모·핸들 ────────────────────────────────────────────────

        void OnSceneGUI()
        {
            var rs = (RailSet)target;
            Transform tr = rs.transform;

            Vector3 anchor = tr.parent != null ? tr.parent.TransformPoint(rs.AnchorLocal) : rs.AnchorLocal;
            Vector3 ax = tr.TransformDirection(rs.AxisLocal).normalized;
            float scale = rs.ParentScaleAlongAxis;

            Vector3 a = anchor + ax * (rs.rangeMin * scale);
            Vector3 b = anchor + ax * (rs.rangeMax * scale);

            Handles.color = Color.cyan;
            Handles.DrawLine(a, b, 3f);

            Handles.color = Color.yellow;
            float hs = HandleUtility.GetHandleSize(anchor) * 0.12f;
            Handles.SphereHandleCap(0, anchor, Quaternion.identity, hs, EventType.Repaint);
            Handles.Label(anchor + Vector3.up * hs * 2f, "앵커");

            // 눈금 — 플릭이 떨어지는 지점. 범위 양 끝(자투리 칸)도 눈금이라 같이 찍는다.
            // 예전엔 Ceil/Floor로 배수만 찍어 끝 지점이 빠졌는데, 플릭은 끝까지 갈 수 있어
            // 보이는 것과 실제 동작이 어긋났다.
            var grid = rs.Grid;
            for (int i = 0; i < grid.Count; i++)
            {
                Vector3 p = anchor + ax * (grid[i] * scale);
                bool end = i == 0 || i == grid.Count - 1;
                Handles.color = end ? new Color(1f, 0.6f, 0.2f, 0.9f) : new Color(1f, 1f, 1f, 0.6f);
                Handles.SphereHandleCap(0, p, Quaternion.identity,
                                        HandleUtility.GetHandleSize(p) * (end ? 0.07f : 0.05f), EventType.Repaint);
            }

            // 양 끝 고스트 — 끝까지 갔을 때 레일 끝이 드러나는지 눈으로 확인
            DrawGhost(rs, ax * (rs.rangeMin * scale));
            DrawGhost(rs, ax * (rs.rangeMax * scale));

            // 범위 양 끝 드래그
            EditorGUI.BeginChangeCheck();
            Vector3 na = Handles.Slider(a, ax, HandleUtility.GetHandleSize(a) * 0.15f, Handles.ConeHandleCap, 0f);
            Vector3 nb = Handles.Slider(b, ax, HandleUtility.GetHandleSize(b) * 0.15f, Handles.ConeHandleCap, 0f);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(rs, "Adjust rail range");
                rs.rangeMin = Mathf.Min(0f, Vector3.Dot(na - anchor, ax) / scale);
                rs.rangeMax = Mathf.Max(0f, Vector3.Dot(nb - anchor, ax) / scale);
                EditorUtility.SetDirty(rs);
            }
        }

        /// <summary>세트가 offset만큼 갔을 때의 레일 위치를 반투명 와이어로.</summary>
        static void DrawGhost(RailSet rs, Vector3 worldOffset)
        {
            if (rs.railRoot == null) return;
            var rends = rs.railRoot.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) return;

            Bounds wb = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) wb.Encapsulate(rends[i].bounds);

            Handles.color = new Color(0.2f, 1f, 0.6f, 0.35f);
            Handles.DrawWireCube(wb.center + worldOffset, wb.size);
        }

        // ── 생성 메뉴 ─────────────────────────────────────────────────────

        [MenuItem("GameObject/MINDHEXER/레일 세트 (그레이박스)", false, 10)]
        static void CreateRailSet(MenuCommand cmd)
        {
            const float cellLen = 2f;      // 1칸 길이
            const int cellCount = 5;       // 총 10 길이
            const float gauge = 0.6f;      // 두 줄 간격
            const float thick = 0.1f;

            var root = new GameObject("RailSet");
            GameObjectUtility.SetParentAndAlign(root, cmd.context as GameObject);

            var rails = new GameObject("Rails");
            rails.transform.SetParent(root.transform, false);
            var riders = new GameObject("Riders");
            riders.transform.SetParent(root.transform, false);

            // 레일 칸들 — 렌더러만(콜라이더 없음). 축 = +X, 폭 방향 = Z.
            float start = -(cellCount - 1) * 0.5f * cellLen;
            for (int i = 0; i < cellCount; i++)
            {
                var cell = new GameObject($"Rail_{i}");
                cell.transform.SetParent(rails.transform, false);
                cell.transform.localPosition = new Vector3(start + i * cellLen, 0f, 0f);

                for (int s = -1; s <= 1; s += 2)
                {
                    var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    bar.name = s < 0 ? "Bar_L" : "Bar_R";
                    var col = bar.GetComponent<Collider>();
                    if (col != null) Object.DestroyImmediate(col);   // 규칙: 레일은 콜라이더 없음
                    bar.transform.SetParent(cell.transform, false);
                    bar.transform.localPosition = new Vector3(0f, 0f, s * gauge * 0.5f);
                    bar.transform.localScale = new Vector3(cellLen, thick, thick);
                }
            }

            var rs = root.AddComponent<RailSet>();
            rs.railRoot = rails.transform;
            rs.riderRoot = riders.transform;
            rs.referenceRail = rails.transform.GetChild(0);
            rs.axis = Vector3.right;
            rs.stepTarget = cellLen;
            rs.rangeMax = 2f * cellLen;
            rs.rangeMin = -rs.rangeMax;

            // 조준용 콜라이더 — 이동을 막지 않게 트리거. Hackable 레이어가 있으면 거기로.
            var gaze = root.AddComponent<BoxCollider>();
            gaze.isTrigger = true;
            gaze.center = Vector3.zero;
            gaze.size = new Vector3(cellCount * cellLen, 0.6f, gauge + thick * 2f);
            int layer = LayerMask.NameToLayer("Hackable");
            if (layer >= 0) root.layer = layer;

            var h = root.AddComponent<Hackable>();
            h.kind = HackableKind.RailCarrier;
            h.controlType = ControlType.ExternalControl;
            h.gazeCollider = gaze;
            h.glowRenderers = rails.GetComponentsInChildren<Renderer>();   // 하이라이트는 레일 몸통만

            root.AddComponent<ControlAxisGizmo>();

            Undo.RegisterCreatedObjectUndo(root, "Create Rail Set");
            Selection.activeGameObject = root;
        }
    }
}
