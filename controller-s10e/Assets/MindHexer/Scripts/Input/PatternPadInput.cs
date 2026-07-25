using System;
using UnityEngine;
using MindHexer.Shared.Protocol;
using MindHexer.Shared.Input;

namespace MindHexer.Controller.Input
{
    /// <summary>
    /// 오른쪽 패턴 패드 — 안드로이드 잠금패턴식 **스와이프** 입력. (가로 화면)
    /// 패드 안에서 누르면 시작, 드래그로 지나가는 셀을 순서대로 잇고(중간 셀 자동 포함), 떼면 완성.
    /// 로직은 shared <see cref="SwipePattern"/>(순수/검증됨), 여기서는 터치 라우팅 + IMGUI 표시만.
    /// </summary>
    public sealed class PatternPadInput : MonoBehaviour
    {
        [Range(1f, 4f)] public float UiScale = 2f;
        public Color PadBg = new Color(0f, 0f, 0f, 0.35f);
        public Color GridLine = new Color(1f, 1f, 1f, 0.30f);
        public Color NodeIdle = new Color(1f, 1f, 1f, 0.35f);
        public Color NodeOn = new Color(0.2f, 0.85f, 1f, 0.95f);
        public Color PathLine = new Color(0.2f, 0.85f, 1f, 0.85f);

        private readonly SwipePattern _pattern = new SwipePattern();
        private int _fingerId = -1;
        private bool _drawing;
        private Vector2 _liveGui; // 현재 손가락 위치(GUI 좌표)

        private Texture2D _white;
        private Texture2D _circle;

        /// <summary>현재(또는 마지막) 패턴 셀 순서.</summary>
        public System.Collections.Generic.IReadOnlyList<int> CurrentPattern => _pattern.Path;

        /// <summary>스와이프 완성 시(손 뗌) 발생. 인자는 셀 시퀀스.</summary>
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

        private void Update()
        {
            int count = UnityEngine.Input.touchCount;

            if (_fingerId < 0)
            {
                // 패드 안에서 시작된 터치를 잡는다.
                for (int i = 0; i < count; i++)
                {
                    Touch t = UnityEngine.Input.GetTouch(i);
                    if (t.phase != TouchPhase.Began) continue;
                    if (!InPadCell(t.position, out int cell)) continue;
                    _pattern.Begin();
                    _pattern.AddCell(cell);
                    _fingerId = t.fingerId;
                    _drawing = true;
                    _liveGui = ToGui(t.position);
                    break;
                }
                return;
            }

            // 내 손가락 추적.
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                Touch t = UnityEngine.Input.GetTouch(i);
                if (t.fingerId != _fingerId) continue;
                found = true;
                _liveGui = ToGui(t.position);

                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                {
                    Complete();
                }
                else if (InPadCell(t.position, out int cell))
                {
                    _pattern.AddCell(cell); // 새 셀만 추가(중간 셀 자동 포함)
                }
                break;
            }
            if (!found) Complete(); // 업 이벤트 놓친 경우 방어
        }

        private void Complete()
        {
            _drawing = false;
            _fingerId = -1;
            if (_pattern.Count > 0)
                PatternCompleted?.Invoke(_pattern.Snapshot());
            // 경로는 다음 Begin 전까지 화면에 남겨 사용자가 확인하게 둔다.
        }

        private bool InPadCell(Vector2 screenPos, out int cell)
        {
            float nx = screenPos.x / Screen.width;
            float ny = screenPos.y / Screen.height;
            return HackGridMath.TryToCellIndex(nx, ny, out cell);
        }

        // ---- 그리기 ----

        private void OnGUI()
        {
            if (_white == null) return;
            Rect pad = PadGuiRect();
            float th = Mathf.Max(1f, UiScale);

            DrawRect(pad, PadBg);
            // 테두리
            DrawRect(new Rect(pad.x, pad.y, pad.width, th), GridLine);
            DrawRect(new Rect(pad.x, pad.yMax - th, pad.width, th), GridLine);
            DrawRect(new Rect(pad.x, pad.y, th, pad.height), GridLine);
            DrawRect(new Rect(pad.xMax - th, pad.y, th, pad.height), GridLine);
            // 내부 분할선
            for (int i = 1; i < HackGridMath.Size; i++)
            {
                float vx = pad.x + pad.width * i / HackGridMath.Size;
                DrawRect(new Rect(vx - th / 2f, pad.y, th, pad.height), GridLine);
                float hy = pad.y + pad.height * i / HackGridMath.Size;
                DrawRect(new Rect(pad.x, hy - th / 2f, pad.width, th), GridLine);
            }

            // 스와이프 경로 선
            var path = _pattern.Path;
            float lineW = 6f * UiScale;
            for (int i = 1; i < path.Count; i++)
                DrawLine(CellCenterGui(pad, path[i - 1]), CellCenterGui(pad, path[i]), lineW, PathLine);
            // 라이브 세그먼트(마지막 노드 → 현재 손가락)
            if (_drawing && path.Count > 0)
                DrawLine(CellCenterGui(pad, path[path.Count - 1]), _liveGui, lineW, PathLine);

            // 노드 점(방문=밝게)
            float dot = pad.width / HackGridMath.Size * 0.30f;
            for (int cell = 0; cell < HackGridMath.CellCount; cell++)
            {
                Vector2 c = CellCenterGui(pad, cell);
                Color col = _pattern.Contains(cell) ? NodeOn : NodeIdle;
                DrawDisc(c, dot, col);
            }
        }

        private Rect PadGuiRect()
        {
            float w = Screen.width, h = Screen.height;
            float x = HackGridMath.PadX * w;
            float pw = HackGridMath.PadW * w;
            float ph = HackGridMath.PadH * h;
            float guiY = h - (HackGridMath.PadY + HackGridMath.PadH) * h;
            return new Rect(x, guiY, pw, ph);
        }

        private static Vector2 CellCenterGui(Rect pad, int cell)
        {
            float cw = pad.width / HackGridMath.Size;
            float ch = pad.height / HackGridMath.Size;
            int col = cell % HackGridMath.Size;
            int row = cell / HackGridMath.Size;              // 0 = 하단(화면 y-up)
            int rowFromTop = HackGridMath.Size - 1 - row;    // GUI는 위가 y작음
            return new Vector2(pad.x + (col + 0.5f) * cw, pad.y + (rowFromTop + 0.5f) * ch);
        }

        private static Vector2 ToGui(Vector2 screenPos) => new Vector2(screenPos.x, Screen.height - screenPos.y);

        private void DrawRect(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _white);
            GUI.color = prev;
        }

        private void DrawDisc(Vector2 center, float diameter, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(center.x - diameter * 0.5f, center.y - diameter * 0.5f, diameter, diameter), _circle);
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
