using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 보스 척추·머리의 <b>유일한 소유자</b> — 플레이어 높이에 맞춰 허리를 굽히고, 머리로 조준한다.
    /// (보스전_설계 §추격 연출)
    ///
    /// <para><b>실행 순서 45 — 팔(<see cref="BossArmRig"/>, 50)보다 먼저여야 한다.</b>
    /// 허리를 굽히면 어깨가 움직이는데, 팔 IK가 그 뒤에 돌아야 벽에 짚은 손이 제자리에 붙어 있다.
    /// 순서가 반대면 손이 벽에서 떨어진다.</para>
    ///
    /// <para><b>굽힘을 여러 관절에 나눈다.</b> 한 관절에 몰면 그 자리에서 메시가 접혀 뭉개진다
    /// (팔꿈치 하나에 112도가 몰려 실제로 겪었다). Waist·Spine01·Spine02에 가중치로 분배한다.</para>
    ///
    /// <para><b>축을 안 쓴다.</b> 굽힘은 "플레이어 방향에 수직인 수평축" 둘레의 월드 회전으로 계산하고,
    /// 머리는 <see cref="headAim"/> 마커의 +Z를 플레이어로 향하게 돌린다. 본의 로컬 X/Y/Z 중 뭐가
    /// 앞인지 알 필요가 없어서, 리그가 바뀌어도 안 깨진다.</para>
    ///
    /// <para>이 컴포넌트가 도는 본들은 걷기 클립에서 <b>빠져 있어야</b> 한다(허리 위쪽 커브 제거).
    /// 안 그러면 애니메이션과 서로 덮어쓴다.</para>
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(45)]   // BossChase(40) → 여기 → BossArmRig(50)
    [DisallowMultipleComponent]
    public class BossSpineAim : MonoBehaviour
    {
        [Header("대상")]
        [Tooltip("바라볼 대상(플레이어). 비우면 MainCamera.")]
        public Transform target;

        [Header("본 (비우면 이름으로 자동 탐색)")]
        public Transform waist;
        public Transform spine01;
        public Transform spine02;
        public Transform head;

        [Tooltip("★ Head의 자식. 이 트랜스폼의 +Z(파랑)가 '얼굴이 보는 쪽'이다. " +
                 "자동 탐색 시 바인드 포즈에서 보스 정면을 향하도록 만들어 둔다.")]
        public Transform headAim;

        [Header("허리 굽힘")]
        [Tooltip("머리를 플레이어보다 이만큼 위에 두려고 한다(m). 눈높이 맞춤이 아니라 내려다보는 자세.")]
        public float headAboveTarget = 6f;

        [Tooltip("최대 굽힘 각도(도). 넘기면 척추 메시가 접혀 뭉개진다 — 눈으로 보며 올릴 것.")]
        [Range(0f, 150f)] public float maxBendDeg = 110f;

        [Tooltip("플레이어가 머리보다 높을 때 뒤로 젖힐 수 있는 최대 각도(도). 0이면 젖히지 않는다.")]
        [Range(0f, 45f)] public float maxLeanBackDeg = 15f;

        [Header("굽힘 분배 (합이 1이 되게)")]
        [Range(0f, 1f)] public float waistWeight = 0.5f;
        [Range(0f, 1f)] public float spine01Weight = 0.3f;
        [Range(0f, 1f)] public float spine02Weight = 0.2f;

        [Header("머리 조준")]
        [Range(0f, 1f)] public float headLookWeight = 1f;

        [Tooltip("Spine02 기준 좌우 한계(도). 목이 뒤로 꺾이지 않게.")]
        [Range(0f, 120f)] public float maxHeadYaw = 70f;

        [Tooltip("Spine02 기준 상하 한계(도).")]
        [Range(0f, 89f)] public float maxHeadPitch = 45f;

        [Header("부드럽게")]
        [Tooltip("굽힘 각도 추종 시간(초). 플레이어가 위아래로 움직일 때 목이 튀지 않게.")]
        public float smoothTime = 0.25f;

        [Header("진단 (읽기 전용)")]
        public float currentBendDeg;
        public float headAngleErrorDeg;

        // 홈 회전 — 매 프레임 여기서 다시 시작해야 각도가 누적되지 않는다.
        [SerializeField, HideInInspector] Transform[] _homeBones;
        [SerializeField, HideInInspector] Quaternion[] _homeRots;

        float _bendVel;

        [ContextMenu("본 자동 탐색")]
        public void AutoFind()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Waist") waist = t;
                else if (t.name == "Spine01") spine01 = t;
                else if (t.name == "Spine02") spine02 = t;
                else if (t.name == "Head") head = t;
            }

            // 얼굴 방향 마커 — 바인드 포즈에서 보스 정면(+Z)을 향하게 만들어 둔다.
            if (head != null)
            {
                Transform found = headAim != null ? headAim : head.Find("HeadAim");
                if (found == null)
                {
                    var go = new GameObject("HeadAim");
                    go.transform.SetParent(head, false);
                    found = go.transform;
                }
                found.rotation = Quaternion.LookRotation(transform.forward, Vector3.up);
                headAim = found;
            }
            CaptureHome();
        }

        [ContextMenu("홈 포즈 재캡처")]
        public void CaptureHome()
        {
            var list = new System.Collections.Generic.List<Transform>();
            if (waist != null) list.Add(waist);
            if (spine01 != null) list.Add(spine01);
            if (spine02 != null) list.Add(spine02);
            if (head != null) list.Add(head);
            _homeBones = list.ToArray();
            _homeRots = new Quaternion[_homeBones.Length];
            for (int i = 0; i < _homeBones.Length; i++) _homeRots[i] = _homeBones[i].localRotation;
        }

        void RestoreHome()
        {
            if (_homeBones == null || _homeRots == null) return;
            for (int i = 0; i < Mathf.Min(_homeBones.Length, _homeRots.Length); i++)
                if (_homeBones[i] != null) _homeBones[i].localRotation = _homeRots[i];
        }

        void OnEnable()
        {
            if (_homeBones == null || _homeBones.Length == 0) CaptureHome();
        }

        void OnDisable() { RestoreHome(); }

        // 편집 모드에서 미리보기가 꺼져 있는 동안은 <b>아무것도 건드리지 않는다</b>(BossArmRig와 같은 규약).
        // 안 그러면 씬 뷰의 Camera.main을 향해 계속 허리가 굽어, 이 리그를 기준으로 재는 모든 측정이
        // 오염된다 — HeadContact가 걷기 자세에서 벗어나 낑김 위치가 12m 넘게 틀리는 것을 실제로 겪었다.
        [Header("에디터")]
        [Tooltip("켜면 편집 모드에서도 Camera.main을 향해 굽힌다. 자세를 눈으로 확인할 때만 켤 것.\n" +
                 "★ 켜 둔 채로 측정하면 값이 오염된다. 확인이 끝나면 반드시 끌 것.")]
        public bool editorPreview = false;

        bool _prevPreview;

        void LateUpdate()
        {
            if (!Application.isPlaying)
            {
                if (!editorPreview)
                {
                    // 미리보기가 꺼지는 그 프레임에만 한 번 복원하고, 그 뒤로는 손을 뗀다 —
                    // 매 프레임 되돌리면 인스펙터에서 본을 손으로 돌려 볼 수가 없다.
                    if (_prevPreview) { RestoreHome(); _prevPreview = false; }
                    return;
                }
                _prevPreview = true;
            }

            if (target == null && Camera.main != null) target = Camera.main.transform;
            if (target == null || head == null) return;

            // 매 프레임 홈에서 다시 푼다 — 누적되면 목이 계속 감긴다.
            RestoreHome();

            Vector3 toTarget = target.position - head.position;
            Vector3 flat = new Vector3(toTarget.x, 0f, toTarget.z);
            if (flat.sqrMagnitude < 1e-4f) flat = transform.forward;
            flat.Normalize();

            // ── 허리 굽힘 ────────────────────────────────────────────────
            // 굽힘 축 = 플레이어 방향에 수직인 수평축. 사선에 있으면 그쪽으로 기울며 숙인다.
            Vector3 bendAxis = Vector3.Cross(Vector3.up, flat);
            if (bendAxis.sqrMagnitude < 1e-6f) bendAxis = transform.right;
            bendAxis.Normalize();

            // 게인(비례 제어)이 아니라 <b>필요한 각도를 직접 푼다</b>. 비례 제어는 숙일수록 높이차가
            // 줄어 굽힘도 줄기 때문에 목표 높이에 영영 도달하지 못하고 그 근처에서 균형만 잡는다.
            // 굽힐수록 머리가 낮아지는 것은 단조라, 이분 탐색 14회면 각도가 정확히 나온다(관절 3개라 공짜).
            float wantHeadY = target.position.y + headAboveTarget;
            float wantBend = SolveBendForHeadHeight(wantHeadY, bendAxis);

            float dt = Application.isPlaying ? Time.deltaTime : 1f;
            currentBendDeg = smoothTime > 1e-3f && Application.isPlaying
                ? Mathf.SmoothDamp(currentBendDeg, wantBend, ref _bendVel, smoothTime, Mathf.Infinity, dt)
                : wantBend;

            RestoreHome();                     // 탐색이 남긴 자세를 지우고 최종 각도로 한 번만 적용
            ApplyBend(currentBendDeg, bendAxis);

            // ── 머리 조준 ────────────────────────────────────────────────
            if (headAim == null || headLookWeight <= 0f) return;

            // 굽힌 뒤의 실제 방향으로 다시 계산해야 한다(어깨가 이미 움직였다).
            Vector3 aimDir = (target.position - head.position);
            if (aimDir.sqrMagnitude < 1e-6f) return;
            aimDir.Normalize();

            // 몸통(Spine02) 기준으로 좌우·상하 한계를 건다 — 목이 뒤로 꺾이지 않게.
            Transform baseT = spine02 != null ? spine02 : transform;
            Vector3 baseFwd = transform.forward;
            Vector3 local = Quaternion.Inverse(Quaternion.LookRotation(baseFwd, Vector3.up)) * aimDir;
            float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -maxHeadYaw, maxHeadYaw);
            float pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg,
                                      -maxHeadPitch, maxHeadPitch);
            Vector3 clamped = Quaternion.LookRotation(baseFwd, Vector3.up) * (Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward);

            Quaternion delta = Quaternion.FromToRotation(headAim.forward, clamped);
            if (headLookWeight < 1f) delta = Quaternion.Slerp(Quaternion.identity, delta, headLookWeight);
            head.rotation = delta * head.rotation;

            headAngleErrorDeg = Vector3.Angle(headAim.forward, aimDir);
        }

        /// <summary>굽힘 각도를 세 관절에 가중치대로 나눠 적용한다(한 관절에 몰면 그 자리에서 뭉개진다).</summary>
        void ApplyBend(float deg, Vector3 axisWorld)
        {
            float wSum = Mathf.Max(1e-4f, waistWeight + spine01Weight + spine02Weight);
            Bend(waist,   deg * waistWeight   / wSum, axisWorld);
            Bend(spine01, deg * spine01Weight / wSum, axisWorld);
            Bend(spine02, deg * spine02Weight / wSum, axisWorld);
        }

        /// <summary>머리가 <paramref name="targetY"/>에 오게 하는 굽힘 각도. 단조성을 이용한 이분 탐색.</summary>
        float SolveBendForHeadHeight(float targetY, Vector3 axisWorld)
        {
            float lo = -maxLeanBackDeg, hi = maxBendDeg;

            // 한계까지 굽혀도 아직 머리가 높으면 그냥 최대. 반대도 마찬가지 — 탐색 자체가 무의미하다.
            RestoreHome(); ApplyBend(hi, axisWorld);
            if (head.position.y > targetY) { RestoreHome(); return hi; }
            RestoreHome(); ApplyBend(lo, axisWorld);
            if (head.position.y < targetY) { RestoreHome(); return lo; }

            for (int i = 0; i < 14; i++)
            {
                float mid = (lo + hi) * 0.5f;
                RestoreHome();
                ApplyBend(mid, axisWorld);
                if (head.position.y > targetY) lo = mid;   // 아직 높다 → 더 굽혀야 한다
                else hi = mid;
            }
            RestoreHome();
            return (lo + hi) * 0.5f;
        }

        void Bend(Transform t, float deg, Vector3 axisWorld)
        {
            if (t == null || Mathf.Abs(deg) < 1e-4f) return;
            t.rotation = Quaternion.AngleAxis(deg, axisWorld) * t.rotation;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (head == null) return;
            Gizmos.color = Color.magenta;
            if (headAim != null) Gizmos.DrawRay(head.position, headAim.forward * 20f);
            if (target != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(head.position, target.position);
            }
        }
#endif
    }
}
