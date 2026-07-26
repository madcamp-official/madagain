namespace Game.View
{
    /// <summary>
    /// 해킹 성공 시 대상이 받는 콜백. 종류별 컨트롤러(RailController·TurretPossession 등)가
    /// 나중에 구현한다. 스캐폴딩 단계에선 구현체 없음 — 계약만 정의. (기초_설계안 §6)
    /// </summary>
    public interface IHackResponse
    {
        void OnHackSucceeded();
    }
}
