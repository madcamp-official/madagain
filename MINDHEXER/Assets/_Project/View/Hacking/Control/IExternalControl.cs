using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 외부 조종형 대상(레일 캐리어·갠트리·피스톤·프레스)이 구현하는 조종 계약. (기초_설계안 §6.2)
    ///
    /// 축은 <b>월드 좌표에 고정</b>이고(레벨에 박힘, 플레이어 위치와 무관), 최대 2개다.
    /// 어떤 물리 입력이 어느 축에 붙는지는 <see cref="HackDriver"/>가 정한다(§2.5 슬롯 표) —
    /// 대상은 "내 축이 월드에서 어느 방향인가"와 "그 축을 이만큼 움직여라"만 안다.
    /// </summary>
    public interface IExternalControl
    {
        /// <summary>이 대상이 쓰는 축 개수(1~2). 레일=1, 갠트리=2.</summary>
        int AxisCount { get; }

        /// <summary>슬롯의 월드 방향(정규화). 화면 투영으로 입력 부호를 정하는 데 쓴다.</summary>
        Vector3 AxisWorld(int slot);

        /// <summary>
        /// 입력 부호를 <b>화면 기준으로 보정할지</b>. (레일·갠트리·회전판 = true)
        ///
        /// <para>true면 <see cref="HackDriver"/>가 해킹 성공 순간의 시점으로 "우클릭 = 화면 오른쪽"이
        /// 되도록 부호를 뒤집는다 — 월드 축이 어느 쪽을 향하든 보이는 대로 움직여야 하는 물체용.</para>
        ///
        /// <para>false면 부호를 건드리지 않고 원 입력을 그대로 넘긴다. 피스톤·프레스처럼 <b>축의 두 끝이
        /// 대칭이 아닌</b> 물체는 "화면 오른쪽"이 아니라 "뻗는다/집어넣는다"가 의미의 전부라,
        /// 어느 방향에서 봐도 <b>좌클릭 = 신장</b>으로 고정돼야 조작이 예측 가능하다.</para>
        /// </summary>
        bool ScreenRelativeSign { get; }

        /// <summary>
        /// 이동 범위 기준 현재 위치(−1~+1). 앵커=0, 각 방향 한계=±1.
        ///
        /// <para>VR의 <b>위치 제어</b>가 오차를 구하는 데 쓴다 — 손 변위로 목표 위치를 만들고
        /// 그 차이를 <see cref="Drive"/>의 analog(속도 지령)로 바꿔 넣는 서보 구조다.
        /// 이게 없으면 대상 타입을 검사해야 하고, 부품이 늘 때마다 깨진다.</para>
        /// </summary>
        float GetNormalized(int slot);

        /// <summary>
        /// 이번 프레임 조종 입력. <paramref name="analog"/>는 -1~+1 등속 크립(홀드),
        /// <paramref name="flick"/>은 -1/0/+1 임펄스(더블클릭). 부호는 이미 화면 기준으로 보정돼 있다.
        /// </summary>
        void Drive(int slot, float analog, int flick);
    }
}
