using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// VR: <c>ScreenSpaceOverlay</c> HUD 캔버스를 머리(카메라) 앞 <c>WorldSpace</c> 패널로 변환한다.
    ///
    /// <para>ScreenSpace UI는 스테레오 카메라를 안 거치고 화면에 한 번만 그려져 한쪽 눈에만 뜬다.
    /// 캔버스를 3D 공간 물체(WorldSpace)로 바꿔 스테레오 카메라가 눈마다 그리게 하면 양안에 정상으로 보인다.</para>
    ///
    /// <para>머리에 고정(카메라 자식)해 항상 시야에 둔다. 늦게 생성되는 HUD(예: CombatHud)도
    /// 매 프레임 검사해 자동 변환한다.</para>
    /// </summary>
    public class VrHudSpace : MonoBehaviour
    {
        public Transform head;             // XR 카메라 transform
        public float distance = 1.3f;      // 머리 앞 거리(m)
        public float panelHeight = 1.5f;   // 패널 세로 크기(m) — 캔버스 refRes.y가 이 높이에 매핑

        void LateUpdate()
        {
            if (head == null) return;
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            for (int i = 0; i < canvases.Length; i++)
            {
                var c = canvases[i];
                if (c != null && c.renderMode == RenderMode.ScreenSpaceOverlay)
                    Convert(c);
            }
        }

        void Convert(Canvas c)
        {
            var scaler = c.GetComponent<CanvasScaler>();
            Vector2 refRes = (scaler != null && scaler.referenceResolution.y > 1f)
                ? scaler.referenceResolution
                : new Vector2(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
            if (scaler != null) scaler.enabled = false;   // WorldSpace에선 화면 스케일러가 오히려 방해

            c.renderMode = RenderMode.WorldSpace;
            c.worldCamera = head.GetComponent<Camera>();

            var rt = c.transform as RectTransform;
            if (rt == null) return;
            rt.SetParent(head, false);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = refRes;
            float scale = panelHeight / refRes.y;
            rt.localScale = new Vector3(scale, scale, scale);
            rt.localRotation = Quaternion.identity;
            rt.localPosition = new Vector3(0f, 0f, distance);   // 머리 정면 distance m
        }
    }
}
