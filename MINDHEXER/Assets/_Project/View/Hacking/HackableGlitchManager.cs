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

        [Header("상태별 세기 — ★ 거리 비례는 폐기. 사거리 안/밖의 이진 판정만 쓴다")]
        [Tooltip("조준 중 + 해킹가능거리 안. ★ 이것이 '최대'다 — 사거리 밖에서는 조준해도 0이다.")]
        [Range(0f, 1f)] public float gazeDensity = 0.55f;
        [Range(0f, 1f)] public float gazeAlpha = 0.8f;

        [Tooltip("패턴을 푸는 중(captureState = Hacking). 조준을 놓쳐도 계속 치지직거린다.")]
        [Range(0f, 1f)] public float hackingDensity = 0.6f;
        [Range(0f, 1f)] public float hackingAlpha = 0.85f;

        [Tooltip("조종 중(captureState = Captured). 아주 낮게 지속 — '내가 잡고 있다'만 알린다.")]
        [Range(0f, 1f)] public float controlDensity = 0.12f;
        [Range(0f, 1f)] public float controlAlpha = 0.4f;

        [Tooltip("한 번이라도 해킹된 것(everHacked). 조종 중이 아니어도 아주 작게 지속 — " +
                 "'여기는 이미 열었다'는 흔적.")]
        [Range(0f, 1f)] public float hackedDensity = 0.06f;
        [Range(0f, 1f)] public float hackedAlpha = 0.3f;

        [Tooltip("밀도·불투명도·모드가 <b>사라지는</b> 속도.")]
        public float responseSpeed = 6f;

        [Tooltip("★ <b>차오르는</b> 속도. 5면 0→0.55가 약 0.11초. " +
                 "사라지는 중에 다시 조준하면 남은 값에서 이어 오른다(0으로 리셋되지 않는다).")]
        public float riseSpeed = 5f;

        [Header("셰이더 — 가로줄 스캔 노이즈")]
        [Tooltip("치지직 색. 색 신호는 폐기했으므로 무채색이다 — 화면 전체 흑백 후처리를 어차피 함께 탄다.")]
        public Color glitchColor = new Color(1f, 1f, 1f, 1f);

        [Tooltip("가로줄 밀도(화면비 기준). VHS 스캔라인처럼 줄 하나당 밝기 하나. 220 → 110 (절반).")]
        public float rowCount = 110f;

        [Tooltip("줄 갱신 속도(초당 스텝). 노이즈가 얼마나 빨리 바뀌는지.")]
        public float scrollSpeed = 18f;

        [Tooltip("트래킹 에러(줄이 옆으로 튀는 것) 발생 확률. ★ 0 — 점이 좌우로 날뛰어 보이는 원인이었다. " +
                 "가로선은 순수 직선이어야 한다.")]
        [Range(0f, 1f)] public float tearChance = 0f;

        [Header("가로선 왜곡 — ★ 전부 0. 순수 직선이어야 한다(꼬불거림 폐기)")]
        [Tooltip("가로선 꼬불거림 진폭. ★ 0 = 직선. 조준 시 선이 물결치던 원인이었다.")]
        public float waveAmp = 0f;

        [Tooltip("꼬불거림 공간 주파수(1/m). waveAmp가 0이면 의미 없다.")]
        public float waveFreq = 6f;

        [Tooltip("꼬불거림 속도. waveAmp가 0이면 의미 없다.")]
        public float waveSpeed = 2.5f;

        [Tooltip("조준 시 진폭 배율. ★ 0 — 조준해도 흔들리지 않는다.")]
        public float wave3DMult = 0f;

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

                // ★ 보스는 더 이상 해킹 대상이 아니다 — 치지직을 걸지 않는다.
                if (h.kind == HackableKind.Boss) continue;

                // ★ 사거리 무시 대상(거대 프레스)은 전역 덮어쓰기에서 뺀다 — 덮어써 봐야 판정이
                //   WithinHackRange를 지나 어차히 무시되지만, 인스펙터 값이 조용히 바뀌면 헷갈린다.
                if (overrideHackRange && !h.ignoreRange) h.hackRange = hackRangeOverride;

                if (!_entries.TryGetValue(h, out Entry e))
                {
                    e = BuildEntry(h);
                    _entries[h] = e;
                }
                if (e == null || e.renderers.Length == 0) continue;   // glowRenderers 미지정 — 조용히 스킵

                float dist = Vector3.Distance(viewerPos, h.transform.position);
                bool inRange = h.WithinHackRange(dist);

                // ── 상태 → 목표 세기 ────────────────────────────────────
                // ★ 거리 비례는 폐기했다. 거리는 "사거리 안인가"라는 <b>이진 판정</b>으로만 쓴다 —
                //   조준해도 사거리 밖이면 0이다. 손이 닿는지가 곧 켜짐/꺼짐이 된다.
                //
                // 상태가 겹칠 때는 <b>더 센 쪽</b>을 쓴다(Max). 예: 이미 해킹한 것을 다시 조준하면
                // 흔적(약함)이 아니라 조준(강함)이 보여야 한다.
                float targetDensity = 0f, targetAlpha = 0f, targetMode = 0f;

                if (h.captureState == CaptureState.Hacking)
                {
                    // 패턴을 푸는 중 — 조준을 놓쳐도 계속 치지직거린다.
                    targetDensity = hackingDensity; targetAlpha = hackingAlpha; targetMode = 1f;
                }
                else if (h.captureState == CaptureState.Captured)
                {
                    // 조종 중 — 아주 낮게 지속.
                    targetDensity = controlDensity; targetAlpha = controlAlpha;
                }
                else if (h.everHacked)
                {
                    // 한 번이라도 열었던 것 — 조종 중이 아니어도 아주 작게 남는다.
                    targetDensity = hackedDensity; targetAlpha = hackedAlpha;
                }

                // ★ 조준 강조는 <b>아직 손대지 않은 대상에만</b> 붙는다(captureState = None).
                //   조종 중인 것을 쳐다봤을 때 더 강해지면 안 된다 — 이미 잡고 있으니 조준할 이유가 없고,
                //   "조준 = 해킹할 수 있다"는 신호가 흐려진다. 패턴 푸는 중도 자기 세기를 유지한다.
                if (h.captureState == CaptureState.None && h.IsGazed && inRange)
                {
                    targetDensity = gazeDensity; targetAlpha = gazeAlpha; targetMode = 1f;
                }

                // 차오를 때와 사라질 때 속도를 따로 둔다 — "빠르게 차오르는" 느낌이 이 비대칭에서 나온다.
                float rise = Mathf.Max(0.01f, riseSpeed) * dt;
                float fall = Mathf.Max(0.01f, responseSpeed) * dt;

                e.smoothIntensity = Mathf.MoveTowards(e.smoothIntensity, targetDensity,
                                        targetDensity > e.smoothIntensity ? rise : fall);
                e.smoothAlpha = Mathf.MoveTowards(e.smoothAlpha, targetAlpha,
                                        targetAlpha > e.smoothAlpha ? rise : fall);
                e.smoothMode = Mathf.MoveTowards(e.smoothMode, targetMode,
                                        targetMode > e.smoothMode ? rise : fall);

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
            // Play 중 스크립트를 다시 컴파일하면 도메인 리로드가 일어나는데, _glitchMat은
            // UnityEngine.Object라 복원되지만 MaterialPropertyBlock은 직렬화 대상이 아니라
            // null이 된다. Update의 _glitchMat 검사는 그걸 못 걸러 여기서 터진다.
            if (_mpb == null) _mpb = new MaterialPropertyBlock();

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
