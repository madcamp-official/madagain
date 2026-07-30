using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 13:12 프레이밍을 <b>카메라 뷰포트 축소가 아니라 오버레이 검은 바</b>로 만든다.
    ///
    /// 카메라 <c>rect</c>로 레터박스하면 URP가 축소된 영역을 중간 텍스처로 렌더/블릿해 화질이 떨어지고,
    /// 바깥을 지우려 두 번째 카메라까지 필요했다. 대신 카메라는 <b>전체 화면 그대로 풀해상도</b>로 렌더하고,
    /// 넘치는 영역만 검은 UI 바로 가려 13:12를 만든다 → 선명함 유지, 카메라 1개.
    ///
    /// 이 컴포넌트가 붙은 캔버스는 <b>CanvasScaler 없이</b> ScreenSpaceOverlay여야 한다(픽셀=화면픽셀, 원점 좌하단).
    /// </summary>
    public sealed class LetterboxBars : MonoBehaviour
    {
        [Tooltip("목표 가로:세로. 기본 13:12.")]
        public float targetWidth = 13f;
        public float targetHeight = 12f;

        [Tooltip("네 방향 검은 바. 필요 없는 방향은 자동으로 크기 0.")]
        public RectTransform left, right, top, bottom;

        void OnEnable() { Layout(); }
        void Update() { Layout(); }

        void Layout()
        {
            if (targetWidth <= 0f || targetHeight <= 0f) return;
            float target = targetWidth / targetHeight;
            float w = Screen.width, h = Mathf.Max(1, Screen.height);

            float activeW = w, activeH = h;
            if (w / h > target) activeW = h * target;   // 화면이 더 넓다 → 좌우 바
            else activeH = w / target;                  // 화면이 더 좁다 → 상하 바

            float barX = Mathf.Max(0f, (w - activeW) * 0.5f);
            float barY = Mathf.Max(0f, (h - activeH) * 0.5f);

            Set(left, 0f, 0f, barX, h);
            Set(right, w - barX, 0f, barX, h);
            Set(top, 0f, h - barY, w, barY);
            Set(bottom, 0f, 0f, w, barY);
        }

        static void Set(RectTransform rt, float x, float y, float wpx, float hpx)
        {
            if (rt == null) return;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(Mathf.Max(0f, wpx), Mathf.Max(0f, hpx));
        }
    }
}
