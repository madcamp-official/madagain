using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 경비병 감지 — <b>입체 부채꼴(쐐기)</b>. (기초_설계안 §2.3·§7)
    ///
    /// <para><b>구 아니고 원뿔 아니다.</b>
    /// <list type="bullet">
    /// <item>수평: 정면 기준 좌우 각 <see cref="halfAngleDeg"/>(기본 25°, 합 50°)</item>
    /// <item>거리: <see cref="DetectRadius"/> 이내</item>
    /// <item>수직: <b>발밑에서 키 높이까지 평행</b> — 원뿔이 아니므로 멀어져도 높이가 커지지 않는다.
    ///       머리 위로 지나가면 안 걸린다.</item>
    /// <item>시야: <see cref="blockedByWalls"/>가 켜져 있으면 벽에 가리면 안 걸린다.</item>
    /// </list></para>
    ///
    /// <para><b>빙의 중인 플레이어는 감지 대상이 아니다.</b> 몸 이동 빙의 중엔 플레이어 리그가 경비병
    /// 자리에 들어가 있다 — 그 상태로 감지되면 "경비병이 경비병을 발각"하는 꼴이다. 위장이 곧 빙의의
    /// 값어치이므로 여기서 명시적으로 제외한다.</para>
    ///
    /// <para><b>발각의 결과는 아직 배선하지 않았다.</b> §6.4는 "실패 → 방 리셋"이지만 리셋 시스템이
    /// 없다. 지금은 <see cref="OnDetected"/> 이벤트와 로그만 낸다 — 조용히 아무 일도 안 일어나는
    /// 것보다 낫고, 리셋이 생기면 이 이벤트 한 곳만 구독하면 된다.</para>
    /// </summary>
    public class GuardDetection : MonoBehaviour
    {
        [Header("부채꼴 모양")]
        [Tooltip("감지 반경 (m, 튜닝 §9). 6 → 3.6 (3/5)로 줄였다 — 6m는 방 하나를 거의 덮어 " +
                 "'피해서 지나갈 틈'이 안 생겼다.")]
        public float DetectRadius = 3.6f;

        [Tooltip("정면에서 좌우로 각각 벌어지는 각도(도). 25면 전체 시야각 50도.")]
        [Range(1f, 180f)] public float halfAngleDeg = 25f;

        [Tooltip("쐐기 높이(m) = 경비병 키. 0 이하면 CapsuleCollider 높이에서 자동으로 잡는다.\n" +
                 "이 높이 위로는 안 걸린다 — 위쪽 발판으로 지나가는 것이 유효한 회피가 된다.")]
        public float height = 0f;

        [Tooltip("벽에 가리면 안 걸린다(§2.3 '벽 뒤로 숨으면 안전'). 끄면 벽 관통 감지.")]
        public bool blockedByWalls = true;

        [Tooltip("시야 차단 판정에 쓰는 레이어. 플레이어·경비병 자신은 결과에서 걸러낸다.")]
        public LayerMask sightBlockers = ~0;

        [Header("상태")]
        [Tooltip("현재 이 경비병이 활성 위협인지. 끄면 판정·표시가 모두 멈춘다.")]
        [System.NonSerialized] public bool Active = true;

        [Tooltip("스폰 즉시 재생할 클립. GuardManual 컨트롤러의 기본 상태가 빈 상태(None)라 " +
                 "휴머노이드 리타깃이 그 상태의 자체 레스트 포즈로 스냅해 캐릭터가 가라앉아 보인다. " +
                 "실제 경비병 로직(순찰·대기 전환)이 생기면 이 필드는 그쪽에 흡수되고 사라질 임시 배선.")]
        public string startClip = "Idle_1";

        /// <summary>플레이어가 지금 이 경비병에게 보이는가.</summary>
        public bool PlayerDetected { get; private set; }

        /// <summary>발각된 프레임에 한 번. 방 리셋(§6.4)이 생기면 여기를 구독한다.</summary>
        public event System.Action<GuardDetection> OnDetected;

        /// <summary>실제로 쓰는 쐐기 높이(m). 인스펙터가 0이면 캡슐에서 재서 쓴다.</summary>
        public float Height
        {
            get
            {
                if (height > 0.01f) return height;
                var cap = GetComponent<CapsuleCollider>();
                return cap != null ? Mathf.Max(0.1f, cap.height * Mathf.Abs(transform.lossyScale.y)) : 1.8f;
            }
        }

        /// <summary>부채꼴 정면 방향(수평 성분만). 경비병이 고개를 숙여도 감지면은 수평이다.</summary>
        public Vector3 FacingFlat
        {
            get
            {
                Vector3 f = transform.forward;
                f.y = 0f;
                return f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
            }
        }

        /// <summary>쐐기 바닥면의 높이(월드 y). 발판 위 경비병이면 이 면이 공중에 뜬다.</summary>
        public float FloorY => transform.position.y;

        void Awake()
        {
            if (string.IsNullOrEmpty(startClip)) return;
            var anim = GetComponent<Animator>();
            if (anim != null) anim.Play(startClip, 0, 0f);
        }

        void Update()
        {
            bool now = Active && SeesPlayer();
            if (now && !PlayerDetected)
            {
                Debug.Log($"[경비병] 발각 — {name}의 부채꼴에 플레이어 진입 " +
                          $"(반경 {DetectRadius}m, 시야 {halfAngleDeg * 2f}도, 높이 {Height:F1}m)");
                OnDetected?.Invoke(this);
            }
            PlayerDetected = now;
        }

        /// <summary>
        /// 지금 플레이어가 쐐기 안에 있고 시야가 통하는가.
        /// </summary>
        bool SeesPlayer()
        {
            // 빙의 중이면 플레이어는 경비병 몸을 쓰고 있다 → 감지 대상이 아니다(위장이 성립해야 한다).
            if (ViewEntryController.Current != null && ViewEntryController.Current.AllowsMove) return false;

            var fpp = FirstPersonPlayer.Instance;
            if (fpp == null) return false;

            return Contains(fpp.transform.position);
        }

        /// <summary>
        /// 한 점이 이 쐐기 안에 있는가. 순찰 AI·다른 위협도 같은 판정을 쓰도록 공개해 둔다.
        /// </summary>
        public bool Contains(Vector3 point)
        {
            Vector3 origin = transform.position;
            Vector3 d = point - origin;

            // ① 높이 — 발밑~키. 평행 쐐기라 거리와 무관하다.
            if (d.y < 0f || d.y > Height) return false;

            // ② 거리 — 수평 거리로 본다(높이는 위에서 이미 잘랐다).
            Vector3 flat = new Vector3(d.x, 0f, d.z);
            float distSq = flat.sqrMagnitude;
            if (distSq > DetectRadius * DetectRadius || distSq < 1e-6f) return false;

            // ③ 각도
            float ang = Vector3.Angle(FacingFlat, flat.normalized);
            if (ang > halfAngleDeg) return false;

            // ④ 시야 차단 — 눈높이에서 대상까지 통해야 한다.
            if (!blockedByWalls) return true;

            Vector3 eye = origin + Vector3.up * (Height * 0.9f);
            Vector3 to = point - eye;
            float len = to.magnitude;
            if (len < 1e-4f) return true;

            var hits = Physics.RaycastAll(eye, to / len, len, sightBlockers, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                // 자기 몸과 대상 자신은 차단물이 아니다.
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (h.collider.GetComponentInParent<FirstPersonPlayer>() != null) continue;
                return false;   // 그 외 무엇이든 사이에 있으면 안 보인다
            }
            return true;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Vector3 o = transform.position;
            Vector3 f = FacingFlat;
            float h = Height;

            Gizmos.color = PlayerDetected ? Color.red : new Color(1f, 0.5f, 0.2f, 0.9f);

            // 바닥 부채꼴 호 + 양 끝 변, 그리고 같은 것을 키 높이에 한 번 더 → 쐐기가 보인다.
            for (int level = 0; level < 2; level++)
            {
                Vector3 up = Vector3.up * (level == 0 ? 0f : h);
                Vector3 prev = o + up + Quaternion.AngleAxis(-halfAngleDeg, Vector3.up) * f * DetectRadius;
                Gizmos.DrawLine(o + up, prev);
                const int seg = 16;
                for (int i = 1; i <= seg; i++)
                {
                    float a = Mathf.Lerp(-halfAngleDeg, halfAngleDeg, i / (float)seg);
                    Vector3 p = o + up + Quaternion.AngleAxis(a, Vector3.up) * f * DetectRadius;
                    Gizmos.DrawLine(prev, p);
                    prev = p;
                }
                Gizmos.DrawLine(prev, o + up);
            }
        }
#endif
    }
}
