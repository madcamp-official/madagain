using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 해킹 패턴 패널 — <b>테두리·연결선·패턴·커서를 메시 하나로</b> 그린다.
    ///
    /// <code>
    ///   ╲                              ╱      화면 네 꼭짓점에서
    ///    ╲ ┌──────────────────┐ ╱        패널 네 꼭짓점으로
    ///      │   ●          ●   │              연결선 4개가 상시 유지
    ///      │                  │              (점은 사각형 안쪽에 여유를 두고 놓인다)
    ///    ╱ │   ●          ●   │ ╲
    ///   ╱  └──────────────────┘  ╲
    /// </code>
    ///
    /// <para><b>이 연출의 핵심</b> — 패널은 머리에 완전히 고정되지 않고 관성으로 늦게 따라온다
    /// (<see cref="VrUiFollow"/>). 그런데 연결선의 바깥 끝은 <b>화면에 고정</b>돼 있다. 그래서 고개를
    /// 돌리면 선 4개가 눈에 띄게 휘고 늘어난다 — <b>관성이 결함이 아니라 연출로 읽히게 된다.</b></para>
    ///
    /// <para>★ <b>바깥 끝은 카메라 프러스텀에서 직접 구한다</b>(<see cref="useCameraFrustum"/>).
    /// 각도를 손으로 추정하면 화면 끝에서 떨어진다 — 실제로 추정값 32°가 실측 39.2°와 어긋나
    /// 선이 화면 안쪽에 떠 있었다. 뷰포트 (0,0)~(1,1)을 쓰면 <b>해상도·종횡비·FOV가 뭐든
    /// 항상 화면 네 꼭짓점에 정확히 닿는다.</b></para>
    ///
    /// <para>★ <b>모든 요소에 검은 테두리를 두른다</b>(<see cref="drawOutline"/>). 흰 배경 앞에서는
    /// 흰 UI가 통째로 사라지기 때문이다. 검은 윤곽을 <b>전부 먼저</b> 그린 뒤 흰 본체를 덮는
    /// 2패스 방식이다 — 요소별로 번갈아 그리면 뒤 요소의 윤곽이 앞 요소를 갉아먹는다.</para>
    ///
    /// <para><b>등장/소멸은 페이드가 아니다.</b> 알파는 건드리지 않고 <b>길이와 크기만</b> 움직인다.</para>
    ///
    /// <para><b>배치</b> — 이 오브젝트는 <c>[Head]</c> 아래에 있고 <b>로컬 트랜스폼이 항등</b>이어야
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

        [Tooltip("화면 네 꼭짓점을 구할 카메라. 비우면 자동으로 찾는다.")]
        public Camera viewCamera;

        // ── 배치 ──────────────────────────────────────────────────────
        [Header("배치")]
        [Tooltip("눈에서 UI 평면까지 거리(m).")]
        [Range(0.5f, 4f)] public float distance = 1.8f;

        [Tooltip("★ 화면 네 꼭짓점을 카메라 프러스텀에서 구한다. 해상도·종횡비·FOV가 바뀌어도 " +
                 "항상 화면 끝에 정확히 닿는다. 끄면 아래 각도 추정값을 쓴다(어긋난다).")]
        public bool useCameraFrustum = true;

        [Tooltip("화면 끝에서 안쪽으로 들이는 양(뷰포트 비율). 0 = 화면 꼭짓점에 정확히 닿는다.")]
        [Range(0f, 0.2f)] public float viewportMargin = 0f;

        [Tooltip("프러스텀을 못 쓸 때의 가로 반각.")]
        [Range(5f, 80f)] public float viewHalfAngleX = 39f;
        [Tooltip("프러스텀을 못 쓸 때의 세로 반각.")]
        [Range(5f, 60f)] public float viewHalfAngleY = 24f;

        [Tooltip("중앙 정사각 패널의 각크기.")]
        [Range(4f, 50f)] public float panelAngularSize = 24f;

        [Tooltip("점을 사각형 안쪽으로 들이는 비율. 사각형이 점 네 개를 여유 있게 감싼다.")]
        [Range(0f, 0.45f)] public float dotInset = 0.16f;

        // ── 굵기 ──────────────────────────────────────────────────────
        [Header("굵기 (m, 위 거리 기준)")]
        public float borderWidth = 0.0035f;
        public float connectorWidth = 0.0025f;
        public float ghostWidth = 0.0035f;

        [Tooltip("가장 최근에 그은 선의 굵기. 순서 정보를 굵기로 전달한다(색을 못 쓰므로).")]
        public float traceWidthNewest = 0.012f;
        [Tooltip("가장 오래된 선의 굵기.")]
        public float traceWidthOldest = 0.005f;

        [Tooltip("같은 변을 여러 번 지날 때 갈라지는 부푼 양(m). 최대 3겹까지 구분된다.")]
        public float fanBulge = 0.035f;

        public float dotRadius = 0.012f;
        [Tooltip("닿은 점이 커지는 배율.")]
        public float dotHitScale = 1.7f;
        public float cursorRadius = 0.016f;

        [Tooltip("다음에 이어야 할 점 중앙에 찍히는 작은 점의 반지름.")]
        public float nextCoreRadius = 0.0035f;

        // ── 검은 테두리 ───────────────────────────────────────────────
        [Header("검은 테두리 (흰 배경에서 안 보이는 것을 막는다)")]
        public bool drawOutline = true;

        [Tooltip("본체 바깥으로 더 나가는 두께(m). 선은 양쪽으로 이만큼씩 넓어진다.")]
        public float outlineWidth = 0.0022f;

        [Range(0f, 1f)] public float outlineLevel = 0f;

        // ── 밝기 (색 신호 폐기 — 명도만 쓴다) ─────────────────────────
        [Header("밝기 0~1")]
        [Range(0f, 1f)] public float borderLevel = 0.55f;
        [Range(0f, 1f)] public float connectorLevel = 0.35f;
        [Range(0f, 1f)] public float ghostLevel = 0.18f;
        [Range(0f, 1f)] public float traceLevel = 0.95f;
        [Range(0f, 1f)] public float dotLevel = 0.6f;
        [Range(0f, 1f)] public float dotHitLevel = 1f;
        [Range(0f, 1f)] public float cursorLevel = 1f;
        [Range(0f, 1f)] public float nextHintLevel = 0.3f;
        [Tooltip("다음 목표 점 중앙의 작은 점. 0 = 검정 — 흰 점 위에 뚫린 것처럼 보인다.")]
        [Range(0f, 1f)] public float nextCoreLevel = 0f;

        // ── 타이밍 ────────────────────────────────────────────────────
        [Header("등장 / 소멸 (초)")]
        public float lineGrowTime = 0.18f;
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
        [Tooltip("중앙 작은 점이 나타나는 시간.")]
        public float coreGrowTime = 0.08f;
        [Tooltip("★ 지나간 목표의 중앙 점이 부드럽게 줄어들며 사라지는 시간.")]
        public float coreShrinkTime = 0.24f;

        // ── 저작용 미리보기 ───────────────────────────────────────────
        [Header("미리보기 (에디터 전용 — 실행 중에는 무시된다)")]
        [Tooltip("에디터에서만 쓴다. ★ 실행 시에는 항상 숨긴 상태로 시작한다 — " +
                 "해킹을 시작하지도 않았는데 UI가 떠 있으면 안 된다.")]
        public bool previewVisible = true;
        [Tooltip("목표 패턴(유령선). 점 인덱스 0=TL 1=TR 2=BL 3=BR.")]
        public int[] previewTarget = { 0, 1, 3, 2, 0, 3 };
        [Tooltip("플레이어가 지난 점.")]
        public int[] previewPlayer = { 0, 1, 3, 2, 0 };
        [Tooltip("커서 위치(정규화 0~1).")]
        public Vector2 previewCursor = new Vector2(0.55f, 0.25f);
        [Tooltip("다음 목표 점(미리보기용). -1이면 없음.")]
        public int previewNextDot = 3;

        // ── 상태 ──────────────────────────────────────────────────────
        enum Phase { Hidden, Appearing, Shown, Disappearing }
        Phase _phase = Phase.Hidden;
        float _clock;

        readonly UiMeshBuilder _mb = new UiMeshBuilder();
        Mesh _mesh;
        MeshFilter _mf;
        MeshRenderer _mr;

        readonly float[] _dotGrow = new float[PatternGraph.DotCount];
        readonly float[] _coreScale = new float[PatternGraph.DotCount];
        readonly int[] _edgeUse = new int[PatternGraph.EdgeCount];
        Vector2 _drawCursor;
        bool _cursorInit;

        int[] _target;
        readonly List<int> _player = new List<int>();
        Vector2 _cursor;
        int _nextDot = -1;

        float _lastTime;
        bool _outlinePass;

        readonly Vector3[] _viewCorner = new Vector3[4];
        readonly Vector3[] _panelCorner = new Vector3[4];

        public bool IsVisible { get { return _phase != Phase.Hidden; } }

        // ── 외부 API ──────────────────────────────────────────────────

        [ContextMenu("패널 — 등장")]
        public void Show()
        {
            if (_phase == Phase.Appearing || _phase == Phase.Shown) return;
            _phase = Phase.Appearing;
            _clock = 0f;
        }

        [ContextMenu("패널 — 소멸")]
        public void Hide()
        {
            if (_phase == Phase.Hidden || _phase == Phase.Disappearing) return;
            _phase = Phase.Disappearing;
            _clock = 0f;
        }

        /// <summary>해킹 시작 — 기존 <c>PatternUI</c>와 같은 시그니처라 미니게임이 그대로 쓸 수 있다.</summary>
        public void Show(DotPattern target, PatternInput input, int nextDot)
        {
            SetPattern(target != null ? target.dots : null,
                       input != null ? (IList<int>)input.PlayerDots : null,
                       input != null ? input.CursorPos : Vector2.zero,
                       nextDot);
            Show();
        }

        /// <summary>매 틱 갱신.</summary>
        public void Refresh(PatternInput input, int nextDot)
        {
            if (input == null) return;
            SetPattern(_target, input.PlayerDots, input.CursorPos, nextDot);
        }

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
                _mesh.MarkDynamic();
                _mesh.hideFlags = HideFlags.DontSave;
            }
            _mf.sharedMesh = _mesh;

            EnsureMaterial();
            _lastTime = Time.realtimeSinceStartup;
            ResetPhase();
        }

        void OnValidate()
        {
            if (!Application.isPlaying) ResetPhase();
        }

        /// <summary>
        /// ★ 실행 중에는 <b>무조건 숨긴 상태</b>로 시작한다. <see cref="previewVisible"/>은 저작 편의용
        /// 플래그일 뿐인데, 프리팹에 켠 채로 저장돼 있으면 게임 시작 즉시 UI가 떠 버린다(실제로 그랬다).
        /// </summary>
        void ResetPhase()
        {
            _phase = (!Application.isPlaying && previewVisible) ? Phase.Shown : Phase.Hidden;
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
        }

        void PullPreview()
        {
            _target = previewTarget;
            _player.Clear();
            if (previewPlayer != null) for (int i = 0; i < previewPlayer.Length; i++) _player.Add(previewPlayer[i]);
            _cursor = previewCursor;
            _nextDot = previewNextDot;
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
            UpdateDrawCursor(dt);
            UpdateDotGrow(dt);
            UpdateCoreScale(dt);

            // 검은 윤곽을 전부 먼저, 그 위에 본체. 요소별로 번갈아 그리면
            // 뒤에 그려지는 요소의 윤곽이 앞 요소의 본체를 갉아먹는다.
            if (drawOutline)
            {
                _outlinePass = true;
                BuildContent(lineT, panelScale, dotScale);
            }
            _outlinePass = false;
            BuildContent(lineT, panelScale, dotScale);

            _mb.Apply(_mesh);
        }

        void BuildContent(float lineT, float panelScale, float dotScale)
        {
            // ① 연결선 — 화면 꼭짓점에서 패널 꼭짓점 쪽으로 lineT 만큼.
            //    확대/축소 중에도 _panelCorner가 매 프레임 갱신되므로 저절로 따라간다.
            if (lineT > 0f && connectorLevel > 0f)
                for (int i = 0; i < 4; i++)
                    L(_viewCorner[i], Vector3.Lerp(_viewCorner[i], _panelCorner[i], lineT),
                      connectorWidth, Grey(connectorLevel));

            if (panelScale <= 0.001f) return;

            // ② 테두리 — 점보다 크게(점은 dotInset 만큼 안쪽에 있다).
            Color border = Grey(borderLevel);
            L(_panelCorner[0], _panelCorner[1], borderWidth, border);
            L(_panelCorner[1], _panelCorner[3], borderWidth, border);
            L(_panelCorner[3], _panelCorner[2], borderWidth, border);
            L(_panelCorner[2], _panelCorner[0], borderWidth, border);

            // ③ 목표 패턴 유령선 (겹치면 부채꼴로 갈라진다)
            if (_target != null && _target.Length > 1 && ghostLevel > 0f)
                DrawSequence(_target, _target.Length - 1, ghostWidth, ghostWidth, Grey(ghostLevel));

            // ④ 확정 트레이스 — 최근일수록 굵다(순서를 굵기로 전달).
            DrawSequence(_player, _player.Count - 1, traceWidthOldest, traceWidthNewest, Grey(traceLevel));

            // ⑤ 진행 중 선 — 커서까지. 점 바로 옆에서는 짧은 선이 지저분하므로 죽인다.
            //    알파식은 AOSP LockPatternView의 calculateLastSegmentAlpha를 그대로 쓴다.
            if (_player.Count > 0)
            {
                int cur = _player[_player.Count - 1];
                float frac = Vector2.Distance(_drawCursor, PatternGraph.Pos[cur]);   // 점 간격 = 1.0
                float a = Mathf.Clamp01((frac - 0.3f) * 4f);
                if (a > 0f) L(DotPoint(cur), Point(_drawCursor), traceWidthNewest, Grey(traceLevel, a));
            }

            // ⑥ 다음 목표 점 후광 — 점보다 먼저 그린다. 나중에 그리면 점을 덮어 버린다.
            if (_nextDot >= 0 && nextHintLevel > 0f)
                C(DotPoint(_nextDot), dotRadius * 2.4f * dotScale, Grey(nextHintLevel));

            // ⑦ 점 — 닿으면 커진다(96ms 커지고 192ms 작아지는 비대칭).
            for (int d = 0; d < PatternGraph.DotCount; d++)
            {
                float g = _dotGrow[d];
                float r = dotRadius * Mathf.Lerp(1f, dotHitScale, g) * dotScale;
                C(DotPoint(d), r, Grey(Mathf.Lerp(dotLevel, dotHitLevel, g)));
            }

            // ⑧ 중앙 작은 점 — 지나간 목표는 부드럽게 줄어들며 사라진다.
            //    이미 검정이라 윤곽을 두르면 그냥 커지는 셈이므로 윤곽 패스에서는 건너뛴다.
            if (!_outlinePass)
                for (int d = 0; d < PatternGraph.DotCount; d++)
                {
                    float s = _coreScale[d];
                    if (s <= 0.001f) continue;
                    _mb.AddCircle(DotPoint(d), nextCoreRadius * s * dotScale, Grey(nextCoreLevel));
                }

            // ⑨ 커서
            if (dotScale > 0f) C(Point(_drawCursor), cursorRadius * dotScale, Grey(cursorLevel));
        }

        // ── 윤곽 패스를 흡수하는 그리기 래퍼 ─────────────────────────

        void L(Vector3 a, Vector3 b, float w, Color c)
        {
            if (_outlinePass) _mb.AddLine(a, b, w + outlineWidth * 2f, Grey(outlineLevel, c.a));
            else _mb.AddLine(a, b, w, c);
        }

        void Cv(Vector3 a, Vector3 b, float bulge, float w, Color c)
        {
            if (_outlinePass) _mb.AddCurve(a, b, bulge, w + outlineWidth * 2f, Grey(outlineLevel, c.a));
            else _mb.AddCurve(a, b, bulge, w, c);
        }

        void C(Vector3 p, float r, Color c)
        {
            if (_outlinePass) _mb.AddCircle(p, r + outlineWidth, Grey(outlineLevel, c.a));
            else _mb.AddCircle(p, r, c);
        }

        /// <summary>
        /// 점 시퀀스를 잇는다. <b>같은 변을 다시 지나면 곡선으로 갈라 놓는다</b> — 완전히 겹치면
        /// 몇 번 지났는지 보이지 않기 때문. 0겹=직선, 1겹=한쪽, 2겹=반대쪽 (최대 3겹 구분).
        /// </summary>
        void DrawSequence(IList<int> dots, int lineCount, float widthFirst, float widthLast, Color color)
        {
            if (dots == null || lineCount <= 0) return;

            for (int i = 0; i < _edgeUse.Length; i++) _edgeUse[i] = 0;

            for (int i = 0; i < lineCount; i++)
            {
                int a = dots[i], b = dots[i + 1];
                if (a == b) continue;

                int e = PatternGraph.EdgeBetween(a, b);
                int k = 0;
                if (e >= 0 && e < _edgeUse.Length) { k = _edgeUse[e]; _edgeUse[e] = k + 1; }

                float bulge = k == 0 ? 0f : (k == 1 ? fanBulge : -fanBulge);
                float t = lineCount > 1 ? i / (float)(lineCount - 1) : 1f;
                Cv(DotPoint(a), DotPoint(b), bulge, Mathf.Lerp(widthFirst, widthLast, t), color);
            }
        }

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

                    dotScale = Mathf.Clamp01((_clock - (lineGrowTime + dotDelay)) / 0.10f);
                    return;
                }

                case Phase.Disappearing:
                {
                    lineT = lineRetractTime > 0f ? 1f - EaseIn(Mathf.Clamp01(_clock / lineRetractTime)) : 0f;

                    float u = panelOutTime > 0f ? Mathf.Clamp01(_clock / panelOutTime) : 1f;
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
                _dotGrow[d] = Mathf.MoveTowards(_dotGrow[d], on ? 1f : 0f, span > 1e-4f ? dt / span : 1f);
            }
        }

        /// <summary>중앙 작은 점 — 목표일 때 빠르게 나타나고, 지나가면 <b>부드럽게 줄어들며</b> 사라진다.</summary>
        void UpdateCoreScale(float dt)
        {
            for (int d = 0; d < PatternGraph.DotCount; d++)
            {
                bool on = d == _nextDot;
                float span = on ? coreGrowTime : coreShrinkTime;
                _coreScale[d] = Mathf.MoveTowards(_coreScale[d], on ? 1f : 0f, span > 1e-4f ? dt / span : 1f);
            }
        }

        // ── 좌표 ──────────────────────────────────────────────────────

        /// <summary>
        /// 화면 네 꼭짓점. 프러스텀에서 구하면 해상도·종횡비·FOV와 무관하게 항상 화면 끝에 닿는다.
        /// </summary>
        void ComputeViewCorners()
        {
            Camera cam = useCameraFrustum ? ResolveCamera() : null;

            if (cam != null)
            {
                float m = viewportMargin;
                _viewCorner[0] = FromWorld(cam.ViewportToWorldPoint(new Vector3(m,      1f - m, distance)));  // TL
                _viewCorner[1] = FromWorld(cam.ViewportToWorldPoint(new Vector3(1f - m, 1f - m, distance)));  // TR
                _viewCorner[2] = FromWorld(cam.ViewportToWorldPoint(new Vector3(m,      m,      distance)));  // BL
                _viewCorner[3] = FromWorld(cam.ViewportToWorldPoint(new Vector3(1f - m, m,      distance)));  // BR
                return;
            }

            // 폴백 — 카메라가 없을 때만. 각도 추정값이라 화면 끝과 어긋난다.
            _viewCorner[0] = VrUiSpace.Direction(-viewHalfAngleX, +viewHalfAngleY) * distance;
            _viewCorner[1] = VrUiSpace.Direction(+viewHalfAngleX, +viewHalfAngleY) * distance;
            _viewCorner[2] = VrUiSpace.Direction(-viewHalfAngleX, -viewHalfAngleY) * distance;
            _viewCorner[3] = VrUiSpace.Direction(+viewHalfAngleX, -viewHalfAngleY) * distance;
        }

        Camera ResolveCamera()
        {
            if (viewCamera != null) return viewCamera;
            if (Camera.main != null) return viewCamera = Camera.main;

            // 리그에서는 카메라가 형제 subtree에 있다([Head] > Main Camera / [UiRig] > 이것).
            Transform p = transform.parent;
            while (p != null)
            {
                Camera c = p.GetComponentInChildren<Camera>(true);
                if (c != null) return viewCamera = c;
                p = p.parent;
            }
            return null;
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
            if (panelRoot == null) return p;
            return transform.InverseTransformPoint(panelRoot.TransformPoint(p));
        }

        Vector3 FromWorld(Vector3 world) => transform.InverseTransformPoint(world);

        /// <summary>
        /// 정규화 패턴 좌표(0~1, y↑) → 패널 사각형 위의 점.
        /// <see cref="dotInset"/> 만큼 안쪽으로 들여 <b>사각형이 점들을 여유 있게 감싸게</b> 한다.
        /// </summary>
        Vector3 Point(Vector2 uv)
        {
            float lo = dotInset, hi = 1f - dotInset;
            float x = Mathf.LerpUnclamped(lo, hi, uv.x);
            float y = Mathf.LerpUnclamped(lo, hi, uv.y);

            Vector3 top = Vector3.LerpUnclamped(_panelCorner[0], _panelCorner[1], x);
            Vector3 bot = Vector3.LerpUnclamped(_panelCorner[2], _panelCorner[3], x);
            return Vector3.LerpUnclamped(bot, top, y);
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
