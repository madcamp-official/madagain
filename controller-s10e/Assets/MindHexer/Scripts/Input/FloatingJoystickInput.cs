using UnityEngine;
using MindHexer.Shared.Input;

namespace MindHexer.Controller.Input
{
    /// <summary>
    /// 브롤스타즈식 플로팅 조이스틱 컨트롤러 컴포넌트. (모바일 이동 입력)
    /// 활성 영역 안에서 처음 누른 지점이 중심이 되고(<see cref="FloatingJoystick"/>), 떼면 사라지며
    /// 다시 누른 새 위치가 새 중심이 된다. 멀티터치에서 자기 손가락(fingerId)만 추적한다.
    ///
    /// 로직은 shared <see cref="FloatingJoystick"/>(순수/검증됨)에 있고, 여기서는 Unity 터치 라우팅과
    /// IMGUI 표시만 담당한다. 출력은 <see cref="MoveAxis"/>(-1..1 디스크).
    /// </summary>
    public sealed class FloatingJoystickInput : MonoBehaviour
    {
        [Header("동작")]
        [Tooltip("반지름 = 화면 높이 × 이 비율. DPI/해상도 독립.")]
        [Range(0.05f, 0.3f)] public float RadiusFraction = 0.12f;

        [Tooltip("데드존(0..1). 중심 근처 미세 입력 무시.")]
        [Range(0f, 0.5f)] public float DeadZone = 0.08f;

        [Tooltip("손가락이 반지름 밖으로 나가면 중심이 따라옴(thumb-walk).")]
        public bool FollowOnOverflow = false;

        [Tooltip("조이스틱 활성 영역(정규화 0..1). 기본: 가로 화면 왼쪽 절반(왼손 엄지). " +
                 "오른쪽 절반은 패턴 스와이프 패드용.")]
        public Rect ActiveRegion = new Rect(0f, 0f, 0.5f, 1f);

        [Header("표시")]
        public Color BaseColor = new Color(1f, 1f, 1f, 0.15f);
        public Color KnobColor = new Color(0.25f, 0.8f, 1f, 0.55f);

        private readonly FloatingJoystick _joy = new FloatingJoystick();
        private int _fingerId = -1;
        private Texture2D _circle;

        /// <summary>정규화된 이동 벡터(-1..1 디스크, x 오른쪽/y 위쪽). 비활성 시 (0,0).</summary>
        public Vector2 MoveAxis => _joy.Value;

        /// <summary>이동 세기(0..1).</summary>
        public float Magnitude => _joy.Magnitude;

        /// <summary>현재 조이스틱이 눌려 있는지.</summary>
        public bool Active => _joy.Active;

        private void Awake()
        {
            _circle = MakeCircleTexture(128);
        }

        private void OnDestroy()
        {
            if (_circle != null) Destroy(_circle);
        }

        private void Update()
        {
            _joy.Radius = RadiusFraction * Screen.height;
            _joy.DeadZone = DeadZone;
            _joy.FollowOnOverflow = FollowOnOverflow;

            int count = UnityEngine.Input.touchCount;

            if (!_joy.Active)
            {
                // 활성 영역 안에서 새로 시작된 터치를 잡아 중심으로 삼는다.
                for (int i = 0; i < count; i++)
                {
                    Touch t = UnityEngine.Input.GetTouch(i);
                    if (t.phase == TouchPhase.Began && InRegion(t.position))
                    {
                        _joy.Press(t.position.x, t.position.y);
                        _fingerId = t.fingerId;
                        break;
                    }
                }
                return;
            }

            // 활성 상태: 내 손가락만 추적.
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                Touch t = UnityEngine.Input.GetTouch(i);
                if (t.fingerId != _fingerId) continue;
                found = true;
                if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                {
                    _joy.Release();
                    _fingerId = -1;
                }
                else
                {
                    _joy.Drag(t.position.x, t.position.y);
                }
                break;
            }
            if (!found)
            {
                // 손가락 업 이벤트를 놓친 경우 방어적으로 해제.
                _joy.Release();
                _fingerId = -1;
            }
        }

        private bool InRegion(Vector2 screenPos)
        {
            float nx = screenPos.x / Screen.width;
            float ny = screenPos.y / Screen.height;
            return ActiveRegion.Contains(new Vector2(nx, ny));
        }

        private void OnGUI()
        {
            if (!_joy.Active || _circle == null) return;

            float radius = _joy.Radius;
            DrawDisc(_joy.Center.x, _joy.Center.y, radius * 2f, BaseColor);   // 베이스
            DrawDisc(_joy.Knob.x, _joy.Knob.y, radius * 0.9f, KnobColor);     // 노브
        }

        // 화면(원점 좌하단, y위) 좌표를 받아 GUI(원점 좌상단) 좌표로 변환해 원을 그린다.
        private void DrawDisc(float screenX, float screenYup, float diameter, Color color)
        {
            float gx = screenX - diameter * 0.5f;
            float gy = Screen.height - screenYup - diameter * 0.5f;
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(gx, gy, diameter, diameter), _circle);
            GUI.color = prev;
        }

        private static Texture2D MakeCircleTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            float r = size * 0.5f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - r;
                    float dy = y + 0.5f - r;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(r - d); // 가장자리 1px 안티에일리어싱
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
