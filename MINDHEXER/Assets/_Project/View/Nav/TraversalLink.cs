using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>마커 종류. 하강은 항상 전 지상몹 허용, 변수는 "상승 권한"뿐이라 3종으로 줄어든다.</summary>
    public enum TraversalLinkKind : byte
    {
        Normal = 0,             // ① 일반      — 하강·상승 모두 전 지상몹
        AscendRestricted = 1,   // ② 상승제한  — 하강 전부, 상승은 Traversal 특성만
        DescendOnly = 2,        // ③ 하강전용  — 하강만(아무도 못 올라옴)
        SpawnDrop = 3,          // ④ 스폰낙하  — 평상시 길찾기용 아님. 몹이 Fan에서 스폰되는 순간
                                //              곧바로 타는 하강 전용 링크(높은 곳=Fan 입 → 낮은 곳=착지점).
    }

    /// <summary>
    /// 층이동 마커. ★ 층 전환의 <b>유일한 권위</b>(NavMesh는 연속 표면=경사로까지만 담당).
    /// 씬에 손으로 배치하고, Bake가 이걸 읽어 NavMeshLink(평상시) + 그래프 링크(예측)를 만든다.
    ///
    /// 표시·길이는 <b>직선</b>, 실제 이동은 <b>정점 보장 탄도</b>(TraversalBallistics).
    /// 주저·멈칫은 직선 길이 비례(데드존 없음 — 짧아도 최소값).
    ///
    /// 좌표: 이 오브젝트의 transform = A점, endOffset(로컬) = B점.
    /// 오브젝트를 옮기면 링크 전체가 따라오고, 핸들로 B점만 따로 조정한다.
    /// </summary>
    [DisallowMultipleComponent]
    public class TraversalLink : MonoBehaviour
    {
        [Header("종류")]
        public TraversalLinkKind kind = TraversalLinkKind.Normal;

        [Header("끝점 (A = 이 오브젝트 위치, B = 아래 오프셋)")]
        public Vector3 endOffset = new Vector3(0f, -3f, 6f);

        [Header("궤적")]
        [Tooltip("체크 해제하면 clearance를 직접 지정(수동)")]
        public bool  clearanceAuto = true;
        [Tooltip("두 끝점 중 높은 쪽보다 얼마나 더 솟을지(m)")]
        public float clearance = 1.5f;
        [Tooltip("0이면 SimConfig.TraversalGravity 사용")]
        public float gravity = 0f;

        [Header("주저·멈칫 (음수 = 길이 비례 자동)")]
        public int pauseTicksOverride   = -1;
        public int recoverTicksOverride = -1;

        [Header("착지 슬롯 (동시 도약 혼잡 방지)")]
        public bool  slotsAuto  = true;
        public int   slotCount  = 3;
        public float slotSpread = 1.4f;

        [Header("검증 기준 몹 크기")]
        [Tooltip("대형몹(3배)도 이 링크를 지나가야 하면 체크. 실내처럼 층간이 낮으면 대부분 무효가 되므로 기본은 해제")]
        public bool validateForLargeMobs = false;

        [Header("기즈모")]
        [Tooltip("궤적 위에 몹 캡슐 크기를 그려 천장 여유를 확인")]
        public bool showCapsules = true;

        // ── 파생값 ──
        public Vector3 PointA => transform.position;
        public Vector3 PointB => transform.TransformPoint(endOffset);

        /// <summary>높은 쪽 / 낮은 쪽. 하강 = High→Low, 상승 = Low→High.</summary>
        public Vector3 High => PointA.y >= PointB.y ? PointA : PointB;
        public Vector3 Low  => PointA.y >= PointB.y ? PointB : PointA;

        public float Length => Vector3.Distance(PointA, PointB);

        /// <summary>상승(낮은 곳 → 높은 곳)이 허용되는가. 종류로만 결정된다.
        /// 하강전용·스폰낙하는 상승 불가(스폰낙하는 스폰 순간 아래로만).</summary>
        public bool AscendAllowed => kind != TraversalLinkKind.DescendOnly
                                  && kind != TraversalLinkKind.SpawnDrop;
        /// <summary>스폰 전용 링크인가 — 평상시 길찾기 그래프에서는 제외해야 한다.</summary>
        public bool IsSpawnDrop => kind == TraversalLinkKind.SpawnDrop;
        /// <summary>상승이 Traversal 특성 몹으로 제한되는가.</summary>
        public bool AscendTraversalOnly => kind == TraversalLinkKind.AscendRestricted;

        /// <summary>저작자가 원한 clearance(자동=길이 비례 / 수동=지정값). 충돌은 아직 고려 안 함.</summary>
        public float DesiredClearance
        {
            get
            {
                if (!clearanceAuto) return Mathf.Max(SimConfig.TraversalMinClearance, clearance);
                float auto = Length * SimConfig.TraversalClearanceRatio;
                return Mathf.Clamp(auto, SimConfig.TraversalMinClearance, SimConfig.TraversalMaxClearance);
            }
        }

        /// <summary>
        /// 실제로 쓰는 clearance = min(희망값, 공간이 허용하는 값).
        /// 궤적이 구조물을 뚫으면 <b>뚫리지 않는 최대치까지 자동으로 낮춘다.</b>
        /// 최소치로도 안 되면 <see cref="IsBlocked"/>가 true가 되고 마커가 무효(빨강)로 표시된다.
        /// </summary>
        public float EffectiveClearance { get { EnsureFit(); return fittedClearance; } }

        /// <summary>최소 clearance로도 궤적이 막히는가(마커 무효).</summary>
        public bool IsBlocked { get { EnsureFit(); return blocked; } }
        /// <summary>막힌 지점(표시용).</summary>
        public Vector3 BlockPoint { get { EnsureFit(); return blockPoint; } }

        // ── 충돌 자동 맞춤 캐시 (에디터에서 매 프레임 물리질의를 반복하지 않도록) ──
        [System.NonSerialized] float fittedClearance;
        [System.NonSerialized] bool  blocked;
        [System.NonSerialized] Vector3 blockPoint;
        [System.NonSerialized] int   fitKey = int.MinValue;

        /// <summary>입력이 바뀌었을 때만 다시 맞춘다.</summary>
        void EnsureFit()
        {
            int key = FitKey();
            if (key == fitKey) return;
            fitKey = key;
            fittedClearance = FitClearance(out blocked, out blockPoint);
        }

        int FitKey()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + PointA.GetHashCode();
                h = h * 31 + PointB.GetHashCode();
                h = h * 31 + DesiredClearance.GetHashCode();
                h = h * 31 + gravity.GetHashCode();
                h = h * 31 + (int)kind;
                h = h * 31 + (validateForLargeMobs ? 1 : 0);
                return h;
            }
        }

        /// <summary>희망 clearance부터 내려가며 "뚫리지 않는 최대치"를 찾는다(이분 탐색).</summary>
        float FitClearance(out bool isBlocked, out Vector3 hitAt)
        {
            float radius = ValidateRadius, height = ValidateHeight;

            // 스폰낙하: 몹이 천장에서 '떨어지는' 링크다. 서 있는 캡슐로 아치를 쓸면 시작점(천장 바로 아래)이
            // 항상 천장·팬에 걸려 무조건 무효가 된다. 낙하 중엔 서 있을 필요가 없으므로,
            // 착지점(Low)에 캡슐이 들어갈 자리만 확인한다(벽·지오메트리 속 스폰 방지).
            if (kind == TraversalLinkKind.SpawnDrop)
            {
                // 착지점에 캡슐을 세워 '벽에 낀 스폰'만 걸러낸다. 단 바닥에서 살짝 띄운다 —
                // 착지점은 바닥 표면이라 캡슐 밑구가 바닥을 그레이징해 무조건 겹침으로 잡히기 때문.
                Vector3 feet = Low + Vector3.up * 0.12f;
                Vector3 b = feet + Vector3.up * radius;
                Vector3 t = feet + Vector3.up * Mathf.Max(radius, height - radius);
                isBlocked = Physics.CheckCapsule(b, t, radius * 0.9f,
                                                 Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                hitAt = Low;
                return 0f;   // 낙하는 상승 아치가 없다
            }

            float desired = DesiredClearance;
            float min = SimConfig.TraversalMinClearance;

            if (SweepClear(desired, radius, height, out hitAt)) { isBlocked = false; return desired; }
            if (!SweepClear(min, radius, height, out hitAt)) { isBlocked = true; return min; }   // 최소로도 막힘 → 무효

            float lo = min, hi = desired;
            for (int i = 0; i < 8; i++)   // 8회면 충분히 수렴
            {
                float mid = (lo + hi) * 0.5f;
                if (SweepClear(mid, radius, height, out _)) lo = mid; else hi = mid;
            }
            isBlocked = false; hitAt = Vector3.zero;
            return lo;
        }

        /// <summary>
        /// 주어진 clearance의 궤적을 <b>몹 캡슐로 쓸어</b> 정적 지오메트리와 충돌하는지 검사.
        /// 양 끝은 바닥에 붙어 있어 반드시 걸리므로 앞뒤 일부 구간은 건너뛴다.
        /// 하강·상승 궤적을 모두 본다(양방향 마커는 둘 다 안전해야 한다).
        /// </summary>
        public bool SweepClear(float testClearance, float radius, float height, out Vector3 hitAt)
        {
            hitAt = Vector3.zero;
            if (!SweepArcClear(TraversalBallistics.Solve(High, Low, testClearance, gravity), radius, height, out hitAt))
                return false;
            if (AscendAllowed &&
                !SweepArcClear(TraversalBallistics.Solve(Low, High, testClearance, gravity), radius, height, out hitAt))
                return false;
            return true;
        }

        /// <summary>
        /// 궤적 충돌 검사. ★ 점만 찍어보면 <b>점 사이의 얇은 벽을 그냥 지나친다</b> —
        /// 그래서 각 구간을 <b>캡슐 스윕</b>으로 쓸어 검사한다(연속 검사).
        /// 양 끝은 바닥에 붙어 있어 반드시 걸리므로 일부만 건너뛴다.
        /// </summary>
        static bool SweepArcClear(BallisticArc arc, float radius, float height, out Vector3 hitAt)
        {
            hitAt = Vector3.zero;
            if (!arc.IsValid) return true;

            const int Samples = 32;        // 촘촘하게
            const float EndSkip = 0.06f;   // 끝 6%만 제외(이전 12%는 너무 관대해 벽을 놓쳤다)
            float halfH = Mathf.Max(radius, height - radius);

            Vector3 prev = Vector3.zero; bool hasPrev = false;
            for (int i = 0; i <= Samples; i++)
            {
                float f = (float)i / Samples;
                if (f < EndSkip || f > 1f - EndSkip) { hasPrev = false; continue; }

                Vector3 p = arc.At(Mathf.RoundToInt(arc.flightTicks * f));

                // 그 자리에 캡슐이 들어가는가
                if (Physics.CheckCapsule(p + Vector3.up * radius, p + Vector3.up * halfH, radius,
                                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                { hitAt = p; return false; }

                // 직전 표본 → 지금 표본 구간을 쓸어서 사이에 낀 벽까지 잡는다
                if (hasPrev)
                {
                    Vector3 seg = p - prev;
                    float dist = seg.magnitude;
                    if (dist > 1e-4f &&
                        Physics.CapsuleCast(prev + Vector3.up * radius, prev + Vector3.up * halfH, radius,
                                            seg / dist, out RaycastHit hit, dist,
                                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    { hitAt = hit.point; return false; }
                }
                prev = p; hasPrev = true;
            }
            return true;
        }

        /// <summary>
        /// 검증에 쓸 캡슐 크기.
        /// 기본은 <b>일반몹</b>(돌진몹 반경 포함) — 대형몹(3배, 높이 4.3m)으로 검증하면 실내처럼
        /// 층간이 낮은 맵에서는 거의 모든 링크가 무효가 되어 층이동 자체가 사라진다.
        /// 대형몹이 반드시 지나가야 하는 링크만 <see cref="validateForLargeMobs"/>를 켠다.
        /// </summary>
        public float ValidateRadius => validateForLargeMobs
            ? SimConfig.EnemyRadius * SimConfig.EnemyLargeScale
            : SimConfig.EnemyRadius * SimConfig.EnemyNormalScale * AIConfig.ChargeRadiusMul;

        public float ValidateHeight => SimConfig.EnemyHeight *
            (validateForLargeMobs ? SimConfig.EnemyLargeScale : SimConfig.EnemyNormalScale);

        /// <summary>슬롯이 쓸 수 있는지 — 아래 바닥이 있고 캡슐이 안 박혀야 한다.</summary>
        public bool SlotUsable(Vector3 slot)
        {
            if (!Physics.Raycast(slot + Vector3.up * 1.5f, Vector3.down, out _, 4f,
                                 Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return false;
            float r = ValidateRadius, h = ValidateHeight;
            Vector3 bottom = slot + Vector3.up * r;
            Vector3 top    = slot + Vector3.up * Mathf.Max(r, h - r);
            return !Physics.CheckCapsule(bottom, top, r, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        public int PauseTicks => pauseTicksOverride >= 0 ? pauseTicksOverride
            : TraversalBallistics.LengthToTicks(Length, SimConfig.TraversalPauseMin, SimConfig.TraversalPauseMax,
                                                SimConfig.TraversalLengthRef, SimConfig.TraversalLengthExp);

        public int RecoverTicks => recoverTicksOverride >= 0 ? recoverTicksOverride
            : TraversalBallistics.LengthToTicks(Length, SimConfig.TraversalRecoverMin, SimConfig.TraversalRecoverMax,
                                                SimConfig.TraversalLengthRef, SimConfig.TraversalLengthExp);

        /// <summary>하강 궤적(High→Low). 항상 존재한다.</summary>
        public BallisticArc DescendArc => TraversalBallistics.Solve(High, Low, EffectiveClearance, gravity);
        /// <summary>상승 궤적(Low→High). 종류가 허용할 때만 의미 있다.</summary>
        public BallisticArc AscendArc  => TraversalBallistics.Solve(Low, High, EffectiveClearance, gravity);

        /// <summary>착지 슬롯 위치들(검증 전). 착지점 둘레 링에 균등 배치.</summary>
        public void GetSlots(Vector3 landing, System.Collections.Generic.List<Vector3> outSlots)
        {
            outSlots.Clear();
            int n = Mathf.Clamp(slotsAuto ? 3 : slotCount, 1, SimConfig.TraversalSlotMax);
            if (n == 1) { outSlots.Add(landing); return; }
            float r = SlotSpread;
            for (int i = 0; i < n; i++)
            {
                float ang = (360f / n) * i * Mathf.Deg2Rad;
                outSlots.Add(landing + new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * r);
            }
        }

        /// <summary>슬롯 간격 — 자동이면 허용 몹 중 최대 반경 기준으로 겹치지 않게 잡는다.</summary>
        public float SlotSpread => slotsAuto
            ? Mathf.Max(0.8f, ValidateRadius * SimConfig.TraversalSlotGapMul)
            : slotSpread;

        /// <summary>검증을 통과한(바닥 있고 캡슐 안 박히는) 슬롯 개수. 못 쓰는 건 자동 폐기.</summary>
        public int UsableSlotCount(Vector3 landing)
        {
            GetSlots(landing, slotBuf);
            int n = 0;
            foreach (var s in slotBuf) if (SlotUsable(s)) n++;
            return Mathf.Max(1, n);   // 전부 막혀도 착지점 자체는 쓴다
        }

        // ── 기즈모 ──
        static readonly System.Collections.Generic.List<Vector3> slotBuf = new System.Collections.Generic.List<Vector3>();

        void OnDrawGizmos() => Draw(false);
        void OnDrawGizmosSelected() => Draw(true);

        void Draw(bool selected)
        {
            Vector3 a = PointA, b = PointB;
            if ((a - b).sqrMagnitude < 1e-6f) return;

            // 직선(저작 표현) — 길이의 기준
            Gizmos.color = new Color(0.6f, 0.6f, 0.6f, selected ? 0.9f : 0.45f);
            Gizmos.DrawLine(a, b);

            // 끝점
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.9f);
            Gizmos.DrawWireSphere(a, 0.25f);
            Gizmos.DrawWireSphere(b, 0.25f);

            bool bad = IsBlocked;   // 최소 clearance로도 뚫림 → 무효(빨강)

            // 하강 궤적(항상 허용)
            DrawArc(DescendArc, bad ? Color.red : KindColor(true), selected);
            // 상승 궤적(허용 시)
            if (AscendAllowed) DrawArc(AscendArc, bad ? Color.red : KindColor(false), selected);

            if (bad)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(BlockPoint, 0.5f);
                Gizmos.DrawLine(BlockPoint + Vector3.up * 1.5f, BlockPoint);
            }

            if (!selected) return;

            // 캡슐 실루엣 — 기본 꺼둠(대형몹 기준이라 크고 시야를 가림). 필요할 때만 켠다.
            if (showCapsules)
                DrawCapsules(DescendArc, bad ? new Color(1f, 0.3f, 0.3f, 0.5f) : new Color(1f, 1f, 1f, 0.35f),
                             ValidateRadius, ValidateHeight);

            // 착지 슬롯 — 유효=초록, 못 쓰는 슬롯=빨강(자동 폐기 대상)
            DrawSlots(Low);
            if (AscendAllowed) DrawSlots(High);
        }

        void DrawSlots(Vector3 landing)
        {
            GetSlots(landing, slotBuf);
            foreach (var s in slotBuf)
            {
                bool ok = SlotUsable(s);
                Gizmos.color = ok ? new Color(0.4f, 1f, 0.6f, 0.85f) : new Color(1f, 0.35f, 0.35f, 0.85f);
                Gizmos.DrawWireSphere(s, 0.28f);
            }
        }

        static void DrawCapsules(BallisticArc arc, Color c, float r, float h)
        {
            if (!arc.IsValid) return;
            Gizmos.color = c;
            const int N = 5;
            for (int i = 1; i < N; i++)
            {
                Vector3 p = arc.At(Mathf.RoundToInt((float)arc.flightTicks * i / N));
                Gizmos.DrawWireSphere(p + Vector3.up * r, r);
                Gizmos.DrawWireSphere(p + Vector3.up * Mathf.Max(r, h - r), r);
            }
        }

        Color KindColor(bool descend)
        {
            if (descend) return new Color(0.35f, 0.95f, 0.45f, 0.95f);          // 하강 = 초록(항상 전부)
            switch (kind)
            {
                case TraversalLinkKind.AscendRestricted:
                    return new Color(1f, 0.75f, 0.15f, 0.95f);                  // 상승제한 = 주황
                default:
                    return new Color(0.35f, 0.8f, 1f, 0.95f);                   // 일반 상승 = 파랑
            }
        }

        static void DrawArc(BallisticArc arc, Color c, bool selected)
        {
            if (!arc.IsValid) return;
            Gizmos.color = c;
            int seg = selected ? 28 : 14;
            Vector3 prev = arc.At(0);
            for (int i = 1; i <= seg; i++)
            {
                int tick = Mathf.RoundToInt((float)arc.flightTicks * i / seg);
                Vector3 cur = arc.At(tick);
                Gizmos.DrawLine(prev, cur);
                prev = cur;
            }
        }
    }
}
