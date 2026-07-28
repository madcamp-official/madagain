using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// 가려진 부품 찾기 — 부품마다 고유색을 칠하고 모델을 둘러싼 여러 방향에서 렌더링해,
    /// <b>어느 시점에서도 한 픽셀도 안 찍힌 부품</b>을 골라낸다. 완전히 가려진 부품은 화면에
    /// 전혀 안 보이면서 드로우콜·삼각형만 먹으므로, 지우면 겉모습 변화 없이 비용만 사라진다.
    ///
    /// <para><b>중요한 한계</b> — 이 결과는 "<i>지금 자세에서</i> 안 보인다"일 뿐이다. 프레스 램이
    /// 올라가면 안쪽이 드러나므로, 움직이는 부품의 자세를 바꿔가며 <b>여러 번 누적 검사</b>해서
    /// 모든 자세에서 한 번도 안 보인 부품만 삭제 후보로 삼아야 한다. 그래서 누적 모드를 둔다.</para>
    ///
    /// Tools ▸ MINDHEXER ▸ 가려진 부품 찾기
    /// </summary>
    public class HiddenPartFinder : EditorWindow
    {
        GameObject _target;

        [Header("검사 설정")]
        int _viewCount = 96;         // 구면상 시점 수
        int _resolution = 512;       // 시점당 렌더 해상도
        bool _includeBottom = true;  // 아래쪽 시점 포함(바닥에 붙는 물체면 끄는 게 현실적)

        // 누적 결과: 한 번이라도 보인 렌더러
        readonly HashSet<Renderer> _everSeen = new HashSet<Renderer>();
        int _passCount;
        List<Renderer> _hidden = new List<Renderer>();
        Vector2 _scroll;
        string _log = "";

        // 시각화 상태
        bool _highlighting;
        readonly Dictionary<Renderer, bool> _originalEnabled = new Dictionary<Renderer, bool>();

        [MenuItem("Tools/MINDHEXER/가려진 부품 찾기")]
        static void Open() => GetWindow<HiddenPartFinder>("가려진 부품 찾기");

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "부품마다 고유색을 칠해 사방에서 렌더링한 뒤, 한 픽셀도 안 찍힌 부품을 찾습니다.\n" +
                "움직이는 부품은 자세를 바꿔가며 [검사 추가]를 여러 번 눌러야 정확합니다.",
                MessageType.Info);

            _target = (GameObject)EditorGUILayout.ObjectField("대상 모델", _target, typeof(GameObject), true);
            _viewCount = EditorGUILayout.IntSlider("시점 수", _viewCount, 12, 256);
            _resolution = EditorGUILayout.IntPopup("해상도", _resolution,
                new[] { "256", "512", "1024", "2048" }, new[] { 256, 512, 1024, 2048 });
            _includeBottom = EditorGUILayout.Toggle("아래쪽에서도 봄", _includeBottom);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_target == null))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(_passCount == 0 ? "검사 실행" : $"검사 추가 (누적 {_passCount}회)", GUILayout.Height(28)))
                    RunPass();
                if (GUILayout.Button("누적 초기화", GUILayout.Width(90), GUILayout.Height(28)))
                {
                    _everSeen.Clear(); _passCount = 0; _hidden.Clear(); _log = "";
                }
                EditorGUILayout.EndHorizontal();
            }

            if (_passCount > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("결과", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(_log, EditorStyles.wordWrappedLabel);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(_highlighting ? "하이라이트 끄기" : "가려진 것만 빨갛게")) ToggleHighlight();
                if (GUILayout.Button("가려진 것만 남기고 숨기기")) IsolateHidden();
                if (GUILayout.Button("표시 복원")) RestoreVisibility();
                EditorGUILayout.EndHorizontal();

                if (GUILayout.Button("가려진 부품 전부 선택 (Delete로 삭제 가능)")) SelectHidden();

                EditorGUILayout.Space();
                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                foreach (var r in _hidden)
                {
                    if (r == null) continue;
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(r.name, GUILayout.Width(180));
                    EditorGUILayout.LabelField(TriOf(r).ToString("N0") + " tri", GUILayout.Width(70));
                    if (GUILayout.Button("선택", GUILayout.Width(50))) Selection.activeGameObject = r.gameObject;
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();
            }
        }

        static long TriOf(Renderer r)
        {
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return 0;
            long t = 0;
            for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) t += mf.sharedMesh.GetIndexCount(s) / 3;
            return t;
        }

        /// <summary>한 번의 검사 패스 — 현재 씬 자세 기준으로 보이는 부품을 누적에 더한다.</summary>
        void RunPass()
        {
            RestoreVisibility();

            var rends = new List<Renderer>();
            foreach (var r in _target.GetComponentsInChildren<Renderer>(false))
                if (r.enabled && r.gameObject.activeInHierarchy) rends.Add(r);

            if (rends.Count == 0) { _log = "렌더러가 없습니다."; return; }
            if (rends.Count > 16000) { _log = "부품이 너무 많습니다(16000 초과)."; return; }

            // 부품 → 고유색. 24bit 중 상위 비트를 써서 색 간 거리를 벌린다(압축·필터 오차 방지).
            var idToRend = new Dictionary<int, Renderer>();
            var mpb = new MaterialPropertyBlock();
            var idMat = new Material(Shader.Find("Unlit/Color"));
            var savedMats = new Dictionary<Renderer, Material[]>();

            for (int i = 0; i < rends.Count; i++)
            {
                int id = i + 1;                        // 0 = 배경
                idToRend[id] = rends[i];
                savedMats[rends[i]] = rends[i].sharedMaterials;
                var mats = new Material[rends[i].sharedMaterials.Length];
                for (int m = 0; m < mats.Length; m++) mats[m] = idMat;
                rends[i].sharedMaterials = mats;
                mpb.Clear();
                mpb.SetColor("_Color", IdToColor(id));
                rends[i].SetPropertyBlock(mpb);
            }

            // 대상만 렌더링하기 위한 전용 카메라
            var camGo = new GameObject("[HiddenPartFinderCam]") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.orthographic = true;
            cam.allowMSAA = false;
            cam.allowHDR = false;
            cam.renderingPath = RenderingPath.Forward;

            // 대상 계층만 보이도록 임시 레이어로 옮긴다(31번 레이어 사용)
            const int TempLayer = 31;
            var savedLayers = new Dictionary<GameObject, int>();
            foreach (var r in rends) { savedLayers[r.gameObject] = r.gameObject.layer; r.gameObject.layer = TempLayer; }
            cam.cullingMask = 1 << TempLayer;

            var b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            float radius = b.extents.magnitude;
            cam.orthographicSize = radius * 1.05f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = radius * 6f;

            var rt = new RenderTexture(_resolution, _resolution, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            { filterMode = FilterMode.Point, antiAliasing = 1 };
            var tex = new Texture2D(_resolution, _resolution, TextureFormat.RGBA32, false, true);
            cam.targetTexture = rt;

            var seenThisPass = new HashSet<int>();
            try
            {
                var dirs = SphereDirections(_viewCount, _includeBottom);
                for (int v = 0; v < dirs.Count; v++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("가려진 부품 검사",
                        $"시점 {v + 1}/{dirs.Count}", (float)v / dirs.Count)) break;

                    cam.transform.position = b.center + dirs[v] * radius * 3f;
                    cam.transform.rotation = Quaternion.LookRotation(-dirs[v], Vector3.up);
                    cam.Render();

                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, _resolution, _resolution), 0, 0, false);
                    tex.Apply(false);
                    RenderTexture.active = null;

                    var px = tex.GetPixels32();
                    for (int i = 0; i < px.Length; i++)
                    {
                        int id = ColorToId(px[i]);
                        if (id > 0) seenThisPass.Add(id);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                cam.targetTexture = null;
                RenderTexture.active = null;
                DestroyImmediate(rt);
                DestroyImmediate(tex);
                DestroyImmediate(camGo);
                DestroyImmediate(idMat);
                foreach (var kv in savedMats) if (kv.Key != null) { kv.Key.sharedMaterials = kv.Value; kv.Key.SetPropertyBlock(null); }
                foreach (var kv in savedLayers) if (kv.Key != null) kv.Key.layer = kv.Value;
            }

            foreach (var id in seenThisPass) if (idToRend.ContainsKey(id)) _everSeen.Add(idToRend[id]);
            _passCount++;

            _hidden.Clear();
            long hiddenTris = 0, totalTris = 0;
            foreach (var r in rends)
            {
                long t = TriOf(r); totalTris += t;
                if (!_everSeen.Contains(r)) { _hidden.Add(r); hiddenTris += t; }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"누적 {_passCount}회 검사 / 시점 {_viewCount} × {_resolution}px");
            sb.AppendLine($"전체 부품 {rends.Count} → 가려진 부품 {_hidden.Count}개");
            sb.AppendLine($"낭비 삼각형 {hiddenTris:N0} / 전체 {totalTris:N0} ({(totalTris > 0 ? 100.0 * hiddenTris / totalTris : 0):F1}%)");
            sb.Append($"지우면 드로우콜 {rends.Count} → {rends.Count - _hidden.Count}");
            _log = sb.ToString();
            Repaint();
        }

        static Color IdToColor(int id)
        {
            // 각 채널 상위 비트부터 채워 색 간 간격을 최대화
            return new Color32(
                (byte)((id & 0xFF)),
                (byte)((id >> 8) & 0xFF),
                (byte)((id >> 16) & 0xFF), 255);
        }

        static int ColorToId(Color32 c) => c.r | (c.g << 8) | (c.b << 16);

        /// <summary>구면에 고르게 분포한 방향(피보나치 구면). 아래쪽 제외 옵션 지원.</summary>
        static List<Vector3> SphereDirections(int n, bool includeBottom)
        {
            var list = new List<Vector3>(n);
            float ga = Mathf.PI * (3f - Mathf.Sqrt(5f));
            for (int i = 0; i < n; i++)
            {
                float y = 1f - (i / (float)(n - 1)) * 2f;
                float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
                float th = ga * i;
                var d = new Vector3(Mathf.Cos(th) * r, y, Mathf.Sin(th) * r);
                if (!includeBottom && d.y < -0.1f) continue;
                list.Add(d.normalized);
            }
            return list;
        }

        void ToggleHighlight()
        {
            if (_highlighting) { RestoreVisibility(); return; }
            var mpb = new MaterialPropertyBlock();
            foreach (var r in _hidden)
            {
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", Color.red);
                mpb.SetColor("_Color", Color.red);
                r.SetPropertyBlock(mpb);
            }
            _highlighting = true;
            SceneView.RepaintAll();
        }

        void IsolateHidden()
        {
            RestoreVisibility();
            if (_target == null) return;
            foreach (var r in _target.GetComponentsInChildren<Renderer>(true))
            {
                _originalEnabled[r] = r.enabled;
                r.enabled = _hidden.Contains(r);
            }
            SceneView.RepaintAll();
        }

        void RestoreVisibility()
        {
            foreach (var kv in _originalEnabled) if (kv.Key != null) kv.Key.enabled = kv.Value;
            _originalEnabled.Clear();
            if (_target != null)
                foreach (var r in _target.GetComponentsInChildren<Renderer>(true))
                    r.SetPropertyBlock(null);
            _highlighting = false;
            SceneView.RepaintAll();
        }

        void SelectHidden()
        {
            var objs = new List<Object>();
            foreach (var r in _hidden) if (r != null) objs.Add(r.gameObject);
            Selection.objects = objs.ToArray();
        }

        void OnDisable() => RestoreVisibility();
    }
}
