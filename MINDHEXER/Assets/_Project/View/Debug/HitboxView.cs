using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// Sim의 충돌 캡슐을 Game 뷰에 와이어프레임으로 겹쳐 그린다(기본 꺼짐, 콘솔 <c>hitbox</c>).
    ///
    /// 용도: <b>모델 크기를 조정하기 전에 실제 판정 크기와 대조</b>하는 것.
    /// 모델이 캡슐보다 작으면 "맞았는데 안 맞은 것처럼" 보이고, 크면 그 반대다.
    ///
    /// Gizmos는 Scene 뷰에서만 보이므로 GL로 직접 그린다(Game 뷰에서 보여야 의미가 있다).
    /// 기본은 벽·모델을 통과해 보이게(ZTest Always) — 모델에 가려지면 대조가 안 되기 때문.
    /// </summary>
    public class HitboxView : MonoBehaviour
    {
        public static HitboxView Instance { get; private set; }

        [Tooltip("몹·플레이어 충돌 캡슐")]
        public bool showBodies;
        [Tooltip("평타 판정 부채꼴 (캡슐이 아니라 콘 모양이다)")]
        public bool showCone;
        /// <summary>찌르기 사거리(최소~최대) 표시</summary>
        public bool showLunge;
        [Tooltip("모델에 가려지지 않게 뚫고 보이기")]
        public bool xray = true;

        public bool AnyOn => showBodies || showCone || showLunge;

        const int Segments = 24;   // 원 분할 수

        static readonly Color ColEnemy  = new Color(0.25f, 1f, 0.35f, 0.9f);
        static readonly Color ColGlory  = new Color(1f, 0.85f, 0.2f, 0.95f);   // 처형 가능
        static readonly Color ColPlayer = new Color(0.35f, 0.7f, 1f, 0.9f);
        static readonly Color ColCone   = new Color(1f, 0.3f, 0.25f, 0.9f);

        Material lineMat;

        void Awake() { Instance = this; }

        void OnDestroy() { if (lineMat != null) Destroy(lineMat); }

        Material Mat()
        {
            if (lineMat != null) return lineMat;
            var sh = Shader.Find("Hidden/Internal-Colored");
            if (sh == null) return null;
            lineMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            lineMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            lineMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            lineMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            lineMat.SetInt("_ZWrite", 0);
            return lineMat;
        }

        void OnRenderObject()
        {
            if (!AnyOn) return;
            var main = Main.Instance;
            if (main == null) return;

            // ★ Camera.current 로 카메라를 거르지 않는다.
            //   URP에서는 OnRenderObject 중 Camera.current 가 기대한 카메라를 가리키지 않는 경우가 있어
            //   그걸로 거르면 아무것도 안 그려진다(같은 프로젝트의 NavMeshDebugView도 이 검사가 없다).

            var m = Mat();
            if (m == null) return;
            m.SetInt("_ZTest", (int)(xray ? UnityEngine.Rendering.CompareFunction.Always
                                          : UnityEngine.Rendering.CompareFunction.LessEqual));
            m.SetPass(0);

            GL.PushMatrix();
            GL.Begin(GL.LINES);

            ref readonly SimWorld w = ref main.World;

            int drawn = 0;
            if (showBodies)
            {
                for (int i = 0; i < w.enemyCount; i++)
                {
                    ref readonly EnemySim e = ref w.enemies[i];
                    if (!e.alive) continue;
                    // 이번 스윙에서 이미 때린 적은 흰색 — "구가 닿았는데 안 맞았다"를 구분한다
                    bool hitThisSwing = w.player.combat.attackPhase != CombatConfig.PhNone && HitMaskHas(in w.player.combat, i);
                    Capsule(e.pos, e.radius, e.height,
                            hitThisSwing            ? Color.white
                          : e.combat.gloryStage > 0 ? ColGlory : ColEnemy);
                    drawn++;
                }
                ref readonly PlayerSim p = ref w.player;
                Capsule(p.pos, SimConfig.PlayerRadius, SimConfig.PlayerHeight, ColPlayer);
                drawn++;
            }
            DrawnLast = drawn;

            // 평타 판정 — 지금 쓰는 방식으로 그린다(구/부채꼴)
            if (showCone)
            {
                if (CombatConfig.UseSphereMelee) MeleeSphere(in w.player);
                else                             Cone(in w.player);
            }
            if (showLunge) LungeRange(in w.player);

            GL.End();
            GL.PopMatrix();
        }

        /// <summary>스윙 히트마스크 조회(Sim의 CombatResolve와 같은 규칙).</summary>
        static bool HitMaskHas(in PlayerCombatState c, int i) =>
            i < 64 ? (c.attackHitMask0 & (1UL << i)) != 0UL
                   : (c.attackHitMask1 & (1UL << (i - 64))) != 0UL;

        /// <summary>발밑 기준 캡슐. Sim의 pos는 발밑이고 radius/height는 개체값(대형몹 반영).</summary>
        static void Capsule(Vector3 feet, float radius, float height, Color c)
        {
            GL.Color(c);
            float r = Mathf.Max(0.01f, radius);
            float h = Mathf.Max(r * 2f, height);
            float yBot = feet.y + r;              // 아래 반구 중심
            float yTop = feet.y + h - r;          // 위 반구 중심

            Ring(new Vector3(feet.x, feet.y + 0.002f, feet.z), r);   // 바닥 접지 링
            Ring(new Vector3(feet.x, yBot, feet.z), r);
            Ring(new Vector3(feet.x, yTop, feet.z), r);
            Ring(new Vector3(feet.x, feet.y + h, feet.z), r * 0.35f); // 정수리

            // 옆면 기둥 4줄
            for (int k = 0; k < 4; k++)
            {
                float a = k * Mathf.PI * 0.5f;
                Vector3 o = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                Line(new Vector3(feet.x, yBot, feet.z) + o, new Vector3(feet.x, yTop, feet.z) + o);
            }
            // 반구 아치(앞뒤·좌우)
            Arc(new Vector3(feet.x, yTop, feet.z), r, true,  1f);
            Arc(new Vector3(feet.x, yTop, feet.z), r, false, 1f);
            Arc(new Vector3(feet.x, yBot, feet.z), r, true,  -1f);
            Arc(new Vector3(feet.x, yBot, feet.z), r, false, -1f);
        }

        static void Ring(Vector3 center, float r)
        {
            Vector3 prev = center + new Vector3(r, 0f, 0f);
            for (int i = 1; i <= Segments; i++)
            {
                float a = i / (float)Segments * Mathf.PI * 2f;
                Vector3 cur = center + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                Line(prev, cur); prev = cur;
            }
        }

        /// <summary>반구 아치. xz=true면 XY평면, false면 ZY평면. dir=+1 위쪽 / -1 아래쪽.</summary>
        static void Arc(Vector3 center, float r, bool xz, float dir)
        {
            int n = Segments / 2;
            Vector3 prev = center + (xz ? new Vector3(r, 0f, 0f) : new Vector3(0f, 0f, r));
            for (int i = 1; i <= n; i++)
            {
                float a = i / (float)n * Mathf.PI;
                float ca = Mathf.Cos(a) * r, sa = Mathf.Sin(a) * r * dir;
                Vector3 cur = center + (xz ? new Vector3(ca, sa, 0f) : new Vector3(0f, sa, ca));
                Line(prev, cur); prev = cur;
            }
        }

        /// <summary>
        /// 오버워치식 평타 판정 — 시선 앞에 놓인 구.
        /// 판정 틱에는 진하게, 아닐 때는 흐리게 그려 "언제 판정되는지"가 눈에 보이게 한다.
        /// 시선(피치 포함) 방향으로 뻗은 선도 함께 그려 어디를 겨냥 중인지 알 수 있다.
        /// </summary>
        static void MeleeSphere(in PlayerSim p)
        {
            // 판정창이 열려 있는 동안만 색이 확 바뀐다 — 즉발인지 눈으로 확인하는 용도.
            //   판정 중: 노랑 → 빨강 (남은 시간이 줄수록 붉어짐)
            //   그 외  : 회색 반투명
            int judgeTicks = CombatConfig.AtkJudge(p.combat.attackStep);
            bool judging = CombatConfig.AttackInstantJudge
                ? (p.combat.attackPhase != CombatConfig.PhNone && p.combat.attackElapsed < judgeTicks)
                : (p.combat.attackPhase == CombatConfig.PhActive);

            Color c;
            if (judging)
            {
                float t = judgeTicks <= 1 ? 0f : Mathf.Clamp01(p.combat.attackElapsed / (float)(judgeTicks - 1));
                c = Color.Lerp(new Color(1f, 0.95f, 0.2f), new Color(1f, 0.2f, 0.1f), t);   // 노랑→빨강
                c.a = 1f;
            }
            else c = new Color(0.6f, 0.6f, 0.65f, 0.22f);                                   // 회색
            GL.Color(c);

            Vector3 eye = p.pos + Vector3.up * CombatConfig.MeleeEyeHeight;
            Vector3 dir = CombatHit.LookDir(p.yaw, p.aimPitch);
            Vector3 ctr = eye + dir * CombatConfig.MeleeOffset;
            float r = CombatConfig.MeleeRadius;

            // 눈 → 구 중심 (조준선)
            Line(eye, ctr);

            // 구를 세 평면의 원으로 근사해 그린다(XZ · XY · YZ)
            const int n = 20;
            for (int plane = 0; plane < 3; plane++)
            {
                Vector3 prev = Vector3.zero;
                for (int i = 0; i <= n; i++)
                {
                    float a = i / (float)n * Mathf.PI * 2f;
                    float s = Mathf.Sin(a) * r, t = Mathf.Cos(a) * r;
                    Vector3 cur = ctr + (plane == 0 ? new Vector3(s, 0f, t)
                                       : plane == 1 ? new Vector3(s, t, 0f)
                                                    : new Vector3(0f, s, t));
                    if (i > 0) Line(prev, cur);
                    prev = cur;
                }
            }
        }

        /// <summary>찌르기(런지) 사거리 — 최소·최대 반경을 바닥 원으로.</summary>
        static void LungeRange(in PlayerSim p)
        {
            Vector3 o = new Vector3(p.pos.x, p.pos.y + 0.04f, p.pos.z);
            const int n = 40;
            for (int k = 0; k < 2; k++)
            {
                float rad = k == 0 ? CombatConfig.LungeMinRange : CombatConfig.LungeMaxRange;
                if (rad <= 0.01f) continue;
                // 최소=주황(이 안쪽은 발동 안 함) · 최대=하늘색
                GL.Color(k == 0 ? new Color(1f, 0.6f, 0.15f, 0.75f) : new Color(0.35f, 0.8f, 1f, 0.75f));
                Vector3 prev = Vector3.zero;
                for (int i = 0; i <= n; i++)
                {
                    float a = i / (float)n * Mathf.PI * 2f;
                    Vector3 cur = o + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * rad;
                    if (i > 0) Line(prev, cur);
                    prev = cur;
                }
            }
            // 조준 방향 — 이 방향의 적만 대상이 된다
            GL.Color(new Color(0.35f, 0.8f, 1f, 0.9f));
            Vector3 eye = p.pos + Vector3.up * CombatConfig.MeleeEyeHeight;
            Line(eye, eye + CombatHit.LookDir(p.yaw, p.aimPitch) * CombatConfig.LungeMaxRange);
        }

        /// <summary>평타 판정 부채꼴 — 캡슐이 아니라 콘이다(거리 + 좌우 각도).</summary>
        static void Cone(in PlayerSim p)
        {
            bool active = p.combat.attackPhase == CombatConfig.PhActive;
            Color c = ColCone; c.a = active ? 1f : 0.35f;   // 판정 순간만 진하게
            GL.Color(c);

            float range = CombatConfig.AttackConeRange;
            float half  = CombatConfig.AttackConeHalfAngle * Mathf.Deg2Rad;
            float yawR  = p.yaw * Mathf.Deg2Rad;
            float y     = p.pos.y + SimConfig.PlayerHeight * 0.55f;
            Vector3 o   = new Vector3(p.pos.x, y, p.pos.z);

            const int n = 16;
            Vector3 prev = Vector3.zero;
            for (int i = 0; i <= n; i++)
            {
                float a = yawR - half + (i / (float)n) * half * 2f;
                Vector3 cur = o + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * range;
                if (i == 0 || i == n) Line(o, cur);         // 양 끝 변
                if (i > 0) Line(prev, cur);                 // 호
                prev = cur;
            }
            // 높이 허용 범위 표시(위/아래)
            float tol = CombatConfig.AttackHeightTolerance;
            GL.Color(new Color(c.r, c.g, c.b, c.a * 0.4f));
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 oo = o + Vector3.up * (tol * s);
                Vector3 pv = Vector3.zero;
                for (int i = 0; i <= n; i++)
                {
                    float a = yawR - half + (i / (float)n) * half * 2f;
                    Vector3 cur = oo + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * range;
                    if (i > 0) Line(pv, cur);
                    pv = cur;
                }
            }
        }

        static void Line(Vector3 a, Vector3 b) { GL.Vertex(a); GL.Vertex(b); }

        /// <summary>실제로 그린 캡슐 수(직전 프레임) — "명령은 먹었는데 안 보인다"를 구분하기 위함.</summary>
        public int DrawnLast { get; private set; }

        /// <summary>현재 상태 요약(콘솔 표시용).</summary>
        public string Status =>
            $"몸통 {(showBodies ? "켬" : "끔")} · 평타판정 {(showCone ? "켬" : "끔")}" +
            $"({(CombatConfig.UseSphereMelee ? $"구 {CombatConfig.MeleeReach:0.00}m" : $"부채꼴 {CombatConfig.AttackConeRange:0.00}m")})" +
            $" · 찌르기범위 {(showLunge ? "켬" : "끔")} · 관통 {(xray ? "켬" : "끔")}" +
            $" · 직전 프레임에 그린 캡슐 {DrawnLast}개" +
            (Shader.Find("Hidden/Internal-Colored") == null ? "  <셰이더 없음>" : "");
    }

    /// <summary>Play 시 자동 부착(기본 꺼짐).</summary>
    public static class HitboxViewBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<HitboxView>() == null)
                new GameObject("[HitboxView]").AddComponent<HitboxView>();
        }
    }
}
