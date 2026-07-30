using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Game.View
{
    /// <summary>
    /// Title 씬이 <b>정말 3D 공간</b>인지 직접 돌려보며 확인하는 검증 창.
    /// (메뉴: MINDHEXER ▸ Build ▸ Title 3D 검증)
    ///
    /// 타이틀은 어둡고 카메라가 거의 고정이라, 게임 화면만 봐서는 평면 이미지 한 장을 띄운 것과
    /// 구분되지 않는다. 이 창에서는 <b>마우스 드래그로 로봇 주위를 궤도 회전</b>하고 휠로 거리를 바꿔가며
    /// 실루엣이 각도마다 달라지는지 눈으로 바로 확인할 수 있다.
    ///
    /// 씬은 전혀 건드리지 않는다 — 씬 카메라의 설정만 복사한 임시 카메라로 렌더하므로
    /// 씬이 dirty로 표시되거나 저장된 카메라 위치가 바뀌는 일이 없다.
    ///
    /// 보조 증거:
    /// <list type="bullet">
    /// <item><b>깊이 리포트</b> — 원근 여부/FOV, 렌더러별 카메라 전방 거리 분포, 라이트가 피사체 앞인지 뒤인지.</item>
    /// <item><b>턴테이블·스테레오 PNG</b> — 프로젝트 폴더 밑 <c>TitleAudit/</c>에 저장(Assets 밖이라 임포트·git 노이즈 없음).</item>
    /// </list>
    /// </summary>
    public sealed class TitleSceneAuditWindow : EditorWindow
    {
        const string ScenePath = "Assets/_Project/Scenes/Title.unity";
        const int ShotWidth = 1300;   // 13:12
        const int ShotHeight = 1200;
        const int TurntableShots = 8;
        const float StereoBaselineRatio = 0.05f;    // 피사체 키 대비 좌·우 눈 간격 — 스테레오 시차용

        static string OutDir => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "TitleAudit");

        // 궤도 상태: (0, 0, 1) = 씬에 저장된 원래 시점.
        float _yaw;
        float _pitch;
        float _zoom = 1f;

        Camera _sceneCam;
        Camera _tempCam;
        RenderTexture _rt;
        List<Renderer> _renderers = new List<Renderer>();
        Bounds _target;
        string _status;

        // 렌더는 OnGUI(Repaint) 안이 아니라 Update에서 한다. GUI 그리는 도중에 Camera.Render()를
        // 부르면 재귀 렌더로 취급돼 경고가 나거나 그림이 깨질 수 있다.
        Vector2Int _viewSize;
        bool _viewDirty = true;

        [MenuItem("MINDHEXER/Build/Title 3D 검증")]
        public static void Open()
        {
            var w = GetWindow<TitleSceneAuditWindow>("Title 3D 검증");
            w.minSize = new Vector2(480f, 560f);
            w.Bind();
        }

        void OnEnable() { Bind(); }

        void OnDisable()
        {
            if (_tempCam != null) DestroyImmediate(_tempCam.gameObject);
            if (_rt != null) { _rt.Release(); DestroyImmediate(_rt); }
            _tempCam = null; _rt = null;
        }

        // ── 씬 바인딩 ─────────────────────────────────────────────────────────

        void Bind()
        {
            _sceneCam = null;
            _renderers.Clear();

            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.name != "Title")
            {
                _status = "Title 씬이 열려 있지 않습니다. [Title 씬 열기]를 누르세요.";
                return;
            }

            foreach (var root in scene.GetRootGameObjects())
            {
                if (_sceneCam == null) _sceneCam = root.GetComponentInChildren<Camera>(true);
                // UI 그래픽은 Renderer가 아니라 CanvasRenderer라 여기 잡히지 않는다 → 3D 지오메트리만 남는다.
                _renderers.AddRange(root.GetComponentsInChildren<Renderer>(true));
            }

            if (_sceneCam == null) { _status = "씬에 카메라가 없습니다. 먼저 타이틀을 조립하세요."; return; }
            if (_renderers.Count == 0) { _status = "3D 렌더러가 없습니다 — 로봇 프리팹이 씬에 없습니다."; return; }

            _target = WorldBounds(_renderers);
            _status = null;
            _viewDirty = true;
        }

        // ── GUI ───────────────────────────────────────────────────────────────

        void OnGUI()
        {
            DrawToolbar();

            if (_status != null)
            {
                EditorGUILayout.HelpBox(_status, MessageType.Warning);
                if (GUILayout.Button("Title 씬 열기"))
                {
                    if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    {
                        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                        Bind();
                    }
                }
                return;
            }

            EditorGUILayout.LabelField(
                "드래그: 궤도 회전   ·   휠: 거리   ·   더블클릭: 원래 시점으로",
                EditorStyles.miniLabel);

            Rect view = GUILayoutUtility.GetRect(10f, 10f,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            HandleInput(view);

            var size = new Vector2Int(Mathf.Max(1, (int)view.width), Mathf.Max(1, (int)view.height));
            if (size != _viewSize) { _viewSize = size; _viewDirty = true; }

            if (Event.current.type == EventType.Repaint && _rt != null)
                GUI.DrawTexture(view, _rt, ScaleMode.ScaleToFit, false);

            EditorGUILayout.LabelField(
                $"yaw {_yaw:0}°   pitch {_pitch:0}°   거리 ×{_zoom:0.00}" +
                (Mathf.Abs(_yaw) < 0.01f && Mathf.Abs(_pitch) < 0.01f && Mathf.Approximately(_zoom, 1f)
                    ? "   (= 게임에서 보이는 시점)" : ""),
                EditorStyles.miniLabel);
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("새로고침", EditorStyles.toolbarButton)) { Bind(); Repaint(); }
                if (GUILayout.Button("원래 시점", EditorStyles.toolbarButton)) ResetOrbit();
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(_status != null))
                {
                    if (GUILayout.Button("깊이 리포트", EditorStyles.toolbarButton))
                        Debug.Log(BuildDepthReport());
                    if (GUILayout.Button("턴테이블·스테레오 저장", EditorStyles.toolbarButton))
                        CaptureAll();
                }
            }
        }

        void ResetOrbit() { _yaw = 0f; _pitch = 0f; _zoom = 1f; Invalidate(); }

        void Invalidate() { _viewDirty = true; Repaint(); }

        void HandleInput(Rect view)
        {
            var e = Event.current;
            if (!view.Contains(e.mousePosition)) return;

            switch (e.type)
            {
                // MouseDown을 소비해야 이어지는 MouseDrag가 이 창으로 들어온다.
                case EventType.MouseDown:
                    if (e.clickCount >= 2) ResetOrbit();
                    e.Use();
                    break;

                case EventType.MouseDrag:
                    _yaw += e.delta.x * 0.5f;
                    _pitch = Mathf.Clamp(_pitch - e.delta.y * 0.35f, -85f, 85f);
                    e.Use();
                    Invalidate();
                    break;

                case EventType.ScrollWheel:
                    _zoom = Mathf.Clamp(_zoom * (1f + e.delta.y * 0.04f), 0.15f, 5f);
                    e.Use();
                    Invalidate();
                    break;
            }
        }

        // ── 렌더 ──────────────────────────────────────────────────────────────

        /// <summary>GUI 밖(Update)에서 현재 궤도 각도로 한 프레임 렌더해 _rt에 담는다.</summary>
        void Update()
        {
            if (!_viewDirty || _status != null || _viewSize.x <= 1) return;
            _viewDirty = false;

            var cam = TempCamera();
            if (cam == null) return;
            PlaceOnOrbit(cam.transform, _yaw, _pitch, _zoom);
            RenderTo(cam, _viewSize.x, _viewSize.y);
            Repaint();
        }

        /// <summary>
        /// 궤도 각도(yaw/pitch/zoom)를 씬 카메라의 원래 시점 기준 상대값으로 해석해 배치한다.
        /// (0, 0, 1)이면 씬에 저장된 위치·거리 그대로다.
        /// </summary>
        void PlaceOnOrbit(Transform t, float yawOffset, float pitchOffset, float zoom)
        {
            Vector3 center = _target.center;
            Vector3 v = _sceneCam.transform.position - center;
            float baseDist = v.magnitude;
            Vector3 d0 = v / Mathf.Max(1e-6f, baseDist);

            float baseYaw = Mathf.Atan2(d0.x, d0.z) * Mathf.Rad2Deg;
            float basePitch = Mathf.Asin(Mathf.Clamp(d0.y, -1f, 1f)) * Mathf.Rad2Deg;

            float yaw = (baseYaw + yawOffset) * Mathf.Deg2Rad;
            float pitch = Mathf.Clamp(basePitch + pitchOffset, -85f, 85f) * Mathf.Deg2Rad;
            float cp = Mathf.Cos(pitch);

            Vector3 dir = new Vector3(cp * Mathf.Sin(yaw), Mathf.Sin(pitch), cp * Mathf.Cos(yaw));
            t.position = center + dir * (baseDist * zoom);
            t.LookAt(center);
        }

        /// <summary>
        /// 씬 카메라를 그대로 복사한 임시 카메라. 씬 오브젝트를 건드리지 않으므로
        /// 검증하다가 실수로 카메라 위치를 저장해 버리는 사고가 없다.
        /// </summary>
        Camera TempCamera()
        {
            if (_sceneCam == null) return null;

            if (_tempCam == null)
            {
                var go = new GameObject("~TitleAuditCamera") { hideFlags = HideFlags.HideAndDontSave };
                _tempCam = go.AddComponent<Camera>();
                go.AddComponent<UniversalAdditionalCameraData>();
            }

            var target = _tempCam.targetTexture;
            _tempCam.CopyFrom(_sceneCam);          // FOV·클립·클리어컬러·HDR까지 게임과 동일
            _tempCam.targetTexture = target;       // CopyFrom이 덮어쓰므로 되돌린다
            _tempCam.enabled = false;              // 수동 Render()만 사용

            // 포스트 처리(블룸·비네트·톤매핑) 설정도 맞춰야 실제 화면과 같은 그림이 나온다.
            var src = _sceneCam.GetComponent<UniversalAdditionalCameraData>();
            var dst = _tempCam.GetComponent<UniversalAdditionalCameraData>();
            if (src != null && dst != null)
            {
                dst.renderPostProcessing = src.renderPostProcessing;
                dst.antialiasing = src.antialiasing;
                dst.antialiasingQuality = src.antialiasingQuality;
                dst.volumeLayerMask = src.volumeLayerMask;
                dst.volumeTrigger = src.volumeTrigger;
                dst.renderShadows = src.renderShadows;
            }
            return _tempCam;
        }

        void RenderTo(Camera cam, int width, int height)
        {
            if (_rt == null || _rt.width != width || _rt.height != height)
            {
                if (_rt != null) { _rt.Release(); DestroyImmediate(_rt); }
                _rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            }
            cam.targetTexture = _rt;
            cam.Render();
            cam.targetTexture = null;
        }

        // ── 저장 ──────────────────────────────────────────────────────────────

        void CaptureAll()
        {
            var cam = TempCamera();
            if (cam == null) return;

            string dir = OutDir;
            Directory.CreateDirectory(dir);

            for (int i = 0; i < TurntableShots; i++)
            {
                PlaceOnOrbit(cam.transform, 360f * i / TurntableShots, _pitch, _zoom);
                SavePng(cam, Path.Combine(dir, $"turntable_{i:00}.png"));
            }

            // 좌·우 눈 위치에서 한 장씩. 두 장의 차이(시차)가 곧 깊이의 증거다.
            // 로봇이 50배로 커졌으므로 사람 눈 간격(6.5cm)으로는 시차가 보이지 않는다 → 피사체 크기에 비례시킨다.
            float eye = _target.size.y * StereoBaselineRatio;
            PlaceOnOrbit(cam.transform, _yaw, _pitch, _zoom);
            Vector3 pos = cam.transform.position;
            Vector3 right = cam.transform.right;
            cam.transform.position = pos - right * (eye * 0.5f);
            SavePng(cam, Path.Combine(dir, "stereo_L.png"));
            cam.transform.position = pos + right * (eye * 0.5f);
            SavePng(cam, Path.Combine(dir, "stereo_R.png"));

            Debug.Log($"[TitleSceneAudit] 촬영 완료 → {dir}\n" +
                      $"  · turntable_00~{TurntableShots - 1:00}.png : 각도마다 실루엣이 달라지면 3D 모델이다.\n" +
                      "  · stereo_L/R.png : 번갈아 보면 로봇과 배경이 다르게 밀린다(시차 = 깊이).");
            EditorUtility.RevealInFinder(dir + Path.DirectorySeparatorChar);
            Invalidate();   // 촬영하느라 옮긴 임시 카메라를 현재 궤도 시점으로 되돌려 다시 그린다
        }

        void SavePng(Camera cam, string path)
        {
            var rt = new RenderTexture(ShotWidth, ShotHeight, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var prevActive = RenderTexture.active;
            var tex = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false);
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0f, 0f, ShotWidth, ShotHeight), 0, 0);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.active = prevActive;
                DestroyImmediate(tex);
                rt.Release();
                DestroyImmediate(rt);
            }
        }

        // ── 리포트 ────────────────────────────────────────────────────────────

        string BuildDepthReport()
        {
            var cam = _sceneCam;
            var sb = new StringBuilder();
            sb.AppendLine("[TitleSceneAudit] Title 씬 3D 깊이 리포트");
            sb.AppendLine($"  카메라: {(cam.orthographic ? "직교(ORTHOGRAPHIC — 원근 없음!)" : "원근(perspective)")}, " +
                          $"FOV {cam.fieldOfView:0.#}, near {cam.nearClipPlane:0.###}, far {cam.farClipPlane:0.#}");
            sb.AppendLine($"  카메라 위치 {V(cam.transform.position)}, 전방 {V(cam.transform.forward)}");
            if (cam.orthographic)
                sb.AppendLine("  ⚠ 직교 투영이면 원근이 없어 평면처럼 보입니다. 원근으로 바꾸세요.");

            sb.AppendLine($"  피사체 바운즈: 중심 {V(_target.center)}, 크기 {V(_target.size)}");
            sb.AppendLine($"  3D 렌더러 {_renderers.Count}개:");

            float min = float.MaxValue, max = float.MinValue;
            int shown = 0;
            foreach (var r in _renderers)
            {
                float d = Vector3.Dot(r.bounds.center - cam.transform.position, cam.transform.forward);
                min = Mathf.Min(min, d); max = Mathf.Max(max, d);
                if (shown++ < 12)
                    sb.AppendLine($"    · {r.name,-28} 거리 {d,9:0.000} m  크기 {V(r.bounds.size)}");
            }
            if (_renderers.Count > 12) sb.AppendLine($"    · … 외 {_renderers.Count - 12}개");

            float span = max - min;
            sb.AppendLine($"  깊이 분포: {min:0.000} m ~ {max:0.000} m (폭 {span:0.000} m)");
            sb.AppendLine(span > _target.size.y * 0.01f
                ? "  → 깊이가 여러 값으로 흩어져 있습니다. 평면 이미지가 아니라 3D 공간입니다."
                : "  ⚠ 깊이 폭이 거의 0입니다 — 모든 것이 한 평면에 있습니다.");

            // ── 조명 도달 검사 ────────────────────────────────────────────────
            // 어두운 씬에서 모델이 안 보이는 사고는 대부분 "빛이 애초에 피사체에 닿지 않는다"가 원인이다.
            // 라이트 위치가 바운즈 안이라고 안심하면 안 된다 — bounds.max.z는 뻗은 팔·다리일 수 있고,
            // 정작 머리는 몸통 중심선(center.z)에 있어 훨씬 멀다. 여기서 실제 거리 대 사거리를 찍는다.
            float subjectDepth = Vector3.Dot(_target.center - cam.transform.position, cam.transform.forward);
            Vector3 headPoint = new Vector3(_target.center.x,
                                            _target.max.y - _target.size.y * 0.12f,
                                            _target.center.z);
            sb.AppendLine($"  조명 도달 검사 (기준점 = 머리 근사 {V(headPoint)}):");

            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var l in root.GetComponentsInChildren<Light>(true))
                {
                    float d = Vector3.Dot(l.transform.position - cam.transform.position, cam.transform.forward);
                    string side = d > subjectDepth ? "피사체 뒤(후광)" : "피사체 앞(정면광)";
                    sb.AppendLine($"  라이트 {l.name,-18} {l.type,-6} 세기 {l.intensity:0.##} " +
                                  $"사거리 {l.range:0.##} (키의 {l.range / Mathf.Max(1e-6f, _target.size.y):0.0%})  {side}");

                    if (l.type == LightType.Directional) continue;

                    float dist = Vector3.Distance(l.transform.position, headPoint);
                    bool inRange = dist <= l.range;
                    sb.AppendLine($"      머리까지 {dist:0.##} m / 사거리 {l.range:0.##} m → " +
                                  (inRange
                                      ? $"닿음 (감쇠 후 세기 ≈ {l.intensity / (1f + 25f * (dist / l.range) * (dist / l.range)):0.###})"
                                      : "⚠ 닿지 않음 — 이 라이트는 머리를 전혀 비추지 못합니다"));

                    if (l.type == LightType.Spot)
                    {
                        float ang = Vector3.Angle(l.transform.forward, headPoint - l.transform.position);
                        sb.AppendLine($"      콘 중심에서 {ang:0.#}° / 반각 {l.spotAngle * 0.5f:0.#}° → " +
                                      (ang <= l.spotAngle * 0.5f
                                          ? $"콘 안 (머리 위치 반경 {dist * Mathf.Tan(l.spotAngle * 0.5f * Mathf.Deg2Rad):0.#} m만 밝아짐)"
                                          : "⚠ 콘 밖 — 스포트가 머리를 빗나갑니다"));
                    }
                }
            }
            return sb.ToString();
        }

        static string V(Vector3 v) => $"({v.x:0.##}, {v.y:0.##}, {v.z:0.##})";

        static Bounds WorldBounds(List<Renderer> renderers)
        {
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Count; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }
    }
}
