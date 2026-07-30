using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.View
{
    /// <summary>
    /// VR 튜닝 값 묶음(직렬화). 기기·뷰어 도착 시 여기 값만 맞추면 된다.
    /// 소프트값(폰/HUD)은 실시간 적용, 렌즈값은 Step 2에서 Cardboard 프로파일로 적용.
    /// </summary>
    [System.Serializable]
    public class VrTuningData
    {
        [Header("폰 / 카메라")]
        [Tooltip("눈높이(카메라 로컬 Y, m).")]
        public float eyeHeight = 1.6f;
        [Tooltip("URP 렌더 스케일 — 폰 성능/발열에 맞춰. 1=풀, 낮추면 가벼워짐.")]
        [Range(0.3f, 1.2f)] public float renderScale = 1.0f;

        [Header("HUD (머리 앞 패널)")]
        [Tooltip("HUD 패널 거리(m).")]
        public float hudDistance = 1.3f;
        [Tooltip("HUD 패널 세로 크기(m).")]
        public float hudPanelHeight = 1.5f;

        [Header("렌즈 프로파일 (Step 2에서 Cardboard 프로파일로 적용 — 지금은 저장만)")]
        [Tooltip("IPD = 두 렌즈 간격(m).")]
        public float interLensDistance = 0.064f;
        [Tooltip("화면-렌즈 거리(m).")]
        public float screenToLensDistance = 0.042f;
        [Tooltip("트레이-렌즈 수직거리(m).")]
        public float trayToLensDistance = 0.035f;
        [Tooltip("시야각(도): 좌·우·하·상.")]
        public float fovLeft = 40f, fovRight = 40f, fovBottom = 40f, fovTop = 40f;
        [Tooltip("배럴 왜곡 계수 k1·k2.")]
        public float distortionK1 = 0.34f, distortionK2 = 0.55f;
        [Tooltip("폴백: WWGC로 만든 Cardboard 프로파일 URI를 넣으면 그대로 사용(Step 2).")]
        public string cardboardProfileUri = "";
    }

    /// <summary>
    /// VR 튜닝 툴 — <see cref="VrTuningData"/>를 JSON으로 저장/로드하고 실시간 적용한다.
    ///
    /// <para>사용: 인스펙터에서 값 조정(Play 중 즉시 반영) 또는 JSON 직접 편집 →
    /// 컨텍스트 메뉴 <b>Save JSON</b>으로 <c>persistentDataPath/vr_tuning.json</c>에 저장.
    /// 다음 실행부터 그 값이 기본이 된다.</para>
    ///
    /// <para>렌즈값 적용(Cardboard 프로파일 생성·주입)은 Step 2. 지금은 폰/HUD 소프트값만 적용한다.</para>
    /// </summary>
    public class VrTuning : MonoBehaviour
    {
        public VrTuningData Data = new VrTuningData();

        [Tooltip("눈높이 적용 대상 = XR 카메라 transform. GameBoot이 세팅.")]
        public Transform head;

        [Tooltip("리그가 이미 확보한 기본 눈높이(m). 통합 리그는 몸 원점이 눈높이라 카메라 로컬은 " +
                 "0이 기본 — 튜닝값과 이 값의 차이만 카메라 로컬에 반영한다. GameBoot이 세팅.")]
        public float eyeBase = 0f;

        /// <summary>빙의 중 [Head]에 얹는 추가 리프트(m). ViewEntryController가 소유·기록한다 —
        /// 여기선 그냥 Apply()가 eyeBase 위에 한 겹 더 얹을 값을 들고 있을 뿐이다.</summary>
        float _possessLift;

        /// <summary>빙의 진입/복귀 시 ViewEntryController가 부른다. 0으로 부르면 원복.</summary>
        public void SetPossessLift(float lift)
        {
            _possessLift = lift;
            ApplyHead();
        }

        /// <summary>기상 연출이 얹는 [Head] 오프셋(m). 일어나는 동안 y가 올라오고 x가 흔들린다.</summary>
        Vector3 _wakeOffset;

        /// <summary>
        /// 기상 연출(<see cref="WakeUpSequence"/>)이 <b>매 프레임</b> 부른다. 0으로 부르면 원복.
        ///
        /// <para><see cref="Apply"/>가 아니라 <see cref="ApplyHead"/>만 부른다 — Apply는
        /// renderScale·Cardboard 프로파일까지 건드리므로 매 프레임 부를 것이 아니다.</para>
        /// </summary>
        public void SetWakeOffset(Vector3 offset)
        {
            _wakeOffset = offset;
            ApplyHead();
        }

        /// <summary>
        /// [Head] 로컬 위치만 갱신한다. <b>이 트랜스폼의 유일한 소유자가 여기다</b> — 눈높이·빙의
        /// 리프트·기상 오프셋이 한 식에서 합성되므로 서로 덮어쓸 일이 없다.
        /// </summary>
        void ApplyHead()
        {
            if (head == null || Data == null) return;
            head.localPosition = new Vector3(
                _wakeOffset.x,
                Data.eyeHeight - eyeBase + _possessLift + _wakeOffset.y,
                _wakeOffset.z);
        }
        [Tooltip("HUD 배치 적용 대상. GameBoot이 세팅.")]
        public VrHudSpace hud;

        static string FilePath { get { return Path.Combine(Application.persistentDataPath, "vr_tuning.json"); } }

        // GameBoot이 참조를 Awake 이후 세팅하므로 Start에서 로드·적용한다.
        void Start()
        {
            Load();
            Apply();
        }

        /// <summary>현재 Data를 각 시스템에 반영(소프트값). 렌즈는 Step 2.</summary>
        public void Apply()
        {
            if (Data == null) return;

            ApplyHead();   // 기본 눈높이는 몸이 갖고, [Head]는 차이 + 빙의 리프트 + 기상 오프셋만

            if (hud != null) hud.SetPlacement(Data.hudDistance, Data.hudPanelHeight);

            // ⚠️ URP 애셋 renderScale은 ScriptableObject라 에디터에선 애셋이 dirty될 수 있음(런타임은 무해).
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp != null) urp.renderScale = Mathf.Clamp(Data.renderScale, 0.3f, 1.2f);

            // 렌즈값 → Cardboard 프로파일 생성·주입 (VR 온디바이스에서만 동작, 에디터/PC에선 no-op).
            CardboardProfile.Apply(Data);
        }

        [ContextMenu("Load JSON")]
        public void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var d = JsonUtility.FromJson<VrTuningData>(File.ReadAllText(FilePath));
                if (d != null) Data = d;
                Debug.Log("[VrTuning] 로드: " + FilePath);
            }
            catch (System.Exception e) { Debug.LogWarning("[VrTuning] 로드 실패: " + e.Message); }
        }

        [ContextMenu("Save JSON")]
        public void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(Data, true));
                Debug.Log("[VrTuning] 저장: " + FilePath);
            }
            catch (System.Exception e) { Debug.LogWarning("[VrTuning] 저장 실패: " + e.Message); }
        }

#if UNITY_EDITOR
        // Play 중 인스펙터에서 값 바꾸면 즉시 반영(튜닝 편의).
        void OnValidate() { if (Application.isPlaying) Apply(); }
#endif
    }
}
