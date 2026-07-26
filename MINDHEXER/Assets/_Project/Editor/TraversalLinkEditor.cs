using UnityEditor;
using UnityEngine;
using Game.Sim;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// 층이동 링크 에디터. 끝점을 Scene 뷰에서 직접 끌고, 정점(apex)을 드래그해 궤적을 눈으로 맞춘다.
    /// 자동이 기본 — 손대면 수동으로 전환되고, "자동으로 되돌리기"로 언제든 복귀한다.
    /// </summary>
    [CustomEditor(typeof(TraversalLink))]
    [CanEditMultipleObjects]
    public class TraversalLinkEditor : Editor
    {
        // ── 고스트 재생 상태 ──
        bool   playing;
        bool   playAscend;          // false = 하강 재생, true = 상승 재생
        double playStartTime;

        void OnEnable()  { EditorApplication.update += OnEditorUpdate; }
        void OnDisable() { EditorApplication.update -= OnEditorUpdate; playing = false; }

        void OnEditorUpdate()
        {
            if (!playing) return;
            SceneView.RepaintAll();
        }

        public override void OnInspectorGUI()
        {
            var link = (TraversalLink)target;

            DrawDefaultInspector();

            // ── 충돌 검증 상태 ──
            EditorGUILayout.Space(6);
            if (link.IsBlocked)
                EditorGUILayout.HelpBox(
                    "무효: 최소 높이로도 궤적이 구조물을 뚫습니다. 위치를 옮기거나 종류를 바꾸십시오.\n" +
                    "이 마커는 Bake에서 제외됩니다.", MessageType.Error);
            else if (link.EffectiveClearance < link.DesiredClearance - 0.01f)
                EditorGUILayout.HelpBox(
                    $"천장에 걸려 clearance를 자동으로 낮췄습니다: " +
                    $"{link.DesiredClearance:0.00} → {link.EffectiveClearance:0.00} m", MessageType.Warning);
            else
                EditorGUILayout.HelpBox("궤적 안전 — 구조물에 걸리지 않습니다.", MessageType.Info);

            // ── 고스트 재생 ──
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("고스트 재생 (Play 없이 타이밍 확인)", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(playing ? "■ 정지" : "▶ 재생"))
                {
                    playing = !playing;
                    playStartTime = EditorApplication.timeSinceStartup;
                }
                using (new EditorGUI.DisabledScope(!link.AscendAllowed))
                    playAscend = GUILayout.Toggle(playAscend, "상승 재생", "Button");
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("계산 결과", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.FloatField("직선 길이(m)", link.Length);
                EditorGUILayout.FloatField("적용 clearance(m)", link.EffectiveClearance);
                EditorGUILayout.IntField("주저(틱)", link.PauseTicks);
                EditorGUILayout.IntField("멈칫(틱)", link.RecoverTicks);
                var d = link.DescendArc;
                EditorGUILayout.IntField("하강 비행(틱)", d.flightTicks);
                EditorGUILayout.FloatField("착지 수직속도(m/s)", d.LandingVy());
                int total = TraversalBallistics.TotalTicks(link.PauseTicks, d.flightTicks, link.RecoverTicks);
                EditorGUILayout.IntField("총 소요(틱)", total);
                EditorGUILayout.FloatField("링크 비용 환산(m)",
                    TraversalBallistics.CostDistance(total, SimConfig.EnemyMoveSpeed));
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "하강은 항상 전 지상몹 허용. 상승 권한만 종류로 정해집니다.\n" +
                "clearance는 자동(길이 비례)이 기본이며, Scene 뷰에서 궤적 꼭대기를 끌면 수동으로 바뀝니다.",
                MessageType.None);

            if (!link.clearanceAuto && GUILayout.Button("clearance 자동으로 되돌리기"))
            {
                Undo.RecordObject(link, "clearance 자동 복귀");
                link.clearanceAuto = true;
                EditorUtility.SetDirty(link);
            }
            if ((link.pauseTicksOverride >= 0 || link.recoverTicksOverride >= 0) &&
                GUILayout.Button("주저·멈칫 자동으로 되돌리기"))
            {
                Undo.RecordObject(link, "주저·멈칫 자동 복귀");
                link.pauseTicksOverride = -1;
                link.recoverTicksOverride = -1;
                EditorUtility.SetDirty(link);
            }
        }

        void OnSceneGUI()
        {
            var link = (TraversalLink)target;

            // ── B 끝점 핸들 ──
            EditorGUI.BeginChangeCheck();
            Vector3 b = Handles.PositionHandle(link.PointB, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(link, "링크 끝점 이동");
                link.endOffset = link.transform.InverseTransformPoint(b);
                EditorUtility.SetDirty(link);
            }

            // ── 정점(apex) 핸들 — 끌면 clearance 수동 전환 ──
            var arc = link.DescendArc;
            if (arc.IsValid)
            {
                int apexTick = Mathf.RoundToInt(arc.launchVy / arc.gravity / SimConfig.TickDelta);
                Vector3 apex = arc.At(Mathf.Clamp(apexTick, 0, arc.flightTicks));
                EditorGUI.BeginChangeCheck();
                float size = HandleUtility.GetHandleSize(apex) * 0.12f;
                Handles.color = new Color(1f, 0.9f, 0.3f, 1f);
                Vector3 moved = Handles.Slider(apex, Vector3.up, size, Handles.SphereHandleCap, 0f);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(link, "clearance 조정");
                    float baseY = Mathf.Max(link.PointA.y, link.PointB.y);
                    link.clearanceAuto = false;
                    link.clearance = Mathf.Clamp(moved.y - baseY,
                        SimConfig.TraversalMinClearance, SimConfig.TraversalMaxClearance);
                    EditorUtility.SetDirty(link);
                }
            }

            // ── 고스트 재생 ──
            if (playing) DrawGhost(link);

            // ── 라벨 ──
            Handles.color = Color.white;
            Vector3 mid = (link.PointA + link.PointB) * 0.5f;
            Handles.Label(mid + Vector3.up * 0.4f,
                $"{KindLabel(link.kind)}\n길이 {link.Length:0.0}m · 주저 {link.PauseTicks} · 멈칫 {link.RecoverTicks}");
        }

        /// <summary>
        /// 주저 → 비행(탄도) → 멈칫을 실제 틱 길이로 재생. 런타임과 같은 함수로 궤적을 풀기 때문에
        /// 여기서 본 타이밍·궤적이 그대로 게임에서 나온다.
        /// </summary>
        void DrawGhost(TraversalLink link)
        {
            BallisticArc arc = playAscend && link.AscendAllowed ? link.AscendArc : link.DescendArc;
            if (!arc.IsValid) return;

            int pause = link.PauseTicks, flight = arc.flightTicks, recover = link.RecoverTicks;
            int total = pause + flight + recover;
            double elapsed = EditorApplication.timeSinceStartup - playStartTime;
            int tick = (int)(elapsed / SimConfig.TickDelta) % Mathf.Max(1, total);

            Vector3 p; string phase;
            if (tick < pause)                    { p = arc.start;                phase = "주저"; }
            else if (tick < pause + flight)      { p = arc.At(tick - pause);     phase = "비행"; }
            else                                 { p = arc.end;                  phase = "멈칫"; }

            float r = link.ValidateRadius, h = link.ValidateHeight;
            Handles.color = phase == "비행" ? new Color(1f, 0.9f, 0.2f, 0.95f)
                                            : new Color(1f, 0.5f, 0.2f, 0.95f);
            Handles.DrawWireDisc(p + Vector3.up * r, Vector3.up, r);
            Handles.DrawWireDisc(p + Vector3.up * Mathf.Max(r, h - r), Vector3.up, r);
            Handles.DrawLine(p + Vector3.up * r, p + Vector3.up * Mathf.Max(r, h - r));
            Handles.Label(p + Vector3.up * (h + 0.3f), $"{phase} {tick}/{total}틱");
        }

        static string KindLabel(TraversalLinkKind k)
        {
            switch (k)
            {
                case TraversalLinkKind.AscendRestricted: return "② 상승제한 (상승=Traversal만)";
                case TraversalLinkKind.DescendOnly:      return "③ 하강전용";
                default:                                 return "① 일반";
            }
        }
    }
}
