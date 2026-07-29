using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 플레이어 시점 조명 리그 — 공포게임식 손전등.
    ///
    /// <para><b>부착 위치는 Main Camera다([Head]가 아니다).</b> 카메라는 <see cref="MotionFeel"/>이
    /// 착지 킥·롤로 흔들므로, 빛이 발걸음마다 같이 흔들린다 — 손전등을 들고 뛰는 감각이 이 연출의
    /// 핵심이다. VR은 <c>vrRollScale=0</c>이 기본이라 사실상 정지하며, 그건 멀미 방지 측면에서 맞다.</para>
    ///
    /// <para><b>★ 라이트가 3개인 이유</b> — 팔은 카메라에서 30cm 안쪽이라 손전등 원뿔의 최대 강도를
    /// 정면으로 받아 <b>새하얗게 뜬다.</b> 그래서 월드용과 뷰모델용을 레이어로 갈라 놓는다(FPS 표준).
    /// 부수 효과로 뷰모델 조명이 스테이지와 무관하게 일정해져, <see cref="OneBitControl"/> 확정값의
    /// 전제였던 "라이트 없는 스튜디오" 조건이 재현된다.
    /// <list type="number">
    /// <item><b>손전등(Spot)</b> — 뷰모델 레이어 <b>제외</b>. 월드만 비춘다.</item>
    /// <item><b>필(Point)</b> — 뷰모델 레이어 <b>제외</b>. 원뿔 바깥이 완전히 죽는 것을 막는다.</item>
    /// <item><b>뷰모델 전용(Point)</b> — 뷰모델 레이어 <b>만</b>. 손·거미 전용 키 라이트.</item>
    /// </list></para>
    ///
    /// <para><b>왜 손전등을 눈 위치에서 비키나</b> — 빛과 눈이 정확히 같은 위치면 그림자가 물체 뒤로
    /// 완전히 숨어 화면에 안 보인다. 손전등인데도 평평해 보이는 원인이 이것이다. 라이트는 씬 객체라
    /// 양 눈이 같은 라이팅 결과를 보므로, <b>이 오프셋은 VR에서도 스테레오 불일치를 만들지 않는다.</b></para>
    ///
    /// <para><b>PC/VR 차이</b> — 값은 모드별로 따로 저장된다(<see cref="SavePath"/>). VR 기본값은
    /// 원뿔을 넓게 잡는다 — 좁은 원뿔은 VR에서 터널 시야가 되어 멀미를 유발한다. 또 <c>Mobile_RPAsset</c>은
    /// Additional Light Shadows가 꺼져 있어 <b>VR에선 손전등 그림자가 안 나온다</b>(Cardboard는 2배 렌더라
    /// 켜려면 그림자 해상도를 낮춰야 한다 — 실기 프레임을 보고 판단할 사항). 그래서 VR은 필을 조금 더
    /// 올려 그림자 없이도 공간이 읽히게 한다.</para>
    /// </summary>
    [DefaultExecutionOrder(1100)]   // ViewmodelCamera(1000)가 레이어를 정리한 뒤에 돈다
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public class PlayerLightRig : MonoBehaviour
    {
        public const string FlashObjectName = "[Flashlight]";
        public const string FillObjectName  = "[Fill]";
        public const string VmObjectName    = "[ViewmodelLight]";

        public static PlayerLightRig Instance { get; private set; }

        [Header("손전등 (Spot · 월드 전용)")]
        public bool flashlightOn = true;
        [Tooltip("세기")] public float spotIntensity = 6f;
        [Tooltip("사거리(m)")] public float spotRange = 25f;
        [Tooltip("바깥 각도(°). VR에선 좁으면 터널 시야가 되어 멀미를 유발한다")]
        [Range(1f, 179f)] public float spotOuterAngle = 45f;
        [Tooltip("안쪽 각도(°). 이 안쪽은 감쇠 없이 균일하다")]
        [Range(0f, 179f)] public float spotInnerAngle = 20f;
        public Color spotColor = Color.white;
        [Tooltip("눈에서 비킨 정도(m). ★ 0이면 그림자가 물체 뒤에 완전히 숨어 평평해 보인다")]
        public Vector3 spotOffset = new Vector3(0.15f, -0.10f, 0f);
        [Tooltip("그림자. VR(Mobile_RPAsset)은 Additional Light Shadows가 꺼져 있어 켜도 안 나온다")]
        public bool spotShadows = true;

        [Header("필 (Point · 월드 전용)")]
        [Tooltip("원뿔 바깥이 완전히 죽는 것을 막는다. 올리면 공포감이 옅어진다")]
        public float fillIntensity = 0.6f;
        public float fillRange = 4f;
        public Color fillColor = Color.white;

        [Header("뷰모델 전용 (Point · 손·거미)")]
        [Tooltip("월드 조명과 무관하게 손을 항상 일정하게 보이게 한다")]
        public float vmIntensity = 2f;
        public float vmRange = 2.5f;
        public Color vmColor = Color.white;
        [Tooltip("카메라 기준 위치. 정면이 아니라 비스듬해야 손의 형태가 드러난다")]
        public Vector3 vmOffset = new Vector3(-0.2f, 0.25f, 0.15f);

        [Header("레이어")]
        [Tooltip("뷰모델 레이어 이름. 월드 라이트는 이 레이어를 빼고, 뷰모델 라이트는 이 레이어만 비춘다")]
        public string viewmodelLayerName = ViewmodelCamera.DefaultLayer;

        Light _spot, _fill, _vm;

        public bool Installed => _spot != null && _fill != null && _vm != null;

        public string Status =>
            !Installed ? "설치 대기"
            : $"{(flashlightOn ? "손전등 켜짐" : "손전등 꺼짐")} · 원뿔 {spotOuterAngle:0}° / {spotRange:0}m" +
              $" · 뷰모델 레이어 {LayerIndex}" +
              (LayerIndex < 0 ? "  <color=#ffb060>(레이어 없음 — 분리 안 됨)</color>" : "");

        public int LayerIndex => LayerMask.NameToLayer(viewmodelLayerName);

        // ── 저장 ────────────────────────────────────────────────────────────────
        // 값은 모드별로 따로 둔다. 한 파일에 섞으면 PC에서 맞춘 값이 VR을 망가뜨린다.
        public static string SavePath =>
            VrMode.Enabled ? "Assets/_Project/Poses/lightrig_vr.json"
                           : "Assets/_Project/Poses/lightrig_pc.json";

        [System.Serializable]
        class Saved
        {
            public bool flashlightOn;
            public float spotIntensity, spotRange, spotOuterAngle, spotInnerAngle;
            public Color spotColor; public Vector3 spotOffset; public bool spotShadows;
            public float fillIntensity, fillRange; public Color fillColor;
            public float vmIntensity, vmRange; public Color vmColor; public Vector3 vmOffset;
        }

        void Awake()
        {
            Instance = this;
            if (!LoadFromDisk()) ApplyDefaults(VrMode.Enabled);
        }

        /// <summary>
        /// 모드별 기본값. VR은 원뿔을 넓게, 필을 밝게 — 그림자가 없어도 공간이 읽혀야 하기 때문.
        /// </summary>
        public void ApplyDefaults(bool vr)
        {
            flashlightOn   = true;
            spotIntensity  = 6f;
            spotRange      = 25f;
            spotOuterAngle = vr ? 60f : 45f;
            spotInnerAngle = vr ? 28f : 20f;
            spotColor      = Color.white;
            spotOffset     = new Vector3(0.15f, -0.10f, 0f);
            spotShadows    = !vr;          // VR은 Mobile_RPAsset이 additional shadow를 꺼 뒀다

            fillIntensity  = vr ? 1.0f : 0.6f;
            fillRange      = vr ? 5f : 4f;
            fillColor      = Color.white;

            vmIntensity    = 2f;
            vmRange        = 2.5f;
            vmColor        = Color.white;
            vmOffset       = new Vector3(-0.2f, 0.25f, 0.15f);
        }

        public bool Save()
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SavePath));
                System.IO.File.WriteAllText(SavePath, JsonUtility.ToJson(new Saved
                {
                    flashlightOn = flashlightOn,
                    spotIntensity = spotIntensity, spotRange = spotRange,
                    spotOuterAngle = spotOuterAngle, spotInnerAngle = spotInnerAngle,
                    spotColor = spotColor, spotOffset = spotOffset, spotShadows = spotShadows,
                    fillIntensity = fillIntensity, fillRange = fillRange, fillColor = fillColor,
                    vmIntensity = vmIntensity, vmRange = vmRange, vmColor = vmColor, vmOffset = vmOffset
                }, true), System.Text.Encoding.UTF8);
                Debug.Log($"[조명 리그] 저장 → {SavePath}");
                return true;
            }
            catch (System.Exception e) { Debug.LogWarning("[조명 리그] 저장 실패: " + e.Message); return false; }
        }

        public bool LoadFromDisk()
        {
            try
            {
                if (!System.IO.File.Exists(SavePath)) return false;
                var s = JsonUtility.FromJson<Saved>(System.IO.File.ReadAllText(SavePath, System.Text.Encoding.UTF8));
                if (s == null || s.spotRange <= 0f) return false;
                flashlightOn = s.flashlightOn;
                spotIntensity = s.spotIntensity; spotRange = s.spotRange;
                spotOuterAngle = s.spotOuterAngle; spotInnerAngle = s.spotInnerAngle;
                spotColor = s.spotColor; spotOffset = s.spotOffset; spotShadows = s.spotShadows;
                fillIntensity = s.fillIntensity; fillRange = s.fillRange; fillColor = s.fillColor;
                vmIntensity = s.vmIntensity; vmRange = s.vmRange; vmColor = s.vmColor; vmOffset = s.vmOffset;
                return true;
            }
            catch { return false; }
        }

        // ── 구동 ────────────────────────────────────────────────────────────────

        void OnEnable()   => EnsureInstalled();
        void LateUpdate() => EnsureInstalled();

        // ★ OnValidate에서는 오브젝트를 만들지 않는다 — 유니티가 OnValidate 중의 생성·파괴를
        //   금지하기 때문에 GameObject가 안 만들어지고 뒤에서 MissingComponentException으로 터진다.
        //   값만 반영하고, 설치는 다음 틱(LateUpdate/에디터 드라이버)에 맡긴다.
        void OnValidate() { if (Installed) Sync(); }

        /// <summary>
        /// 설치·동기화 진입점. 에디터에서는 <see cref="ExecuteAlways"/>의 틱이 씬 뷰 리페인트에
        /// 의존해 확실하지 않으므로, 에디터 드라이버가 이걸 직접 부른다(<see cref="ViewmodelCamera"/>와 같은 이유).
        /// </summary>
        public void EnsureInstalled()
        {
            if (!Installed) Install();
            if (!Installed) return;   // 만들다 실패했으면 아무것도 하지 않는다
            Sync();
        }

        void Install()
        {
            _spot = MakeLight(FlashObjectName, LightType.Spot);
            _fill = MakeLight(FillObjectName,  LightType.Point);
            _vm   = MakeLight(VmObjectName,    LightType.Point);
        }

        Light MakeLight(string name, LightType type)
        {
            Transform t = transform.Find(name);
            GameObject go = t != null ? t.gameObject : new GameObject(name);
            if (go == null) return null;

            go.transform.SetParent(transform, false);

            // 씬에 저장하지 않는다 — 에디터에서도 도는 이상, 저장되면 씬마다 유령 라이트가 쌓인다.
            // 이미 있으면 Find로 다시 잡으므로 재컴파일에도 중복되지 않는다.
            go.hideFlags = HideFlags.DontSave;

            // ★ `??`를 쓰면 안 된다. GetComponent는 컴포넌트가 없을 때 <b>가짜 null</b>을 돌려주는데
            //   `??`는 유니티가 오버로드한 `==`를 거치지 않아 그 가짜 null을 그대로 통과시킨다.
            //   그러면 AddComponent가 아예 호출되지 않고, 뒤에서 MissingComponentException으로 터진다.
            Light L = go.GetComponent<Light>();
            if (L == null) L = go.AddComponent<Light>();
            if (L == null) return null;

            L.type = type;
            return L;
        }

        void Sync()
        {
            if (!Installed) return;

            int layer = LayerIndex;
            // 레이어가 없으면 분리를 포기한다 — 전부를 비추는 편이, 손이 통째로 사라지는 것보다 낫다.
            int worldMask = layer >= 0 ? ~(1 << layer) : -1;
            int vmMask    = layer >= 0 ? (1 << layer)  : 0;

            _spot.transform.localPosition = spotOffset;
            _spot.transform.localRotation = Quaternion.identity;
            _spot.enabled          = flashlightOn;
            _spot.intensity        = spotIntensity;
            _spot.range            = Mathf.Max(0.1f, spotRange);
            _spot.spotAngle        = spotOuterAngle;
            _spot.innerSpotAngle   = Mathf.Min(spotInnerAngle, spotOuterAngle - 1f);
            _spot.color            = spotColor;
            _spot.cullingMask      = worldMask;
            _spot.shadows          = spotShadows ? LightShadows.Soft : LightShadows.None;
            _spot.renderMode       = LightRenderMode.ForcePixel;   // 픽셀 단위여야 원뿔 경계가 산다

            _fill.transform.localPosition = Vector3.zero;
            _fill.intensity   = fillIntensity;
            _fill.range       = Mathf.Max(0.1f, fillRange);
            _fill.color       = fillColor;
            _fill.cullingMask = worldMask;
            _fill.shadows     = LightShadows.None;
            _fill.renderMode  = LightRenderMode.ForcePixel;

            _vm.transform.localPosition = vmOffset;
            _vm.intensity   = vmIntensity;
            _vm.range       = Mathf.Max(0.1f, vmRange);
            _vm.color       = vmColor;
            _vm.cullingMask = vmMask;
            _vm.shadows     = LightShadows.None;    // 손이 자기 그림자로 지저분해질 이유가 없다
            _vm.renderMode  = LightRenderMode.ForcePixel;
        }
    }
}
