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

        /// <summary>매 프레임 마우스 델타로 커서를 옮기고, 새 점에 닿았으면 그 점 인덱스를 반환(없으면 -1).</summary>
        public int Move(Vector2 mouseDelta)
        {
            CursorPos += mouseDelta * sensitivity;   // 클램프 없음 — 격자 밖으로 자유롭게

            for (int d = 0; d < PatternGraph.DotCount; d++)
            {
                if (d == CurrentDot) continue;
                if (Vector2.Distance(CursorPos, PatternGraph.Pos[d]) <= hitRadius)
                    return d;
            }
            return -1;
        }

        /// <summary>히트된 점을 확정(전진).</summary>
        public void Advance(int dot)
        {
            CurrentDot = dot;
            StrokeCount++;
            PlayerDots.Add(dot);
        }
    }
}
