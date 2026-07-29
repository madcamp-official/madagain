using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.View
{
    /// <summary>
    /// 흑백 화면을 <b>검정→빨강</b> 램프로 물들이는 튜닝 컨트롤.
    ///
    /// <para><b>왜 RGB 커브인가</b> — URP의 컬러 그레이딩 LUT는 순서가 정해져 있다:
    /// <c>Channel Mixer → Lift/Gamma/Gain → 채도 → RGB 커브</c>.
    /// 흑백은 채도 −100이 담당하는데, 그 <b>앞</b>에서 무슨 색을 섞든 뒤이은 완전 무채화가 전부 지운다.
    /// <b>채도보다 뒤에 오는 색 연산은 RGB 커브뿐이고, 그게 체인의 마지막이다.</b>
    /// (Channel Mixer로 하려다 이 순서 때문에 화면이 그대로 흑백으로 남는 것을 확인했다.)</para>
    ///
    /// <para><b>원리</b> — 채도 −100을 거친 픽셀은 <c>R = G = B = 휘도 L</c>이다.
    /// G·B 커브를 <see cref="whiteMix"/> 기울기로 눌러 <c>(L, wL, wL)</c>을 만든다.
    /// w = 0이면 순수 빨강, 올릴수록 흰색이 섞여 분홍빛으로 밝아진다.</para>
    ///
    /// <para><b>밝기</b> — 순수 빨강의 휘도는 0.2126뿐이라 그대로 두면 화면이 확 가라앉는다.
    /// <see cref="exposure"/>가 그 보정이다. Post Exposure는 커브보다 앞에서 돌지만
    /// <b>채도 연산이 휘도를 보존</b>하므로 그대로 살아남는다.</para>
    ///
    /// <para><b>왜 별도 볼륨인가</b> — 흑백 볼륨과 분리해야 <see cref="weight"/> 하나로
    /// 흑백↔흑빨 전환을 조절할 수 있다. 보스 등장에 맞춰 몇 초에 걸쳐 물들이는 연출이 이 값 하나로 된다.</para>
    ///
    /// <para><see cref="ExecuteAlways"/>라 Play 없이 씬 뷰에서 바로 보인다.</para>
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Volume))]
    public class BossTintControl : MonoBehaviour
    {
        [Header("전환")]
        [Tooltip("0 = 완전 흑백, 1 = 흑빨 최대. 보스 등장 연출은 이 값을 시간에 걸쳐 올리면 된다.")]
        [Range(0f, 1f)] public float weight = 1f;

        [Header("색")]
        [Tooltip("흰색 섞임. 0 = 순수 빨강, 올릴수록 분홍빛으로 밝아진다.")]
        [Range(0f, 1f)] public float whiteMix = 0.3f;

        [Tooltip("밝기 보정(EV). 빨강은 휘도가 낮아 그대로 두면 화면이 가라앉는다.")]
        [Range(0f, 3f)] public float exposure = 0.8f;

        Volume _volume;
        float _lastMix = -1f;   // 커브는 매 프레임 새로 만들면 GC가 돈다 — 값이 바뀔 때만 다시 만든다

        void OnEnable()   { _lastMix = -1f; Apply(); }
        void OnValidate() => Apply();
        void Update()     => Apply();

        public void Apply()
        {
            if (_volume == null) _volume = GetComponent<Volume>();
            if (_volume == null) return;

            _volume.weight = weight;

            VolumeProfile profile = _volume.sharedProfile;
            if (profile == null) return;

            ColorAdjustments adj;
            if (profile.TryGet(out adj))
            {
                adj.active = true;
                adj.postExposure.overrideState = true;
                adj.postExposure.value = exposure;
            }

            ColorCurves curves;
            if (!profile.TryGet(out curves)) return;
            curves.active = true;

            if (Mathf.Approximately(_lastMix, whiteMix)) return;
            _lastMix = whiteMix;

            // 빨강은 그대로 통과(항등), 초록·파랑만 기울기를 눌러 흰색 섞임을 만든다.
            SetLinear(curves.red,   1f);
            SetLinear(curves.green, whiteMix);
            SetLinear(curves.blue,  whiteMix);
        }

        /// <summary>0→0, 1→slope 인 직선 커브로 교체한다.</summary>
        static void SetLinear(TextureCurveParameter param, float slope)
        {
            if (param.value != null) param.value.Dispose();
            param.value = new TextureCurve(
                AnimationCurve.Linear(0f, 0f, 1f, slope), 0f, false, new Vector2(0f, 1f));
            param.overrideState = true;
        }
    }
}
