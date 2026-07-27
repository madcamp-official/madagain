using System;
using System.Collections.Generic;
using UnityEngine;
using MindHexer.Shared.Input;

namespace MindHexer.Controller.Input
{
    /// <summary>
    /// 오른쪽 절반 = **플로팅 2x2 스와이프 패턴** 패드. (가로 화면)
    /// 조이스틱처럼 자유 위치에서 시작하며, **처음 누른 지점이 항상 좌상단 노드(0)** 가 된다.
    /// 거기서 오른쪽/아래로 2x2 격자가 펼쳐지고, 안드로이드 잠금패턴처럼 노드를 스와이프로 잇는다.
    /// 로직은 shared <see cref="SwipePattern"/>(size 2), 여기서는 터치 라우팅 + IMGUI 표시만.
    ///
    /// 화면 양분: 이 컴포넌트는 <see cref="ActiveRegion"/>(오른쪽 절반)에서 시작한 터치만 받는다.
    /// (왼쪽 절반은 FloatingJoystickInput 전용.)
    ///
    ///   node 0 (첫 터치, 좌상단) ── node 1
    ///        │                      │
    ///   node 2 ──────────────── node 3
    /// </summary>
    public sealed class PatternPadInput : MonoBehaviour
    {
        [Tooltip("패턴 인식 영역(정규화 0..1). 기본: 화면 오른쪽 절반.")]
        public Rect ActiveRegion = new Rect(0.5f, 0f, 0.5f, 1f);

        [Tooltip("최대 노드 간격 = 화면 짧은 변 × 이 비율(2x2 박스 한 변). **가장 커질 수 있는 크기의 상한**으로, " +
                 "안드로이드 3x3 패턴보다 조금 작은 정도. 대부분의 시작 위치에선 아래/오른쪽 프레임까지 남은 공간에 " +
                 "맞춰 이보다 작아지므로 **터치 위치에 따라 크기가 가변**한다(여유가 아주 많은 곳에서만 이 상한에 걸림).")]
        [Range(0.15f, 0.7f)] public float MaxSpacingFraction = 0.5f;

        [Tooltip("프레임에서 남길 여백 = 화면 높이 × 이 비율(패턴이 화면 밖으로 넘어가지 않게).")]
        [Range(0f, 0.1f)] public float EdgeMarginFraction = 0.02f;

        [Tooltip("노드 히트 반경 = 간격 × 이 비율.")]
        [Range(0.2f, 0.5f)] public float HitRadiusFraction = 0.42f;

        [Range(1f, 4f)] public float UiScale = 2f;
        public Color NodeIdle = new Color(1f, 1f, 1f, 0.35f);
        public Color NodeOn = new Color(0.2f, 0.85f, 1f, 0.95f);
        public Color PathLine = new Color(0.2f, 0.85f, 1f, 0.85f);

        private const int Nodes = 4; // 2x2

        private readonly SwipePattern _pattern = new SwipePattern(2);
        private readonly Vector2[] _nodeScreen = new Vector2[Nodes]; // 화면(y-up) 노드 좌표
        private int _fingerId = -1;
        private bool _drawing;
        private bool _hasAnchor;
        private Vector2 _liveScreen;

        private Texture2D _white;
        private Texture2D _circle;

        /// <summary>현재(또는 마지막) 패턴 노드 순서.</summary>
        public IReadOnlyList<int> CurrentPattern => _pattern.Path;

        /// <summary>스와이프 완성 시(손 뗌) 발생. 인자는 노드 시퀀스(0..3).</summary>
        public event Action<int[]> PatternCompleted;

        private void Awake()
        {
            _white = MakeSolid();
            _circle = MakeCircle(96);
        }

        private void OnDestroy()
        {
            if (_white != null) Destroy(_white);
            if (_circle != null) Destroy(_circle);
        }

        private float _spacing;                        // 시작 시 화면에 맞춰 계산된 노드 간격(px)
        private float HitRadius => _spacing * HitRadiusFraction;

        private void Update()
        {
            int count = UnityEngine.Input.touchCount;

            if (_fingerId < 0)
            {
                for (int i = 0; i < count; i++)
                {
                    Touch t = UnityEngine.Input.GetTouch(i);
                    if (t.phase != TouchPhase.Began) continue;
                    if (!InRegion(t.position)) continue;
                    StartPattern(t.position);
                    _fingerId = t.fingerId;
                    break;
                }
                return;
            }

            bool found = false;
            for (int i = 0; i < count; i++)
            {
                Touch t = UnityEngine.Input.GetTouch(i);
                if (t.fingerId != _fingerId) continue;
                found = true;
                _liveScreen = t.position;

                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                    Complete();
                else
                    HitTestAndAdd(t.position);
                break;
            }
            if (!found) Complete();
        }

        private void StartPattern(Vector2 press)
        {
            // 시작점에서 아래(-y)·오른쪽(+x)로 펼쳐지므로, 프레임까지 남은 공간에 맞춰 간격을 정한다
            // (아래/오른쪽 프레임에 가까울수록 작게 → 화면 밖으로 안 나감). 상단·가로중앙 시작 시 최대.
            // 최대치도 화면 짧은 변 × MaxSpacingFraction으로 제한 → 여백이 충분해도 프레임에 닿지 않는다.
            float shorter = Mathf.Min(Screen.width, Screen.height);
            float maxSpacing = MaxSpacingFraction * shorter;
            float edgeMargin = EdgeMarginFraction * shorter;
            _spacing = PatternPadLayout.FitSpacing(press.x, press.y, Screen.width, maxSpacing, edgeMargin);
            float s = _spacing;
            // 첫 터치 = 좌상단 노드(0). 오른쪽(+x)/아래(-y, 화면 y-up)로 2x2 전개.
            _nodeScreen[0] = new Vector2(press.x, press.y);         // TL
            _nodeScreen[1] = new Vector2(press.x + s, press.y);     // TR
            _nodeScreen[2] = new Vector2(press.x, press.y - s);     // BL
            _nodeScreen[3] = new Vector2(press.x + s, press.y - s); // BR
            _hasAnchor = true;
            _drawing = true;
            _liveScreen = press;

            _pattern.Begin();
            _pattern.AddCell(0); // 시작 노드
        }

        private void HitTestAndAdd(Vector2 screenPos)
        {
            float hr = HitRadius;
            int best = -1;
            float bestSq = hr * hr;
            for (int k = 0; k < Nodes; k++)
            {
                if (_pattern.Contains(k)) continue;
                float dx = screenPos.x - _nodeScreen[k].x;
                float dy = screenPos.y - _nodeScreen[k].y;
                float sq = dx * dx + dy * dy;
                if (sq <= bestSq) { bestSq = sq; best = k; }
            }
            if (best >= 0) _pattern.AddCell(best);
        }

        private void Complete()
        {
            _drawing = false;
            _fingerId = -1;
            if (_pattern.Count > 0)
                PatternCompleted?.Invoke(_pattern.Snapshot());
            // 경로/노드는 다음 시작 전까지 화면에 남겨 확인하게 둔다.
        }

        private bool InRegion(Vector2 screenPos)
        {
            float nx = screenPos.x / Screen.width;
            float ny = screenPos.y / Screen.height;
            return ActiveRegion.Contains(new Vector2(nx, ny));
        }

        // ---- 그리기 ----

        private void OnGUI()
        {
            if (!_hasAnchor || _white == null) return;

            var path = _pattern.Path;
            float lineW = 6f * UiScale;

            // 스와이프 경로 선
            for (int i = 1; i < path.Count; i++)
                DrawLine(ToGui(_nodeScreen[path[i - 1]]), ToGui(_nodeScreen[path[i]]), lineW, PathLine);
            // 라이브 세그먼트
            if (_drawing && path.Count > 0)
                DrawLine(ToGui(_nodeScreen[path[path.Count - 1]]), ToGui(_liveScreen), lineW, PathLine);

            // 노드 점
            float dot = _spacing * 0.28f;
            for (int k = 0; k < Nodes; k++)
                DrawDisc(ToGui(_nodeScreen[k]), dot, _pattern.Contains(k) ? NodeOn : NodeIdle);
        }

        private static Vector2 ToGui(Vector2 screenPos) => new Vector2(screenPos.x, Screen.height - screenPos.y);

        private void DrawDisc(Vector2 centerGui, float diameter, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(centerGui.x - diameter * 0.5f, centerGui.y - diameter * 0.5f, diameter, diameter), _circle);
            GUI.color = prev;
        }

        private void DrawLine(Vector2 a, Vector2 b, float width, Color c)
        {
            Vector2 d = new Vector2(b.x - a.x, b.y - a.y);
            float len = Mathf.Sqrt(d.x * d.x + d.y * d.y);
            if (len < 0.5f) return;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;

            Matrix4x4 m = GUI.matrix;
            Color prev = GUI.color;
            GUI.color = c;
            GUIUtility.RotateAroundPivot(angle, a);
            GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, len, width), _white);
            GUI.matrix = m;
            GUI.color = prev;
        }

        private static Texture2D MakeSolid()
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, Color.white);
            t.Apply();
            return t;
        }

        private static Texture2D MakeCircle(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            float r = size * 0.5f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - r, dy = y + 0.5f - r;
                    float a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
