using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 런지·글로리킬 중 카메라를 대상에 고정. ★ combat 소유·독립. 화면연출 계층.
    /// Main이 Update에서 매 프레임 카메라를 생 입력각으로 세팅한다. 그래서 여기서 슬러프의
    /// "출발점"을 cam.transform.rotation으로 삼으면 매 프레임 리셋에 휘둘려 끊기고 안 잠긴다.
    ///
    /// 설계:
    ///  - 지속 상태 curYaw를 들고 목표(lockYaw)로 부드럽게 이은 뒤 rotation을 "절대값"으로 세팅.
    ///    → Main의 리셋·마우스·프레임 지터에 안 휘둘리고 실제로 대상에 도달·고정된다.
    ///  - 대상 yaw는 잠금 시작 시 1회만 확정(러쉬는 직진이라 이후 거의 안 변함) → 막판 급회전 제거.
    ///  - pitch는 input.Pitch를 그대로 써 해제 시 pitch 튐이 없다(input에 pitch 쓰기가 없어서).
    ///  - 해제 시 최종 yaw를 input.Yaw로 인계 → 원복(스냅백) 없음.
    /// </summary>
    // ★ Brain(실행 순서 0)보다 먼저. 아래에서 vcam 회전을 직접 써야 이번 프레임에 반영된다.
    [DefaultExecutionOrder(-50)]
    public class CombatCamera : MonoBehaviour
    {
        public static CombatCamera Instance { get; private set; }
        void Awake()
        {
            Instance = this;
            // CombatTuningSave의 자동 로드는 씬 로드 '전'에 돌아 이 컴포넌트가 아직 없다.
            // Instance를 세운 뒤 한 번 더 읽어야 카메라 값까지 복원된다.
            CombatTuningSave.Load();
        }

        // ── 둠식 연출 (전부 View 전용 — 예지에 영향 없음) ──
        [Header("둠식 락온")]
        [Tooltip("끄면 예전 동작(몸통 중심 겨냥·pitch 완전 강제·복귀 없음)으로 되돌아간다")]
        public bool doomStyle = true;

        [Tooltip("대상의 키 대비 겨냥 높이. 0.5=몸통 중심(예전) · 0.85=가슴~머리")]
        [Range(0.2f, 1.2f)] public float aimHeightRatio = 0.85f;

        // 1.0 = 대상을 정확히 겨냥. 경로가 포물선이라 시선이 처음부터 그쪽을 향하므로
        // 예전처럼 "억지로 내려가는" 느낌이 나지 않는다 — 낮출 이유가 없다.
        [Tooltip("pitch를 대상 쪽으로 얼마나 끌어당길지. 1=완전 강제(권장)")]
        [Range(0f, 1f)] public float pitchWeight = 1f;

        [Tooltip("강제로 내려갈 수 있는 최저 pitch(도). 89면 사실상 제한 없음")]
        public float pitchDownLimit = 89f;

        [Tooltip("대상에 붙는 속도. 클수록 탁 붙는다")]
        public float enterRate = 55f;

        // ★ 0 = 끝난 자리에서 그대로 끝낸다(기본).
        //   예전엔 pitch를 강제로 내려놓고 끝난 뒤 되돌렸는데, 그건 원인을 놔두고 결과를 지우는
        //   땜질이라 "끝나고 카메라가 한 번 더 움직이는" 어색함이 생겼다.
        //   지금은 경로 자체를 포물선으로 만들어 시선이 처음부터 어긋나지 않으므로 되돌릴 게 없다.
        [Tooltip("락온이 끝난 뒤 원래 pitch로 되돌아가는 시간(초). 0 = 되돌리지 않음(권장)")]
        public float exitRestore = 0f;

        // ── 찌르기 목표각 고정 ──
        // 글로리킬이 부드러운 이유는 적도 플레이어도 멈춰 있어 <b>목표각이 상수</b>이기 때문이다.
        // 찌르기는 돌진하는 내내 거리가 줄어드는데, 가까워질수록 같은 위치 오차가 만드는
        // 각도 변화가 폭발한다(7m에서 0.5m 어긋남=4°, 1m에서는 27°).
        // 그래서 도착 직전 각속도가 치솟아 카메라가 홱 돌아버린다.
        //   → 발동 순간 "도착점에서 대상을 볼 각도"를 1회 확정하고 그것만 쫓는다.
        [Tooltip("찌르기 목표각을 발동 시 1회 확정한다(끊김 방지). 끄면 매 프레임 재계산(예전)")]
        public bool lockLungeAim = true;


        const float TurnRateLegacy = 24f;   // 예전 값

        float curYaw, curPitch;   // 부드럽게 따라가는 현재 시선 (지속 상태 — 매 프레임 절대 세팅)
        bool  locked;

        // 종료 복귀 — 락온 진입 전 pitch를 기억했다가 되돌린다.
        // 예전엔 내려간 pitch를 그대로 인계해서 "찌르고 화면이 강제로 내려간 채로 남는" 느낌이 났다.
        float restorePitch, restoreT;
        bool  restoring;

        /// <summary>현재 상태 요약(콘솔 표시용).</summary>
        public string Status =>
            $"{(doomStyle ? "둠식" : "예전")} · 겨냥높이 {aimHeightRatio:0.00} · pitch가중 {pitchWeight:0.00}" +
            $" · 하한 {pitchDownLimit:0}° · 진입 {(doomStyle ? enterRate : TurnRateLegacy):0} · 복귀 {exitRestore:0.00}초" +
            $" · 목표각 {(lockLungeAim ? "고정" : "매프레임")}";

        void LateUpdate()
        {
            var main = Main.Instance;
            if (main == null) return;
            Camera cam = main.Cam;
            if (cam == null) return;

            ref readonly SimWorld w = ref main.World;
            ref readonly PlayerCombatState c = ref w.player.combat;

            bool glory = c.gloryPhase != CombatConfig.GlNone;
            bool lunge = c.lungePhase != CombatConfig.LgNone && c.lungeTargetId >= 0;
            if (!glory && !lunge)
            {
                if (locked)
                {
                    main.SetLookYaw(curYaw);          // yaw는 그대로 인계(적을 본 채로 끝나는 게 자연스럽다)
                    main.SetLookPitch(curPitch);
                    locked = false;
                    // 둠식: pitch만 원래대로 부드럽게 되돌린다 — 화면이 내려간 채 남지 않게
                    if (doomStyle && exitRestore > 0.001f) { restoring = true; restoreT = 0f; }
                }

                if (restoring)
                {
                    restoreT += Time.unscaledDeltaTime;
                    float u = Mathf.Clamp01(restoreT / exitRestore);
                    u = u * u * (3f - 2f * u);                       // smoothstep — 끝이 부드럽게 멎음
                    float pt = Mathf.LerpAngle(curPitch, restorePitch, u);
                    main.SetLookPitch(pt);
                    var vc = main.GameplayVcam;                      // 복귀 중에도 이번 프레임에 반영
                    if (vc != null) vc.transform.rotation = Quaternion.Euler(pt, main.LookYaw, 0f);
                    if (u >= 1f) restoring = false;
                }
                return;
            }
            restoring = false;   // 다시 잠기면 복귀 중단

            // 대상: 글로리킬 처형 몹 우선, 아니면 런지 표적 몹(몸통 겨냥). 표적은 바인드로 정지.
            // 대상의 '실제' 높이로 중심을 겨냥 — 대형몹(×3)의 발치를 보던 버그 수정.
            Vector3 targetPos; float targetHeight;
            if (glory)
            { targetPos = w.enemies[c.gloryTargetId].pos; targetHeight = w.enemies[c.gloryTargetId].height; }
            else
            { targetPos = LungeTargetPos(in w, c.lungeTargetId, out targetHeight); }
            // 둠식은 가슴~머리를 본다. 예전(0.5=몸통 중심)은 겨냥이 낮아 화면이 아래로 다이빙했다.
            float ratio = doomStyle ? aimHeightRatio : 0.5f;
            Vector3 aimAt = targetPos + Vector3.up * (targetHeight * ratio);

            float lockYaw, lockPitch;
            if (!glory && lockLungeAim && c.lungeTravelTicks > 0)
            {
                // ★ 에임이 <b>실제 포물선 경로</b>를 따라간다.
                //   Sim과 같은 LungeArcPoint를 써서 "몸이 지나갈 지점"에서 대상을 보는 각도를 낸다.
                //   경로가 대상 쪽으로 휘어 있으므로 시선도 자연스럽게 호를 그리고,
                //   도착 지점에서는 최종 각도와 일치해 <b>끝날 때 이미 멈춰 있다</b>.
                //   그래서 종료 시 되돌릴 것이 없다.
                float t = Mathf.Clamp01(c.lungeTicks / (float)c.lungeTravelTicks);
                float inv = 1f - t;
                float e = 1f - inv * inv * inv;                     // Sim과 동일한 ease-out

                // 도착 직전에는 목표점을 도착 각도로 미리 수렴시켜, 거리가 0에 가까워질 때
                // 각속도가 폭발하는 것을 막는다(직선 시절 끊김의 원인).
                const float SettleFrom = 0.6f;
                float settle = t <= SettleFrom ? 0f : Mathf.Clamp01((t - SettleFrom) / (1f - SettleFrom));
                settle = settle * settle * (3f - 2f * settle);      // smoothstep

                Vector3 here = PlayerCombat.LungeArcPoint(in c, e) + Vector3.up * (SimConfig.PlayerHeight * 0.7f);
                Vector3 end  = c.lungeDest + Vector3.up * (SimConfig.PlayerHeight * 0.7f);
                Vector3 eye  = Vector3.Lerp(here, end, settle);

                Vector3 dd = aimAt - eye;
                if (dd.sqrMagnitude > 1e-4f)
                {
                    Vector3 e0 = Quaternion.LookRotation(dd).eulerAngles;
                    lockPitch = e0.x; lockYaw = e0.y;
                }
                else { lockYaw = main.LookYaw; lockPitch = main.LookPitch; }
            }
            else
            {
                // 글로리킬(대상·플레이어 모두 정지 → 목표각이 상수) 또는 예전 방식
                Vector3 d = aimAt - cam.transform.position;
                if (d.sqrMagnitude > 1e-4f)
                {
                    Vector3 e = Quaternion.LookRotation(d).eulerAngles;
                    lockPitch = e.x; lockYaw = e.y;
                }
                else { lockYaw = main.LookYaw; lockPitch = main.LookPitch; }
            }

            if (!locked)
            {
                curYaw = main.LookYaw; curPitch = main.LookPitch; locked = true;
                restorePitch = main.LookPitch;   // 끝나면 여기로 되돌린다
            }

            if (doomStyle)
            {
                // ★ 화면이 강제로 내려가던 원인: pitch를 대상 각도로 100% 끌어당겼다.
                //   가중치로 일부만 반영하고, 아래로는 하한을 둬 바닥 다이빙을 막는다.
                float wantPitch = Mathf.LerpAngle(restorePitch, lockPitch, Mathf.Clamp01(pitchWeight));
                float delta = Mathf.DeltaAngle(0f, wantPitch);
                if (delta > pitchDownLimit) wantPitch = pitchDownLimit;   // +가 아래(유니티 관례)
                lockPitch = wantPitch;
            }

            float rate = doomStyle ? enterRate : TurnRateLegacy;
            float k = 1f - Mathf.Exp(-rate * Time.unscaledDeltaTime);
            curYaw   = Mathf.LerpAngle(curYaw, lockYaw, k);
            curPitch = Mathf.LerpAngle(curPitch, lockPitch, k);
            // 입력 시선을 갱신(다음 틱 sim·조준에 반영)
            main.SetLookYaw(curYaw);
            main.SetLookPitch(curPitch);

            // ★ vcam 회전도 지금 바로 쓴다.
            //   Main은 Update에서 vcam pose를 세팅하므로, 여기(LateUpdate)서 시선만 바꾸면
            //   실제 카메라에는 <b>다음 프레임에야</b> 반영되어 락온이 한 박자 늦게 따라온다.
            var vcam = main.GameplayVcam;
            if (vcam != null)
                vcam.transform.rotation = Quaternion.Euler(curPitch, curYaw, 0f);
        }

        static Vector3 LungeTargetPos(in SimWorld w, int targetId, out float height)
        {
            for (int i = 0; i < w.enemyCount; i++)
                if (w.enemies[i].id == targetId) { height = w.enemies[i].height; return w.enemies[i].pos; }
            height = SimConfig.EnemyHeight;
            return w.player.combat.lungeDest;   // 표적 소실 시 도착점
        }
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class CombatCameraBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<CombatCamera>() == null)
                new GameObject("[CombatCamera]").AddComponent<CombatCamera>();
        }
    }
}
