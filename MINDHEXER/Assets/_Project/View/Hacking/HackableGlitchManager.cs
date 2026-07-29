using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 해킹 대상 "치지직" 환경 하이라이트 — 테두리 없이, 거리 기반 2D 치지직 + 조준 시 3D 치지직.
    /// (기초_설계안 §7 후속 — 테두리안은 폐기, 이걸로 대체)
    ///
    /// <para><b>기준점은 거리가 아니라 해킹가능거리(<see cref="Hackable.hackRange"/>)다.</b>
    /// 그 범위 안이면(<c>dist &lt;= hackRange</c>) 무조건 최대(<see cref="densityMax"/>/<see cref="alphaMax"/>) —
    /// "해킹 가능 = 최대 치지직"이 즉시 읽혀야 하기 때문. 범위를 벗어나면 <see cref="fadeDistance"/>만큼
    /// 더 가면서 <c>t^<see cref="power"/></c> 곡선으로 <see cref="densityFloor"/>/<see cref="alphaFloor"/>까지 흐려진다.</para>
    ///
    /// <para><b>밀도와 불투명도를 둘 다 강도로 판별한다</b> — 밀도(셰이더가 강도를 문턱값으로 써서
    /// 켜지는 선의 개수를 바꿈)와 불투명도(켜진 선 자체가 얼마나 진한지)를 따로 보간한다.
    /// 밀도는 멀어지면 좀 더 떨어져도 되지만(<see cref="densityFloor"/>), 불투명도는 아무리 멀어도
    /// 잘 안 보일 만큼 낮아지면 안 되므로 <see cref="alphaFloor"/>를 densityFloor보다 높게 잡는다.</para>
    ///
    /// <para><b>조준(IsGazed)</b>이면 거리 무관 <see cref="gazeDensity"/>/<see cref="gazeAlpha"/>(2D 최댓값과 동일) +
    /// <b>3D 모드</b>(가로선이 좌우로 꼬불거림)로 전환한다. 2D는 <b>무조건 직선</b> — 웨이브 왜곡은 3D 전용.</para>
    ///
    /// <para><b>번짐 방지</b>: <see cref="Hackable.glowRenderers"/>에 명시된 렌더러에만 적용한다.
    /// 비어 있어도 <c>GetComponentsInChildren</c>로 자동 스윕하지 <b>않는다</b> — 그 자동 스윕이
    /// 번짐의 원인이었다(예: 터렛 감지범위 시각화용 원기둥이 같이 딸려 들어감). 명시 안 된 대상은
    /// 그냥 치지직이 안 켜진다 — 조용히 새는 것보다 눈에 띄게 빠지는 게 낫다.</para>
    ///
    /// <para>렌더러당 셰이더 패스를 <b>추가 머티리얼 슬롯</b>으로 덧붙인다(기존 머티리얼 비파괴).
    /// 머티리얼 자산 자체는 공유하고, 인스턴스별 강도·모드는 <c>SetPropertyBlock(mpb, index)</c>로
    /// 그 슬롯에만 밀어넣는다 — 배치가 안 깨진다.</para>
    /// </summary>
    public class HackableGlitchManager : MonoBehaviour
    {
        [Header("해킹가능거리(hackRange) — 아직 확정값 아님, 여기서 전역으로 덮어써서 테스트한다")]
        [Tooltip("켜면 씬의 모든 Hackable.hackRange를 매 프레임 hackRangeOverride 값으로 강제한다. " +
                 "꺼두면 각 Hackable 컴포넌트에 원래 세팅된 값을 그대로 쓴다.")]
        public bool overrideHackRange = true;

        [Tooltip("모든 Hackable에 강제 적용할 해킹가능거리(m). overrideHackRange가 켜져 있을 때만 적용됨.")]
        public float hackRangeOverride = 20f;

        [Header("기준 = 해킹가능거리(hackRange). 그 안쪽은 항상 최대, 벗어나면 fadeDistance만큼 더 흐려짐")]
        [Tooltip("hackRange를 벗어난 뒤 추가로 흐려지는 거리(m). 이 거리만큼 더 멀어지면 바닥값(Floor)에 도달.")]
        public float fadeDistance = 10f;

        [Tooltip("hackRange 밖에서의 감쇠 지수. 클수록 hackRange를 벗어나자마자 확 죽는다.")]
        public float power = 3f;

        [Header("밀도(선 개수) — hackRange 안쪽=Max, 밖=거리에 따라 Floor까지")]
        [Tooltip("아무리 멀어도 유지하는 최소 밀도. 예전 '가까이서 보던' 밀도 수준을 바닥으로 잡는다 — " +
                 "이보다 낮으면 듬성듬성해서 안 보인다.")]
        [Range(0f, 1f)] public float densityFloor = 0.85f;

        [Tooltip("hackRange 안쪽(=해킹 가능)일 때의 밀도. 사실상 최대.")]
        [Range(0f, 1f)] public float densityMax = 0.97f;

        [Header("불투명도(켜진 선의 진하기) — 밀도보다 덜 떨어지게 바닥을 높게 잡는다")]
        [Tooltip("아무리 멀어도 유지하는 최소 불투명도. 밀도(densityFloor)보다 높게 — " +
                 "선이 줄어드는 건 괜찮아도 남은 선까지 흐릿해지면 안 보인다.")]
        [Range(0f, 1f)] public float alphaFloor = 0.8f;

        [Tooltip("hackRange 안쪽일 때의 불투명도. 사실상 최대.")]
        [Range(0f, 1f)] public float alphaMax = 1f;

        [Header("조준(IsGazed) — 2D 최댓값과 동일. 3D다움은 웨이브 왜곡만 담당")]
        [Range(0f, 1f)] public float gazeDensity = 0.97f;
        [Range(0f, 1f)] public float gazeAlpha = 1f;

        [Tooltip("밀도·불투명도·모드 전환 속도(1/초에 가까운 감쇠). 뚝 끊기지 않고 부드럽게 튀도록.")]
        public float responseSpeed = 10f;

        [Header("셰이더 — 인광 톤 + 가로줄 스캔 노이즈")]
        [Tooltip("치지직 색. 형광 초록 인광 톤(§7 색언어의 '해킹 가능=초록'과 맞춘다).")]
        public Color glitchColor = new Color(0.3f, 1.4f, 0.5f, 1f);

        [Tooltip("가로줄 밀도(화면비 기준). VHS 스캔라인처럼 줄 하나당 밝기 하나.")]
        public float rowCount = 220f;

        [Tooltip("줄 갱신 속도(초당 스텝). 노이즈가 얼마나 빨리 바뀌는지.")]
        public float scrollSpeed = 18f;

        [Tooltip("트래킹 에러(줄이 옆으로 튀는 것) 발생 확률.")]
        [Range(0f, 1f)] public float tearChance = 0.06f;

        [Header("3D(조준) 전용 — 가로선을 꼬불꼬불하게(림글로우·팝·플리커·HDR부스트는 눈아프다는 " +
                 "피드백으로 전부 제거, 이 웨이브 왜곡 하나로 2D와 구분한다)")]
        [Tooltip("가로선 꼬불거림 진폭(uv 단위, 대략 미터).")]
        public float waveAmp = 0.03f;

        [Tooltip("꼬불거림 공간 주파수(1/m). 클수록 잔물결이 촘촘해진다.")]
        public float waveFreq = 6f;

        [Tooltip("꼬불거림 속도.")]
        public float waveSpeed = 2.5f;

        [Tooltip("3D(조준) 시 진폭·속도 배율 — 2D 대비 몇 배로 격렬해지는지.")]
        public float wave3DMult = 1.6f;

        [Header("켜진 선의 밝기 (불투명도와 무관하게 항상 고정 — 강도는 alphaFloor/Max가 담당)")]
        public float lineBrightness = 1f;

        static readonly int IntensityId = Shader.PropertyToID("_GlitchIntensity");
        static readonly int ModeId = Shader.PropertyToID("_GlitchMode");
        static readonly int AlphaId = Shader.PropertyToID("_LineAlpha");

        class Entry
        {
            public Renderer[] renderers;
            public int[] slot;          // 렌더러별 치지직 머티리얼이 들어간 슬롯 인덱스
            public float smoothIntensity;
            public float smoothAlpha;
            public float smoothMode;
        }

        readonly Dictionary<Hackable, Entry> _entries = new Dictionary<Hackable, Entry>();
        Material _glitchMat;
        MaterialPropertyBlock _mpb;
        Camera _viewer;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindAnyObjectByType<HackableGlitchManager>() == null)
            {
                var go = new GameObject("[HackableGlitchManager]");
                go.AddComponent<HackableGlitchManager>();
                go.AddComponent<GlitchTuningPanel>();   // F5 실시간 튜닝(PC 전용, OnGUI) — F1~F4는 다른 디버그 패널들이 이미 씀
            }
        }

        void Awake()
        {
            var shader = Shader.Find("MINDHEXER/HackGlitch");
            if (shader == null)
            {
                Debug.LogError("[HackableGlitchManager] MINDHEXER/HackGlitch 셰이더를 못 찾음 — " +
                                "빌드에선 Always Included Shaders에 넣어야 한다.");
                enabled = false;
                return;
            }

            _glitchMat = new Material(shader) { name = "HackGlitch (runtime shared)" };
            _mpb = new MaterialPropertyBlock();
        }

        void Update()
        {
            if (_glitchMat == null) return;

            // 머티리얼 전역 파라미터는 매 프레임 다시 밀어넣는다 — F2 튜닝 패널에서 필드를 바꾸면
            // 값 변경 이벤트 없이 그냥 이 필드를 직접 고치므로, 여기서 매 프레임 반영해야 즉시 보인다.
            _glitchMat.SetColor("_GlitchColor", glitchColor);
            _glitchMat.SetFloat("_RowCount", rowCount);
            _glitchMat.SetFloat("_ScrollSpeed", scrollSpeed);
            _glitchMat.SetFloat("_TearChance", tearChance);
            _glitchMat.SetFloat("_WaveAmp", waveAmp);
            _glitchMat.SetFloat("_WaveFreq", waveFreq);
            _glitchMat.SetFloat("_WaveSpeed", waveSpeed);
            _glitchMat.SetFloat("_Wave3DMult", wave3DMult);
            _glitchMat.SetFloat("_LineBrightness", lineBrightness);

            if (_viewer == null) _viewer = Camera.main;
            if (_viewer == null) return;

            Vector3 viewerPos = _viewer.transform.position;
            float dt = Time.deltaTime;

            for (int i = 0; i < Hackable.All.Count; i++)
            {
                Hackable h = Hackable.All[i];
                if (h == null) continue;

                if (overrideHackRange) h.hackRange = hackRangeOverride;

                if (!_entries.TryGetValue(h, out Entry e))
                {
                    e = BuildEntry(h);
                    _entries[h] = e;
                }
                if (e == null || e.renderers.Length == 0) continue;   // glowRenderers 미지정 — 조용히 스킵

                float dist = Vector3.Distance(viewerPos, h.transform.position);
                float targetDensity, targetAlpha, targetMode;

                if (h.IsGazed)
                {
                    targetDensity = gazeDensity;
                    targetAlpha = gazeAlpha;
                    targetMode = 1f;
                }
                else
                {
                    // hackRange 안쪽은 무조건 최대(t=1) — "해킹 가능 = 최대 치지직"이 거리와 무관하게 즉시 읽혀야 한다.
                    float hackRange = Mathf.Max(0.01f, h.hackRange);
                    float t = dist <= hackRange
                        ? 1f
                        : Mathf.Clamp01(1f - (dist - hackRange) / Mathf.Max(0.01f, fadeDistance));
                    float shaped = Mathf.Pow(t, power);

                    targetDensity = Mathf.Lerp(densityFloor, densityMax, shaped);
                    targetAlpha = Mathf.Lerp(alphaFloor, alphaMax, shaped);
                    targetMode = 0f;
                }

                e.smoothIntensity = Mathf.MoveTowards(e.smoothIntensity, targetDensity, responseSpeed * dt);
                e.smoothAlpha = Mathf.MoveTowards(e.smoothAlpha, targetAlpha, responseSpeed * dt);
                e.smoothMode = Mathf.MoveTowards(e.smoothMode, targetMode, responseSpeed * dt);

                Apply(e);
            }
        }

        Entry BuildEntry(Hackable h)
        {
            if (h.glowRenderers == null || h.glowRenderers.Length == 0) return new Entry { renderers = System.Array.Empty<Renderer>(), slot = System.Array.Empty<int>() };

            var rends = new List<Renderer>();
            var slots = new List<int>();

            foreach (var r in h.glowRenderers)
            {
                if (r == null) continue;

                var mats = new List<Material>(r.sharedMaterials);
                int idx = mats.Count;
                mats.Add(_glitchMat);   // 추가 슬롯 — 기존 머티리얼은 그대로, 위에 한 번 더 그린다.
                r.sharedMaterials = mats.ToArray();

                rends.Add(r);
                slots.Add(idx);
            }

            return new Entry { renderers = rends.ToArray(), slot = slots.ToArray() };
        }

        void Apply(Entry e)
        {
            for (int i = 0; i < e.renderers.Length; i++)
            {
                var r = e.renderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(_mpb, e.slot[i]);
                _mpb.SetFloat(IntensityId, e.smoothIntensity);
                _mpb.SetFloat(ModeId, e.smoothMode);
                _mpb.SetFloat(AlphaId, e.smoothAlpha);
                r.SetPropertyBlock(_mpb, e.slot[i]);
            }
        }
    }
}
