using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 레일 캐리어 — 레일 위 이동. 레일 방향은 레벨마다 임의(벽 세로/바닥/천장/대각). (기초_설계안 §6.2·§6.1)
    ///
    /// 조종(§2.5): 좌클릭 홀드 = 슬롯0 −방향 / 우클릭 홀드 = + / 더블클릭 = 플릭.
    /// 슬롯1이 있으면 Shift+좌클릭 = + / Shift+우클릭 = −.
    /// 부호는 <see cref="HackDriver"/>가 화면 기준으로 보정하므로 여기선 그대로 쓴다.
    ///
    /// ※ 설계 원안의 레일은 1축이다(§6.2). 지금 기본값이 2축(월드 Z·Y)인 것은
    ///   Shift 슬롯 조작을 시험하기 위한 <b>테스트 설정</b>이다.
    /// </summary>
    [DisallowMultipleComponent]
    public class RailCarrier : MonoBehaviour, IExternalControl
    {
        [Tooltip("조종 축(최대 2). 슬롯0 = 좌/우클릭, 슬롯1 = Shift+좌/우클릭.")]
        // X·Z 둘 다 수평이라, 어느 쪽이 화면 가로로 보이는지가 <b>보는 방향에 따라 완전히 뒤바뀐다</b>
        // → 시점 기준 슬롯 배정(FreezeControlMapping)을 검증하기에 가장 좋은 조합.
        public ControlAxis[] axes =
        {
            new ControlAxis { world = Vector3.right },     // 월드 X
            new ControlAxis { world = Vector3.forward },   // 월드 Z
        };

        Vector3 _start;

        public int AxisCount => axes != null ? Mathf.Min(axes.Length, 2) : 0;
        public Vector3 AxisWorld(int slot) => axes[slot].Dir;

        /// <summary>레일은 양 끝이 대칭이라 "보이는 대로" 움직여야 한다 → 화면 기준 부호 보정을 쓴다.</summary>
        public bool ScreenRelativeSign => true;

        /// <summary>시작 위치=0, 양 끝=±1. 범위가 비대칭이어도 각 방향을 따로 정규화한다.</summary>
        public float GetNormalized(int slot)
        {
            if (slot < 0 || slot >= AxisCount) return 0f;
            ControlAxis a = axes[slot];
            if (a.Offset >= 0f) return a.travelMax > 1e-4f ? Mathf.Clamp01(a.Offset / a.travelMax) : 0f;
            return a.travelMin < -1e-4f ? -Mathf.Clamp01(a.Offset / a.travelMin) : 0f;
        }

        void Awake() => _start = transform.position;

        public void Drive(int slot, float analog, int flick)
        {
            if (slot < 0 || slot >= AxisCount) return;
            axes[slot].Drive(analog, flick);
        }

        void Update()
        {
            Vector3 p = _start;
            for (int i = 0; i < AxisCount; i++)
            {
                axes[i].Step(Time.deltaTime);
                p += axes[i].Dir * axes[i].Offset;
            }
            transform.position = p;
        }

        // 이동 범위를 씬 뷰에서 눈으로 확인(에디터 전용).
        void OnDrawGizmosSelected()
        {
            if (axes == null) return;
            Vector3 a = Application.isPlaying ? _start : transform.position;
            for (int i = 0; i < Mathf.Min(axes.Length, 2); i++)
            {
                Gizmos.color = i == 0 ? Color.cyan : new Color(1f, 0.6f, 0.2f);
                Gizmos.DrawLine(a + axes[i].Dir * axes[i].travelMin, a + axes[i].Dir * axes[i].travelMax);
            }
        }
    }
}
