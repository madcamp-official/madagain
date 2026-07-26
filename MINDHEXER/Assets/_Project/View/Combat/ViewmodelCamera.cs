using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Game.View
{
    /// <summary>
    /// 1인칭 뷰모델(팔·칼) 전용 카메라. FPS 표준 기법이다.
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
    // ★ 실행 순서를 Brain 뒤로 민다.
    //   오버레이 카메라는 메인 카메라의 FOV를 복사하는데, 메인 FOV는 CinemachineBrain이
    //   LateUpdate에서 갱신한다. 순서가 앞서면 <b>직전 프레임 FOV</b>를 쓰게 되어,
    //   FOV 킥이 들어오는 동안 팔·칼만 한 프레임씩 어긋나 덜덜 떨린다.
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public class ViewmodelCamera : MonoBehaviour
    {
        public const string DefaultLayer = "Viewmodel";
        public const string CamObjectName = "[ViewmodelCam]";

        public static ViewmodelCamera Instance { get; private set; }

        [Header("레이어")]
        [Tooltip("뷰모델을 올릴 전용 레이어 이름")]
        public string layerName = DefaultLayer;

        [Header("클리핑")]
        // 낮추면 가까운 부분을 더 보여주고, 높이면 깔끔하게 잘라낸다.
        // 팔뿌리(어깨)가 카메라를 뚫고 지나가는 구조에서는 오히려 높게 잡아
        // 지저분한 단면을 아예 안 보이게 하는 편이 낫다 — 그래서 기본 0.2.
        [Tooltip("뷰모델 카메라 근평면. 낮추면 가까운 팔이 더 보이고, 높이면 지저분한 어깨 단면을 깔끔히 잘라낸다")]
        public float nearClip = DefNearClip;
        public float farClip = 12f;

        [Header("카메라 뒤 지오메트리 살리기")]
        [Tooltip("카메라를 이만큼 뒤로 물린다(m). 카메라 뒤에 있던 어깨·팔꿈치가 화면에 들어온다")]
        [Range(0f, 3f)] public float pullBack = 0f;
        [Tooltip("뒤로 물린 만큼 FOV를 좁혀 뷰모델 크기를 유지한다")]
        public bool autoFov = true;
        [Tooltip("크기를 맞출 기준 거리(m) — 보통 칼이 있는 거리")]
        public float refDist = 0.6f;

        Camera baseCam, vmCam;
        Transform vmRoot;
        int layer = -1;
        bool fallbackMode;      // 레이어가 없어 근평면만 낮춘 상태

        /// <summary>설치 상태 요약(F2 표시용).</summary>
        public string Status =>
            vmCam != null ? $"분리됨 (레이어 {layerName}, near {nearClip:0.000}, 후퇴 {pullBack:0.00}m)"
            : fallbackMode ? $"<color=#ffb060>대체 모드</color> — '{layerName}' 레이어 없음. Tools/뷰모델/① 로 생성"
            : "설치 대기";

        // ── 저장 (Play 중 바꾼 값은 씬에 남지 않는다 — 파일로 남긴다) ──
        [System.Serializable] class Saved { public float nearClip, farClip, pullBack, refDist; public bool autoFov; }

        public static string SavePath => "Assets/_Project/Poses/viewmodelcam.json";

        void Awake()
        {
            Instance = this;
            LoadFromDisk();
        }

        /// <summary>현재 값을 파일로. 다음 Play부터 자동 적용된다.</summary>
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

        void LateUpdate()
        {
            if (vmCam == null) { TryInstall(); return; }
            Sync();
        }

        // ── 설치 ──
        void TryInstall()
        {
            // VR: URP 오버레이 카메라는 XR 스테레오를 지원하지 않아 한쪽 눈에만 렌더된다(스테레오 파괴).
            //     VR 모드에선 뷰모델 오버레이를 설치하지 않는다. (추후 XR 카메라 안 근거리 레이어로 재구현)
            if (VrMode.Enabled) return;

            baseCam = ResolveBaseCam();
            if (baseCam == null) return;

            vmRoot = FindViewmodel(baseCam);
            if (vmRoot == null) return;          // SwordView가 아직 안 만들었으면 다음 프레임에 재시도

            layer = LayerMask.NameToLayer(layerName);
            if (layer < 0)
            {
                // 레이어가 없으면 분리 불가 — 근평면만 낮춰 최소한의 완화.
                // (레이어가 나중에 생기면 다음 프레임에 자동으로 정식 설치된다)
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
            baseCam.cullingMask &= ~(1 << layer);          // 메인은 뷰모델을 그리지 않는다

            // 오버레이 카메라 생성
            Transform t = baseCam.transform.Find(CamObjectName);
            GameObject go = t != null ? t.gameObject : new GameObject(CamObjectName);
            go.transform.SetParent(baseCam.transform, false);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            vmCam = go.GetComponent<Camera>();
            if (vmCam == null) vmCam = go.AddComponent<Camera>();
            vmCam.cullingMask = 1 << layer;
            vmCam.clearFlags = CameraClearFlags.Depth;
            vmCam.depth = baseCam.depth + 1;
            vmCam.allowMSAA = baseCam.allowMSAA;

            // URP 오버레이로 등록 — renderType을 먼저 바꾼 뒤 스택에 넣어야 경고가 안 뜬다
            var vmData = vmCam.GetUniversalAdditionalCameraData();
            vmData.renderType = CameraRenderType.Overlay;
            vmData.renderShadows = false;
            // 포스트프로세싱은 뷰모델에 끈다 — 켜면 Bloom이 밝은 팔 가장자리를 번지게 해서
            // "합성한 듯한 흰 테두리(halo)"가 생긴다. 대신 SMAA로 테두리만 다듬는다.
            vmData.renderPostProcessing = false;
            vmData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            var baseData = baseCam.GetUniversalAdditionalCameraData();
            if (baseData != null && !baseData.cameraStack.Contains(vmCam))
                baseData.cameraStack.Add(vmCam);

            fallbackMode = false;
            Sync();
            Debug.Log($"[ViewmodelCamera] 설치 완료 — {Status}");
        }

        /// <summary>매 프레임 근평면·후퇴·FOV를 반영(F2에서 실시간 튜닝 가능하게).</summary>
        void Sync()
        {
            if (baseCam == null) { baseCam = ResolveBaseCam(); if (baseCam == null) return; }

            // 뷰모델이 새로 생겼으면(vm 명령·씬 전환) 다시 찾아 레이어를 입힌다
            if (vmRoot == null && layer >= 0)
            {
                vmRoot = FindViewmodel(baseCam);
                if (vmRoot != null) SetLayerRecursive(vmRoot, layer);
            }

            vmCam.nearClipPlane = Mathf.Max(0.001f, nearClip);
            vmCam.farClipPlane  = Mathf.Max(nearClip + 0.1f, farClip);
            vmCam.transform.localPosition = new Vector3(0f, 0f, -Mathf.Max(0f, pullBack));

            // 뒤로 물린 만큼 FOV를 좁혀 기준 거리에서의 크기를 유지한다.
            //   원래:  H = refDist * tan(fov0/2)
            //   후퇴:  H = (refDist + pullBack) * tan(fov1/2)   → 같은 H가 되도록 fov1 계산
            if (autoFov && pullBack > 0.0001f)
            {
                float half = baseCam.fieldOfView * 0.5f * Mathf.Deg2Rad;
                float t = Mathf.Tan(half) * refDist / (refDist + pullBack);
                vmCam.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(t) * Mathf.Rad2Deg, 1f, 179f);
            }
            else vmCam.fieldOfView = baseCam.fieldOfView;
        }

        // 권장 기본값 — 어깨가 카메라를 뚫고 지나가는 구조라
        // 근평면을 높여 지저분한 단면을 잘라내는 쪽이 깔끔하다(후퇴는 쓰지 않음).
        public const float DefNearClip = 0.151f;
        public const float DefPullBack = 0f;
        public const float DefRefDist  = 0.6f;

        /// <summary>권장 기본값으로 되돌린다.
        /// 씬에 저장된 컴포넌트는 <b>직렬화된 값이 코드 기본값을 덮으므로</b>,
        /// 값을 바꾼 뒤에는 이걸로 명시적으로 맞춰야 한다.</summary>
        public void ResetToDefaults()
        {
            nearClip = DefNearClip;
            pullBack = DefPullBack;
            refDist  = DefRefDist;
            autoFov  = true;
        }

        /// <summary>현재 값이 권장 기본값과 같은가(F2 표시용).</summary>
        public bool IsDefault =>
            Mathf.Approximately(nearClip, DefNearClip) &&
            Mathf.Approximately(pullBack, DefPullBack);

        /// <summary>뷰모델이 새로 만들어졌을 때 캐시를 버리고 다시 설치되게 한다.
        /// (오버레이 카메라는 그대로 두고 레이어만 다시 입히면 되므로 vmRoot만 비운다)</summary>
        public void ForgetRoot()
        {
            vmRoot = null;
            fallbackMode = false;
        }

        /// <summary>뷰모델에 자식이 추가된 뒤 레이어를 다시 입힌다.</summary>
        public void RefreshLayers()
        {
            if (vmRoot == null) vmRoot = baseCam != null ? FindViewmodel(baseCam) : null;
            if (vmRoot == null || layer < 0) return;
            SetLayerRecursive(vmRoot, layer);
        }

        // ── 진단 (F2에서 숫자로 확인 — 추측하지 않기 위한 장치) ──

        public int  LayerIndex   => layer;
        public bool LayerExists  => LayerMask.NameToLayer(layerName) >= 0;
        public bool HasOverlay   => vmCam != null;

        /// <summary>오버레이 카메라가 메인 카메라 스택에 실제로 등록돼 있는가.</summary>
        public bool IsStacked()
        {
            if (baseCam == null || vmCam == null) return false;
            var d = baseCam.GetUniversalAdditionalCameraData();
            return d != null && d.cameraStack != null && d.cameraStack.Contains(vmCam);
        }

        /// <summary>메인 카메라가 뷰모델 레이어를 (제대로) 안 그리고 있는가.</summary>
        public bool BaseExcludesLayer =>
            baseCam != null && layer >= 0 && (baseCam.cullingMask & (1 << layer)) == 0;

        /// <summary>뷰모델 렌더러 중 전용 레이어에 있지 않은 개수. -1 = 확인 불가.</summary>
        public int CountWrongLayer()
        {
            if (vmRoot == null || layer < 0) return -1;
            int n = 0;
            foreach (var r in vmRoot.GetComponentsInChildren<Renderer>(true))
                if (r.gameObject.layer != layer) n++;
            return n;
        }

        /// <summary>뷰모델에서 <b>메인 카메라 기준 가장 가까운 지점의 깊이(m)</b>.
        /// 음수면 그만큼 카메라 뒤에 있다는 뜻 — 근평면으로는 절대 못 고치는 부분이다.</summary>
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

        /// <summary>측정된 최근접 깊이를 근거로 후퇴량을 자동 결정한다.
        /// 뷰모델 카메라 기준으로 모든 지오메트리가 근평면 앞에 오도록 만든다.</summary>
        public bool AutoFitPullBack(float margin = 0.08f)
        {
            float z = NearestZ();
            if (float.IsNaN(z)) return false;
            // 점 z(메인 기준)는 뷰모델 카메라 기준으로 z + pullBack.
            // z + pullBack >= nearClip + margin  →  pullBack >= nearClip + margin - z
            pullBack = Mathf.Clamp(nearClip + margin - z, 0f, 3f);
            return true;
        }

        // ── 유틸 ──
        static Camera ResolveBaseCam()
        {
            var main = Main.Instance;
            var c = main != null ? main.Cam : null;
            return c != null ? c : Camera.main;
        }

        static Transform FindViewmodel(Camera cam)
        {
            var t = cam.transform.Find("KatanaViewmodel");
            if (t != null) return t;
            var go = GameObject.Find("KatanaViewmodel");
            return go != null ? go.transform : null;
        }

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
