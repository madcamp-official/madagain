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
        /// 이번 프레임 조종 입력. <paramref name="analog"/>는 -1~+1 등속 크립(홀드),
        /// <paramref name="flick"/>은 -1/0/+1 임펄스(더블클릭). 부호는 이미 화면 기준으로 보정돼 있다.
        /// </summary>
        void Drive(int slot, float analog, int flick);
    }
}
