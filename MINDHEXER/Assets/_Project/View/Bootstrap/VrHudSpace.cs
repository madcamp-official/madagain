using System.Collections.Generic;
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
    /// 주기적으로 검사해 자동 변환한다(매 프레임 전수조사는 하지 않는다).</para>
    ///
    /// <para>⚠️ <see cref="distance"/>·<see cref="panelHeight"/>는 <b>임시값</b>이다 — 뷰어 렌즈
    /// 프로파일(FOV) 확정 후 안전영역(safe zone) 기반으로 정식화해야 코너 HUD 잘림이 풀린다(Phase 2).</para>
    /// </summary>
    public class VrHudSpace : MonoBehaviour
    {
        [Tooltip("XR 카메라 transform. 비우면 이 오브젝트/자식에서 카메라를 찾는다.")]
        public Transform head;

        [Header("패널 배치 (뷰어 FOV 확정 전 임시값)")]
        [Tooltip("머리 앞 거리(m).")]
        [SerializeField, Range(0.4f, 4f)] float distance = 1.3f;
        [Tooltip("패널 세로 크기(m). 캔버스 세로 해상도가 이 높이에 매핑된다.")]
        [SerializeField, Range(0.4f, 4f)] float panelHeight = 1.5f;
        [Tooltip("새 HUD 캔버스를 다시 훑는 주기(초). 매 프레임 전수조사를 피한다.")]
        [SerializeField, Range(0.1f, 2f)] float rescanInterval = 0.5f;

        Camera _headCam;
        readonly HashSet<Canvas> _converted = new HashSet<Canvas>();
        float _nextScan;

        void Awake()
        {
            if (head == null) head = transform;   // GameBoot이 곧 카메라로 덮어씀(없어도 안전)
        }

        void LateUpdate()
        {
            if (head == null) return;

            // 헤드 카메라는 head가 세팅된 뒤 지연 해석(GameBoot이 Awake 이후 head를 넣는다).
            if (_headCam == null)
            {
                _headCam = head.GetComponent<Camera>();
                if (_headCam == null) _headCam = head.GetComponentInChildren<Camera>();
            }

            // 매 프레임 전수조사 대신 주기적으로만 새 캔버스를 찾는다.
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + Mathf.Max(0.05f, rescanInterval);

            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas c = canvases[i];
                if (c == null || _converted.Contains(c)) continue;
                if (c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                if (Convert(c)) _converted.Add(c);
            }
        }

        /// <summary>ScreenSpaceOverlay 캔버스를 머리 앞 WorldSpace 패널로 변환. 성공 시 true.</summary>
        bool Convert(Canvas c)
        {
            RectTransform rt = c.transform as RectTransform;
            if (rt == null) return false;

            CanvasScaler scaler = c.GetComponent<CanvasScaler>();
            Vector2 refRes = (scaler != null && scaler.referenceResolution.y > 1f)
                ? scaler.referenceResolution
                : new Vector2(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
            if (scaler != null) scaler.enabled = false;   // WorldSpace에선 화면 스케일러가 방해

            c.renderMode = RenderMode.WorldSpace;
            c.worldCamera = _headCam;                      // null이어도 WorldSpace 렌더는 됨(레이캐스트용)

            rt.SetParent(head, false);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = refRes;
            ApplyPlacement(rt, refRes.y);
            return true;
        }

        /// <summary>튜닝 툴(VrTuning)이 거리·높이를 바꿀 때 호출 — 값 갱신 + 이미 변환된 패널에 즉시 반영.</summary>
        public void SetPlacement(float newDistance, float newPanelHeight)
        {
            distance = newDistance;
            panelHeight = newPanelHeight;
            foreach (Canvas c in _converted)
            {
                if (c == null) continue;
                RectTransform rt = c.transform as RectTransform;
                if (rt == null) continue;
                CanvasScaler scaler = c.GetComponent<CanvasScaler>();
                float refY = (scaler != null && scaler.referenceResolution.y > 1f)
                    ? scaler.referenceResolution.y : Mathf.Max(1, Screen.height);
                ApplyPlacement(rt, refY);
            }
        }

        void ApplyPlacement(RectTransform rt, float refY)
        {
            float scale = refY > 1f ? panelHeight / refY : 0.001f;
            rt.localScale = new Vector3(scale, scale, scale);
            rt.localRotation = Quaternion.identity;
            rt.localPosition = new Vector3(0f, 0f, distance);   // 머리 정면 distance m
        }

#if UNITY_EDITOR
        /// <summary>Play 중 인스펙터에서 거리·높이를 바꾸면 이미 변환된 패널에 즉시 반영(튜닝 편의).</summary>
        void OnValidate()
        {
            if (!Application.isPlaying) return;
            foreach (Canvas c in _converted)
            {
                if (c == null) continue;
                RectTransform rt = c.transform as RectTransform;
                if (rt == null) continue;
                CanvasScaler scaler = c.GetComponent<CanvasScaler>();
                float refY = (scaler != null && scaler.referenceResolution.y > 1f)
                    ? scaler.referenceResolution.y : Mathf.Max(1, Screen.height);
                ApplyPlacement(rt, refY);
            }
        }
#endif
    }
}
