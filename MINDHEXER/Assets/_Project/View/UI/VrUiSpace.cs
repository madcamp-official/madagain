using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 각도 기반 UI 배치의 <b>루트</b>. 자식 <see cref="VrUiAnchor"/>들의 위치·크기를
    /// 방위/고도/각크기에서 계산해 준다.
    ///
    /// <para><b>왜 각도인가</b> — 뷰어 렌즈 FOV가 아직 미지수다. 미터·픽셀로 배치하면 FOV를 알아야
    /// 어디까지 놓을 수 있는지 정해지지만, 각도로 선언해 두면 FOV는 <b>나중에 채워 넣는 상수 하나</b>
    /// (<see cref="safeHalfAngleX"/>/<see cref="safeHalfAngleY"/>)가 된다. 값 하나를 바꾸면 전체
    /// 레이아웃이 비율을 유지한 채 따라온다. <see cref="distance"/>를 바꿔도 각크기가 유지되므로
    /// 패널이 커지거나 작아지지 않는다.</para>
    ///
    /// <para><b>배치 방법 두 가지</b> — 이 루트를 어디에 붙이느냐로 성격이 갈린다.
    /// <list type="bullet">
    /// <item><b><c>[Head]</c>의 자식</b> — 시선에 딱 붙는다(레티클 등). 지연이 0이다.
    ///       <see cref="VrUiFollow"/>를 <b>붙이지 않는다</b>.</item>
    /// <item><b><c>[PlayerBody]</c>의 자식 + <see cref="VrUiFollow"/></b> — yaw만 감쇠 추종한다(패널류).
    ///       큰 패널을 머리에 완전 고정하면 멀미를 부르기 때문이다.</item>
    /// </list>
    /// 루트는 여러 개 둘 수 있다 — 성격이 다른 UI를 각자 다른 루트에 담으면 된다.</para>
    ///
    /// <para>★ <b><c>Main Camera</c>의 자식으로 두지 말 것.</b> 카메라는 연출(<c>MotionFeel</c>의
    /// 롤·킥·딥) 소유자라, 점프·착지마다 UI가 함께 튀고 기울어진다. 시야에 붙은 패널이 기우는 것은
    /// VR 멀미의 직접 원인이다. 시점만 따라가는 자리는 <c>[Head]</c>다.</para>
    ///
    /// <para><see cref="ExecuteAlways"/>라 Play 없이 씬 뷰에서 각도를 돌리면 즉시 움직인다.</para>
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class VrUiSpace : MonoBehaviour
    {
        [Header("배치")]
        [Tooltip("눈에서 UI 평면까지 거리(m). 1m보다 가까우면 수렴-조절 충돌로 눈이 아프다. 편한 범위 1.5~2.5m.")]
        [Range(0.5f, 4f)] public float distance = 1.8f;

        [Header("안전영역 (렌즈 FOV 확정 전 추정값)")]
        [Tooltip("가로 안전 반각(도). 시야 가장자리는 렌즈 왜곡·색수차가 심해 못 쓴다.")]
        [Range(5f, 80f)] public float safeHalfAngleX = 32f;

        [Tooltip("세로 안전 반각(도).")]
        [Range(5f, 60f)] public float safeHalfAngleY = 24f;

        [Header("에디터")]
        [Tooltip("씬 뷰에 안전영역 테두리를 그린다 — 어디까지가 시야 안인지 눈으로 확인용.")]
        public bool drawGizmo = true;

        [Tooltip("자식 앵커 목록을 다시 훑는 주기(초). 매 프레임 전수조사를 피한다.")]
        [Range(0.1f, 2f)] public float rescanInterval = 0.5f;

        readonly List<VrUiAnchor> _anchors = new List<VrUiAnchor>();
        float _nextScan;

        void OnEnable()
        {
            Rescan();
            ApplyAll(true);
        }

        void OnValidate()
        {
            Rescan();
            ApplyAll(true);   // 인스펙터로 반각·거리를 바꾸면 즉시 전부 다시 잡는다
        }

        void LateUpdate()
        {
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + Mathf.Max(0.05f, rescanInterval);
                Rescan();
            }
            ApplyAll(false);   // 앵커가 값이 안 바뀌었으면 스스로 건너뛴다
        }

        void Rescan()
        {
            _anchors.Clear();
            GetComponentsInChildren(true, _anchors);
        }

        void ApplyAll(bool force)
        {
            for (int i = 0; i < _anchors.Count; i++)
            {
                VrUiAnchor a = _anchors[i];
                if (a != null) a.Apply(this, force);
            }
        }

        /// <summary>모든 앵커를 강제로 다시 계산한다 — JSON 로드 등 외부에서 값을 바꾼 뒤 호출.</summary>
        public void Refresh()
        {
            Rescan();
            ApplyAll(true);
        }

        /// <summary>방위·고도(도) → 이 루트 로컬 방향.</summary>
        public static Vector3 Direction(float azimuth, float elevation)
        {
            return Quaternion.Euler(-elevation, azimuth, 0f) * Vector3.forward;
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            if (!drawGizmo) return;

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.7f);

            // 안전영역 네 모서리 — 구면 위의 사각형이라 변을 조금씩 쪼개 그린다.
            const int seg = 8;
            DrawEdge(-safeHalfAngleX, safeHalfAngleX, +safeHalfAngleY, true, seg);
            DrawEdge(-safeHalfAngleX, safeHalfAngleX, -safeHalfAngleY, true, seg);
            DrawEdge(-safeHalfAngleY, safeHalfAngleY, -safeHalfAngleX, false, seg);
            DrawEdge(-safeHalfAngleY, safeHalfAngleY, +safeHalfAngleX, false, seg);

            // 정면 축 — 방위 0/고도 0이 어디인지 표시.
            Gizmos.color = new Color(1f, 1f, 1f, 0.35f);
            Gizmos.DrawLine(Vector3.zero, Vector3.forward * distance);
        }

        void DrawEdge(float from, float to, float fixedAngle, bool horizontal, int seg)
        {
            Vector3 prev = Vector3.zero;
            for (int i = 0; i <= seg; i++)
            {
                float t = Mathf.Lerp(from, to, i / (float)seg);
                Vector3 p = (horizontal ? Direction(t, fixedAngle) : Direction(fixedAngle, t)) * distance;
                if (i > 0) Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }
#endif
    }
}
