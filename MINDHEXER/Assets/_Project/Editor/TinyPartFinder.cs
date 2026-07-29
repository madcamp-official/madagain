using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// 티끌 부품 찾기 — AI 생성 메시(Tripo 등)가 흩뿌려놓은, 실제 부품이 아닌 극소 파편을 찾는다.
    ///
    /// <para><b>판정 기준은 순수 크기</b>(바운즈 대각선)다. 위치·고립도는 안 본다 — 티끌이
    /// 모델 전체에 넓게 흩어져 있어도(위치 기반 판정이 못 잡는 경우) 크기만으로는 똑같이 잡힌다.</para>
    ///
    /// <para><b>한계</b> — 볼트·핀 같은 정상 소형 부품도 작다. 크기 하나로는 못 가른다.
    /// 그래서 목록을 <b>오름차순 + 로그 히스토그램</b>으로 보여줘 "정상 소형 부품 무리"와
    /// "티끌 무리" 사이의 <b>크기 단절 구간</b>을 눈으로 확인하고 임계값을 그 사이에 놓게 한다.
    /// 삼각형 밀도(삼각형수/부피)도 같이 보여준다 — 티끌은 밀도가 비정상적으로 튀는 경우가 많다.</para>
    ///
    /// <para>자동 삭제 없음. 하이라이트로 확인 → 비활성화 또는 <c>_Debris</c> 홀더로 이동(둘 다 되돌릴 수 있음).</para>
    ///
    /// Tools ▸ MINDHEXER ▸ 티끌 부품 찾기
    /// </summary>
    public class TinyPartFinder : EditorWindow
    {
        GameObject _target;
        float _threshold = 0.02f;   // 바운즈 대각선(m)
        bool _logScale = true;
        Vector2 _scroll;

        class Entry { public Renderer r; public float size; public long tri; public float density; }
        List<Entry> _entries = new List<Entry>();
        float _minSize, _maxSize;

        bool _highlighting;
        readonly Dictionary<Renderer, bool> _originalEnabled = new Dictionary<Renderer, bool>();
        const string DebrisHolderName = "_Debris";

        [MenuItem("Tools/MINDHEXER/티끌 부품 찾기")]
        static void Open() => GetWindow<TinyPartFinder>("티끌 부품 찾기");

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "부품을 크기(바운즈 대각선) 오름차순으로 나열합니다. 정상 소형 부품(볼트 등)과 " +
                "티끌 사이에 크기가 뚝 끊기는 구간이 있으면 그 사이에 임계값을 놓으세요.",
                MessageType.Info);

            var newTarget = (GameObject)EditorGUILayout.ObjectField("대상 모델", _target, typeof(GameObject), true);
            if (newTarget != _target) { _target = newTarget; RestoreVisibility(); Scan(); }

            using (new EditorGUI.DisabledScope(_target == null))
            {
                if (GUILayout.Button("다시 스캔", GUILayout.Height(24))) Scan();
            }

            if (_entries.Count == 0) return;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"부품 {_entries.Count}개  ·  크기 {_minSize:0.###}m ~ {_maxSize:0.###}m", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _threshold = EditorGUILayout.Slider("임계값(m)", _threshold, _minSize, Mathf.Max(_minSize + 0.001f, _maxSize * 0.3f));
            _logScale = EditorGUILayout.Toggle("히스토그램 로그 스케일", _logScale);
            if (EditorGUI.EndChangeCheck()) { if (_highlighting) Highlight(); SceneView.RepaintAll(); }

            int under = _entries.Count(e => e.size <= _threshold);
            long underTri = _entries.Where(e => e.size <= _threshold).Sum(e => e.tri);
            EditorGUILayout.LabelField($"→ 임계값 이하 {under}개 / 삼각형 {underTri:N0}개");

            DrawHistogram();

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(_highlighting ? "하이라이트 끄기" : "임계값 이하만 빨갛게")) { if (_highlighting) RestoreVisibility(); else Highlight(); }
            if (GUILayout.Button($"'{DebrisHolderName}'로 이동")) MoveToDebrisHolder();
            if (GUILayout.Button("비활성화")) DisableUnderThreshold();
            if (GUILayout.Button("표시 복원")) RestoreVisibility();
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("임계값 이하 전부 선택 (Delete로 직접 삭제 가능)")) SelectUnderThreshold();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("목록 (크기 오름차순)", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(260));
            foreach (var e in _entries)
            {
                if (e.r == null) continue;
                bool flagged = e.size <= _threshold;
                var c = GUI.color;
                if (flagged) GUI.color = new Color(1f, 0.55f, 0.5f);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(e.r.name, GUILayout.Width(200));
                EditorGUILayout.LabelField($"{e.size:0.0000}m", GUILayout.Width(70));
                EditorGUILayout.LabelField($"{e.tri} tri", GUILayout.Width(60));
                EditorGUILayout.LabelField($"밀도 {e.density:0.0}", GUILayout.Width(80));
                if (GUILayout.Button("선택", GUILayout.Width(50))) Selection.activeGameObject = e.r.gameObject;
                EditorGUILayout.EndHorizontal();
                GUI.color = c;
            }
            EditorGUILayout.EndScrollView();
        }

        void DrawHistogram()
        {
            const int buckets = 40;
            var counts = new int[buckets];
            float lo = _logScale ? Mathf.Log(Mathf.Max(1e-5f, _minSize)) : _minSize;
            float hi = _logScale ? Mathf.Log(Mathf.Max(_minSize + 1e-4f, _maxSize)) : _maxSize;
            float span = Mathf.Max(1e-6f, hi - lo);

            foreach (var e in _entries)
            {
                float v = _logScale ? Mathf.Log(Mathf.Max(1e-5f, e.size)) : e.size;
                int b = Mathf.Clamp(Mathf.FloorToInt((v - lo) / span * buckets), 0, buckets - 1);
                counts[b]++;
            }
            int maxCount = Mathf.Max(1, counts.Max());
            int thresholdBucket = Mathf.Clamp(Mathf.FloorToInt((( _logScale ? Mathf.Log(Mathf.Max(1e-5f,_threshold)) : _threshold) - lo) / span * buckets), 0, buckets - 1);

            Rect area = GUILayoutUtility.GetRect(10, 60, GUILayout.ExpandWidth(true));
            for (int b = 0; b < buckets; b++)
            {
                float h = (counts[b] / (float)maxCount) * area.height;
                Rect bar = new Rect(area.x + b * (area.width / buckets), area.yMax - h, area.width / buckets - 1, h);
                EditorGUI.DrawRect(bar, b <= thresholdBucket ? new Color(0.85f, 0.35f, 0.3f) : new Color(0.3f, 0.6f, 0.85f));
            }
            // 임계값 위치 표시선
            float xLine = area.x + (thresholdBucket + 0.5f) * (area.width / buckets);
            EditorGUI.DrawRect(new Rect(xLine, area.y, 1.5f, area.height), Color.white);
        }

        void Scan()
        {
            RestoreVisibility();
            _entries.Clear();
            if (_target == null) return;

            foreach (var r in _target.GetComponentsInChildren<Renderer>(true))
            {
                if (r.gameObject.name == DebrisHolderName || IsUnderDebrisHolder(r.transform)) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                float size = r.bounds.size.magnitude;
                long tri = 0;
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) tri += mf.sharedMesh.GetIndexCount(s) / 3;
                float vol = Mathf.Max(1e-9f, r.bounds.size.x * r.bounds.size.y * r.bounds.size.z);
                _entries.Add(new Entry { r = r, size = size, tri = tri, density = tri / vol });
            }

            _entries.Sort((a, b) => a.size.CompareTo(b.size));
            _minSize = _entries.Count > 0 ? _entries[0].size : 0f;
            _maxSize = _entries.Count > 0 ? _entries[_entries.Count - 1].size : 0.01f;
            _threshold = Mathf.Clamp(_threshold, _minSize, _maxSize);
            Repaint();
        }

        bool IsUnderDebrisHolder(Transform t)
        {
            for (var p = t.parent; p != null; p = p.parent)
                if (p.name == DebrisHolderName) return true;
            return false;
        }

        void Highlight()
        {
            RestoreVisibility();
            var mpb = new MaterialPropertyBlock();
            foreach (var e in _entries)
            {
                if (e.r == null || e.size > _threshold) continue;
                _originalEnabled[e.r] = e.r.enabled;   // 복원용(값은 안 바꾸지만 키에 등록)
                e.r.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", Color.red);
                mpb.SetColor("_Color", Color.red);
                e.r.SetPropertyBlock(mpb);
            }
            _highlighting = true;
            SceneView.RepaintAll();
        }

        void RestoreVisibility()
        {
            foreach (var kv in _originalEnabled) if (kv.Key != null) { kv.Key.SetPropertyBlock(null); }
            _originalEnabled.Clear();
            _highlighting = false;
            SceneView.RepaintAll();
        }

        void DisableUnderThreshold()
        {
            Undo.SetCurrentGroupName("티끌 부품 비활성화");
            int group = Undo.GetCurrentGroup();
            foreach (var e in _entries)
            {
                if (e.r == null || e.size > _threshold) continue;
                Undo.RecordObject(e.r.gameObject, "티끌 비활성화");
                e.r.gameObject.SetActive(false);
            }
            Undo.CollapseUndoOperations(group);
        }

        void MoveToDebrisHolder()
        {
            if (_target == null) return;
            var holder = _target.transform.Find(DebrisHolderName);
            if (holder == null)
            {
                var go = new GameObject(DebrisHolderName);
                Undo.RegisterCreatedObjectUndo(go, "티끌 홀더 생성");
                go.transform.SetParent(_target.transform, false);
                holder = go.transform;
            }

            Undo.SetCurrentGroupName("티끌 부품 이동");
            int group = Undo.GetCurrentGroup();
            foreach (var e in _entries)
            {
                if (e.r == null || e.size > _threshold) continue;
                Undo.SetTransformParent(e.r.transform, holder, "티끌 재부모화");
            }
            Undo.CollapseUndoOperations(group);
            Scan();
        }

        void SelectUnderThreshold()
        {
            Selection.objects = _entries.Where(e => e.r != null && e.size <= _threshold)
                                         .Select(e => (Object)e.r.gameObject).ToArray();
        }

        void OnDisable() => RestoreVisibility();
    }
}
