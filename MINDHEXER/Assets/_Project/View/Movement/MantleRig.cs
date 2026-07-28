using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 잡고 올라가기 팔 표시 — <b>손이 모서리에 월드 고정</b>되는 리그.
    ///
    /// <para>일반 FPS 뷰모델(카메라 자식)과 반대다: 등반 중 손은 씬 공간의 잡는 지점에 핀 고정되고,
    /// 어깨는 머리(카메라)를 따라온다. 고개를 돌리면 팔이 시야에서 벗어나는데, 그게 정확히
    /// "내 손은 여전히 모서리를 잡고 있다"는 사실적 결과다. 카메라 회전은 일절 건드리지 않는다.</para>
    ///
    /// <para><b>임시 표시</b>: 아직 1인칭 팔 모델이 없어 캡슐 프리미티브로 어깨→손을 잇는다.
    /// 실제 팔 모델/애니메이션이 생기면 이 컴포넌트의 손 앵커(<see cref="Show"/>가 받는 두 점)에
    /// IK 타깃을 꽂으면 된다 — 앵커 계산은 그대로 재사용.</para>
    /// </summary>
    public class MantleRig : MonoBehaviour
    {
        [Tooltip("어깨 폭(m). 손 앵커 간격도 이 값으로 정한다.")]
        public float shoulderWidth = 0.42f;

        [Tooltip("머리(카메라)에서 어깨까지 내려가는 거리(m).")]
        public float shoulderDrop = 0.22f;

        [Tooltip("임시 팔 캡슐 두께(m).")]
        public float armThickness = 0.05f;

        Transform _left, _right;
        Vector3 _leftHand, _rightHand;
        bool _visible;

        void Awake()
        {
            _left = CreateArm("[MantleArm L]");
            _right = CreateArm("[MantleArm R]");
        }

        Transform CreateArm(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(null, true);
            go.SetActive(false);
            return go.transform;
        }

        void OnDestroy()
        {
            if (_left != null) Destroy(_left.gameObject);
            if (_right != null) Destroy(_right.gameObject);
        }

        /// <summary>손을 잡는 지점(월드)에 핀 고정하고 표시 시작.</summary>
        public void Show(Vector3 leftHand, Vector3 rightHand)
        {
            _leftHand = leftHand;
            _rightHand = rightHand;
            _visible = true;
            _left.gameObject.SetActive(true);
            _right.gameObject.SetActive(true);
        }

        public void Hide()
        {
            _visible = false;
            if (_left != null) _left.gameObject.SetActive(false);
            if (_right != null) _right.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (!_visible) return;

            // 어깨 = 머리 아래 + 몸 좌우. 좌우 축은 머리 yaw의 수평 성분(수직으로 보면 폴백).
            Vector3 fwd = transform.forward; fwd.y = 0f;
            Vector3 right = fwd.sqrMagnitude > 1e-4f
                ? Vector3.Cross(Vector3.up, fwd.normalized) * -1f
                : transform.right;
            right.y = 0f;
            if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
            right.Normalize();

            Vector3 baseP = transform.position + Vector3.down * shoulderDrop;
            PlaceArm(_left, baseP - right * (shoulderWidth * 0.5f), _leftHand);
            PlaceArm(_right, baseP + right * (shoulderWidth * 0.5f), _rightHand);
        }

        void PlaceArm(Transform arm, Vector3 shoulder, Vector3 hand)
        {
            Vector3 d = hand - shoulder;
            float len = Mathf.Max(0.05f, d.magnitude);
            arm.position = (shoulder + hand) * 0.5f;
            arm.rotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
            arm.localScale = new Vector3(armThickness, len * 0.5f, armThickness);
        }
    }
}
