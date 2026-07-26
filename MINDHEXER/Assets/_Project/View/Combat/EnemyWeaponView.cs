using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 적 무기 뷰 + SFX. ★ combat 소유·독립. 읽기 전용.
    /// EntityViews가 그린 적 몸(이름 "Enemy_i")을 이름으로 찾아 무기를 물린다.
    /// 근접(칼)·원거리(총) 몹은 실물 모델의 RightHand 본 밑에 "월드 스케일 1" 홀더를 끼우고 그 안에
    /// 무기를 붙인다 — 캐릭터의 실제 Walk/Aim/Attack 스켈레톤 애니메이션을 그대로 따라가므로 절차적
    /// 스윙 없이도 자연스럽고, 손잡이 그립을 실제 미터/도 단위로 잡을 수 있다(swordGrip*/gunGrip*).
    /// 본을 못 찾는 예외 상황(캡슐 폴백 등)에서만 예전의 월드 좌표 피벗 + 절차적 스윙으로 폴백한다.
    /// 상태 전이에서 몹 SFX(EnemyWindup/Melee/Aim/Fire)도 재생.
    /// </summary>
    public class EnemyWeaponView : MonoBehaviour
    {
        class Slot
        {
            public Transform  capsule;      // 적 몸(EntityViews)
            public Transform  pivot;        // 폴백용 무기 회전축(월드 배치, scale 1) — 본을 못 찾았을 때만 사용
            public Transform  handBone;     // 실물 모델의 오른손 본 — 있으면 이걸 우선 사용
            public Transform  weaponInst;   // 실제로 생성한 무기 인스턴스(본 부착 시 홀더, 피벗 폴백 시 피벗)
            public Transform  swordChild;   // 본 부착 시 홀더 밑 실제 검 — 손잡이 오프셋을 미터 단위로 여기에 적용
            public CombatType combat;       // 무기 종류 결정(근접=칼/원거리=활)
            // ★ 캡슐(구)은 원점이 몸 중심, 실물 모델(Charge/Melee/Ranged)은 원점이 발밑이라
            //   같은 hand 오프셋을 그대로 쓰면 무기가 발 근처로 내려가 붕 떠 보인다. Animator가
            //   있으면(자식 포함) 발밑 원점 실물 모델로 보고 손 높이만큼 더 올려준다.
            public bool       feetRooted;
            public bool       built;
            public EnemyState prevState;
            public bool       prevSeen;
            public float      recoilT;      // 발사 반동(0..1, 감쇠)
        }

        readonly Slot[] slots = new Slot[SimConfig.MaxEnemies];
        Material meleeMat, bowMat;

        const float SoundRange = 18f;   // 이 거리 안 적만 공격음(스팸 방지)

        void Awake()
        {
            meleeMat = Mat(new Color(0.72f, 0.72f, 0.78f));   // 금속 칼
            bowMat   = Mat(new Color(0.45f, 0.28f, 0.14f));   // 나무 활
        }

        void LateUpdate()
        {
            var main = Main.Instance;
            if (main == null) return;
            ref readonly SimWorld w = ref main.World;

            for (int i = 0; i < slots.Length; i++)
            {
                Slot s = slots[i];
                bool active = i < w.enemyCount && w.enemies[i].alive;
                if (!active)
                {
                    if (s != null && s.weaponInst != null) s.weaponInst.gameObject.SetActive(false);
                    continue;
                }

                ref readonly EnemySim e = ref w.enemies[i];
                if (s == null) s = slots[i] = new Slot();

                if (s.capsule == null)
                {
                    var go = GameObject.Find($"Enemy_{i}");
                    if (go == null) continue;   // 아직 안 생김 — 다음 프레임 재시도
                    s.capsule = go.transform;
                    s.feetRooted = go.GetComponentInChildren<Animator>() != null;
                    s.handBone = FindBone(go.transform, "RightHand");
                    // 몸이 새로 잡혔으면 예전 몸에 붙어 있던 무기 참조는 전부 무효다
                    s.built = false;
                    s.weaponInst = null;
                    s.swordChild = null;
                    s.pivot = null;
                }

                // ★ 근접(칼)·원거리(총)만 무기를 든다 — 돌진/공중은 맨몸. Monolith 원거리 바디엔 총이
                //   모델링돼 있지 않아(빈손) 실제 총 메시를 손 본에 붙인다. 층이동(Traversal)도 combat 기준.
                bool showWeapon = e.ai.mobility != MobilityType.Charge && e.ai.mobility != MobilityType.Flying
                                  && (e.ai.combat == CombatType.Melee || e.ai.combat == CombatType.Ranged);
                if (!showWeapon)
                {
                    if (s.weaponInst != null) s.weaponInst.gameObject.SetActive(false);
                    continue;
                }

                // ★ 적 뷰가 파괴·재사용되면(사망·clear·풀 재활용) 손 본 밑의 홀더도 함께 사라진다.
                //   그런데 built는 true라 재생성이 안 되고, swordChild==null이 되면서 폴백 경로(Pose)로
                //   빠지는데 그쪽이 쓰는 pivot도 null이라 NullReference가 났다. 유실을 감지해 다시 만든다.
                if (s.built && s.weaponInst == null) s.built = false;
                if (s.handBone == null && s.capsule != null)
                    s.handBone = FindBone(s.capsule, "RightHand");   // 본도 같이 날아갔으면 다시 찾는다

                if (!s.built || s.combat != e.ai.combat)
                    BuildWeapon(s, e.ai.combat);

                if (s.weaponInst != null) s.weaponInst.gameObject.SetActive(true);
                HandleTransitions(s, in e, in w.player);
                // 본 부착(swordChild 존재) 시엔 위치/회전은 스켈레톤을 자동 추종하니 홀더 스케일 상쇄 +
                //   검 손잡이 그립(미터/도)만 매 프레임 적용(Play 중 Inspector 조정 즉시 반영).
                //   폴백 피벗(프리팹 로드 실패 등)일 땐 예전 절차적 배치를 쓴다.
                if (s.swordChild != null) ApplyBoneGrip(s);
                else                      Pose(s, in e);
            }
        }

        static Transform FindBone(Transform root, string name)
        {
            var all = root.GetComponentsInChildren<Transform>();
            foreach (var t in all) if (t.name == name) return t;
            return null;
        }

        // ── 상태 전이 → SFX + 반동 ──
        void HandleTransitions(Slot s, in EnemySim e, in PlayerSim p)
        {
            EnemyState st = e.ai.state;
            if (s.prevSeen && st != s.prevState)
            {
                Vector3 d = e.pos - p.pos; d.y = 0f;
                bool near = d.sqrMagnitude < SoundRange * SoundRange;

                if (near)
                {
                    if (st == EnemyState.Windup)      CombatAudio.EnemyWindup();
                    else if (st == EnemyState.Active) CombatAudio.EnemyMelee();
                    else if (st == EnemyState.Aim)    CombatAudio.EnemyAim();
                }
                if (s.prevState == EnemyState.Aim && st != EnemyState.Aim)   // 발사됨
                {
                    // [2026-07-22] 발사음은 ProjectileView(투사체 생성 시)로 옮겼다 — 지상·공중
                    // 원거리 모두 같은 레이저음이 나게. 여기선 반동 연출만 남긴다.
                    s.recoilT = 1f;
                }
            }
            s.prevState = st;
            s.prevSeen  = true;
        }

        // ── 포즈 + 월드 배치 ──
        void Pose(Slot s, in EnemySim e)
        {
            // 폴백 경로는 pivot·capsule이 반드시 있어야 한다. 유실됐으면 다음 프레임에
            // 재생성되도록 표시만 하고 이번 프레임은 건너뛴다(예전엔 여기서 NullReference).
            if (s.pivot == null || s.capsule == null) { s.built = false; return; }

            if (s.recoilT > 0f) s.recoilT = Mathf.MoveTowards(s.recoilT, 0f, Time.deltaTime / 0.18f);

            Vector3 hand; Quaternion swing;
            if (s.combat == CombatType.Melee) MeleePose(in e.ai, out hand, out swing);
            else                              RangedPose(s, in e.ai, out hand, out swing);
            if (s.feetRooted) hand += new Vector3(0f, e.height * 0.55f, 0f);   // 발밑 원점 보정 — 손 높이만큼 올림

            Quaternion bodyRot = s.capsule.rotation;   // Euler(0,yaw,0) — 스케일 무시됨
            s.pivot.SetPositionAndRotation(s.capsule.position + bodyRot * hand, bodyRot * swing);
        }

        static readonly Vector3 MeleeRest   = new Vector3(25f, 0f, -10f);
        static readonly Vector3 MeleeRaise  = new Vector3(-80f, 0f, 20f);
        static readonly Vector3 MeleeStrike = new Vector3(55f, 0f, -5f);

        static void MeleePose(in EnemyAI ai, out Vector3 hand, out Quaternion swing)
        {
            hand = new Vector3(0.2f, 0f, 0.08f);
            Vector3 e;
            switch (ai.state)
            {
                case EnemyState.Windup:
                    e = Vector3.Lerp(MeleeRest, MeleeRaise, Frac(ai.stateTicks, AIConfig.MeleeWindupTicks));
                    break;
                case EnemyState.Active:
                    e = Vector3.Lerp(MeleeRaise, MeleeStrike, Frac(ai.stateTicks, AIConfig.MeleeActiveTicks));
                    break;
                case EnemyState.Recovery:
                    e = Vector3.Lerp(MeleeStrike, MeleeRest, Frac(ai.stateTicks, AIConfig.MeleeRecoveryTicks));
                    break;
                default:
                    e = MeleeRest;
                    break;
            }
            swing = Quaternion.Euler(e);
        }

        static readonly Vector3 RangedRest = new Vector3(12f, 0f, 0f);
        static readonly Vector3 RangedAim  = new Vector3(0f, 0f, 0f);

        static void RangedPose(Slot s, in EnemyAI ai, out Vector3 hand, out Quaternion swing)
        {
            hand = new Vector3(0.16f, 0.05f, 0.1f);
            Vector3 e = ai.state == EnemyState.Aim
                ? Vector3.Lerp(RangedRest, RangedAim, Frac(ai.stateTicks, AIConfig.RangedAimTicks))
                : RangedRest;

            hand += new Vector3(0f, 0f, -0.08f * s.recoilT);   // 발사 반동: 뒤로 당김
            e    += new Vector3(-15f * s.recoilT, 0f, 0f);
            swing = Quaternion.Euler(e);
        }

        static float Frac(int ticks, int total) => total <= 0 ? 1f : Mathf.Clamp01((float)ticks / total);

        // ── 무기 생성 ──
        static GameObject swordPrefab, gunPrefab;
        static bool weaponPrefabsLoaded;

        static void LoadWeaponPrefabsOnce()
        {
            if (weaponPrefabsLoaded) return;
            swordPrefab = Resources.Load<GameObject>("Weapons/SwordWeapon");
            gunPrefab   = Resources.Load<GameObject>("Weapons/GunWeapon");
            weaponPrefabsLoaded = true;
        }

        // ── 검 손잡이 그립(홀더가 월드 스케일 1이라 아래 값은 전부 실제 미터/도/배율 단위) ──
        //   본에 직접 붙이면 본이 축소돼(월드 스케일 ~0.015) 손잡이 오프셋 숫자가 비직관적이라,
        //   본 밑에 "월드 스케일 1" 홀더를 끼우고 그 안에서 실제 미터 단위로 검을 잡는다.
        //   ★ Play 중 Inspector에서 이 값들을 돌려가며 손잡이가 손바닥에 오게 맞춘 뒤 그 값을 확정하면 된다.
        //   (씬에 [EnemyWeaponView]가 없으면 부팅이 런타임에 자동 생성하므로, 직접 튜닝하려면
        //    빈 GameObject에 이 컴포넌트를 붙여 씬에 하나 두면 된다.)
        [Header("검 손잡이 그립 (실제 미터/도 단위)")]
        [Tooltip("손 본 기준 검의 위치 오프셋(미터). 손잡이가 손바닥에 오도록 조정.")]
        [SerializeField] Vector3 swordGripPos = new Vector3(-0.05f, 0.14f, -0.12f);   // Play 튜너로 실측 확정
        [Tooltip("손 본 기준 검의 회전(도). 검신이 자연스럽게 뻗도록 조정.")]
        [SerializeField] Vector3 swordGripEuler = new Vector3(190f, 100f, 5f);        // Play 튜너로 실측 확정
        [Tooltip("검 크기 배율. 폴백 피벗 크기를 1로 봤을 때의 배율.")]
        [SerializeField] float swordSize = 1f;

        // ── 총 그립(원거리) — 검과 동일한 홀더 방식. GunWeapon 길이축은 로컬 X(0.4m). ──
        //   Play 중 Inspector에서 총이 손에 자연스럽게 들리도록 돌려가며 확정한다.
        [Header("총 그립 (실제 미터/도 단위)")]
        [Tooltip("손 본 기준 총의 위치 오프셋(미터). 손잡이가 손바닥에 오도록 조정.")]
        [SerializeField] Vector3 gunGripPos = new Vector3(0.01f, 0.25f, 0.04f);    // Play 튜너로 실측 확정
        [Tooltip("손 본 기준 총의 회전(도). 총열이 조준 방향으로 뻗도록 조정.")]
        [SerializeField] Vector3 gunGripEuler = new Vector3(-35f, -85f, 60f);      // Play 튜너로 실측 확정
        [Tooltip("총 크기 배율.")]
        [SerializeField] float gunSize = 1f;

        // 매 프레임: 홀더를 본의 월드 스케일 상쇄로 "월드 스케일 1"에 고정하고(위치/회전은 본을 자동 추종),
        //   그 안의 검에 그립 값(미터/도/배율)을 적용한다 — 자식이라 애니메이션은 그대로 따라간다.
        //   매 프레임 적용하므로 Play 중 Inspector 조정이 즉시 반영된다.
        void ApplyBoneGrip(Slot s)
        {
            if (s.weaponInst == null || s.handBone == null) return;
            Vector3 ls = s.handBone.lossyScale;
            s.weaponInst.localScale = new Vector3(SafeInv(ls.x), SafeInv(ls.y), SafeInv(ls.z));
            if (s.swordChild != null)
            {
                bool gun = s.combat == CombatType.Ranged;
                s.swordChild.localPosition = gun ? gunGripPos : swordGripPos;
                s.swordChild.localRotation = Quaternion.Euler(gun ? gunGripEuler : swordGripEuler);
                s.swordChild.localScale    = Vector3.one * (gun ? gunSize : swordSize);
            }
        }

        static float SafeInv(float v) => Mathf.Abs(v) > 1e-6f ? 1f / v : 1f;

        void BuildWeapon(Slot s, CombatType combat)
        {
            if (s.pivot != null) Destroy(s.pivot.gameObject);
            if (s.weaponInst != null) Destroy(s.weaponInst.gameObject);
            LoadWeaponPrefabsOnce();

            GameObject bonePrefab = combat == CombatType.Melee ? swordPrefab
                                  : combat == CombatType.Ranged ? gunPrefab : null;
            if (bonePrefab != null && s.handBone != null)
            {
                // ★ 본 밑에 홀더를 끼워 붙이면 실제 Walk/Aim/Attack 스켈레톤 애니메이션을 그대로 따라가
                //   자연스럽고, 홀더를 "월드 스케일 1"로 상쇄하므로 홀더 안의 무기 그립 오프셋을 실제 미터
                //   단위로 잡을 수 있다(절차적 스윙 불필요 — 손 본을 그대로 따라간다). 검/총 공통 경로.
                var holder = new GameObject("WeaponHolder").transform;
                holder.SetParent(s.handBone, false);
                var weapon = Instantiate(bonePrefab, holder);
                s.weaponInst = holder;      // 홀더가 스케일 상쇄 대상
                s.swordChild = weapon.transform;
                s.pivot = null;
            }
            else
            {
                // 폴백: 본을 못 찾은 경우(예외적 상황)에만 예전 월드 좌표 피벗 + 절차적 스윙 사용.
                var pv = new GameObject($"EnemyWeapon_{combat}");
                pv.transform.SetParent(transform, false);   // 매니저(scale 1) 아래
                s.pivot = pv.transform;
                s.weaponInst = pv.transform;
                s.swordChild = null;   // 폴백 경로는 그립 미사용

                if (combat == CombatType.Melee)
                {
                    if (swordPrefab != null)
                    {
                        var sword = Instantiate(swordPrefab, s.pivot);
                        sword.transform.localPosition = new Vector3(0f, 0f, 0.22f);
                        sword.transform.localRotation = Quaternion.identity;
                    }
                    else
                        Box(s.pivot, "Blade", new Vector3(0.05f, 0.05f, 0.45f), new Vector3(0f, 0f, 0.22f),
                            Quaternion.identity, meleeMat);
                }
                else
                {
                    if (gunPrefab != null)
                    {
                        var gun = Instantiate(gunPrefab, s.pivot);
                        gun.transform.localPosition = new Vector3(0f, 0f, 0.15f);
                        gun.transform.localRotation = Quaternion.identity;
                    }
                    else
                    {
                        Box(s.pivot, "BowUpper", new Vector3(0.025f, 0.22f, 0.025f), new Vector3(0f, 0.1f, 0.05f),
                            Quaternion.Euler(30f, 0f, 0f), bowMat);
                        Box(s.pivot, "BowLower", new Vector3(0.025f, 0.22f, 0.025f), new Vector3(0f, -0.1f, 0.05f),
                            Quaternion.Euler(-30f, 0f, 0f), bowMat);
                    }
                }
            }

            s.combat = combat;
            s.built = true;
        }

        static void Box(Transform parent, string name, Vector3 scale, Vector3 pos, Quaternion rot, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Destroy(go.GetComponent<Collider>());   // 순수 시각 — 충돌 없음
            go.transform.SetParent(parent, false);
            go.transform.localScale = scale;
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.GetComponent<Renderer>().material = m;
        }

        static Material Mat(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class EnemyWeaponBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<EnemyWeaponView>() == null)
                new GameObject("[EnemyWeaponView]").AddComponent<EnemyWeaponView>();
        }
    }
}
