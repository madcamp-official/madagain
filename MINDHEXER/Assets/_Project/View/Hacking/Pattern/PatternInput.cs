using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 방향 스트로크 입력 → K4 변 커밋. 마우스 방향을 누적하다 임계·정렬 넘으면 그 이웃을 "후보"로 감지.
    /// 실제 전진(Advance)은 판정(맞으면)한 뒤 오케스트레이터가 호출 → 오답 스냅 모드 지원. (기초_설계안 §2.4)
    /// </summary>
    public class PatternInput
    {
        public int CurrentDot { get; private set; } = PatternGraph.StartDot;
        public int StrokeCount { get; private set; }
        public int PendingNeighbor { get; private set; } = -1;   // 러버밴드 미리보기
        public Vector2 Accum { get; private set; }
        public float CommitThreshold = 40f;                       // 누적 크기 임계(px, 튜닝)

        /// <summary>플레이어가 실제로 지난 점 시퀀스(UI 라이브 트레이스용). [0]=시작.</summary>
        public readonly List<int> PlayerDots = new List<int> { PatternGraph.StartDot };

        public void Reset()
        {
            CurrentDot = PatternGraph.StartDot;
            StrokeCount = 0;
            PendingNeighbor = -1;
            Accum = Vector2.zero;
            PlayerDots.Clear();
            PlayerDots.Add(PatternGraph.StartDot);
        }

        /// <summary>이번 프레임 방향(delta) 누적. 임계·정렬 넘으면 후보 이웃 반환+리셋. 아니면 -1. (전진 안 함)</summary>
        public int Detect(Vector2 strokeDelta)
        {
            Accum += strokeDelta;
            PendingNeighbor = PatternGraph.DirectionToNeighbor(CurrentDot, Accum);
            if (PendingNeighbor >= 0 && Accum.magnitude >= CommitThreshold)
            {
                int cand = PendingNeighbor;
                Accum = Vector2.zero;
                return cand;
            }
            return -1;
        }

        /// <summary>후보를 확정(맞은 획) → 전진.</summary>
        public void Advance(int neighbor)
        {
            CurrentDot = neighbor;
            StrokeCount++;
            PendingNeighbor = -1;
            PlayerDots.Add(neighbor);
        }
    }
}
