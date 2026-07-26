using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// 점 패턴 화면 왼쪽 UI (기능형: 점·선). 4겹 — ① 타겟 유령선 ② 라이브 트레이스 ③ 현재 헤드 ④ 방향 러버밴드.
    /// 겹친 변은 부채꼴 오프셋. 기어·실 스티치·글리치 폴리시는 나중/fork(§7·§2.4). ScreenSpaceOverlay = PC 전용.
    /// </summary>
    public class PatternUI : MonoBehaviour
    {
        [Header("배치")]
        public float panelSize = 320f;
        public Vector2 leftMargin = new Vector2(90f, 0f);

        [Header("모양")]
        public float lineThickness = 7f;
        public float dotRadius = 15f;
        public float fanSpacing = 10f;   // 겹친 변 부채꼴 간격

        [Header("색")]
        public Color ghostColor  = new Color(1f, 1f, 1f, 0.16f);
        public Color traceColor  = new Color(0.35f, 1f, 0.45f, 0.95f);
        public Color rubberColor = new Color(1f, 1f, 1f, 0.45f);
        public Color dotColor    = new Color(1f, 1f, 1f, 0.55f);
        public Color headColor   = new Color(1f, 0.95f, 0.5f, 1f);

        Canvas _canvas;
        RectTransform _panel;
        readonly Image[] _dots = new Image[PatternGraph.DotCount];
        readonly List<Image> _linePool = new List<Image>();
        Image _head;
        int _lineUsed;
        DotPattern _target;

        void EnsureCanvas()
        {
            if (_canvas != null) return;

            var cgo = new GameObject("[PatternCanvas]");
            cgo.transform.SetParent(transform, false);
            _canvas = cgo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 500;
            cgo.AddComponent<CanvasScaler>();

            var pgo = new GameObject("Panel");
            _panel = pgo.AddComponent<RectTransform>();
            _panel.SetParent(_canvas.transform, false);
            _panel.anchorMin = _panel.anchorMax = new Vector2(0f, 0.5f);  // 화면 왼쪽 중앙
            _panel.pivot = new Vector2(0f, 0.5f);
            _panel.anchoredPosition = leftMargin;
            _panel.sizeDelta = new Vector2(panelSize, panelSize);

            for (int i = 0; i < PatternGraph.DotCount; i++) _dots[i] = MakeImage("Dot", dotRadius * 2f, new Vector2(0.5f, 0.5f));
            _head = MakeImage("Head", dotRadius * 2.2f, new Vector2(0.5f, 0.5f));
        }

        Image MakeImage(string name, float size, Vector2 pivot)
        {
            var go = new GameObject(name);
            var img = go.AddComponent<Image>();
            var rt = img.rectTransform;
            rt.SetParent(_panel, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);  // 패널 중앙 기준
            rt.pivot = pivot;
            rt.sizeDelta = new Vector2(size, size);
            return img;
        }

        Vector2 DotPos(int dot)
        {
            return (PatternGraph.Pos[dot] - new Vector2(0.5f, 0.5f)) * panelSize;  // 패널 중앙 기준 좌표
        }

        Image GetLine()
        {
            if (_lineUsed < _linePool.Count) return _linePool[_lineUsed++];
            var img = MakeImage("Line", 1f, new Vector2(0f, 0.5f));
            _linePool.Add(img);
            _lineUsed++;
            return img;
        }

        void DrawLine(Vector2 a, Vector2 b, Color col, float perpOffset)
        {
            var img = GetLine();
            img.enabled = true;
            img.color = col;

            Vector2 dir = b - a;
            float len = dir.magnitude;
            Vector2 perp = len > 0.001f ? new Vector2(-dir.y, dir.x).normalized * perpOffset : Vector2.zero;

            var rt = img.rectTransform;
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = a + perp;
            rt.sizeDelta = new Vector2(len, lineThickness);
            rt.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        }

        public void Show(DotPattern target, PatternInput input)
        {
            EnsureCanvas();
            _canvas.gameObject.SetActive(true);
            _target = target;
            Refresh(input);
        }

        public void Hide()
        {
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }

        /// <summary>매 틱 다시 그린다. 라인은 풀 재사용.</summary>
        public void Refresh(PatternInput input)
        {
            if (_canvas == null || _target == null) return;

            _lineUsed = 0;
            foreach (var l in _linePool) l.enabled = false;

            var use = new Dictionary<int, int>();  // 변별 그린 횟수 → 부채꼴 오프셋

            // ① 타겟 유령선
            DrawSequence(_target.dots, _target.LineCount, ghostColor, use);

            // ② 라이브 트레이스 (플레이어가 실제 그은 것) — 진하게
            var use2 = new Dictionary<int, int>();
            DrawSequence(input.PlayerDots.ToArray(), input.PlayerDots.Count - 1, traceColor, use2);

            // ④ 러버밴드 (현재 → pending)
            if (input.PendingNeighbor >= 0)
                DrawLine(DotPos(input.CurrentDot), DotPos(input.PendingNeighbor), rubberColor, 0f);

            // 점 + ③ 헤드
            for (int i = 0; i < PatternGraph.DotCount; i++)
            {
                _dots[i].enabled = true;
                _dots[i].color = dotColor;
                _dots[i].rectTransform.anchoredPosition = DotPos(i);
            }
            _head.enabled = true;
            _head.color = headColor;
            _head.rectTransform.anchoredPosition = DotPos(input.CurrentDot);
        }

        void DrawSequence(int[] dots, int lineCount, Color col, Dictionary<int, int> use)
        {
            for (int i = 0; i < lineCount; i++)
            {
                int a = dots[i], b = dots[i + 1];
                int e = PatternGraph.EdgeBetween(a, b);
                int k = use.TryGetValue(e, out int v) ? v : 0;
                use[e] = k + 1;
                float offset = (k - 1) * fanSpacing;   // 0,±spacing,... 부채꼴로 펼침
                DrawLine(DotPos(a), DotPos(b), col, offset);
            }
        }
    }
}
