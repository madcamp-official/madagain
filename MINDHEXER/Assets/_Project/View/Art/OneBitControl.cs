using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 1비트 재질(<c>MINDHEXER/OneBit</c>)의 전역 값을 한 곳에서 구동한다.
    ///
    /// <para>손·거미 머티리얼이 <b>100개가 넘어</b> 개별 조절이 불가능하다. 셰이더가 전역 프로퍼티를
    /// 읽도록 만들어 두고, 여기 슬라이더 하나가 전부를 움직인다(흑백 후처리와 사용감이 같다).</para>
    ///
    /// <para><see cref="ExecuteAlways"/>라 <b>Play 없이 씬 뷰에서 바로</b> 보인다 — 자세를 잡으면서
    /// 동시에 대비를 맞출 수 있다.</para>
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class OneBitControl : MonoBehaviour
    {
        [Header("계조 축소 (손·거미)")]
        [Tooltip("계단 수. 2 = 완전한 흑백 2치, 4~5 = 조금만 불연속.")]
        [Range(2f, 8f)] public float levels = 4f;

        [Tooltip("입력 검정점. 이보다 어두운 곳은 전부 최하 계단.")]
        [Range(0f, 1f)] public float inBlack = 0f;

        [Tooltip("입력 흰색점. ★ 팔처럼 어두운 재질은 이 값을 내려야 계단이 생긴다 — 1로 두면 통째로 한 색이 된다.")]
        [Range(0f, 1f)] public float inWhite = 0.5f;

        [Tooltip("흑백을 뒤집는다. 손·거미에만 걸린다(배경은 후처리 흑백 그대로).")]
        public bool invert = true;

        [Tooltip("계단 경계를 점 패턴으로 흩는다. 0이면 경계가 딱 떨어진다.")]
        [Range(0f, 1f)] public float dither = 0f;

        [Tooltip("어두운 면이 통째로 검게 죽는 것을 막아 형태를 살린다.")]
        [Range(0f, 1f)] public float lightWrap = 0.35f;

        static readonly int IdLevels    = Shader.PropertyToID("_OneBitLevels");
        static readonly int IdInBlack   = Shader.PropertyToID("_OneBitInBlack");
        static readonly int IdInWhite   = Shader.PropertyToID("_OneBitInWhite");
        static readonly int IdInvert    = Shader.PropertyToID("_OneBitInvert");
        static readonly int IdDither    = Shader.PropertyToID("_OneBitDither");
        static readonly int IdLightWrap = Shader.PropertyToID("_OneBitLightWrap");

        void OnEnable()   => Apply();
        void OnValidate() => Apply();
        void Update()     => Apply();   // 패널·인스펙터 어느 쪽으로 바꿔도 즉시 반영

        public void Apply()
        {
            Shader.SetGlobalFloat(IdLevels,    levels);
            Shader.SetGlobalFloat(IdInBlack,   inBlack);
            Shader.SetGlobalFloat(IdInWhite,   Mathf.Max(inBlack + 0.01f, inWhite));
            Shader.SetGlobalFloat(IdInvert,    invert ? 1f : 0f);
            Shader.SetGlobalFloat(IdDither,    dither);
            Shader.SetGlobalFloat(IdLightWrap, lightWrap);
        }
    }

    /// <summary>씬에 없어도 Play 시 자동으로 붙는다 — 값이 안 실려 손이 새까맣게 나오는 것을 막는다.</summary>
    public static class OneBitControlBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<OneBitControl>() == null)
                new GameObject("[OneBit]").AddComponent<OneBitControl>();
        }
    }
}
