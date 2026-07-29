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

        [Header("1인칭 기준 자세 (씬에서 잡아둔 값)")]
        [Tooltip("1인칭일 때 뷰모델 루트의 로컬 위치. Awake에서 씬 값을 그대로 읽어 채운다.\n" +
                 "★ 여기를 원점으로 덮으면 손으로 잡아둔 구도가 Play 때 날아간다.")]
        public Vector3 firstPersonLocalPos;
        public Vector3 firstPersonLocalEuler;

        [Tooltip("끄면 위 값을 무시하고 씬에 있는 그대로 둔다(다른 시스템이 루트를 몰 때).")]
        public bool applyFirstPersonPose = true;

        Mode _mode;
        bool _applied;
        Vector3 _startPos;
        Quaternion _startRot;
        Transform _originalParent;
        bool _capturedFp;

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

                // 씬에서 잡아둔 1인칭 구도를 그대로 기준으로 삼는다.
                // 인스펙터 값이 비어 있을 때만 읽는다 — 한번 잡아두면 그게 정본이다.
                if (firstPersonLocalPos == Vector3.zero && firstPersonLocalEuler == Vector3.zero)
                {
                    firstPersonLocalPos   = parts.transform.localPosition;
                    firstPersonLocalEuler = parts.transform.localRotation.eulerAngles;
                }
                _capturedFp = true;
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
                // ★ 원점으로 리셋하지 않는다 — 씬에서 손으로 잡아둔 구도가 곧 기준이고,
                //   ViewmodelMotion이 이 값을 basePos로 캡처해 그 위에 절차 모션을 얹는다.
                if (applyFirstPersonPose && _capturedFp)
                {
                    parts.transform.localPosition = firstPersonLocalPos;
                    parts.transform.localRotation = Quaternion.Euler(firstPersonLocalEuler);
                }
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
