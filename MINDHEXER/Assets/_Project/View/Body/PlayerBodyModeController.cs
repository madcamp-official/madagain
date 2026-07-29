using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 1인칭 ↔ 3인칭 전환. 설계: `docs/KJH/design/플레이어_신체표현_설계.md` §5.4
    ///
    /// <para><b>인스턴스는 하나다.</b> 자기 몸일 때는 팔만 켜서 카메라에 붙이고,
    /// 빙의 중에는 전체를 켜서 월드 앵커(남겨진 셸 자리)에 둔다.
    /// 모델을 두 벌 두지 않으므로 재질·리그가 갈라질 일이 없다.</para>
    ///
    /// <para><b>전환 조건</b>(§5.1): 빙의(시점 진입) 중에만 3인칭이다.
    /// 외부 조종은 내 시점을 유지하므로 내 몸이 안 보인다 → 1인칭 그대로.</para>
    ///
    /// <para><b>소유권</b>(§3): 이 컴포넌트는 <b>파츠 활성화와 부모</b>만 만진다.
    /// 절차 모션은 각각 <see cref="ViewmodelMotion"/>(1인칭)·<see cref="BodyIdleMotion"/>(3인칭)이
    /// 담당하며, 둘이 <b>동시에 돌지 않도록</b> 여기서 켜고 끈다.</para>
    ///
    /// <para><see cref="HackDriver"/>는 읽기만 한다 — 그쪽 파일은 건드리지 않는다.</para>
    /// </summary>
    [DefaultExecutionOrder(-60)]   // 모션들(-50/-30)보다 먼저 모드를 확정한다
    public class PlayerBodyModeController : MonoBehaviour
    {
        public enum Mode { FirstPerson, ThirdPerson }

        [Header("대상")]
        [Tooltip("비우면 자기 자신·자식에서 찾는다.")]
        public PlayerBodyParts parts;

        [Tooltip("비우면 씬에서 찾는다. 빙의 상태를 읽기만 한다.")]
        public HackDriver hack;

        [Header("모션 (모드에 따라 켜고 끈다)")]
        public ViewmodelMotion viewmodelMotion;
        public BodyIdleMotion  bodyIdleMotion;

        [Header("설정")]
        [Tooltip("시작 모드.")]
        public Mode startMode = Mode.FirstPerson;

        [Tooltip("끄면 자동 전환하지 않는다(작업 씬에서 수동으로 보고 싶을 때).")]
        public bool autoSwitch = true;

        [Tooltip("3인칭일 때 셸이 서 있을 자리. 비우면 시작 위치를 기억한다.")]
        public Transform worldAnchor;

        Mode _mode;
        bool _applied;
        Vector3 _startPos;
        Quaternion _startRot;
        Transform _originalParent;

        public Mode Current => _mode;

        void Awake()
        {
            if (parts == null) parts = GetComponentInChildren<PlayerBodyParts>(true);
            if (hack == null)  hack  = FindFirstObjectByType<HackDriver>();

            if (parts != null)
            {
                if (viewmodelMotion == null) viewmodelMotion = FindFirstObjectByType<ViewmodelMotion>();
                if (bodyIdleMotion  == null) bodyIdleMotion  = parts.GetComponent<BodyIdleMotion>()
                                                            ?? parts.GetComponentInChildren<BodyIdleMotion>(true);
                _originalParent = parts.transform.parent;
                _startPos = parts.transform.position;
                _startRot = parts.transform.rotation;
                parts.AutoFindBones();
            }
        }

        void Start()
        {
            _mode = startMode;
            _applied = false;
            Apply(_mode, force: true);
        }

        void Update()
        {
            if (!autoSwitch) return;

            // 빙의 중일 때만 3인칭. 외부 조종은 내 시점 유지라 내 몸이 안 보인다(§5.1).
            bool possessing = hack != null && hack.viewEntry != null && hack.viewEntry.Active;
            Mode want = possessing ? Mode.ThirdPerson : Mode.FirstPerson;
            if (want != _mode) Apply(want, force: false);
        }

        /// <summary>모드를 강제로 바꾼다(작업 씬에서 버튼으로 확인할 때도 쓴다).</summary>
        public void Apply(Mode mode, bool force)
        {
            if (parts == null) return;
            if (_applied && !force && mode == _mode) return;

            _mode = mode;
            _applied = true;

            bool first = mode == Mode.FirstPerson;

            // ① 파츠 — 팔은 항상 보이고, 나머지는 3인칭에서만
            SetActive(parts.armParts, true);
            SetActive(parts.bodyParts, !first);

            // ② 부착 지점
            Transform target = first ? ResolveViewmodelAnchor() : ResolveWorldAnchor();
            if (target != null && parts.transform.parent != target)
                parts.transform.SetParent(target, worldPositionStays: false);

            if (first)
            {
                // 카메라 하위에서는 원점에 맞춘다. 세부 위치는 ViewmodelMotion의 basePos가 잡는다.
                parts.transform.localPosition = Vector3.zero;
                parts.transform.localRotation = Quaternion.identity;
            }
            else if (target == null)
            {
                // 월드 앵커가 없으면 시작 위치로 되돌린다.
                parts.transform.SetPositionAndRotation(_startPos, _startRot);
            }

            // ③ 모션 — 절대 동시에 돌지 않는다(§3 규칙1)
            if (viewmodelMotion != null) viewmodelMotion.enabled = first;
            if (bodyIdleMotion  != null) bodyIdleMotion.enabled  = !first;
        }

        Transform ResolveViewmodelAnchor()
        {
            if (parts.viewmodelAnchor != null) return parts.viewmodelAnchor;
            var cam = Camera.main;
            return cam != null ? cam.transform : null;
        }

        Transform ResolveWorldAnchor()
        {
            if (worldAnchor != null) return worldAnchor;
            if (parts.worldAnchor != null) return parts.worldAnchor;

            // 빙의 중이면 남겨진 셸 자리가 곧 3인칭 자리다.
            if (hack != null && hack.viewEntry != null && hack.viewEntry.Shell != null)
                return hack.viewEntry.Shell;

            return _originalParent;
        }

        static void SetActive(System.Collections.Generic.List<Renderer> list, bool on)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                var r = list[i];
                if (r != null && r.gameObject.activeSelf != on) r.gameObject.SetActive(on);
            }
        }

        [ContextMenu("1인칭으로")]
        void DebugFirst() => Apply(Mode.FirstPerson, force: true);

        [ContextMenu("3인칭으로")]
        void DebugThird() => Apply(Mode.ThirdPerson, force: true);
    }
}
