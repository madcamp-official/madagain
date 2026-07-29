using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// UI 루트가 머리를 <b>yaw만 감쇠 추종</b>하게 한다.
    ///
    /// <para><b>왜 필요한가</b> — 큰 패널을 머리에 완전 고정하면 멀미를 부른다. 시야가 어디를 향하든
    /// 같은 자리에 못 박혀 있으면, 눈이 그것을 "세계"로도 "화면"으로도 해석하지 못한다.
    /// 데드존 안에서는 가만히 두고, 밖으로 나가야 천천히 따라오게 하면 그 충돌이 사라진다.</para>
    ///
    /// <para><b>pitch는 따라가지 않는다.</b> 위아래로 함께 움직이면 지평선과의 관계가 계속 흔들려
    /// 오히려 더 불편하다. 고개를 들면 UI는 시야 아래로 빠지는 게 자연스럽다.</para>
    ///
    /// <para><b>붙이는 위치</b> — <c>[PlayerBody]</c>의 자식(회전을 물려받지 않는 자리)에 붙인다.
    /// <c>[Head]</c>의 자식에 붙이면 이미 머리 회전을 물려받은 위에 또 추종해 이중으로 돈다.
    /// 시선에 딱 붙어야 하는 것(레티클)은 이 컴포넌트 없이 <c>[Head]</c>의 자식으로 두면 된다 —
    /// 그쪽이 지연이 0이다.</para>
    ///
    /// <para>위치는 지연 없이 머리를 따른다. 위치까지 늦추면 패널이 몸에서 헤엄치듯 보인다.</para>
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class VrUiFollow : MonoBehaviour
    {
        [Tooltip("따라갈 머리. 비우면 Camera.main에서 [Head]를 거슬러 찾는다.")]
        public Transform head;

        [Header("추종")]
        [Tooltip("이 각도 안에서는 따라가지 않는다(도). 작으면 머리에 붙은 느낌, 크면 세계에 붙은 느낌.")]
        [Range(0f, 45f)] public float deadzone = 8f;

        [Tooltip("데드존 밖에서 따라오는 속도. 클수록 빨리 붙는다.")]
        [Range(0.5f, 20f)] public float damping = 4f;

        void LateUpdate()
        {
            Transform h = ResolveHead();
            if (h == null) return;

            // 위치는 즉시 따른다 — 늦추면 패널이 몸에서 헤엄친다.
            transform.position = h.position;

            float target = h.eulerAngles.y;
            float current = transform.eulerAngles.y;
            float delta = Mathf.DeltaAngle(current, target);

            if (Mathf.Abs(delta) <= deadzone) return;

            // 데드존 '가장자리'를 향해 간다 — 중심으로 가면 머리에 딱 붙어 데드존이 무의미해진다.
            float desired = target - Mathf.Sign(delta) * deadzone;
            float t = 1f - Mathf.Exp(-damping * Mathf.Max(0f, Time.unscaledDeltaTime));
            float yaw = Mathf.LerpAngle(current, desired, t);

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        Transform ResolveHead()
        {
            if (head != null) return head;

            Camera cam = Camera.main;
            if (cam == null) return null;

            // 정상 리그는 [Head] > Main Camera. 카메라는 연출(MotionFeel) 소유자라
            // 그쪽을 따라가면 킥·롤이 UI에 실린다 — 부모 [Head]를 우선한다.
            Transform p = cam.transform.parent;
            head = p != null ? p : cam.transform;
            return head;
        }
    }
}
