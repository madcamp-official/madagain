using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 두 입력 소스를 합친다. 에디터 튜닝용 — 폰으로 조작하면서 키보드·마우스도 그대로 쓸 수 있어야
    /// "폰이 이상한 건지 게임이 이상한 건지"를 즉시 가를 수 있다.
    ///
    /// <para>합치는 규칙: 불리언은 OR, 축은 합산 후 클램프, 플릭은 <see cref="FlickDir.None"/>이 아닌 쪽 우선.
    /// 둘이 동시에 반대 축을 밀면 상쇄되는데, 그건 사람이 동시에 두 장치를 미는 경우라 문제되지 않는다.</para>
    ///
    /// <para>실기(S24+)에선 키보드·마우스가 없어 PC 소스가 항상 빈 값을 내므로, 이 클래스를 그대로 둬도
    /// 네트워크 소스만 통과한다 — 빌드용으로 갈아끼울 필요가 없다.</para>
    /// </summary>
    public sealed class CompositeHexInputSource : IHexInputSource
    {
        public IHexInputSource A;
        public IHexInputSource B;

        public CompositeHexInputSource(IHexInputSource a, IHexInputSource b) { A = a; B = b; }

        public HexInput Poll(ControlContext context)
        {
            HexInput ra = A != null ? A.Poll(context) : HexInput.Empty;
            HexInput rb = B != null ? B.Poll(context) : HexInput.Empty;

            HexInput r = HexInput.Empty;
            r.context = context;

            r.hackHeld = ra.hackHeld || rb.hackHeld;
            r.hackPressed = ra.hackPressed || rb.hackPressed;
            r.hackReleased = ra.hackReleased || rb.hackReleased;
            r.strokeDir = ra.strokeDir + rb.strokeDir;

            r.axisH = Mathf.Clamp(ra.axisH + rb.axisH, -1f, 1f);
            r.axisV = Mathf.Clamp(ra.axisV + rb.axisV, -1f, 1f);
            r.flick = ra.flick != FlickDir.None ? ra.flick : rb.flick;

            r.primary = ra.primary || rb.primary;
            r.primaryHeld = ra.primaryHeld || rb.primaryHeld;
            return r;
        }
    }
}
