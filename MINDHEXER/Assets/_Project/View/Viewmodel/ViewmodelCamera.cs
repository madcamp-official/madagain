using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Game.View
{
    /// <summary>
    /// 1인칭 뷰모델(팔) 전용 카메라. FPS 표준 기법이다. (Precog에서 포팅 — 원본은 카타나 전용이었음)
    ///
    /// 왜 필요한가 — 뷰모델은 카메라에서 수십 cm 안쪽에 있어서
    ///   ① 메인 카메라의 근평면(보통 0.05~0.3m)에 잘려 <b>단면이 뚫려 보이고</b>
    ///   ② 벽·적에 파고들어 <b>지오메트리를 관통</b>한다.
    /// 근평면을 그냥 낮추면 월드 전체의 깊이 정밀도가 나빠져 z-fighting이 생긴다.
    ///
    /// 그래서 뷰모델만 <b>별도 레이어 + 별도 카메라</b>로 분리해
    /// URP 오버레이 스택으로 월드 위에 덧그린다.
    ///   · 근평면 0.01 — 뷰모델 카메라에만 적용되므로 월드 정밀도 손해 없음
    ///   · 항상 위에 그려짐 — 벽 관통 해결
    ///   · pullBack — 카메라를 뒤로 물려 <b>카메라 뒤에 있는 어깨·팔꿈치까지</b> 담는다
    ///     (근평면으로는 절대 못 고치는 부분. FOV는 자동 보정해 크기를 유지한다)
    ///
    /// 레이어가 없으면 설치를 건너뛰고 근평면만 낮추는 <b>대체 모드</b>로 동작한다.
    /// 레이어 생성은 Tools/뷰모델/① 뷰모델 카메라 설치 에서 한다.
    /// </summary>
    [DefaultExecutionOrder(1000)]   // CinemachineBrain류가 FOV를 갱신한 뒤에 돌게(있다면)
    [DisallowMultipleComponent]
    [ExecuteAlways]                 // ★ Play 중에만 설치되면 '자세 잡는 내내' 팔이 잘리고 벽에 파묻힌다
    public class ViewmodelCamera : MonoBehaviour
    {
        public const string DefaultLayer = "Viewmodel";
        public const string CamObjectName = "[ViewmodelCam]";

        /// <summary>씬에서 뷰모델 루트를 찾을 이름. 못 찾으면 카메라의 첫 자식으로 대체.</summary>
        public const string ViewmodelRootName = "Viewmodel";

        public static ViewmodelCamera Instance { get; private set; }

        [Header("레이어")]
        [Tooltip("뷰모델을 올릴 전용 레이어 이름")]
        public string layerName = DefaultLayer;

        [Header("클리핑")]
        [Tooltip("뷰모델 카메라 근평면. 낮추면 가까운 팔이 더 보이고, 높이면 지저분한 어깨 단면을 깔끔히 잘라낸다")]
        public float nearClip = DefNearClip;
        public float farClip = 12f;

        [Header("VR — 오버레이를 쓸 수 없는 경로")]
        [Tooltip("VR에선 URP 오버레이 카메라가 XR 스테레오를 지원하지 않아 한쪽 눈에만 렌더된다. " +
                 "그래서 메인 카메라의 근평면을 직접 낮춘다 — 손가락은 카메라에서 몇 cm 앞이라 " +
                 "0.15로는 통째로 잘린다.")]
        public float vrNearClip = 0.03f;
        [Tooltip("근평면을 낮추면 깊이 정밀도가 나빠진다(z-fighting). 원평면을 함께 줄여 되찾는다. " +
                 "0 이하면 건드리지 않는다.")]
        public float vrFarClip = 400f;

        [Header("카메라 뒤 지오메트리 살리기")]
        [Tooltip("카메라를 이만큼 뒤로 물린다(m). 카메라 뒤에 있던 어깨·팔꿈치가 화면에 들어온다")]
        [Range(0f, 3f)] public float pullBack = 0f;
        [Tooltip("뒤로 물린 만큼 FOV를 좁혀 뷰모델 크기를 유지한다")]
        public bool autoFov = true;
        [Tooltip("크기를 맞출 기준 거리(m)")]
        public float refDist = 0.6f;

        Camera baseCam, vmCam;
        Transform vmRoot;
        int layer = -1;
        bool fallbackMode;
        bool _vrLogged;

        public string Status =>
            vmCam != null ? $"분리됨 (레이어 {layerName}, near {nearClip:0.000}, 후퇴 {pullBack:0.00}m)"
            : VrMode.Enabled ? $"<color=#ffb060>VR 경로</color> — 오버레이 불가, 메인 근평면 {vrNearClip:0.000} / 원평면 {vrFarClip:0}"
            : fallbackMode ? $"<color=#ffb060>대체 모드</color> — '{layerName}' 레이어 없음. Tools/뷰모델/① 로 생성"
            : "설치 대기";

        [System.Serializable] class Saved { public float nearClip, farClip, pullBack, refDist; public bool autoFov; }

        public static string SavePath => "Assets/_Project/Poses/viewmodelcam.json";

        void Awake()
        {
            Instance = this;
            LoadFromDisk();
        }

        public bool Save()
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SavePath));
                System.IO.File.WriteAllText(SavePath, JsonUtility.ToJson(new Saved
                {
                    nearClip = nearClip, farClip = farClip,
                    pullBack = pullBack, refDist = refDist, autoFov = autoFov
                }, true), System.Text.Encoding.UTF8);
                Debug.Log($"[뷰모델 카메라] 저장: 근평면 {nearClip:0.000} · 후퇴 {pullBack:0.00}m → {SavePath}");
                return true;
            }
            catch (System.Exception e) { Debug.LogWarning("[뷰모델 카메라] 저장 실패: " + e.Message); return false; }
        }

        public bool LoadFromDisk()
        {
            try
            {
                if (!System.IO.File.Exists(SavePath)) return false;
                var s = JsonUtility.FromJson<Saved>(System.IO.File.ReadAllText(SavePath, System.Text.Encoding.UTF8));
                if (s == null || s.nearClip <= 0f) return false;
                nearClip = s.nearClip;
                farClip  = s.farClip > 0f ? s.farClip : farClip;
                pullBack = s.pullBack;
                refDist  = s.refDist > 0f ? s.refDist : refDist;
                autoFov  = s.autoFov;
                return true;
            }
            catch { return false; }
        }

        void LateUpdate() => EnsureInstalled();

        /// <summary>
        /// 설치·동기화 진입점. 에디터에서는 <see cref="ExecuteAlways"/>의 틱이 씬 뷰 리페인트에
        /// 의존해 확실하지 않으므로, 에디터 드라이버가 <c>EditorApplication.update</c>에서 이걸 직접 부른다.
        /// </summary>
        public void EnsureInstalled()
        {
            if (vmCam == null) TryInstall();
            else Sync();
        }

        void TryInstall()
        {
            // VR: URP 오버레이 카메라는 XR 스테레오를 지원하지 않아 한쪽 눈에만 렌더된다(스테레오 파괴).
            //     그래서 VR에선 오버레이를 포기하고 메인 카메라의 근평면을 직접 낮춘다.
            if (VrMode.Enabled) { ApplyVrPath(); return; }

            baseCam = Camera.main;
            if (baseCam == null) return;

            vmRoot = FindViewmodel(baseCam);
            if (vmRoot == null) return;

            layer = LayerMask.NameToLayer(layerName);
            if (layer < 0)
            {
                if (!fallbackMode)
                {
                    fallbackMode = true;
                    baseCam.nearClipPlane = Mathf.Max(0.01f, nearClip);
                    Debug.LogWarning($"[ViewmodelCamera] '{layerName}' 레이어가 없어 카메라를 분리하지 못했습니다.\n" +
                                     "  Tools/뷰모델/① 뷰모델 카메라 설치 를 한 번 실행하십시오. (지금은 근평면만 낮춘 대체 모드)");
                }
                return;
            }

            SetLayerRecursive(vmRoot, layer);

            Transform t = baseCam.transform.Find(CamObjectName);
            GameObject go = t != null ? t.gameObject : new GameObject(CamObjectName);
            go.transform.SetParent(baseCam.transform, false);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            // 씬에 저장하지 않는다 — 에디터에서도 도는 이상, 저장되면 씬마다 유령 카메라가 쌓인다.
            // 이미 있으면 Find로 다시 잡으므로 재컴파일에도 중복되지 않는다.
            go.hideFlags = HideFlags.DontSave;

            vmCam = go.GetComponent<Camera>();
            if (vmCam == null) vmCam = go.AddComponent<Camera>();
            vmCam.cullingMask = 1 << layer;
            vmCam.clearFlags = CameraClearFlags.Depth;
            vmCam.depth = baseCam.depth + 1;
            vmCam.allowMSAA = baseCam.allowMSAA;

            var vmData = vmCam.GetUniversalAdditionalCameraData();
            vmData.renderType = CameraRenderType.Overlay;
            vmData.renderShadows = false;
            vmData.renderPostProcessing = false;
            vmData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            var baseData = baseCam.GetUniversalAdditionalCameraData();
            if (baseData != null && !baseData.cameraStack.Contains(vmCam))
                baseData.cameraStack.Add(vmCam);

            // ★ 베이스에서 레이어를 빼는 것은 <b>오버레이가 성립한 뒤에</b> 한다.
            //   먼저 빼면, 오버레이 생성이 실패했을 때 뷰모델이 어디에도 안 그려져 통째로 사라진다.
            baseCam.cullingMask &= ~(1 << layer);

            fallbackMode = false;
            Sync();
            Debug.Log($"[ViewmodelCamera] 설치 완료 — {Status}");
        }

        /// <summary>
        /// VR 경로 — 오버레이 없이 근평면만 낮춘다.
        ///
        /// <para><b>왜 이렇게밖에 못 하나</b> — 근평면 클리핑은 <b>투영 단계</b>에서 일어나므로 깊이
        /// 클리어나 렌더 순서로는 못 고친다. 근평면을 줄이는 것 말고 방법이 없고, VR에선 오버레이
        /// 카메라를 쓸 수 없다(스테레오가 깨진다). 그래서 메인 카메라를 직접 낮춘다.</para>
        ///
        /// <para>대가는 깊이 정밀도다. 근/원 비율이 정밀도를 지배하므로 <b>원평면을 함께 줄여</b>
        /// 되찾는다 — 0.03/400은 0.15/1000보다 오히려 비율이 낫다.</para>
        ///
        /// <para>오버레이가 없으니 뷰모델 레이어를 메인에서 빼면 팔이 통째로 사라진다. 다시 넣어 준다.</para>
        /// </summary>
        void ApplyVrPath()
        {
            baseCam = Camera.main;
            if (baseCam == null) return;

            int l = LayerMask.NameToLayer(layerName);
            if (l >= 0) baseCam.cullingMask |= (1 << l);

            float near = Mathf.Max(0.01f, vrNearClip);
            baseCam.nearClipPlane = near;
            if (vrFarClip > 0f) baseCam.farClipPlane = Mathf.Max(vrFarClip, near + 1f);

            if (!_vrLogged)
            {
                _vrLogged = true;
                Debug.Log($"[ViewmodelCamera] VR 경로 — 오버레이 없이 메인 근평면 {near:0.000} / 원평면 " +
                          $"{baseCam.farClipPlane:0}. 손가락이 잘리면 근평면을 더 낮추고, z-fighting이 " +
                          $"보이면 원평면을 더 줄이십시오.");
            }
        }

        void Sync()
        {
            if (baseCam == null) { baseCam = Camera.main; if (baseCam == null) return; }

            if (vmRoot == null && layer >= 0)
            {
                vmRoot = FindViewmodel(baseCam);
                if (vmRoot != null) SetLayerRecursive(vmRoot, layer);
            }

            vmCam.nearClipPlane = Mathf.Max(0.001f, nearClip);
            vmCam.farClipPlane  = Mathf.Max(nearClip + 0.1f, farClip);
            vmCam.transform.localPosition = new Vector3(0f, 0f, -Mathf.Max(0f, pullBack));

            if (autoFov && pullBack > 0.0001f)
            {
                float half = baseCam.fieldOfView * 0.5f * Mathf.Deg2Rad;
                float t = Mathf.Tan(half) * refDist / (refDist + pullBack);
                vmCam.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(t) * Mathf.Rad2Deg, 1f, 179f);
            }
            else vmCam.fieldOfView = baseCam.fieldOfView;
        }

        // ★ 0.01. 예전 0.151은 Precog에서 '지저분한 어깨 단면을 잘라내려고' 올려 둔 값이었다.
        //   전완만 쓰는 지금은 어깨가 없고, 대신 <b>손가락이 카메라에서 몇 cm 앞</b>까지 온다 —
        //   0.151이면 손가락이 통째로 잘린다. 오버레이 전용 근평면이라 월드 깊이 정밀도와 무관하다.
        public const float DefNearClip = 0.01f;
        public const float DefPullBack = 0f;
        public const float DefRefDist  = 0.6f;

        public void ResetToDefaults()
        {
            nearClip = DefNearClip;
            pullBack = DefPullBack;
            refDist  = DefRefDist;
            autoFov  = true;
        }

        public bool IsDefault =>
            Mathf.Approximately(nearClip, DefNearClip) &&
            Mathf.Approximately(pullBack, DefPullBack);

        public void ForgetRoot()
        {
            vmRoot = null;
            fallbackMode = false;
        }

        public void RefreshLayers()
        {
            if (vmRoot == null) vmRoot = baseCam != null ? FindViewmodel(baseCam) : null;
            if (vmRoot == null || layer < 0) return;
            SetLayerRecursive(vmRoot, layer);
        }

        public int  LayerIndex   => layer;
        public bool LayerExists  => LayerMask.NameToLayer(layerName) >= 0;
        public bool HasOverlay   => vmCam != null;

        public bool IsStacked()
        {
            if (baseCam == null || vmCam == null) return false;
            var d = baseCam.GetUniversalAdditionalCameraData();
            return d != null && d.cameraStack != null && d.cameraStack.Contains(vmCam);
        }

        public bool BaseExcludesLayer =>
            baseCam != null && layer >= 0 && (baseCam.cullingMask & (1 << layer)) == 0;

        public int CountWrongLayer()
        {
            if (vmRoot == null || layer < 0) return -1;
            int n = 0;
            foreach (var r in vmRoot.GetComponentsInChildren<Renderer>(true))
                if (r.gameObject.layer != layer) n++;
            return n;
        }

        /// <summary>뷰모델에서 메인 카메라 기준 가장 가까운 지점의 깊이(m). 음수면 카메라 뒤.</summary>
        public float NearestZ()
        {
            if (vmRoot == null || baseCam == null) return float.NaN;
            var ct = baseCam.transform;
            float min = float.MaxValue;
            foreach (var r in vmRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                Bounds b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x,
                                        (i & 2) == 0 ? b.min.y : b.max.y,
                                        (i & 4) == 0 ? b.min.z : b.max.z);
                    float z = ct.InverseTransformPoint(c).z;
                    if (z < min) min = z;
                }
            }
            return min == float.MaxValue ? float.NaN : min;
        }

        public bool AutoFitPullBack(float margin = 0.08f)
        {
            float z = NearestZ();
            if (float.IsNaN(z)) return false;
            pullBack = Mathf.Clamp(nearClip + margin - z, 0f, 3f);
            return true;
        }

        /// <summary>판정은 <see cref="ViewmodelRoot"/>에 위임한다 — 카메라를 뷰모델로 잡는 사고를 막는다.</summary>
        public static Transform FindViewmodel(Camera cam)
            => ViewmodelRoot.Find(cam != null ? cam.transform : null);

        public static void SetLayerRecursive(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++) SetLayerRecursive(root.GetChild(i), layer);
        }
    }

    /// <summary>Play 시 자동 부착 — 툴을 안 눌러도 동작한다(레이어는 있어야 분리됨).</summary>
    public static class ViewmodelCameraBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<ViewmodelCamera>() == null)
                new GameObject("[ViewmodelCamera]").AddComponent<ViewmodelCamera>();
        }
    }
}
