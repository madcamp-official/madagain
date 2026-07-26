using UnityEngine;

namespace Game.Sim
{
    /// <summary>
    /// 층이동 도약의 탄도(포물선) 해법. ★ 에디터 프리뷰와 런타임 실행이 <b>같은 함수</b>를 쓴다
    /// — 그래야 "에디터에서 본 궤적"과 "실제 몹이 나는 궤적"이 일치한다.
    ///
    /// 모델: 정점(apex)을 보장하는 탄도.
    ///   apexY = max(출발.y, 착지.y) + clearance   → 항상 확실히 솟는다(하강도 박차고 뛰어내림)
    ///   거기 닿는 수직 발사속도를 역산 → 중력으로 낙하 → 비행시간은 <b>물리로 결정</b>(고정 틱 아님)
    ///   높이차가 있으면 상승시간 ≠ 하강시간 → 자연스러운 비대칭(빠른 낙하)이 공짜로 나온다.
    ///
    /// 결정론: 위치를 누적 적분이 아니라 <b>닫힌 형식</b>으로 tick에서 직접 계산한다.
    /// 같은 (start,end,clearance,gravity)면 언제 어디서 계산해도 동일 → 예측 포크에 안전.
    /// </summary>
    public struct BallisticArc
    {
        public Vector3 start;
        public Vector3 end;
        public Vector3 horizVel;    // 수평 속도(고정)
        public float   launchVy;    // 초기 수직 속도
        public float   gravity;     // 양수
        public int     flightTicks; // 총 비행 틱(>=1)
        public bool    ascend;      // 착지점이 더 높은가(상승 도약)

        public bool IsValid => flightTicks > 0 && gravity > 0f;

        /// <summary>
        /// 비행 시작 후 tick 시점의 위치. tick >= flightTicks면 착지점으로 스냅.
        ///
        /// ★ 궤적의 <b>모양</b>은 탄도 그대로 두고, 그 위를 지나는 <b>속도 배분만</b> 바꾼다(시간 워핑).
        ///   · 상승: 초반을 크게 가속하고 도착하며 느려짐(박차고 오르는 느낌)
        ///   · 하강: 초반은 느리고 <b>막판에 크게 가속</b>(쿵 내리꽂히는 느낌)
        ///   순수 물리라면 등속 수평 + 중력이지만, 그건 밋밋해서 의도적으로 연출을 얹은 것이다.
        ///   닫힌 형식이라 결정론은 그대로 유지된다.
        /// </summary>
        public Vector3 At(int tick)
        {
            if (tick >= flightTicks) return end;
            float x = flightTicks > 0 ? (float)tick / flightTicks : 0f;   // 0..1 정규화 진행도
            float p2 = Mathf.Max(0.05f, ascend ? SimConfig.TraversalAscendShape
                                               : SimConfig.TraversalDescendShape);
            // 상승 = ease-out(초반 빠름), 하강 = ease-in(막판 빠름)
            float u = ascend ? 1f - Mathf.Pow(1f - x, p2) : Mathf.Pow(x, p2);

            float t = u * flightTicks * SimConfig.TickDelta;
            Vector3 p = start + horizVel * t;
            p.y = start.y + launchVy * t - 0.5f * gravity * t * t;
            return p;
        }

        /// <summary>착지 순간의 수직 속도(음수 = 하강). 착지 충격 연출용.</summary>
        public float LandingVy()
        {
            float t = flightTicks * SimConfig.TickDelta;
            return launchVy - gravity * t;
        }
    }

    public static class TraversalBallistics
    {
        /// <summary>
        /// 출발→착지 궤적 해법. clearance는 "두 끝점 중 높은 쪽보다 얼마나 더 솟을지".
        /// gravity <= 0 이면 SimConfig 기본 중력을 쓴다.
        /// </summary>
        public static BallisticArc Solve(Vector3 start, Vector3 end, float clearance, float gravity = 0f)
        {
            var arc = new BallisticArc { start = start, end = end, ascend = end.y > start.y + 0.05f };
            float g = gravity > 0f ? gravity : SimConfig.TraversalGravity;
            arc.gravity = g;

            // 정점: 높은 쪽 + 여유. 최소 여유를 보장해 "솟는 느낌"이 반드시 나오게 한다.
            float c = Mathf.Max(SimConfig.TraversalMinClearance, clearance);
            float apexY = Mathf.Max(start.y, end.y) + c;

            float riseFromStart = Mathf.Max(0.0001f, apexY - start.y);
            float fallToEnd     = Mathf.Max(0.0001f, apexY - end.y);

            arc.launchVy = Mathf.Sqrt(2f * g * riseFromStart);
            float tUp   = arc.launchVy / g;
            float tDown = Mathf.Sqrt(2f * fallToEnd / g);
            float total = tUp + tDown;

            arc.flightTicks = Mathf.Max(1, Mathf.CeilToInt(total / SimConfig.TickDelta));

            // 수평 속도는 "실제 사용할 틱 수"로 나눠야 착지점에 정확히 꽂힌다(반올림 오차 흡수).
            float usedT = arc.flightTicks * SimConfig.TickDelta;
            Vector3 flat = end - start; flat.y = 0f;
            arc.horizVel = usedT > 0f ? flat / usedT : Vector3.zero;
            return arc;
        }

        /// <summary>
        /// 경로 길이(직선) → 주저·멈칫 틱. 가속형 곡선: 짧을 땐 완만, 길수록 급격히 증가.
        /// 데드존 없음 — 작은 단차도 최소값을 가진다.
        /// </summary>
        public static int LengthToTicks(float length, int minTicks, int maxTicks, float refLength, float exponent)
        {
            if (refLength <= 0f) return minTicks;
            float n = Mathf.Clamp01(length / refLength);
            float shaped = Mathf.Pow(n, Mathf.Max(0.01f, exponent));   // exponent>1 = 가속형
            int t = minTicks + Mathf.RoundToInt(shaped * (maxTicks - minTicks));
            return Mathf.Clamp(t, minTicks, maxTicks);
        }

        /// <summary>도약 1회의 총 소요 틱(주저+비행+멈칫). 링크 비용 환산에 쓴다.</summary>
        public static int TotalTicks(int pauseTicks, int flightTicks, int recoveryTicks)
            => Mathf.Max(1, pauseTicks + flightTicks + recoveryTicks);

        /// <summary>
        /// 링크 비용 = "그 시간 동안 걸었다면 갔을 거리". NavMesh·그래프가 걷기와 도약을
        /// 같은 저울로 비교하게 만든다(안 하면 몹이 아무 데서나 뛰어내려 경직으로 굳는다).
        /// </summary>
        public static float CostDistance(int totalTicks, float moveSpeed)
            => totalTicks * SimConfig.TickDelta * Mathf.Max(0.01f, moveSpeed);
    }
}
