using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 휴대폰 패턴 락과 동일한 방식 — <b>절대 위치</b> 기반. 방향/각도 판정 없음.
    /// 마우스 델타를 정규화 좌표(0~1, <see cref="PatternGraph.Pos"/>와 같은 공간)로 누적해
    /// "가상 손가락 위치"(<see cref="CursorPos"/>)를 만들고, 그 위치가 어떤 점의 히트 반경 안에
    /// 들어오면 그 점으로 커밋한다. (기초_설계안 §2.4)
    /// </summary>
    public class PatternInput
    {
        /// <summary>마우스 px → 정규화 좌표 변환 배율. PatternMinigame이 인스펙터 값으로 주입.</summary>
        public float sensitivity = 1f / 300f;

        /// <summary>커서가 이 반경 안에 들어오면 그 점으로 커밋. PatternMinigame이 인스펙터 값으로 주입.</summary>
        public float hitRadius = 0.16f;

        /// <summary>
        /// 가상 손가락 위치(정규화 좌표 — 격자는 0~1이지만 <b>범위 제한 없음</b>).
        /// 휴대폰 패턴처럼 손가락이 격자 사각형 밖으로 자유롭게 나갈 수 있다. 매 프레임 항상 유효 — UI가 그대로 그린다.
        /// </summary>
        public Vector2 CursorPos { get; private set; } = PatternGraph.Pos[PatternGraph.StartDot];

        public int CurrentDot { get; private set; } = PatternGraph.StartDot;
        public int StrokeCount { get; private set; }

        /// <summary>플레이어가 실제로 지난 점 시퀀스(UI 라이브 트레이스용). [0]=시작.</summary>
        public readonly List<int> PlayerDots = new List<int> { PatternGraph.StartDot };

        public void Reset()
        {
            CurrentDot = PatternGraph.StartDot;
            CursorPos = PatternGraph.Pos[PatternGraph.StartDot];
            StrokeCount = 0;
            PlayerDots.Clear();
            PlayerDots.Add(PatternGraph.StartDot);
        }

        /// <summary>
        /// 커서를 마지막으로 닿은 점으로 되돌린다. <b>손가락을 뗐을 때</b> 호출한다.
        ///
        /// <para>클러치(뗐다 다시 잡기)로 이어 그릴 때, 뗀 지점이 점에서 벗어나 있으면 그 오차를
        /// 안고 다시 시작하게 된다. 화면이 안 보이는 컨트롤러에선 눈으로 보정할 수가 없어
        /// 오차가 계속 쌓인다. 뗄 때마다 점으로 되돌리면 다시 잡는 위치와 무관하게 항상
        /// 정확한 지점에서 출발한다.</para>
        /// </summary>
        public void SnapCursorToCurrent() => CursorPos = PatternGraph.Pos[CurrentDot];

        /// <summary>
        /// 매 프레임 델타로 커서를 옮기고, 새 점에 닿았으면 그 점 인덱스를 반환(없으면 -1).
        ///
        /// <para>★ <b>이동한 '선분'으로 검사한다. 도착한 '점'만 보면 안 된다.</b>
        /// 컨트롤러가 <b>30Hz</b>로 보내는데 화면은 60fps라, 빠르게 그으면 한 프레임 이동량이
        /// 히트 반경(<see cref="hitRadius"/> 0.16)보다 쉽게 커진다. 그러면 점을 <b>뚫고 지나가</b>
        /// 아무 일도 일어나지 않는다(터널링). 격자가 2×2라 점 간격이 넓어 더 잘 난다.</para>
        ///
        /// <para>여러 점이 한 프레임에 걸리면 <b>먼저 지난 것</b>을 반환한다 — 그래야 획 순서가
        /// 실제 손 움직임과 일치한다.</para>
        /// </summary>
        public int Move(Vector2 mouseDelta)
        {
            Vector2 from = CursorPos;
            CursorPos += mouseDelta * sensitivity;   // 클램프 없음 — 격자 밖으로 자유롭게

            int best = -1;
            float bestT = float.MaxValue;

            for (int d = 0; d < PatternGraph.DotCount; d++)
            {
                if (d == CurrentDot) continue;

                float t;
                if (!SweepHit(from, CursorPos, PatternGraph.Pos[d], hitRadius, out t)) continue;
                if (t >= bestT) continue;

                bestT = t;
                best = d;
            }
            return best;
        }

        /// <summary>
        /// 선분 <paramref name="a"/>→<paramref name="b"/>가 원(<paramref name="c"/>, <paramref name="r"/>)에
        /// 처음 들어가는 지점의 매개변수 t(0~1). 안 닿으면 false.
        /// </summary>
        static bool SweepHit(Vector2 a, Vector2 b, Vector2 c, float r, out float t)
        {
            t = 0f;

            Vector2 ac = a - c;
            if (ac.sqrMagnitude <= r * r) return true;   // 시작부터 안에 있다

            Vector2 d = b - a;
            float dd = Vector2.Dot(d, d);
            if (dd < 1e-12f) return false;               // 안 움직였고, 위에서 밖이라고 판정됐다

            // |a + t·d − c|² = r² 을 t에 대해 푼다.
            float bq = 2f * Vector2.Dot(ac, d);
            float cq = Vector2.Dot(ac, ac) - r * r;
            float disc = bq * bq - 4f * dd * cq;
            if (disc < 0f) return false;

            float root = (-bq - Mathf.Sqrt(disc)) / (2f * dd);   // 작은 해 = 처음 들어가는 지점
            if (root < 0f || root > 1f) return false;

            t = root;
            return true;
        }

        /// <summary>
        /// 히트된 점을 확정(전진). <b>커서를 그 점에 붙인다.</b>
        ///
        /// <para>예전엔 커서가 자유 위치에 남았다. 화면을 보고 마우스로 그릴 땐 눈으로 보정되니
        /// 문제가 없었는데, VR에선 컨트롤러 화면이 안 보여 손 감각만으로 그린다. 그러면 히트 반경
        /// 안 어디에서 커밋됐는지에 따라 오차가 남고, 획을 거듭할수록 <b>누적</b>되어 손 위치와
        /// 격자가 어긋난다. 점에 붙이면 매 획마다 오차가 0으로 초기화된다.</para>
        /// </summary>
        public void Advance(int dot)
        {
            CurrentDot = dot;
            CursorPos = PatternGraph.Pos[dot];
            StrokeCount++;
            PlayerDots.Add(dot);
        }
    }
}
