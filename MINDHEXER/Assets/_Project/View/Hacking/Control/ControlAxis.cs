using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 외부 조종 축 하나의 설정 + 런타임 상태. 월드 방향에 고정이고(§6.2), 슬롯 순서대로
    /// 입력에 배정된다 — 슬롯0 = 좌/우클릭, 슬롯1 = Shift+좌/우클릭 (§2.5).
    /// </summary>
    [System.Serializable]
    public class ControlAxis
    {
        [Tooltip("이 축의 월드 방향(레벨에 고정). 정규화는 자동.")]
        public Vector3 world = Vector3.forward;

        [Tooltip("시작 위치 기준 이동 가능 범위 최소(m, 보통 음수).")]
        public float travelMin = -6f;

        [Tooltip("시작 위치 기준 이동 가능 범위 최대(m).")]
        public float travelMax = 6f;

        [Tooltip("홀드 시 등속 크립 속도(m/s). 아날로그 조종.")]
        public float moveSpeed = 3f;

        [Tooltip("플릭 1회당 이동 거리(m). ※임시 — 원안은 스냅 격자 N분할(§6.2).")]
        public float flickDistance = 50f;

        [Tooltip("플릭 이동 속도(m/s). 크게 잡아야 '확' 가는 게 눈에 보인다.")]
        public float flickSpeed = 300f;

        [System.NonSerialized] public float Offset;
        [System.NonSerialized] public float FlickTarget;
        [System.NonSerialized] public bool  Flicking;

        public Vector3 Dir => world.sqrMagnitude > 0.0001f ? world.normalized : Vector3.forward;

        /// <summary>조종 입력 반영. analog=-1~+1 등속, flick=-1/0/+1 임펄스. 부호는 보정된 상태로 들어온다.</summary>
        public void Drive(float analog, int flick)
        {
            if (flick != 0)
            {
                FlickTarget = Mathf.Clamp(Offset + flick * flickDistance, travelMin, travelMax);
                Flicking = true;
                return;
            }

            // 플릭 중에는 아날로그를 무시한다. 더블클릭 = 클릭 2회라 그 직후에도 버튼이 눌려 있어
            // 아날로그가 살아 있는데, 여기서 플릭을 끊으면 시작하자마자 취소돼 버린다.
            if (Flicking) return;

            if (!Mathf.Approximately(analog, 0f))
                Offset = Mathf.Clamp(Offset + analog * moveSpeed * Time.deltaTime, travelMin, travelMax);
        }

        /// <summary>매 프레임 플릭 보간.</summary>
        public void Step(float dt)
        {
            if (!Flicking) return;
            Offset = Mathf.MoveTowards(Offset, FlickTarget, flickSpeed * dt);
            if (Mathf.Approximately(Offset, FlickTarget)) Flicking = false;
        }
    }
}
