using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 화면 정중앙에 점(+선택적 십자선)을 표시한다. 1인칭 뷰모델 구도를 잡을 때 기준선.
    /// 해킹 조준점을 겸한다(§7 — 사거리 안 대상은 하이라이트로 판정하므로 점은 기준선 역할).
    /// [ExecuteAlways] — Play를 안 눌러도 Game 뷰에 바로 보인다.
    /// 켜고 끄기: Tools/뷰모델/화면 중앙 점 표시  (또는 이 컴포넌트를 비활성)
    /// (Precog에서 포팅, 변경 없음)
    /// </summary>
    [ExecuteAlways]
    public class ScreenCenterDot : MonoBehaviour
    {
        [Header("점")]
        public float dotSize = 4f;
        public Color dotColor = new Color(1f, 0.2f, 0.2f, 0.95f);

        [Header("십자선")]
        public bool  showCross = true;
        public float crossLength = 14f;
        public float crossThickness = 1f;
        public Color crossColor = new Color(1f, 1f, 1f, 0.45f);

        [Header("보조선")]
        [Tooltip("화면을 3분할하는 안내선(구도 잡기용)")]
        public bool showThirds;
        public Color thirdsColor = new Color(1f, 1f, 1f, 0.15f);

        static Texture2D px;
        static Texture2D Px
        {
            get
            {
                if (px == null)
                {
                    px = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                    px.SetPixel(0, 0, Color.white);
                    px.Apply();
                }
                return px;
            }
        }

        void OnGUI()
        {
            float w = Screen.width, h = Screen.height;
            float cx = w * 0.5f, cy = h * 0.5f;
            Color prev = GUI.color;

            if (showThirds)
            {
                GUI.color = thirdsColor;
                for (int i = 1; i <= 2; i++)
                {
                    GUI.DrawTexture(new Rect(w * i / 3f, 0f, 1f, h), Px);   // 세로
                    GUI.DrawTexture(new Rect(0f, h * i / 3f, w, 1f), Px);   // 가로
                }
            }

            if (showCross)
            {
                GUI.color = crossColor;
                float t = Mathf.Max(1f, crossThickness), L = crossLength;
                GUI.DrawTexture(new Rect(cx - L, cy - t * 0.5f, L * 2f, t), Px);   // 가로
                GUI.DrawTexture(new Rect(cx - t * 0.5f, cy - L, t, L * 2f), Px);   // 세로
            }

            GUI.color = dotColor;
            float s = Mathf.Max(1f, dotSize);
            GUI.DrawTexture(new Rect(cx - s * 0.5f, cy - s * 0.5f, s, s), Px);

            GUI.color = prev;
        }
    }
}
