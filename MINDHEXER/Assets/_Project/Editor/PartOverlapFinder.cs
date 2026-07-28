using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// 관통(겹침) 부품 찾기 — 두 부품의 메시가 실제로 서로를 뚫고 들어간 쌍을 찾는다.
    /// Tripo 익스포트가 부품 위치를 틀어놓았거나, 부품이 다른 부품 속에 파묻힌 경우를 잡아낸다.
    ///
    /// <para>판정: ① AABB가 겹치는 쌍만 후보로 추리고(싼 검사) ② A의 표면 정점을 샘플링해
    /// B의 메시 <b>내부</b>에 있는지 레이 패리티(홀수 교차 = 내부)로 센다. 내부 비율이
    /// 임계 이상이면 관통으로 본다.</para>
    ///
    /// <para><b>주의</b> — 기계는 부품이 서로 살짝 박혀 있는 게 정상이다(축이 구멍에 들어감 등).
    /// 그래서 "관통 = 오류"가 아니다. <b>완전히 파묻힌 것</b>(내부 비율이 매우 높음)만 의심하면 된다.</para>
    ///
    /// Tools ▸ MINDHEXER ▸ 관통 부품 찾기
    /// </summary>
    public class PartOverlapFinder : EditorWindow
    {
        GameObject _target;
        int _samplesPerPart = 200;      // 부품당 샘플 정점 수
        float _reportRatio = 0.5f;      // 이 비율 이상 내부면 보고
        Vector2 _scroll;
        string _log = "";

        class Hit { public Renderer a, b; public float ratio; public long triA; }
        readonly List<Hit> _hits = new List<Hit>();

        [MenuItem("Tools/MINDHEXER/관통 부품 찾기")]
        static void Open() => GetWindow<PartOverlapFinder>("관통 부품 찾기");

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "부품 A의 정점이 부품 B 내부에 얼마나 들어가 있는지 재서 관통 쌍을 찾습니다.\n" +
                "기계는 축이 구멍에 박히는 등 정상적인 관통도 있으니, 비율이 매우 높은 것만 의심하십시오.",
                MessageType.Info);

            _target = (GameObject)EditorGUILayout.ObjectField("대상 모델", _target, typeof(GameObject), true);
            _samplesPerPart = EditorGUILayout.IntSlider("부품당 샘플 수", _samplesPerPart, 20, 1000);
            _reportRatio = EditorGUILayout.Slider("보고 임계(내부 비율)", _reportRatio, 0.1f, 1f);

            using (new EditorGUI.DisabledScope(_target == null))
                if (GUILayout.Button("검사 실행", GUILayout.Height(28))) Run();

            if (!string.IsNullOrEmpty(_log))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField(_log, EditorStyles.wordWrappedLabel);
                if (GUILayout.Button("결과 전부 선택")) SelectAll();

                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                foreach (var h in _hits)
                {
                    if (h.a == null || h.b == null) continue;
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"{h.a.name} → {h.b.name} 안", GUILayout.Width(260));
                    EditorGUILayout.LabelField($"{h.ratio * 100f:F0}%", GUILayout.Width(45));
                    EditorGUILayout.LabelField($"{h.triA:N0} tri", GUILayout.Width(70));
                    if (GUILayout.Button("선택", GUILayout.Width(50))) Selection.activeGameObject = h.a.gameObject;
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();
            }
        }

        void Run()
        {
            _hits.Clear();

            var mfs = new List<MeshFilter>();
            foreach (var mf in _target.GetComponentsInChildren<MeshFilter>(false))
                if (mf.sharedMesh != null && mf.GetComponent<Renderer>() != null) mfs.Add(mf);

            int n = mfs.Count;
            if (n < 2) { _log = "부품이 2개 미만입니다."; return; }

            // 월드 공간 정점·삼각형 캐시
            var wverts = new Vector3[n][];
            var wtris = new int[n][];
            var bounds = new Bounds[n];
            for (int i = 0; i < n; i++)
            {
                var m = mfs[i].sharedMesh;
                var mtx = mfs[i].transform.localToWorldMatrix;
                var vs = m.vertices;
                var w = new Vector3[vs.Length];
                for (int k = 0; k < vs.Length; k++) w[k] = mtx.MultiplyPoint3x4(vs[k]);
                wverts[i] = w;
                wtris[i] = m.triangles;
                bounds[i] = mfs[i].GetComponent<Renderer>().bounds;
            }

            try
            {
                for (int i = 0; i < n; i++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("관통 검사", $"{i + 1}/{n}", (float)i / n)) break;
                    for (int j = 0; j < n; j++)
                    {
                        if (i == j) continue;
                        if (!bounds[i].Intersects(bounds[j])) continue;
                        // 큰 쪽이 감싸는 관계만 본다(작은 A가 큰 B 안에 있는지)
                        if (bounds[j].size.magnitude < bounds[i].size.magnitude) continue;

                        float ratio = InsideRatio(wverts[i], wverts[j], wtris[j], bounds[j], _samplesPerPart);
                        if (ratio >= _reportRatio)
                        {
                            long t = 0;
                            var mm = mfs[i].sharedMesh;
                            for (int s = 0; s < mm.subMeshCount; s++) t += mm.GetIndexCount(s) / 3;
                            _hits.Add(new Hit { a = mfs[i].GetComponent<Renderer>(), b = mfs[j].GetComponent<Renderer>(), ratio = ratio, triA = t });
                            break;   // 부품당 한 건만 보고
                        }
                    }
                }
            }
            finally { EditorUtility.ClearProgressBar(); }

            _hits.Sort((x, y) => y.ratio.CompareTo(x.ratio));

            long sumTri = 0;
            foreach (var h in _hits) sumTri += h.triA;
            var sb = new StringBuilder();
            sb.AppendLine($"부품 {n}개 검사 → 내부 비율 {_reportRatio * 100f:F0}% 이상 {_hits.Count}건");
            sb.Append($"해당 부품 삼각형 합계 {sumTri:N0}");
            _log = sb.ToString();
            Repaint();
        }

        /// <summary>A의 정점 중 B 메시 내부에 있는 비율. 레이 패리티(홀수 교차 = 내부).</summary>
        static float InsideRatio(Vector3[] aVerts, Vector3[] bVerts, int[] bTris, Bounds bB, int samples)
        {
            if (aVerts.Length == 0) return 0f;
            int step = Mathf.Max(1, aVerts.Length / samples);
            int tested = 0, inside = 0;
            Vector3 dir = new Vector3(0.5773f, 0.5774f, 0.5775f);   // 축과 평행하지 않은 임의 방향
            float far = bB.size.magnitude * 2f + 1f;

            for (int i = 0; i < aVerts.Length; i += step)
            {
                var p = aVerts[i];
                if (!bB.Contains(p)) { tested++; continue; }
                int cross = 0;
                for (int t = 0; t < bTris.Length; t += 3)
                {
                    if (RayTri(p, dir, bVerts[bTris[t]], bVerts[bTris[t + 1]], bVerts[bTris[t + 2]], far)) cross++;
                }
                if ((cross & 1) == 1) inside++;
                tested++;
            }
            return tested == 0 ? 0f : (float)inside / tested;
        }

        /// <summary>Möller–Trumbore. 앞뒤 양방향 히트 카운트(패리티용).</summary>
        static bool RayTri(Vector3 o, Vector3 d, Vector3 v0, Vector3 v1, Vector3 v2, float far)
        {
            const float EPS = 1e-7f;
            Vector3 e1 = v1 - v0, e2 = v2 - v0;
            Vector3 h = Vector3.Cross(d, e2);
            float a = Vector3.Dot(e1, h);
            if (a > -EPS && a < EPS) return false;
            float f = 1f / a;
            Vector3 s = o - v0;
            float u = f * Vector3.Dot(s, h);
            if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(s, e1);
            float v = f * Vector3.Dot(d, q);
            if (v < 0f || u + v > 1f) return false;
            float t = f * Vector3.Dot(e2, q);
            return t > EPS && t < far;
        }

        void SelectAll()
        {
            var objs = new List<Object>();
            foreach (var h in _hits) if (h.a != null) objs.Add(h.a.gameObject);
            Selection.objects = objs.ToArray();
        }
    }
}
