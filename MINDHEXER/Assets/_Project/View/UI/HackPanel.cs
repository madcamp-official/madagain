using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 해킹 패턴 패널 — <b>테두리·연결선·패턴·커서를 메시 하나로</b> 그린다.
    ///
    /// <code>
    ///   ╲                              ╱      시야 네 꼭짓점에서
    ///    ╲ ┌──────────────────┐ ╱        패널 네 꼭짓점으로
    ///      │ ●              ● │              연결선 4개가 상시 유지
    ///      │                  │
    ///    ╱ │ ●              ● │ ╲
    ///   ╱  └──────────────────┘  ╲
    /// </code>
    ///
    /// <para><b>이 연출의 핵심</b> — 패널은 머리에 완전히 고정되지 않고 관성으로 늦게 따라온다
    /// (<see cref="VrUiFollow"/>). 그런데 연결선의 바깥 끝은 <b>시야에 고정</b>돼 있다. 그래서 고개를
    /// 돌리면 선 4개가 눈에 띄게 휘고 늘어난다 — <b>관성이 결함이 아니라 연출로 읽히게 된다.</b>
    /// 선이 없으면 그냥 "UI가 굼뜨다"로 보인다.</para>
    ///
    /// <para><b>선이 두 좌표계를 잇는다</b>는 것이 구현을 결정했다. 캔버스는 평면이라 서로 다른 두
    /// 공간의 점을 이을 수 없다. 그래서 캔버스를 쓰지 않고 <see cref="UiMeshBuilder"/>로 직접 그린다
    /// (성능상으로도 그쪽이 낫다 — 그쪽 주석 참조).</para>
    ///
    /// <para><b>등장/소멸은 페이드가 아니다.</b> 알파는 건드리지 않고 <b>길이와 크기만</b> 움직인다.
    /// 확대·축소 중에도 연결선은 패널의 <b>현재</b> 꼭짓점을 매 프레임 다시 읽으므로 저절로 따라간다.</para>
    ///
    /// <para><b>배치</b> — 이 오브젝트는 <c>[Head]</c>의 자식이며 <b>로컬 트랜스폼이 항등</b>이어야
    /// 한다(눈 = 로컬 원점). <see cref="UiMeshBuilder"/>가 그 전제로 선 두께 방향을 구한다.</para>
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class HackPanel : MonoBehaviour
    {
        // ── 연결 ──────────────────────────────────────────────────────
        [Header("연결")]
        [Tooltip("관성 추종 루트([VrUiRoot_Follow]). 패널이 여기에 매달린다.")]
        public Transform panelRoot;

        [Tooltip("선/점을 그릴 머티리얼(MINDHEXER/UiLine). 비우면 런타임에 만든다.")]
        public Material material;

        // ── 배치 ──────────────────────────────────────────────────────
        [Header("배치 (도)")]
        [Tooltip("눈에서 UI 평면까지 거리(m).")]
        [Range(0.5f, 4f)] public float distance = 1.8f;

        [Tooltip("'화면 네 꼭짓점'의 가로 반각. 렌즈 FOV 확정 전 추정값 — 실기에서 이 값만 바꾸면 된다.")]
        [Range(5f, 80f)] public float viewHalfAngleX = 32f;

        [Tooltip("'화면 네 꼭짓점'의 세로 반각.")]
        [Range(5f, 60f)] public float viewHalfAngleY = 24f;

        [Tooltip("중앙 정사각 패널의 각크기.")]
        [Range(4f, 50f)] public float panelAngularSize = 24f;

        // ── 굵기 ──────────────────────────────────────────────────────
        [Header("굵기 (m, 위 거리 기준)")]
        public float borderWidth = 0.0035f;
        public float connectorWidth = 0.0025f;
        public float ghostWidth = 0.0035f;

        [Tooltip("가장 최근에 그은 선의 굵기. 순서 정보를 굵기로 전달한다(색을 못 쓰므로).")]
        public float traceWidthNewest = 0.012f;
        [Tooltip("가장 오래된 선의 굵기.")]
        public float traceWidthOldest = 0.005f;

        public float dotRadius = 0.012f;
        [Tooltip("닿은 점이 커지는 배율.")]
        public float dotHitScale = 1.7f;
        public float cursorRadius = 0.016f;

        // ── 밝기 (색 신호 폐기 — 명도만 쓴다) ─────────────────────────
        [Header("밝기 0~1")]
        [Range(0f, 1f)] public float borderLevel = 0.55f;
        [Range(0f, 1f)] public float connectorLevel = 0.35f;
        [Range(0f, 1f)] public float ghostLevel = 0.18f;
        [Range(0f, 1f)] public float traceLevel = 0.95f;
        [Range(0f, 1f)] public float dotLevel = 0.6f;
        [Range(0f, 1f)] public float dotHitLevel = 1f;
        [Range(0f, 1f)] public float cursorLevel = 1f;
        [Range(0f, 1f)] public float nextHintLevel = 0.4f;

        // ── 타이밍 ────────────────────────────────────────────────────
        [Header("등장 / 소멸 (초)")]
        [Tooltip("연결선이 시야 모서리에서 패널까지 자라는 시간.")]
        public float lineGrowTime = 0.18f;
        [Tooltip("선이 도달한 뒤 패널이 톡 뜨는 시간.")]
        public float panelPopTime = 0.22f;
        [Tooltip("등장 시 잠깐 커지는 배율.")]
        public float popOvershoot = 1.12f;
        [Tooltip("점이 패널보다 늦게 뜨는 지연.")]
        public float dotDelay = 0.06f;

        public float panelOutTime = 0.18f;
        [Tooltip("소멸 시 잠깐 커지는 배율.")]
        public float outOvershoot = 1.06f;
        public float lineRetractTime = 0.16f;

        [Header("감각")]
        [Tooltip("표시 커서가 논리 커서를 따라가는 시정수(초). ★ 커밋 순간의 순간이동을 눈에 안 보이게 한다.")]
        [Range(0f, 0.3f)] public float cursorFollowTime = 0.05f;
        [Tooltip("점이 커지는 시간. AOSP 패턴 락은 96ms.")]
        public float dotGrowTime = 0.096f;
        [Tooltip("점이 작아지는 시간. AOSP는 192ms — 빠르게 반응하고 느리게 이완해야 '닿았다'가 느껴진다.")]
        public float dotShrinkTime = 0.192f;

        // ── 저작용 미리보기 ───────────────────────────────────────────
        [Header("미리보기 (저작 전용 — 실제 게임에서는 SetPattern이 덮어쓴다)")]
        public bool previewVisible = true;
        [Tooltip("목표 패턴(유령선). 점 인덱스 0=TL 1=TR 2=BL 3=BR.")]
        public int[] previewTarget = { 0, 1, 3, 2 };
        [Tooltip("플레이어가 지난 점.")]
        public int[] previewPlayer = { 0, 1 };
        [Tooltip("커서 위치(정규화 0~1).")]
        public Vector2 previewCursor = new Vector2(0.75f, 0.6f);

        // ── 상태 ──────────────────────────────────────────────────────
        enum Phase { Hidden, Appearing, Shown, Disappearing }
        Phase _phase = Phase.Hidden;
        float _clock;

        readonly UiMeshBuilder _mb = new UiMeshBuilder();
        Mesh _mesh;
        MeshFilter _mf;
        MeshRenderer _mr;

        readonly float[] _dotGrow = new float[PatternGraph.DotCount];
        Vector2 _drawCursor;
        bool _cursorInit;

        int[] _target;
        readonly List<int> _player = new List<int>();
        Vector2 _cursor;
        int _nextDot = -1;

        float _lastTime;

        readonly Vector3[] _viewCorner = new Vector3[4];
        readonly Vector3[] _panelCorner = new Vector3[4];

        public bool IsVisible { get { return _phase != Phase.Hidden; } }

        // ── 외부 API ──────────────────────────────────────────────────

        /// <summary>패널을 띄운다. 이미 떠 있거나 뜨는 중이면 무시.</summary>
        [ContextMenu("패널 — 등장")]
        public void Show()
        {
            if (_phase == Phase.Appearing || _phase == Phase.Shown) return;
            _phase = Phase.Appearing;
            _clock = 0f;
        }

        /// <summary>패널을 접는다.</summary>
        [ContextMenu("패널 — 소멸")]
        public void Hide()
        {
            if (_phase == Phase.Hidden || _phase == Phase.Disappearing) return;
            _phase = Phase.Disappearing;
            _clock = 0f;
        }

        /// <summary>패턴 미니게임이 매 틱 호출한다.</summary>
        public void SetPattern(int[] target, IList<int> playerDots, Vector2 cursor, int nextDot)
        {
            _target = target;
            _player.Clear();
            if (playerDots != null) for (int i = 0; i < playerDots.Count; i++) _player.Add(playerDots[i]);
            _cursor = cursor;
            _nextDot = nextDot;
        }

        // ── 수명 ──────────────────────────────────────────────────────

        void OnEnable()
        {
            _mf = GetComponent<MeshFilter>();
            _mr = GetComponent<MeshRenderer>();

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "[HackPanel]" };
                _mesh.MarkDynamic();               // 매 프레임 바뀐다고 알려 둔다
                _mesh.hideFlags = HideFlags.DontSave;
            }
            _mf.sharedMesh = _mesh;

            EnsureMaterial();
            _lastTime = Time.realtimeSinceStartup;
            _phase = previewVisible ? Phase.Shown : Phase.Hidden;
        }

        void OnValidate()
        {
            // 저작 중 체크박스로 등장/소멸을 확인할 수 있게 한다.
            if (!Application.isPlaying)
                _phase = previewVisible ? Phase.Shown : Phase.Hidden;
        }

        void EnsureMaterial()
        {
            if (material != null) { _mr.sharedMaterial = material; return; }

            Shader sh = Shader.Find("MINDHEXER/UiLine");
            if (sh == null)
            {
                // ★ Shader.Find는 빌드에서만 실패한다 — 에디터에서 멀쩡하다고 안심하면 안 된다.
                Debug.LogError("[HackPanel] 셰이더 'MINDHEXER/UiLine'을 찾지 못했습니다. " +
                               "머티리얼을 인스펙터에 직접 물리거나 Always Included Shaders에 추가하십시오.", this);
                return;
            }
            material = new Material(sh) { name = "UiLine (auto)", hideFlags = HideFlags.DontSave };
            _mr.sharedMaterial = material;
        }

        void LateUpdate() => Tick();

        /// <summary>
        /// 한 프레임 진행 + 메시 재생성.
        ///
        /// <para>에디터에서는 <see cref="ExecuteAlways"/>의 틱이 씬 뷰 리페인트에 의존해 확실하지 않다.
        /// 그러면 <b>저작 중에는 아무것도 안 보인다</b> — 정작 이 UI를 만지는 곳이 에디터인데도.
        /// 그래서 <c>UiEditorDriver</c>가 <c>EditorApplication.update</c>에서 이걸 직접 부른다.</para>
        /// </summary>
        public void Tick()
        {
            float now = Time.realtimeSinceStartup;
            float dt = Mathf.Clamp(now - _lastTime, 0f, 0.1f);
            _lastTime = now;

            if (!Application.isPlaying) PullPreview();

            Advance(dt);
            Rebuild(dt);

#if UNITY_EDITOR
            // 에디터에서는 씬 뷰가 다시 그려질 때만 틱이 돈다 — 애니메이션 중에는 직접 돌려 준다.
            if (!Application.isPlaying && (_phase == Phase.Appearing || _phase == Phase.Disappearing))
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        void PullPreview()
        {
            _target = previewTarget;
            _player.Clear();
            if (previewPlayer != null) for (int i = 0; i < previewPlayer.Length; i++) _player.Add(previewPlayer[i]);
            _cursor = previewCursor;
            _nextDot = -1;
        }

        void Advance(float dt)
        {
            _clock += dt;
            if (_phase == Phase.Appearing && _clock >= lineGrowTime + panelPopTime) _phase = Phase.Shown;
            else if (_phase == Phase.Disappearing && _clock >= Mathf.Max(panelOutTime, lineRetractTime)) _phase = Phase.Hidden;
        }

        // ── 그리기 ────────────────────────────────────────────────────

        void Rebuild(float dt)
        {
            _mb.Clear();

            float lineT, panelScale, dotScale;
            Timeline(out lineT, out panelScale, out dotScale);

            if (_phase == Phase.Hidden || (lineT <= 0f && panelScale <= 0f))
            {
                _mb.Apply(_mesh);
                return;
            }

            ComputeViewCorners();
            ComputePanelCorners(panelScale);

            // ① 연결선 — 시야 모서리에서 패널 모서리 쪽으로 lineT 만큼.
            //    확대/축소 중에도 _panelCorner가 매 프레임 갱신되므로 저절로 따라간다.
            if (lineT > 0f && connectorLevel > 0f)
                for (int i = 0; i < 4; i++)
                    _mb.AddLine(_viewCorner[i], Vector3.Lerp(_viewCorner[i], _panelCorner[i], lineT),
                                connectorWidth, Grey(connectorLevel));

            if (panelScale <= 0.001f) { _mb.Apply(_mesh); return; }

            // ② 테두리 — TL-TR-BR-BL
            Color border = Grey(borderLevel);
            _mb.AddLine(_panelCorner[0], _panelCorner[1], borderWidth, border);
            _mb.AddLine(_panelCorner[1], _panelCorner[3], borderWidth, border);
            _mb.AddLine(_panelCorner[3], _panelCorner[2], borderWidth, border);
            _mb.AddLine(_panelCorner[2], _panelCorner[0], borderWidth, border);

            // ③ 목표 패턴 유령선
            if (_target != null && _target.Length > 1 && ghostLevel > 0f)
                for (int i = 0; i < _target.Length - 1; i++)
                    _mb.AddLine(DotPoint(_target[i]), DotPoint(_target[i + 1]), ghostWidth, Grey(ghostLevel));

            // ④ 확정 트레이스 — 최근일수록 굵다(순서를 굵기로 전달).
            int segs = _player.Count - 1;
            for (int i = 0; i < segs; i++)
            {
                float age = segs > 1 ? i / (float)(segs - 1) : 1f;   // 0=가장 오래됨, 1=가장 최근
                float w = Mathf.Lerp(traceWidthOldest, traceWidthNewest, age);
                _mb.AddLine(DotPoint(_player[i]), DotPoint(_player[i + 1]), w, Grey(traceLevel));
            }

            // ⑤ 진행 중 선 — 커서까지. 점 바로 옆에서는 짧은 선이 지저분하므로 죽인다.
            //    알파식은 AOSP LockPatternView의 calculateLastSegmentAlpha를 그대로 쓴다.
            UpdateDrawCursor(dt);
            if (_player.Count > 0)
            {
                int cur = _player[_player.Count - 1];
                float frac = Vector2.Distance(_drawCursor, PatternGraph.Pos[cur]);   // 점 간격 = 1.0
                float a = Mathf.Clamp01((frac - 0.3f) * 4f);
                if (a > 0f)
                    _mb.AddLine(DotPoint(cur), Point(_drawCursor), traceWidthNewest,
                                Grey(traceLevel, a));
            }

            // ⑥ 점 — 닿으면 커진다(96ms 커지고 192ms 작아지는 비대칭).
            UpdateDotGrow(dt);
            for (int d = 0; d < PatternGraph.DotCount; d++)
            {
                float g = _dotGrow[d];
                float r = dotRadius * Mathf.Lerp(1f, dotHitScale, g) * dotScale;
                float lv = Mathf.Lerp(dotLevel, dotHitLevel, g);
                _mb.AddCircle(DotPoint(d), r, Grey(lv));
            }

            // ⑦ 다음 목표 점 — 색을 못 쓰므로 '더 큰 흐린 원'으로 알린다.
            if (_nextDot >= 0 && nextHintLevel > 0f)
                _mb.AddCircle(DotPoint(_nextDot), dotRadius * 2.4f * dotScale, Grey(nextHintLevel));

            // ⑧ 커서
            if (dotScale > 0f)
                _mb.AddCircle(Point(_drawCursor), cursorRadius * dotScale, Grey(cursorLevel));

            _mb.Apply(_mesh);
        }

        /// <summary>단계별 진행값. 등장·소멸의 모든 시간 규칙이 여기 한 곳에 있다.</summary>
        void Timeline(out float lineT, out float panelScale, out float dotScale)
        {
            switch (_phase)
            {
                case Phase.Shown:
                    lineT = 1f; panelScale = 1f; dotScale = 1f;
                    return;

                case Phase.Appearing:
                {
                    lineT = lineGrowTime > 0f ? EaseOut(Mathf.Clamp01(_clock / lineGrowTime)) : 1f;

                    float u = panelPopTime > 0f ? Mathf.Clamp01((_clock - lineGrowTime) / panelPopTime) : 1f;
                    panelScale = _clock < lineGrowTime ? 0f : Pop(u, popOvershoot);

                    float dstart = lineGrowTime + dotDelay;
                    dotScale = Mathf.Clamp01((_clock - dstart) / 0.10f);
                    return;
                }

                case Phase.Disappearing:
                {
                    lineT = lineRetractTime > 0f ? 1f - EaseIn(Mathf.Clamp01(_clock / lineRetractTime)) : 0f;

                    float u = panelOutTime > 0f ? Mathf.Clamp01(_clock / panelOutTime) : 1f;
                    // 잠깐 커졌다가 0으로 — 페이드가 아니라 크기로만 사라진다.
                    panelScale = u < 0.35f
                        ? Mathf.Lerp(1f, outOvershoot, EaseOut(u / 0.35f))
                        : Mathf.Lerp(outOvershoot, 0f, EaseIn((u - 0.35f) / 0.65f));
                    dotScale = panelScale > 0f ? 1f : 0f;
                    return;
                }

                default:
                    lineT = 0f; panelScale = 0f; dotScale = 0f;
                    return;
            }
        }

        /// <summary>0 → 살짝 넘겼다가 → 1. 오버슛 구간과 정착 구간을 명시적으로 나눈다.</summary>
        static float Pop(float u, float overshoot)
        {
            const float peak = 0.6f;
            return u < peak
                ? Mathf.Lerp(0f, overshoot, EaseOut(u / peak))
                : Mathf.Lerp(overshoot, 1f, EaseOut((u - peak) / (1f - peak)));
        }

        static float EaseOut(float x) { x = Mathf.Clamp01(x); float k = 1f - x; return 1f - k * k * k; }
        static float EaseIn(float x)  { x = Mathf.Clamp01(x); return x * x * x; }

        void UpdateDrawCursor(float dt)
        {
            if (!_cursorInit) { _drawCursor = _cursor; _cursorInit = true; return; }

            // ★ 논리 커서는 점에 커밋될 때 순간이동한다(입력 정확도상 필요하다 — 획마다 오차를 초기화).
            //   표시 커서를 따로 두고 감쇠 추종시키면, 판정은 그대로 정확하면서
            //   화면에서는 스윽 빨려 들어간다. 보정이 있다는 사실이 눈에 안 보이게 된다.
            float k = cursorFollowTime > 1e-4f ? 1f - Mathf.Exp(-dt / cursorFollowTime) : 1f;
            _drawCursor = Vector2.Lerp(_drawCursor, _cursor, k);
        }

        void UpdateDotGrow(float dt)
        {
            for (int d = 0; d < PatternGraph.DotCount; d++)
            {
                bool on = _player.Contains(d);
                float span = on ? dotGrowTime : dotShrinkTime;
                float step = span > 1e-4f ? dt / span : 1f;
                _dotGrow[d] = Mathf.MoveTowards(_dotGrow[d], on ? 1f : 0f, step);
            }
        }

        // ── 좌표 ──────────────────────────────────────────────────────

        void ComputeViewCorners()
        {
            // 시야 모서리는 이 오브젝트(=[Head]) 로컬에 고정이다.
            _viewCorner[0] = VrUiSpace.Direction(-viewHalfAngleX, +viewHalfAngleY) * distance;  // TL
            _viewCorner[1] = VrUiSpace.Direction(+viewHalfAngleX, +viewHalfAngleY) * distance;  // TR
            _viewCorner[2] = VrUiSpace.Direction(-viewHalfAngleX, -viewHalfAngleY) * distance;  // BL
            _viewCorner[3] = VrUiSpace.Direction(+viewHalfAngleX, -viewHalfAngleY) * distance;  // BR
        }

        void ComputePanelCorners(float scale)
        {
            float h = panelAngularSize * 0.5f * scale;   // 크기 애니메이션 = 각크기를 줄였다 늘리는 것

            _panelCorner[0] = ToLocal(VrUiSpace.Direction(-h, +h));
            _panelCorner[1] = ToLocal(VrUiSpace.Direction(+h, +h));
            _panelCorner[2] = ToLocal(VrUiSpace.Direction(-h, -h));
            _panelCorner[3] = ToLocal(VrUiSpace.Direction(+h, -h));
        }

        /// <summary>추종 루트 기준 방향 → 이 오브젝트 로컬 좌표. 관성 지연이 여기서 들어온다.</summary>
        Vector3 ToLocal(Vector3 dirInPanelRoot)
        {
            Vector3 p = dirInPanelRoot * distance;
            if (panelRoot == null) return p;                       // 루트가 없으면 머리에 붙은 것과 같다
            return transform.InverseTransformPoint(panelRoot.TransformPoint(p));
        }

        /// <summary>정규화 패턴 좌표(0~1, y↑) → 패널 사각형 위의 점. 네 꼭짓점을 겹선형 보간한다.</summary>
        Vector3 Point(Vector2 uv)
        {
            Vector3 top = Vector3.LerpUnclamped(_panelCorner[0], _panelCorner[1], uv.x);
            Vector3 bot = Vector3.LerpUnclamped(_panelCorner[2], _panelCorner[3], uv.x);
            return Vector3.LerpUnclamped(bot, top, uv.y);
        }

        Vector3 DotPoint(int dot)
        {
            return (dot >= 0 && dot < PatternGraph.DotCount) ? Point(PatternGraph.Pos[dot]) : Vector3.zero;
        }

        static Color Grey(float level, float alpha = 1f)
        {
            return new Color(level, level, level, alpha);
        }
    }
}
