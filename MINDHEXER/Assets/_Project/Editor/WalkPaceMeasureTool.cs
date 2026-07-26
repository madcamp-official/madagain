using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace Game.EditorTools
{
    /// <summary>
    /// 걷기/달리기 클립의 <b>진짜 보폭 속도</b>(m/s)를 측정한다.
    ///
    /// ── 왜 필요한가 ──
    /// EntityViews는 <c>anim.speed = 실제이동속도 / WalkClipPace</c> 로 발 미끄러짐을 없애려 하는데,
    /// 그 WalkClipPace(1.8)가 <b>측정값이 아니라 추측값</b>이다(코드 주석에도 그렇게 적혀 있다).
    /// 분모가 틀리면 어떤 이동 속도에서도 발이 맞지 않는다.
    ///
    /// ── 어떻게 재는가 ──
    /// 클립은 제자리 모션이라 루트 모션이 없다. 대신 <b>발 궤적</b>으로 역산한다.
    ///   매 프레임 "땅에 닿아 있는 발"(Y가 낮은 쪽)을 찾고,
    ///   그 발이 <b>뒤로 간 거리</b>를 누적한다.
    ///   발이 땅에 붙은 채 뒤로 간 만큼 몸이 앞으로 가야 안 미끄러진다.
    ///   누적 거리 ÷ 클립 길이 = 이 클립의 보폭 속도.
    ///
    /// 마지막으로 <b>렌더 배율</b>을 곱한다 — 다리가 1.4배 길면 보폭도 1.4배다.
    /// </summary>
    public class WalkPaceMeasureTool : EditorWindow
    {
        // EntityViews와 같은 값 — 여기서 참조할 수 없어(내부 const) 복사해 둔다.
        // ★ 저쪽이 바뀌면 여기도 맞춰야 한다.
        const float EnemyHeight = 1.4375f;
        const float ChargeVisualScaleMul = 1.4f;
        const float MeleeVisualScaleMul  = 1.4f;
        const float RangedVisualScaleMul = 1.4f;

        [System.Serializable]
        class Row
        {
            public string label, prefabPath, clipHint;
            public float  visualMul;
            public string result = "";
            public float  paceRaw, paceWorld, cycleSec;
            public bool   ok;
        }

        readonly List<Row> rows = new List<Row>
        {
            new Row { label = "근접 (FootSoldier)",      prefabPath = "Enemies/MeleeEnemy",  clipHint = "walk", visualMul = MeleeVisualScaleMul },
            new Row { label = "근접-달리기",             prefabPath = "Enemies/MeleeEnemy",  clipHint = "run",  visualMul = MeleeVisualScaleMul },
            new Row { label = "원거리 (Monolith)",       prefabPath = "Enemies/RangedEnemy", clipHint = "walk", visualMul = RangedVisualScaleMul },
            new Row { label = "돌진 걷기 (Obsidian)",    prefabPath = "Enemies/ChargeEnemy", clipHint = "walk", visualMul = ChargeVisualScaleMul },
            new Row { label = "돌진 달리기 (Obsidian)",  prefabPath = "Enemies/ChargeEnemy", clipHint = "run",  visualMul = ChargeVisualScaleMul },
        };

        Vector2 scroll;
        int samples = 60;

        [MenuItem("Tools/몹/걷기 속도 측정")]
        static void Open() => GetWindow<WalkPaceMeasureTool>("걷기 속도 측정").minSize = new Vector2(560f, 420f);

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "클립의 발 궤적을 적분해 '이 클립이 가정하는 이동 속도'를 구합니다.\n" +
                "EntityViews의 WalkClipPace / RunClipPace 를 이 값으로 바꾸면 발 미끄러짐이 사라집니다.",
                MessageType.Info);

            samples = EditorGUILayout.IntSlider("샘플 수(프레임)", samples, 20, 200);
            if (GUILayout.Button("전부 측정", GUILayout.Height(28f))) MeasureAll();

            EditorGUILayout.Space();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var r in rows)
            {
                using (new EditorGUILayout.VerticalScope("box"))
                {
                    EditorGUILayout.LabelField(r.label, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"  {r.prefabPath}  ·  클립 검색어 \"{r.clipHint}\"", EditorStyles.miniLabel);
                    if (string.IsNullOrEmpty(r.result)) { EditorGUILayout.LabelField("  (측정 전)"); continue; }
                    EditorGUILayout.LabelField("  " + r.result, r.ok ? EditorStyles.label : EditorStyles.helpBox);
                    if (r.ok)
                        EditorGUILayout.LabelField(
                            $"  → <b>{r.paceWorld:0.00} m/s</b>  (원본 {r.paceRaw:0.000} × 스케일 {EnemyHeight * r.visualMul:0.00})",
                            Rich());
                }
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "적용 방법 — EntityViews.cs 상단 상수를 측정값으로 바꾸십시오.\n" +
                "  const float WalkClipPace = <걷기 측정값>;\n" +
                "  const float RunClipPace  = <달리기 측정값>;\n" +
                "그 뒤 클램프 범위(WalkSpeedClampMin/Max)도 함께 보십시오 — 분모가 맞으면 배속이 1 근처가 됩니다.",
                MessageType.None);
        }

        static GUIStyle Rich() => new GUIStyle(EditorStyles.label) { richText = true };

        void MeasureAll()
        {
            foreach (var r in rows) Measure(r);
            Repaint();
        }

        void Measure(Row r)
        {
            r.ok = false; r.result = "";
            var prefab = Resources.Load<GameObject>(r.prefabPath);
            if (prefab == null) { r.result = "프리팹 없음 (Resources/" + r.prefabPath + ")"; return; }

            // 클립 찾기 — 프리팹이 참조하는 컨트롤러의 클립 중 이름에 검색어가 든 것
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var anim = inst.GetComponentInChildren<Animator>();
                if (anim == null) { r.result = "Animator 없음"; return; }
                AnimationClip clip = FindClip(anim, r.clipHint);
                if (clip == null)
                {
                    r.result = $"'{r.clipHint}' 클립을 못 찾음. 보유: " + string.Join(", ", ClipNames(anim));
                    return;
                }

                Transform footL = FindBone(inst.transform, "leftfoot", "lefttoe");
                Transform footR = FindBone(inst.transform, "rightfoot", "righttoe");
                if (footL == null || footR == null) { r.result = "발 본을 못 찾음(LeftFoot/RightFoot)"; return; }
                Transform root = anim.transform;

                float len = clip.length;
                if (len <= 1e-4f) { r.result = "클립 길이가 0"; return; }

                // ── 발 궤적 적분 ──
                // 매 프레임 "낮은 쪽 발"을 접지로 보고, 그 발이 루트 기준으로 뒤로 간 거리를 누적한다.
                float travel = 0f;
                Vector3 prevL = Vector3.zero, prevR = Vector3.zero;
                bool first = true;
                for (int s = 0; s <= samples; s++)
                {
                    float t = len * s / samples;
                    clip.SampleAnimation(inst, t);

                    Vector3 l = root.InverseTransformPoint(footL.position);
                    Vector3 rr = root.InverseTransformPoint(footR.position);
                    if (!first)
                    {
                        // 접지 발 = Y가 낮은 쪽. 그 발의 뒤(-Z) 방향 이동만 센다.
                        bool leftDown = l.y <= rr.y;
                        Vector3 cur = leftDown ? l : rr;
                        Vector3 prv = leftDown ? prevL : prevR;
                        float back = prv.z - cur.z;              // 뒤로 갔으면 양수
                        if (back > 0f) travel += back;
                    }
                    prevL = l; prevR = rr; first = false;
                }

                if (travel <= 1e-4f) { r.result = "발 이동이 감지되지 않음(제자리 클립이 아닐 수 있음)"; return; }

                r.cycleSec  = len;
                r.paceRaw   = travel / len;                       // 프리팹 원본 크기 기준
                r.paceWorld = r.paceRaw * (EnemyHeight * r.visualMul);
                r.ok = true;
                r.result = $"클립 {clip.name} · 길이 {len:0.000}초 · 누적 보폭 {travel:0.000}m";
            }
            finally { DestroyImmediate(inst); }
        }

        static IEnumerable<string> ClipNames(Animator a)
        {
            var c = a.runtimeAnimatorController;
            if (c == null) yield break;
            foreach (var cl in c.animationClips) yield return cl.name;
        }

        static AnimationClip FindClip(Animator a, string hint)
        {
            var c = a.runtimeAnimatorController;
            if (c == null) return null;
            hint = hint.ToLowerInvariant();
            foreach (var cl in c.animationClips)
                if (cl != null && cl.name.ToLowerInvariant().Contains(hint)) return cl;
            return null;
        }

        static Transform FindBone(Transform root, params string[] keys)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                foreach (var k in keys) if (n.Contains(k)) return t;
            }
            return null;
        }
    }
}
