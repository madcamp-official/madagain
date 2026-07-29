using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 터렛의 위협 로직 — 총구가 향한 방향으로 레이캐스트해서 <b>플레이어가 정면에 걸리면 즉발로 쏜다.</b>
    /// 벽에 가려지면(레이가 다른 지형에 먼저 맞으면) 당연히 안 맞는다.
    ///
    /// <para><b>안 쏘는 경우 셋</b>:
    ///  · 경비병(<see cref="HackableKind.Guard"/>) — 평상시든 <see cref="Hackable.everHacked"/>든 상관없이 아군 취급.
    ///  · <b>빙의당한 경비병</b> — 몸 이동 빙의 중엔 플레이어 리그가 경비병 자리로 옮겨가 있으므로,
    ///    맞은 게 리그의 콜라이더라도 <see cref="ViewEntryController.Current"/>.AllowsMove가 true면 쏘지 않는다.
    ///  · <b>한 번이라도 해킹된 터렛 자신</b> — <see cref="Hackable.everHacked"/>가 true면 영구 무력화(발사 로직 자체를 끈다).</para>
    ///
    /// <para><b>즉발 사망은 플레이스홀더다.</b> 정식 사망/리스폰 시스템이 아직 없어서, 맞으면
    /// 시작 지점으로 순간이동만 시킨다. 연출·게임오버 UI 등은 별도로 만들어야 한다.</para>
    ///
    /// <para><b>플레이어 조종 사격</b>: 터렛을 빙의한 상태에서 좌클릭 — <see cref="TickPlayerFire"/>를
    /// <see cref="HackDriver"/>가 <c>HexInput.primary</c>/<c>primaryHeld</c>로 매 프레임 구동한다(그
    /// 필드 자체는 이미 있었는데 아무도 안 읽고 있었다). 히트스캔, 경비병 한 방. 시각 연출이 아직
    /// 없어서 결과를 콘솔 로그로만 알린다(사용자 지시) — 실제 파괴는 경비병 세션의 파괴 시스템에 연결 예정.</para>
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

        [Tooltip("발사 후 다음 판정까지 최소 간격(초). 연사 스팸 방지용 — 즉발 사망 자체는 그대로.")]
        public float fireInterval = 0.2f;

        [Header("임시 시각화 — 감지범위 원기둥")]
        [Tooltip("총구에서 뻗는 반투명 빨강 원기둥으로 감지 범위를 표시(정식 연출 전 임시).")]
        public bool showRangeGizmo = true;

        public float gizmoRadius = 0.15f;
        public Color gizmoColor = new Color(1f, 0.15f, 0.1f, 0.22f);

        [Header("플레이어 조종 사격")]
        [Tooltip("좌클릭 발사 간격(초). 누르고 있으면 이 간격마다 단발이 반복된다(연사 아님, 반자동).")]
        public float playerFireInterval = 0.25f;

        [Tooltip("플레이어 사격 사거리(m). 감지 range와 별개로 둘 수 있게 분리.")]
        public float playerFireRange = 40f;

        Hackable _hackable;
        float _cooldown;
        float _playerCooldown;
        Transform _gizmo;

        Transform Muzzle => muzzle != null ? muzzle : transform;

        void Awake()
        {
            _hackable = GetComponent<Hackable>();
            if (showRangeGizmo) BuildRangeGizmo();
        }

        void Update()
        {
            bool disabled = _hackable.everHacked;
            if (_gizmo != null) _gizmo.gameObject.SetActive(!disabled && showRangeGizmo);
            if (disabled) return;   // 한 번이라도 해킹되면 영구 무력화

            if (_cooldown > 0f) { _cooldown -= Time.deltaTime; return; }

            if (TryHitPlayer(out FirstPersonPlayer player))
            {
                Fire(player);
                _cooldown = fireInterval;
            }
        }

        bool TryHitPlayer(out FirstPersonPlayer player)
        {
            player = null;
            Transform m = Muzzle;
            if (!Physics.Raycast(m.position, m.forward, out RaycastHit hit, range, hitMask, QueryTriggerInteraction.Ignore))
                return false;

            // 경비병(아군) — 해킹 여부 무관, 터렛은 경비병을 절대 쏘지 않는다.
            var hitHackable = hit.collider.GetComponentInParent<Hackable>();
            if (hitHackable != null && hitHackable.kind == HackableKind.Guard) return false;

            var fpp = hit.collider.GetComponentInParent<FirstPersonPlayer>();
            if (fpp == null) return false;

            // 빙의당한 경비병 — 플레이어 리그가 경비병 자리로 옮겨가 있는 상태(몸 이동 빙의)라
            // 맞은 게 리그 콜라이더여도 지금은 "경비병"으로 취급해 쏘지 않는다.
            if (ViewEntryController.Current != null && ViewEntryController.Current.AllowsMove) return false;

            player = fpp;
            return true;
        }

        /// <summary>
        /// 플레이어가 이 터렛을 빙의한 상태에서 좌클릭 구동. <see cref="HackDriver"/>가 매 프레임 호출한다.
        /// pressed=이번 프레임 눌림(엣지), held=계속 눌려 있음. 첫 프레임은 즉시 발사, 누르고 있으면
        /// <see cref="playerFireInterval"/>마다 반복 — "꾹 누르면 단발로 툭툭툭"(반자동, 완전자동 아님).
        /// </summary>
        public void TickPlayerFire(bool pressed, bool held)
        {
            if (_playerCooldown > 0f) _playerCooldown -= Time.deltaTime;
            if (!pressed && !held) return;
            if (_playerCooldown > 0f) return;

            _playerCooldown = playerFireInterval;
            FirePlayerShot();
        }

        void FirePlayerShot()
        {
            Transform m = Muzzle;

            // 히트스캔 — 즉시 판정, 탄속·예측 없음.
            if (!Physics.Raycast(m.position, m.forward, out RaycastHit hit, playerFireRange, hitMask, QueryTriggerInteraction.Ignore))
            {
                Debug.Log($"[Turret] 발사(플레이어) — 빗나감. 사거리 {playerFireRange}m 안에 아무것도 없음.");
                return;
            }

            var hitHackable = hit.collider.GetComponentInParent<Hackable>();
            if (hitHackable != null && hitHackable.kind == HackableKind.Guard)
            {
                // TODO: 실제 파괴는 미연결 — 시각 확인 전까지 콘솔로만(사용자 지시). 경비병 세션의
                // 파괴 시스템(Entities/GuardDestruction 등)이 붙으면 여기서 호출하면 된다.
                Debug.Log($"[Turret] 발사(플레이어) — 경비병 명중, 1발 즉사: {hitHackable.name} " +
                          $"(거리 {hit.distance:F1}m)");
            }
            else
            {
                Debug.Log($"[Turret] 발사(플레이어) — 명중했지만 경비병 아님: {hit.collider.name} " +
                          $"(거리 {hit.distance:F1}m)");
            }
        }

        void Fire(FirstPersonPlayer player)
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

        void BuildRangeGizmo()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "[TurretRangeGizmo]";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);   // 시각화 전용 — 물리·레이캐스트에 안 걸리게

            var rend = go.GetComponent<MeshRenderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetFloat("_Surface", 1f);       // Transparent
            mat.SetFloat("_Blend", 0f);
            mat.SetColor("_BaseColor", gizmoColor);
            mat.renderQueue = 3000;
            rend.sharedMaterial = mat;

            go.transform.SetParent(Muzzle, false);
            // Unity 원기둥은 로컬 Y축이 길이 방향 — 총구 forward(Z)로 90도 돌리고, 절반 앞으로 밀어
            // 원기둥의 한쪽 끝이 총구에 오게 한다(중심 기준이라 length*0.5만큼 offset).
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localPosition = new Vector3(0f, 0f, range * 0.5f);
            go.transform.localScale = new Vector3(gizmoRadius * 2f, range * 0.5f, gizmoRadius * 2f);

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
