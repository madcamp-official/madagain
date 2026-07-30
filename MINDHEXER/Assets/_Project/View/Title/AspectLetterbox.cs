using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 카메라 뷰포트를 지정 비율(기본 13:12)로 레터박스/필러박스한다. (spec: VR 시야 프레이밍 13:12)
    ///
    /// 화면이 목표 비율보다 넓으면 좌우 필러박스, 좁으면 상하 레터박스로 잘라 정확히 13:12를 유지한다.
    /// 뷰포트 밖 영역은 <c>TitleSceneBuilder</c>가 붙이는 배경 클리어 카메라가 검게 지운다.
    /// 화면 회전/리사이즈에 매 프레임 대응한다.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [ExecuteAlways]
    public sealed class AspectLetterbox : MonoBehaviour
    {
        [Tooltip("목표 가로:세로 비율. 기본 13:12.")]
        public float targetWidth = 13f;
        public float targetHeight = 12f;

        Camera _cam;

        void OnEnable() { _cam = GetComponent<Camera>(); Apply(); }
        void Update() { Apply(); }

        void Apply()
        {
            if (_cam == null) _cam = GetComponent<Camera>();
            if (_cam == null || targetWidth <= 0f || targetHeight <= 0f) return;

            float target = targetWidth / targetHeight;
            float window = (float)Screen.width / Mathf.Max(1, Screen.height);

            Rect r;
            if (window > target)
            {
                // 창이 더 넓음 → 좌우 필러박스
                float w = target / window;
                r = new Rect((1f - w) * 0.5f, 0f, w, 1f);
            }
            else
            {
                // 창이 더 좁음(세로가 김) → 상하 레터박스
                float h = window / target;
                r = new Rect(0f, (1f - h) * 0.5f, 1f, h);
            }
            _cam.rect = r;
        }
    }
}
