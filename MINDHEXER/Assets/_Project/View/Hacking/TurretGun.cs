using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 터렛의 사격 — 총구가 향한 방향으로 레이캐스트해서 정면에 걸린 것을 즉발로 쏜다.
    /// 벽에 가려지면(레이가 다른 지형에 먼저 맞으면) 당연히 안 맞는다.
    ///
    /// <para><b>★ 쏘는 대상은 "지금 조종 중인가"로 갈린다</b>(기초_설계안 §6.2).
    /// <list type="table">
    /// <item><term>조종 중 아님</term><description><b>플레이어만</b> 쏜다. 경비병은 아군이라 안 쏜다.</description></item>
    /// <item><term>조종 중</term><description><b>경비병만</b> 쏜다. 플레이어는 안 쏜다.</description></item>
    /// </list></para>
    ///
    /// <para><b>기준이 <see cref="Hackable.everHacked"/>가 아니라 <see cref="CaptureState.Captured"/>다.</b>
    /// 한 번 해킹했다고 영구히 아군이 되는 게 아니다 — <b>손을 떼면 다시 나를 쏜다.</b>
    /// 그래서 "겨눠 놓고 지나가기"가 안 통하고, 지나가는 동안 계속 붙잡고 있어야 한다.
    /// 아군 판정을 영구화하면 터렛은 "한 번 켜면 이기는 버튼"이 된다.</para>
    ///
    /// <para><b>빙의당한 경비병은 어느 모드에서도 안 쏜다.</b> 몸 이동 빙의 중엔 플레이어 리그가
    /// 경비병 자리에 겹쳐 있어서 콜라이더만으로는 구분이 안 된다 — 실체는 "내가 들어가 있는 몸"이므로
    /// 위협 모드에서도(플레이어니까) 조종 모드에서도(내 몸이니까) 쏘지 않는다.</para>
    ///
    /// <para><b>조준은 플레이어가, 발사는 터렛이.</b> 발사 버튼이 없다 — 플레이어가 하는 일은
    /// <see cref="RotationPlatform"/>으로 <b>각도를 돌리는 것</b> 하나뿐이고, 조준선에 경비병이
    /// 들어오면 알아서 쏜다. 퍼즐이 "각도 맞추기"로 깔끔하게 환원된다.
    /// (예전의 빙의 + 좌클릭 반자동 사격은 삭제됐다 — 터렛은 더 이상 빙의 대상이 아니다.)</para>
    ///
    /// <para><b>즉발 사망은 플레이스홀더다.</b> 정식 사망/리스폰 시스템이 아직 없어서, 플레이어는
    /// 시작 지점으로 순간이동만 시킨다. 경비병은 <see cref="GuardDestruction"/>이 있으면 파괴한다.</para>
    /// </summary>
    [RequireComponent(typeof(Hackable))]
    public class TurretGun : MonoBehaviour
    {
        [Header("조준")]
        [Tooltip("총구 — 이 오브젝트의 forward가 조준 방향. 비우면 자기 자신.")]
        public Transform muzzle;

        [Tooltip("감지·사거리(m).")]
        public float range = 20f;

        [Tooltip("레이캐스트가 맞는 레이어(벽 포함 — 벽에 가려지면 당연히 안 맞음).")]
        public LayerMask hitMask = ~0;

        [Tooltip("플레이어 판정만 보이는 레이저보다 이만큼(m) 아래까지 넓힌다. 0이면 끔.\n" +
                 "레이저를 굵게·낮게 만들면 연출이 죽으므로, 보이는 것은 그대로 두고 판정 띠만 아래로 넓힌다.\n" +
                 "월드 기준 아래 방향이다 — 플레이어는 바닥에 서 있으므로 총구가 기울어도 이게 맞다.\n" +
                 "경비병 판정과 보이는 레이저는 이 값에 영향받지 않는다.")]
        public float playerHitDrop = 0.2f;

        [Tooltip("발사 후 다음 판정까지 최소 간격(초). 연사 스팸 방지용 — 즉발 사망 자체는 그대로.")]
        public float fireInterval = 0.2f;

        [Header("위험 표시")]
        [Tooltip("총구에서 뻗는 사선을 흑백 노이즈 띠로 표시(§7). 조종 중일 때는 자동으로 꺼진다 " +
                 "— 그때는 나를 쏘지 않으므로 위험 표시가 거짓이 된다.")]
        public bool showRangeGizmo = true;

        [Header("발사 연출")]
        [Tooltip("반동 애니메이션을 재생할 Animator(터렛 Head). 비우면 자식에서 찾는다.\n" +
                 "구입 애셋(Smart Turret Template)의 컨트롤러에 이미 반동 상태가 들어 있어 그걸 그대로 쓴다.")]
        public Animator fireAnimator;

        [Tooltip("반동 애니메이션 트리거 이름. 애셋 컨트롤러 기준 'Shot'.")]
        public string fireTrigger = "Shot";

        [Tooltip("발사 소리. 비우면 무음.")]
        public AudioClip shotClip;

        [Tooltip("소리를 낼 AudioSource. 비우면 자식에서 찾는다.")]
        public AudioSource audioSource;

        [Tooltip("총구에 붙일 발사 이펙트 프리팹. 비우면 없음.")]
        public GameObject muzzleFlash;

        [Tooltip("발사 이펙트 자동 정리 시간(초).")]
        public float muzzleFlashLife = 2f;

        Hackable _hackable;
        float _cooldown;
        Transform _gizmo;

        Transform Muzzle => muzzle != null ? muzzle : transform;

        void Awake()
        {
            _hackable = GetComponent<Hackable>();
            if (fireAnimator == null) fireAnimator = GetComponentInChildren<Animator>();
            if (audioSource == null) audioSource = GetComponentInChildren<AudioSource>();
            if (showRangeGizmo) BuildRangeGizmo();
        }

        /// <summary>
        /// 발사 연출 — 반동·소리·총구 섬광. <b>판정과 분리</b>돼 있어 무엇을 맞혔는지와 무관하게 같다.
        ///
        /// <para>반동은 애니메이션이 담당한다. 코드로 총구를 흔들면 <see cref="RotationPlatform"/>이
        /// 매 프레임 <c>localRotation</c>을 대입하므로 즉시 지워진다 — 회전 소유자가 하나라는 규칙
        /// (§카메라 소유권과 같은 문제)이 여기서도 적용된다. Animator는 그 아래 본을 움직이므로 안 싸운다.</para>
        /// </summary>
        void PlayFireFx()
        {
            if (fireAnimator != null && !string.IsNullOrEmpty(fireTrigger))
                fireAnimator.SetTrigger(fireTrigger);

            if (audioSource != null && shotClip != null)
                audioSource.PlayOneShot(shotClip, Random.Range(0.75f, 1f));

            if (muzzleFlash != null)
            {
                var fx = Instantiate(muzzleFlash, Muzzle);
                if (muzzleFlashLife > 0f) Destroy(fx, muzzleFlashLife);
            }
        }

        /// <summary>
        /// 지금 플레이어가 이 터렛을 조종하고 있는가.
        ///
        /// <para><see cref="Hackable.everHacked"/>가 아니라 <see cref="CaptureState.Captured"/>를 본다 —
        /// <see cref="HackDriver.SetControlled"/>가 조종 대상만 Captured로 두고 손을 떼면 None으로
        /// 돌린다. 그래서 이 한 줄이 곧 "지금 붙잡고 있는가"다.</para>
        /// </summary>
        bool BeingDriven => _hackable != null && _hackable.captureState == CaptureState.Captured;

        void Update()
        {
            // 감지 범위 표시는 조종 중이 아닐 때만 = 나를 위협하는 동안만.
            if (_gizmo != null) _gizmo.gameObject.SetActive(showRangeGizmo && !BeingDriven);

            if (_cooldown > 0f) { _cooldown -= Time.deltaTime; return; }

            Transform m = Muzzle;
            if (!Physics.Raycast(m.position, m.forward, out RaycastHit hit, range, hitMask,
                                 QueryTriggerInteraction.Ignore))
                return;   // 벽에 먼저 맞으면 그게 결과다 — 엄폐가 통한다

            // 몸 이동 빙의 중엔 플레이어 리그가 경비병 자리에 겹쳐 있어 콜라이더만으로는 구분이 안 된다.
            // 그 몸은 "내가 들어가 있는 것"이므로 어느 모드에서도 쏘지 않는다.
            bool possessedBody = ViewEntryController.Current != null && ViewEntryController.Current.AllowsMove;

            var hitHackable = hit.collider.GetComponentInParent<Hackable>();
            bool hitGuard = hitHackable != null && hitHackable.kind == HackableKind.Guard;

            if (BeingDriven)
            {
                // 조종 중 — 경비병만.
                if (!hitGuard || possessedBody) return;
                PlayFireFx();
                KillGuard(hitHackable, hit);
            }
            else
            {
                // 조종 중 아님 — 플레이어만. 경비병은 아군이라 건드리지 않는다.
                if (possessedBody) return;
                var fpp = hitGuard ? null : hit.collider.GetComponentInParent<FirstPersonPlayer>();
                // 레이저가 머리 위로 지나가 안 맞는 문제 — 판정만 아래로 넓힌 두 번째 레이로 한 번 더 본다.
                if (fpp == null && playerHitDrop > 0f) fpp = PlayerOnDropRay(m);
                if (fpp == null) return;
                PlayFireFx();
                KillPlayer(fpp);
            }

            _cooldown = fireInterval;
        }

        /// <summary>
        /// 보이는 레이저를 <see cref="playerHitDrop"/>만큼 <b>아래로 평행 이동</b>한 판정 전용 레이.
        /// 여기에 플레이어가 걸리면 죽는다.
        ///
        /// <para><b>왜 필요한가</b>: 터렛이 커서 총구가 높은데, 크기를 줄이면 위압감이 사라진다.
        /// 그래서 보이는 것(<see cref="DangerZoneVisual"/>이 <see cref="muzzle"/>·<see cref="range"/>·
        /// beamWidth로 그린다)은 그대로 두고 <b>판정 띠만</b> 아래로 넓힌다.</para>
        ///
        /// <para><b>엄폐는 그대로 통한다</b>: 이 레이도 같은 <see cref="hitMask"/>로 따로 쏘므로 벽에
        /// 막히면 거기서 끝난다. 원래 레이가 벽에 막혀도 이 레이는 그 아래로 지나갈 수 있는데,
        /// 그건 실제로 0.2m 아래를 지나는 사선이 그렇다는 뜻이라 오히려 맞는 거동이다.</para>
        ///
        /// <para>주의: 시작점이 총구에서 <see cref="playerHitDrop"/>만큼 아래라, 그 지점이 터렛 몸체
        /// 안에 들어갈 만큼 값을 키우면 자기 콜라이더에 막힌다. 0.2 정도에서는 문제없다.</para>
        /// </summary>
        FirstPersonPlayer PlayerOnDropRay(Transform m)
        {
            Vector3 origin = m.position + Vector3.down * playerHitDrop;
            if (!Physics.Raycast(origin, m.forward, out RaycastHit hit, range, hitMask,
                                 QueryTriggerInteraction.Ignore))
                return null;

            var hackable = hit.collider.GetComponentInParent<Hackable>();
            if (hackable != null && hackable.kind == HackableKind.Guard) return null;   // 경비병은 아군

            return hit.collider.GetComponentInParent<FirstPersonPlayer>();
        }

        void KillGuard(Hackable guard, RaycastHit hit)
        {
            var destruction = guard.GetComponent<GuardDestruction>();
            if (destruction != null && !destruction.Destroyed)
            {
                destruction.Destruct((hit.point - Muzzle.position).normalized);
                Debug.Log($"[Turret] 사살 — 경비병 파괴: {guard.name} (거리 {hit.distance:F1}m)");
                return;
            }

            // 파괴 컴포넌트가 없는 경비병 — 조용히 아무 일도 안 일어나면 원인을 못 찾으므로 남긴다.
            Debug.Log($"[Turret] 경비병 명중했으나 GuardDestruction이 없어 파괴하지 못함: {guard.name}");
        }

        void KillPlayer(FirstPersonPlayer player)
        {
            Debug.Log($"[Turret] 사살 — {_hackable.kind}({name})가 플레이어를 조준선에서 포착.");

            // 플레이스홀더: 정식 사망/리스폰 없음 — 시작 지점으로 순간이동만.
            var boot = FindFirstObjectByType<GameBoot>();
            Vector3 pos = boot != null
                ? boot.startPosition + Vector3.up * boot.eyeHeight
                : player.transform.position;

            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.position = pos;
            if (cc != null) cc.enabled = true;

            player.move.Reset();
            player.VerticalVelocity = 0f;
        }

        /// <summary>
        /// 사선을 <see cref="DangerZoneVisual"/>(흑백 노이즈)에 맡긴다 — 경비병 부채꼴과 <b>같은 재질</b>을
        /// 쓰므로 위험 신호가 한 가지 언어로 통일된다(§7).
        ///
        /// <para>예전의 반투명 빨강 원기둥은 지웠다. 흑백 맵에 빨강 하나만 떠 있어 눈에는 띄었지만
        /// 경비병 위험 표시와 생김새가 달라 "같은 종류의 위험"으로 안 읽혔다.</para>
        /// </summary>
        void BuildRangeGizmo()
        {
            var go = new GameObject("[TurretDangerZone]");
            go.transform.SetParent(Muzzle, false);

            var vis = go.AddComponent<DangerZoneVisual>();
            vis.turret = this;
            vis.guard = null;   // 부모 체인에서 경비병을 잘못 집지 않게 명시적으로 비운다

            _gizmo = go.transform;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Transform m = Muzzle;
            Gizmos.color = Color.red;
            Gizmos.DrawLine(m.position, m.position + m.forward * range);
        }
#endif
    }
}
