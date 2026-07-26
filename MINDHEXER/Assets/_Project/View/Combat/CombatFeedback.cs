using UnityEngine;
using Unity.Cinemachine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 전투 손맛(juice). ★ combat 소유·독립. 읽기 전용(SimWorld 안 씀).
    /// 적 피격/처치를 프레임 간 상태 비교로 감지 → 카메라 셰이크 + 스파크 + 타격음.
    /// 플레이어 대시/런지 시작에도 셰이크.
    /// ※ 히트스톱은 HitStop.cs 담당(A안=sim 틱 스킵, timeScale 안 건드림). 카메라 고정·HUD도 SIM 세션 몫.
    /// </summary>
    // ★ Brain(실행 순서 0)보다 먼저 돌아야 한다.
    //   vcam 렌즈에 FOV 킥을 얹는데, Brain이 그걸 읽어 실제 카메라에 반영하기 때문이다.
    //   순서가 뒤면 킥이 한 프레임 늦게 반영되어 화면이 끊겨 보인다.
    [DefaultExecutionOrder(-50)]
    public class CombatFeedback : MonoBehaviour
    {
        // 셰이크 = Cinemachine Impulse (방향·세기 파라미터). vcam의 ImpulseListener가 수신.
        // 상황별(타격 각도·런지 방향·착지)로 임펄스 "방향 벡터"를 달리 줘 손맛을 낸다.
        const float ShakeForce = 6f;   // amp(0~) → 임펄스 힘 배율 (전체 쉐이크 세기 튜닝)
        // 착지 충격(수직): 하강 속도 비례. 튜닝 상수.
        const float LandMinSpeed = 3f;     // 이 속도 미만 착지는 무시(잔착지)
        const float LandPerSpeed = 0.018f; // 하강속도 → amp 환산
        CinemachineImpulseSource impulseSource;

        // 착지 감지용 이전 프레임 상태
        bool  prevGrounded = true;
        float prevVelY;

        static CombatFeedback inst;
        /// <summary>튜닝 패널이 대시 연출 값을 만지기 위한 접근자.</summary>
        public static CombatFeedback Instance => inst;
        /// <summary>다른 연출(플레이어 피격 등)이 같은 셰이크 시스템을 쓰게 하는 정적 진입점.</summary>
        public static void Shake(float amp) { if (inst != null) inst.AddShake(amp); }

        // 적 상태 추적. ★ sim이 죽은 슬롯을 재사용하므로 인덱스가 아니라 id로 점유자를 식별한다
        //   (슬롯 재사용 시 사망 셰이크·음 누락, HP 비교 오작동 방지).
        readonly int[]     prevHp    = new int[SimConfig.MaxEnemies];
        readonly bool[]    prevAlive = new bool[SimConfig.MaxEnemies];
        readonly bool[]    seen      = new bool[SimConfig.MaxEnemies];
        readonly int[]     prevId    = new int[SimConfig.MaxEnemies];
        readonly Vector3[] prevPos   = new Vector3[SimConfig.MaxEnemies];   // 떠난 적의 마지막 자리

        // 플레이어 상태 추적
        bool prevDash;
        byte prevLunge;

        // 대시 FOV 킥 (둠식 속도감)
        const float FovKickAmount = 10f;
        float fovKick, baseFov = -1f;

        // 아래 기본값은 실제 플레이로 튜닝해 확정한 값이다.
        [Tooltip("FOV 킥이 0으로 돌아오는 속도. 작을수록 천천히 풀린다")]
        public float fovKickDecay = 5.31f;
        [Tooltip("킥이 최대치에 도달하는 속도. 작을수록 부드럽게 들어간다(0=즉시)")]
        public float fovKickAttack = 22.39f;

        float fovKickTarget;   // 목표치 — 여기로 부드럽게 다가간 뒤 0으로 풀린다

        // ── 대시 방향별 연출 ──
        // 예전엔 방향과 무관하게 fovKick=1(넓어짐)이라, 뒤로 빠져도 앞으로 튀어나가는 느낌이었다.
        //   앞 : FOV 넓어짐 — 속도감
        //   뒤 : FOV 좁아짐 — 물러나는 느낌
        //   옆 : FOV 거의 그대로, 대신 <b>롤(기울임)</b> 로 표현
        [Tooltip("앞뒤 대시 FOV 킥 크기")]
        public float dashFovFwd = 1.69f;
        // 사람 눈은 FOV가 좁아지는 것을 넓어지는 것보다 훨씬 둔하게 느낀다.
        // 그래서 뒤 대시는 앞과 <b>같은 크기 이상</b>이어야 겨우 비슷하게 체감된다 — 기본 1.4배.
        [Tooltip("뒤 대시에서 좁아지는 비율(앞 대비). 1보다 커야 앞뒤가 비슷하게 느껴진다")]
        public float dashFovBackScale = 1.4f;
        [Tooltip("옆 대시 롤 각도(도)")]
        public float dashRoll = 3.94f;
        [Tooltip("롤이 풀리는 속도")]
        public float dashRollDecay = 3.01f;

        [Tooltip("FOV 킥 부호 뒤집기 — 체감이 반대라고 느껴지면 켠다")]
        public bool dashFovInvert;

        // ── 찌르기 FOV (둠 글로리킬식) ──
        // Travel~히트스톱 동안 FOV를 좁혀 대상에 빨려드는 압박을 주고,
        // 얼음이 풀려 조작이 돌아오는 순간 <b>확 넓혔다가</b> 천천히 제자리로 — 해방감.
        [Tooltip("찌르기 돌진~히트스톱 동안 좁아지는 정도(도). 이동 시작부터 걸려 끝까지 유지된다")]
        public float lungeZoomIn = 15f;
        [Tooltip("좁아지는 데 걸리는 시간(초). 돌진 시작과 함께 이만큼에 걸쳐 조인다")]
        public float lungeZoomInTime = 0.08f;
        [Tooltip("히트스톱이 풀린 뒤 원래대로 돌아오는 시간(초)")]
        public float lungeZoomOutTime = 0.4f;

        // 해제 순간 한 번 터지던 확장 킥 — 실플레이에서 별로여서 기본 0(사실상 끔).
        // 구조는 남겨 두어 필요하면 F1에서 다시 켤 수 있다.
        [Tooltip("히트스톱이 풀리는 순간 추가로 넓어지는 정도(도). 0 = 안 씀")]
        public float lungeZoomOut = 0f;
        // ★ "속도"가 아니라 "시간(초)"으로 둔다. 예전엔 속도라서 슬라이더를 최대로 올리면
        //   오히려 더 빨리 돌아왔다(의미가 직관과 반대).
        [Tooltip("확장이 최대까지 벌어지는 시간(초). 0이면 즉시 — 즉시는 계단처럼 튀어 끊겨 보인다")]
        public float lungeReleaseRise = 0.07f;
        [Tooltip("확장이 원래대로 돌아오는 시간(초). 클수록 여운이 길다")]
        public float lungeReleaseFall = 0.45f;

        float lungeFov;        // 찌르기 구간에서 유지되는 FOV 오프셋(음수=좁아짐)
        float releaseKick;     // 해제 순간 터지는 확장(양수=넓어짐)
        float releaseT = -1f;  // 확장 진행 시간(초). 음수 = 비활성
        bool  wasFrozen;       // 직전 프레임에 히트스톱이 걸려 있었는가

        float rollCur;   // 현재 롤(도) — 옆 대시에서 기울었다가 풀린다

        // ── 진단(F1 표시용) — 추측 대신 실제 값을 본다 ──
        public float LastDashFwd  { get; private set; }   // +앞 / −뒤
        public float LastDashSide { get; private set; }   // +오른쪽 / −왼쪽
        public float FovKickNow   => fovKick;
        public float RollNow      => rollCur;
        /// <summary>현재 실제로 얹히는 FOV 변화량(도). +면 넓어짐.</summary>
        public float FovDeltaNow  => FovKickAmount * fovKick + lungeFov + releaseKick;
        public float LungeFovNow  => lungeFov;
        public float ReleaseNow   => releaseKick;

        ParticleSystem sparks;

        void Awake()
        {
            inst = this;
            // 저장값 자동 로드는 씬 로드 '전'에 돌아 이 컴포넌트가 아직 없다.
            // inst를 세운 뒤 한 번 더 읽어야 FOV 연출 값까지 복원된다(CombatCamera와 같은 이유).
            CombatTuningSave.Load();
            BuildSparks();
            impulseSource = gameObject.AddComponent<CinemachineImpulseSource>();
            impulseSource.DefaultVelocity = new Vector3(0.4f, 0.4f, 0.15f);   // 기본 쉐이크 방향(힘으로 스케일)
        }

        void Update()
        {
            var main = Main.Instance;
            if (main == null) return;
            ref readonly SimWorld w = ref main.World;

            // ── 적 피격/처치 감지 (스턴 폐기 → HP 감소 기반) ──
            for (int i = 0; i < w.enemyCount; i++)
            {
                ref readonly EnemySim e = ref w.enemies[i];
                bool sameId = seen[i] && e.id == prevId[i];
                if (seen[i])
                {
                    if (sameId && e.alive && e.combat.health < prevHp[i])   // HP 감소 = 새 타격(동일 적일 때만)
                        OnHit(e.pos);
                    // 처치 = 죽어서 남았거나 슬롯이 재사용됨(id 변경). 재사용이면 떠난 적의 마지막 자리.
                    if (prevAlive[i] && (!e.alive || !sameId))
                        OnDeath(sameId ? e.pos : prevPos[i]);
                }
                seen[i]      = true;
                prevHp[i]    = e.combat.health;
                prevAlive[i] = e.alive;
                prevId[i]    = e.id;
                prevPos[i]   = e.pos;
            }

            // ── 플레이어 액션 셰이크(방향성) + 대시 FOV 킥 ──
            bool dash = w.player.dashTicks > 0;
            if (dash && !prevDash)
            {
                AxisShake(w.player.dashDir, 0.10f);   // 대시 방향으로 훅

                // 대시 방향을 카메라 기준 전후/좌우 성분으로 분해해 연출을 나눈다
                float yr = w.player.yaw * Mathf.Deg2Rad;
                Vector3 fwd   = new Vector3(Mathf.Sin(yr), 0f, Mathf.Cos(yr));
                Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
                Vector3 d = w.player.dashDir; d.y = 0f;
                if (d.sqrMagnitude > 1e-6f) d.Normalize();

                float f = Vector3.Dot(d, fwd);      // +앞 / −뒤
                float s = Vector3.Dot(d, right);    // +오른쪽 / −왼쪽
                LastDashFwd = f; LastDashSide = s;  // 진단용 기록

                // 둠식 speed FOV — 앞으로 빠를수록 FOV를 <b>넓혀</b> 화면 가장자리 흐름을 빠르게 만든다.
                // (둠 이터널 옵션에 "Dash FOV"가 따로 있고 멀미 때문에 끄는 사람이 있다는 것 자체가
                //  기본이 넓어짐이라는 뜻이다. 체감이 약했던 건 부호가 아니라 킥이 너무 빨리 사라져서였다)
                fovKick = f >= 0f ? f * dashFovFwd
                                  : f * dashFovFwd * dashFovBackScale;
                if (dashFovInvert) fovKick = -fovKick;
                // 옆 성분만큼 화면을 기울인다 — 오른쪽 대시면 화면이 왼쪽으로 눕는다
                rollCur = -s * dashRoll;

                fovKickTarget = fovKick;
                if (fovKickAttack > 0.01f) fovKick = 0f;   // 0에서 목표로 부드럽게 올라간다
            }
            prevDash = dash;

            // 들어갈 때는 fovKickAttack, 풀릴 때는 fovKickDecay — 속도를 따로 준다.
            // 예전엔 하나뿐이라 "확 켜졌다 확 꺼지는" 느낌이었다.
            float dtu = Time.unscaledDeltaTime;
            if (Mathf.Abs(fovKickTarget) > 0.001f)
            {
                float ka = 1f - Mathf.Exp(-Mathf.Max(0.01f, fovKickAttack) * dtu);
                fovKick = Mathf.Lerp(fovKick, fovKickTarget, ka);
                // 목표에 거의 닿으면 이제 0으로 풀기 시작
                if (Mathf.Abs(fovKick - fovKickTarget) < Mathf.Abs(fovKickTarget) * 0.08f) fovKickTarget = 0f;
            }
            else
            {
                float kd = 1f - Mathf.Exp(-Mathf.Max(0.01f, fovKickDecay) * dtu);
                fovKick = Mathf.Lerp(fovKick, 0f, kd);
                if (Mathf.Abs(fovKick) < 0.002f) fovKick = 0f;
            }
            rollCur = Mathf.MoveTowards(rollCur, 0f, dashRollDecay * dashRoll * dtu);

            // ── 찌르기 FOV: 돌진·히트스톱 동안 좁힘 → 풀리는 순간 확장 ──
            // "조작이 돌아오는 시점"의 기준은 히트스톱 해제다. 히트스톱은 sim 틱을 건너뛰므로
            // 그동안 마우스 입력도 sim에 안 들어간다 — 즉 얼음이 풀리는 순간이 곧 조작 복귀다.
            bool frozen  = HitStop.FrozenTicks > 0;
            bool lunging = w.player.combat.lungePhase != CombatConfig.LgNone;
            bool holding = lunging || frozen;          // 좁혀 두는 구간

            if (wasFrozen && !frozen) releaseT = 0f;   // ★ 풀리는 순간 확장 시작(계단 아님)
            wasFrozen = frozen;

            // 좁힘 — 시간 기반. "몇 초에 걸쳐"가 직관적이라 지수 감쇠 대신 선형 속도로 민다.
            //   돌진 시작 → lungeZoomInTime 에 걸쳐 -lungeZoomIn 까지
            //   히트스톱 해제 → lungeZoomOutTime 에 걸쳐 0 까지
            float wantLunge = holding ? -lungeZoomIn : 0f;
            float span = holding ? Mathf.Max(0.01f, lungeZoomInTime)
                                 : Mathf.Max(0.01f, lungeZoomOutTime);
            // 목표까지의 전체 폭을 span 초에 주파하는 속도(도/초)
            float rate = Mathf.Max(0.01f, lungeZoomIn) / span;
            lungeFov = Mathf.MoveTowards(lungeFov, wantLunge, rate * dtu);

            // 해제 확장 — 시간 기반 포락선(0 → 최대 → 0). 계단이 아니라 곡선이라 안 튄다.
            if (releaseT >= 0f)
            {
                releaseT += dtu;
                float rise = Mathf.Max(0.001f, lungeReleaseRise);
                float fall = Mathf.Max(0.001f, lungeReleaseFall);
                float env;
                if (releaseT < rise)
                {
                    float u = releaseT / rise;
                    env = u * u * (3f - 2f * u);                       // smoothstep 상승
                }
                else
                {
                    float u = (releaseT - rise) / fall;
                    if (u >= 1f) { releaseT = -1f; env = 0f; }
                    else { float v = 1f - u; env = v * v * (3f - 2f * v); }   // smoothstep 하강
                }
                releaseKick = lungeZoomOut * env;
            }
            else releaseKick = 0f;

            byte lg = w.player.combat.lungePhase;
            if (lg != prevLunge)
            {
                Vector3 lungeDir = w.player.combat.lungeDest - w.player.pos;   // 런지 표적 방향
                if (lg == CombatConfig.LgTravel) AxisShake(lungeDir, 0.10f);   // 발동: 표적 방향 훅
                if (prevLunge == CombatConfig.LgTravel && lg == CombatConfig.LgRecovery)
                {
                    AxisShake(lungeDir, 0.22f); fovKick = CombatConfig.LungeFovKick / FovKickAmount;   // 임팩트: 표적 방향 강한 훅 + FOV 킥
                    ScreenFx.Impact(0.55f);   // 색수차 버스트
                }
                prevLunge = lg;
            }
            // 런지 Travel 동안 렌즈 왜곡 풀백(매 프레임 목표 세팅 → 종료 시 자연 복귀)
            ScreenFx.LungePull(lg == CombatConfig.LgTravel ? 1f : 0f);

            // ── 착지 충격: grounded false→true 전환 시 직전 하강 속도 비례 수직 임펄스 ──
            bool grounded = w.player.grounded;
            if (grounded && !prevGrounded)
            {
                float impact = Mathf.Max(0f, -prevVelY);
                if (impact > LandMinSpeed)
                    AxisShake(Vector3.down, Mathf.Clamp(impact * LandPerSpeed, 0.08f, 0.30f));
            }
            prevGrounded = grounded;
            prevVelY     = w.player.vel.y;
        }

        void LateUpdate()
        {
            // FOV 킥은 vcam 렌즈에 얹는다(Brain이 실카메라에 반영). 쉐이크는 Impulse가 처리(여기 없음).
            var vcam = Main.Instance != null ? Main.Instance.GameplayVcam : null;
            if (vcam == null) return;

            // 컷신은 렌즈를 통째로 바꾼다 — 여기서 매 프레임 FOV를 되돌리면 서로 싸운다.
            if (Cutscene.Active) { baseFov = -1f; return; }

            // 옆 대시 롤 — Dutch(화면 기울임)에 얹는다.
            // 롤이 0으로 잦아든 뒤에도 Dutch에 잔값이 남으면 화면이 계속 기운 채로 있으므로,
            // 남아 있을 때는 0을 한 번 더 써서 확실히 편다.
            if (Mathf.Abs(rollCur) > 0.001f || Mathf.Abs(vcam.Lens.Dutch) > 0.001f)
                vcam.Lens.Dutch = Mathf.Abs(rollCur) > 0.001f ? rollCur : 0f;

            // 대시 킥 + 찌르기 좁힘 + 해제 확장을 합산한다(도 단위).
            float delta = FovKickAmount * fovKick + lungeFov + releaseKick;

            // ★ 변화가 없으면 아예 손대지 않는다.
            //   예전엔 조건 없이 매 프레임 baseFov로 덮어써서, 다른 곳(컷신·인스펙터)이
            //   FOV를 바꿔도 즉시 되돌아가고 baseFov가 낡으면 영구히 잘못된 값에 고정됐다.
            if (Mathf.Abs(delta) <= 0.01f)
            {
                baseFov = -1f;                     // 다음 킥 때 현재 FOV를 새로 기준 삼는다
                return;
            }
            if (baseFov < 0f) baseFov = vcam.Lens.FieldOfView;
            vcam.Lens.FieldOfView = Mathf.Clamp(baseFov + delta, 5f, 170f);
        }

        void OnHit(Vector3 pos)
        {
            DirectionalShake(pos, 0.12f, 0.35f);   // 타격 각도: 적→카메라 반동 + 약간 위로
            EmitSparks(pos, 14);
            CombatAudio.Hit();        // [되돌림] 예전 금속 검격음
            CombatAudio.EnemyPain();  // [되돌림] 예전 합성 신음
        }

        void OnDeath(Vector3 pos)
        {
            DirectionalShake(pos, 0.18f, 0.5f);    // 처치: 더 강한 반동 + 위로 펀치
            ScreenFx.Impact(0.4f);                 // 색수차 버스트
            EmitSparks(pos, 30);
            // [2026-07-22] 죽이는 타격도 일반 타격과 같은 소리(평타피격+로봇피격)로 통일.
            // 예전 Death()(옛 살점 "썰리는 소리")는 최근 에셋과 안 어울려 제거.
            CombatAudio.Hit();
            CombatAudio.EnemyPain();
        }

        // 무방향(정적 진입점 등): DefaultVelocity 방향으로 세기만.
        void AddShake(float amp)
        {
            if (impulseSource != null) impulseSource.GenerateImpulseWithForce(amp * ShakeForce);
        }

        // 타격 각도 셰이크: 타격 지점 → 카메라(반동 방향) + upBias 만큼 위로 튐.
        void DirectionalShake(Vector3 fromWorldPos, float amp, float upBias)
        {
            if (impulseSource == null) return;
            Vector3 dir = Vector3.up;
            var cam = Main.Instance != null ? Main.Instance.Cam : null;
            if (cam != null)
            {
                Vector3 toCam = cam.transform.position - fromWorldPos;
                toCam.y = 0f;
                dir = toCam.sqrMagnitude > 1e-4f ? toCam.normalized : cam.transform.forward;
            }
            impulseSource.GenerateImpulse((dir + Vector3.up * upBias) * (amp * ShakeForce));
        }

        // 방향 지정 셰이크(런지·대시·착지): 주어진 월드 방향으로 훅.
        void AxisShake(Vector3 worldDir, float amp)
        {
            if (impulseSource == null) return;
            if (worldDir.sqrMagnitude < 1e-6f) { AddShake(amp); return; }
            impulseSource.GenerateImpulse(worldDir.normalized * (amp * ShakeForce));
        }

        void EmitSparks(Vector3 pos, int count)
        {
            if (sparks == null) return;
            sparks.transform.position = pos + Vector3.up * 0.6f;
            sparks.Emit(count);
        }

        void BuildSparks()
        {
            var go = new GameObject("HitSparks");
            go.transform.SetParent(transform, false);
            sparks = go.AddComponent<ParticleSystem>();
            sparks.Stop();

            var m = sparks.main;
            m.startLifetime   = 0.35f;
            m.startSpeed      = 6f;
            m.startSize       = 0.12f;
            m.startColor      = new Color(0.5f, 0.05f, 0.05f);   // 검붉은
            m.gravityModifier = 1.2f;
            m.maxParticles    = 300;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.playOnAwake     = false;

            var em = sparks.emission; em.enabled = false;   // 수동 Emit만
            var sh = sparks.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.1f;

            var r = sparks.GetComponent<ParticleSystemRenderer>();
            var sh2 = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                   ?? Shader.Find("Sprites/Default");
            if (sh2 != null) r.material = new Material(sh2);
        }
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class CombatFeedbackBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<CombatFeedback>() == null)
                new GameObject("[CombatFeedback]").AddComponent<CombatFeedback>();
        }
    }
}
