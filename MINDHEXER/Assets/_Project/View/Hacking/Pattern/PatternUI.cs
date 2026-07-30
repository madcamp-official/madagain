using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// 점 패턴 화면 왼쪽 UI (기능형: 점·선). 휴대폰 패턴 락과 동일하게 커서(손가락) 위치가
    /// 항상 화면에 있고, 마지막 확정 점부터 커서까지 선이 끊기지 않고 이어진다.
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

        // 시작 전엔 "첫 점", 진행 중엔 "다음 점"을 같은 강조 표시(_nextHint) 하나가 가리킨다.
        // 기존 2.4배에 사용자 지시로 1.35배를 추가로 곱해 3.24배로 키웠다.
        [Tooltip("다음 목표 점 강조 표시 크기 배율(점 반지름 기준).")]
        public float nextHintScale = 2.4f * 1.35f;

        [Tooltip("타겟 전체 경로를 유령선으로 깔지. 순서는 '다음 목표 점' 강조가 전달하므로 꺼도 플레이 가능.")]
        public bool showGhostPath = true;

        [Header("색")]
        public Color ghostColor    = new Color(1f, 1f, 1f, 0.16f);
        public Color traceColor    = new Color(0.35f, 1f, 0.45f, 0.95f);
        public Color dotColor      = new Color(1f, 1f, 1f, 0.55f);
        public Color headColor     = new Color(1f, 0.95f, 0.5f, 1f);
        public Color nextHintColor = new Color(0.3f, 0.9f, 1f, 0.5f);   // 다음 목표 점 후광

        Canvas _canvas;
        RectTransform _panel;
        readonly Image[] _dots = new Image[PatternGraph.DotCount];
        readonly List<Image> _linePool = new List<Image>();
        Image _head;
        Image _nextHint;
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

            // 다음 목표 후광은 점보다 먼저 만들어 뒤에 깔리게 한다.
            _nextHint = MakeImage("NextHint", dotRadius * nextHintScale, new Vector2(0.5f, 0.5f));
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

        /// <summary>정규화 좌표(0~1, PatternGraph.Pos 공간) → 패널 중앙 기준 픽셀 좌표.</summary>
        Vector2 ToPanel(Vector2 normalized)
        {
            return (normalized - new Vector2(0.5f, 0.5f)) * panelSize;
        }

        Vector2 DotPos(int dot) => ToPanel(PatternGraph.Pos[dot]);

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

        public void Show(DotPattern target, PatternInput input, int nextDot)
        {
            EnsureCanvas();
            _canvas.gameObject.SetActive(true);
            _target = target;
            Refresh(input, nextDot);
        }

        public void Hide()
        {
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }

        /// <summary>매 틱 다시 그린다. 라인은 풀 재사용. nextDot=다음 목표 점(-1이면 없음).</summary>
        public void Refresh(PatternInput input, int nextDot)
        {
            if (_canvas == null || _target == null) return;

            _lineUsed = 0;
            foreach (var l in _linePool) l.enabled = false;

            var use = new Dictionary<int, int>();  // 변별 그린 횟수 → 부채꼴 오프셋

            // ① 타겟 유령선(선택) — 순서 정보는 ⑤ 다음 목표 강조가 담당한다.
            if (showGhostPath) DrawSequence(_target.dots, _target.LineCount, ghostColor, use);

            // ② 라이브 트레이스(확정된 점들) + 마지막 확정 점 → 커서(진행 중인 획, 끊김 없음)
            var use2 = new Dictionary<int, int>();
            DrawSequence(input.PlayerDots.ToArray(), input.PlayerDots.Count - 1, traceColor, use2);
            DrawLine(DotPos(input.CurrentDot), ToPanel(input.CursorPos), traceColor, 0f);

            // ⑤ 다음 목표 점 강조 — 획 순서를 전달하는 유일한 신호
            if (nextDot >= 0)
            {
                _nextHint.enabled = true;
                _nextHint.color = nextHintColor;
                _nextHint.rectTransform.sizeDelta = Vector2.one * (dotRadius * nextHintScale);
                _nextHint.rectTransform.anchoredPosition = DotPos(nextDot);
            }
            else _nextHint.enabled = false;

            // 점 + ③ 헤드(항상 실제 커서 위치에 — 휴대폰 패턴처럼)
            for (int i = 0; i < PatternGraph.DotCount; i++)
            {
                _dots[i].enabled = true;
                _dots[i].color = dotColor;
                _dots[i].rectTransform.anchoredPosition = DotPos(i);
            }
            _head.enabled = true;
            _head.color = headColor;
            _head.rectTransform.anchoredPosition = ToPanel(input.CursorPos);
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
