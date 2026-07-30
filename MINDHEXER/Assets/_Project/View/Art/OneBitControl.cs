using UnityEngine;

namespace Game.View
{
    /// <summary>어느 전역 세트를 구동하는지. 세트마다 값이 완전히 따로 간다.</summary>
    public enum OneBitChannel
    {
        /// <summary>손·거미(플레이어 몸). 거의 검은 재질이라 입력 범위를 크게 펼쳐야 한다.</summary>
        Player = 0,
        /// <summary>해킹 가능 대상. 밝은 금속이라 플레이어와 같은 값을 쓰면 하얗게 날아간다.</summary>
        Hackable = 1,
    }

    /// <summary>
    /// 계조 축소 재질(<c>MINDHEXER/OneBit</c>)의 전역 값을 구동한다.
    ///
    /// <para>머티리얼이 100개가 넘어 개별 조절이 불가능하다. 셰이더가 전역 프로퍼티를 읽도록 만들어
    /// 두고, 여기 슬라이더 하나가 전부를 움직인다.</para>
    ///
    /// <para><b>세트가 둘이다</b>(<see cref="channel"/>) — 플레이어(손·거미)와 해킹 대상은 재질 밝기가
    /// 정반대라 한 세트로 맞추면 한쪽이 죽는다. 팔은 거의 검정이어서 <c>inWhite</c>를 크게 낮춰야
    /// 계단이 생기는데, 그 값을 밝은 금속에 그대로 쓰면 통째로 흰색이 된다.
    /// 셰이더의 <c>_HackSet</c> 토글이 머티리얼별로 어느 세트를 읽을지 정한다.</para>
    ///
    /// <para><b>씬에 직접 배치한다</b> — 예전에는 없으면 런타임에 자동 생성했는데, 그러면 값이 씬에
    /// 남지 않아 매번 기본값으로 돌아간다. <c>Tools/흑백/씬에 흑백 리그 배치</c>로 넣는다.</para>
    ///
    /// <para><see cref="ExecuteAlways"/>라 Play 없이 씬 뷰에서 바로 보인다.</para>
    /// </summary>
    [ExecuteAlways]
    public class OneBitControl : MonoBehaviour
    {
        [Tooltip("이 컴포넌트가 구동할 전역 세트. 씬에 Player용·Hackable용을 각각 하나씩 둔다.")]
        public OneBitChannel channel = OneBitChannel.Player;

        [Header("계조 축소")]
        [Tooltip("계단 수. 2 = 완전한 흑백 2치, 4~5 = 조금만 불연속, 8 = 디더와 합쳐 하프톤 점묘.")]
        [Range(2f, 8f)] public float levels = 8f;

        [Tooltip("입력 검정점. 이보다 어두운 곳은 전부 최하 계단.")]
        [Range(0f, 1f)] public float inBlack = 0f;

        [Tooltip("입력 흰색점. ★ 가장 중요한 값. 어두운 재질은 내려야 계단이 생기고, " +
                 "밝은 재질은 올려야 하얗게 날아가지 않는다.")]
        [Range(0f, 1f)] public float inWhite = 0.5f;

        [Tooltip("흑백을 뒤집는다. ★ 기본은 반전 안 함 — 처음엔 켜고 시작했는데 쓰지 않기로 정해졌다.")]
        public bool invert = false;

        [Tooltip("계단 경계를 점 패턴으로 흩는다. 0이면 경계가 딱 떨어진다.")]
        [Range(0f, 1f)] public float dither = 1f;

        [Tooltip("어두운 면이 통째로 검게 죽는 것을 막아 형태를 살린다.")]
        [Range(0f, 1f)] public float lightWrap = 1f;

        [Tooltip("★★★ 비활성화됨(사용자 지시) — 씬 조명과 무관하게 밝아지는 게 원치 않는 동작이었다. " +
                 "Apply()가 이 값과 무관하게 전역값을 0으로 강제한다. 값을 바꿔도 화면에 반영되지 않는다 — " +
                 "되돌리려면 forceAmbientOff를 끄면 된다(코드에서).")]
        [Range(0f, 2f)] public float ambientFloor = 0f;

        // 삭제하지 않고 무력화만 해 둔다 — 필요해지면 이 한 줄만 지우면 되돌아간다.
        const bool ForceAmbientOff = true;

        // 채널별 프로퍼티 ID — 이름 조합을 매 프레임 하지 않도록 캐시한다.
        int _idLevels, _idInBlack, _idInWhite, _idInvert, _idDither, _idWrap, _idAmbient;
        OneBitChannel _cachedFor = (OneBitChannel)(-1);

        void OnEnable()   => Apply();
        void OnValidate() => Apply();
        void Update()     => Apply();   // 패널·인스펙터 어느 쪽으로 바꿔도 즉시 반영

        void CacheIds()
        {
            if (_cachedFor == channel) return;
            string p = channel == OneBitChannel.Hackable ? "_OneBitH" : "_OneBit";
            _idLevels  = Shader.PropertyToID(p + "Levels");
            _idInBlack = Shader.PropertyToID(p + "InBlack");
            _idInWhite = Shader.PropertyToID(p + "InWhite");
            _idInvert  = Shader.PropertyToID(p + "Invert");
            _idDither  = Shader.PropertyToID(p + "Dither");
            _idWrap    = Shader.PropertyToID(p + "LightWrap");
            _idAmbient = Shader.PropertyToID(p + "Ambient");
            _cachedFor = channel;
        }

        public void Apply()
        {
            CacheIds();
            Shader.SetGlobalFloat(_idLevels,  levels);
            Shader.SetGlobalFloat(_idInBlack, inBlack);
            Shader.SetGlobalFloat(_idInWhite, Mathf.Max(inBlack + 0.01f, inWhite));
            Shader.SetGlobalFloat(_idInvert,  invert ? 1f : 0f);
            Shader.SetGlobalFloat(_idDither,  dither);
            Shader.SetGlobalFloat(_idWrap,    lightWrap);
            Shader.SetGlobalFloat(_idAmbient, ForceAmbientOff ? 0f : ambientFloor);
        }
    }

    /// <summary>
    /// 세트가 하나도 구동되지 않는 씬을 위한 <b>최소 안전값</b>.
    ///
    /// <para>예전에는 여기서 <c>[OneBit]</c> 오브젝트를 만들어 줬다. 그 방식은 값이 씬에 남지 않아
    /// 매번 기본값으로 되돌아가는 문제가 있어 폐기했다 — 이제는 씬에 직접 배치한다.</para>
    ///
    /// <para>다만 전역값이 <b>전부 0인 채로</b> 셰이더가 돌면 <c>inWhite = 0</c>이 되어 화면이 통째로
    /// 흰색이 된다. 오브젝트를 만들지는 않되 <b>값만</b> 안전한 기본으로 채우고, 컨트롤이 없다는 사실은
    /// 경고로 알린다 — 조용히 이상하게 보이는 것이 가장 나쁘다.</para>
    /// </summary>
    public static class OneBitDefaults
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            // ambient는 0으로 강제(위 OneBitControl.ForceAmbientOff와 같은 방침) — 컨트롤이
            // 붙는 순간 그쪽이 다시 덮어쓰므로, 여긴 컨트롤이 없는 짧은 순간의 폴백일 뿐이다.
            Push("_OneBit",  8f, 0f, 0.5f, 1f, 1f,    0f);
            Push("_OneBitH", 8f, 0f, 0.6f, 1f, 1f,    0f);

            if (Object.FindFirstObjectByType<OneBitControl>() == null)
                Debug.LogWarning("[OneBit] 씬에 OneBitControl이 없어 기본값으로 돕니다. " +
                                 "Tools/흑백/씬에 흑백 리그 배치 로 넣으십시오.");
        }

        static void Push(string p, float levels, float inBlack, float inWhite, float dither, float wrap, float ambient)
        {
            Shader.SetGlobalFloat(p + "Ambient", ambient);
            Shader.SetGlobalFloat(p + "Levels", levels);
            Shader.SetGlobalFloat(p + "InBlack", inBlack);
            Shader.SetGlobalFloat(p + "InWhite", inWhite);
            Shader.SetGlobalFloat(p + "Invert", 0f);
            Shader.SetGlobalFloat(p + "Dither", dither);
            Shader.SetGlobalFloat(p + "LightWrap", wrap);
        }
    }
}
