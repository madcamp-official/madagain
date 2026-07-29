using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 신축 기구를 밟을 수 있게 하는 콜라이더 — 시각 파츠(Shaft의 여러 막대)와 무관하게
    /// <b>평평한 박스 하나</b>로 Anchor~Head 사이를 덮는다. 막대 개수·모양과 상관없이 항상
    /// 매끈한 발판이 되고, Head 쪽 면은 Head를 그대로 따라가므로 "Head를 완전히 덮는" 상태가 유지된다.
    ///
    /// <para>사용법: <see cref="box"/>에 이미 만들어 둔(위치·크기를 눈으로 맞춘) BoxCollider를 연결하면,
    /// <see cref="Awake"/>가 그 상태를 "Anchor 쪽 고정면 / Head 쪽 추종면"으로 기록하고, 이후
    /// <see cref="LateUpdate"/>가 <b>Head의 실제 이동량</b>에 맞춰 <c>size</c>/<c>center</c>의 축 성분만
    /// 갱신한다. 두께·폭(축과 수직인 다른 두 성분)은 처음 설정한 값 그대로 보존된다.</para>
    ///
    /// <para>Head의 실제 <see cref="Transform"/>에서 직접 읽으므로 <see cref="TelescopingActuator"/>가
    /// PD로 부드럽게 움직이는 중에도 항상 정확히 같은 프레임의 위치를 따라간다(LateUpdate가
    /// 모든 Update보다 나중에 실행되는 유니티 기본 순서를 이용 — 별도 실행순서 지정 불필요).</para>
    ///
    /// <para><b>주의</b>: <see cref="box"/>가 Head 자신에게 붙어 있어도(=<c>box.transform == actuator.head</c>)
    /// 정확하다. Anchor 쪽 "고정면"은 <b>월드 좌표를 Awake에서 한 번만 캡처</b>해 상수로 들고 있고,
    /// 매 프레임 Head의 Transform을 다시 통과시켜 재계산하지 않는다 — 그렇게 하면 Head가 움직일 때
    /// 고정돼야 할 점이 Head를 따라 같이 끌려가 버린다.</para>
    /// </summary>
    public class WalkSurfaceCollider : MonoBehaviour
    {
        [Tooltip("이 기구의 TelescopingActuator. Anchor/Head 참조와 축을 여기서 가져온다.")]
        public TelescopingActuator actuator;

        [Tooltip("눈으로 맞춰서 미리 배치해 둔 BoxCollider. 이 컴포넌트가 size.center의 축 성분만 매 프레임 갱신한다.")]
        public BoxCollider box;

        [Tooltip("Anchor 쪽 고정면 자동판정이 반대로 잡히면 켤 것.")]
        public bool invertAnchorSide = false;

        Vector3 _axisN;
        float _worldPerLocal;
        Vector3 _anchorFaceWorld;   // Anchor 쪽 면의 월드 좌표 — Awake에서 1회 캡처한 상수(다시 계산 안 함)
        Vector3 _headLocalOffset;   // Head 로컬 공간 기준 오프셋 — 콜라이더의 Head 쪽 면이 이 오프셋으로 계속 따라감
        Vector3 _sizeOther, _centerOther;   // 축과 수직인 두 성분(두께·폭) 보존용

        void Awake()
        {
            if (actuator == null || box == null || actuator.anchor == null || actuator.head == null)
            {
                enabled = false;
                return;
            }

            _axisN = actuator.axis.sqrMagnitude > 1e-6f ? actuator.axis.normalized : Vector3.right;
            _sizeOther = box.size;
            _centerOther = box.center;

            // 지금 사용자가 맞춰둔 월드 바운즈에서 축 방향 두 면의 월드 좌표를 구한다.
            Vector3 c = box.center, e = box.size * 0.5f;
            Vector3 worldA = box.transform.TransformPoint(c - Vector3.Scale(_axisN, e));
            Vector3 worldB = box.transform.TransformPoint(c + Vector3.Scale(_axisN, e));

            float dA = (worldA - actuator.anchor.position).sqrMagnitude;
            float dB = (worldB - actuator.anchor.position).sqrMagnitude;
            bool aIsAnchorSide = dA <= dB;
            if (invertAnchorSide) aIsAnchorSide = !aIsAnchorSide;

            _anchorFaceWorld = aIsAnchorSide ? worldA : worldB;      // 상수로 고정(§클래스 주석 "주의" 참조)
            Vector3 headFaceWorld = aIsAnchorSide ? worldB : worldA;

            _headLocalOffset = actuator.head.InverseTransformPoint(headFaceWorld);
            _worldPerLocal = box.transform.TransformVector(_axisN).magnitude;
            if (_worldPerLocal < 1e-6f) _worldPerLocal = 1f;
        }

        void LateUpdate()
        {
            Vector3 headFaceWorld = actuator.head.TransformPoint(_headLocalOffset);

            float worldLen = Vector3.Dot(headFaceWorld - _anchorFaceWorld, _axisN);
            Vector3 worldCenter = (headFaceWorld + _anchorFaceWorld) * 0.5f;
            Vector3 localCenterAxisVal = box.transform.InverseTransformPoint(worldCenter);

            Vector3 size = _sizeOther;
            SetAxis(ref size, Mathf.Abs(worldLen) / _worldPerLocal);
            Vector3 center = _centerOther;
            SetAxis(ref center, Vector3.Dot(localCenterAxisVal, _axisN));

            box.size = size;
            box.center = center;
        }

        void SetAxis(ref Vector3 v, float value)
        {
            if (Mathf.Abs(_axisN.x) > 0.5f) v.x = value;
            else if (Mathf.Abs(_axisN.y) > 0.5f) v.y = value;
            else if (Mathf.Abs(_axisN.z) > 0.5f) v.z = value;
        }
    }
}
