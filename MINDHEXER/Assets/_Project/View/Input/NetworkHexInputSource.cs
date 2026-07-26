using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// VR/네트워크 입력 소스 — S10e 컨트롤러 샘플을 <b>지연 가리기 층</b>(<see cref="InputSmoother"/>)으로
    /// 평활화한 뒤, 제어 스킴 매핑(§2.5)을 거쳐 <see cref="HexInput"/>으로 낸다.
    ///
    /// <para>역할 경계:
    ///  · <b>원시 샘플 주입</b>: SYB 네트워크 수신부가 패킷 도착 시 <see cref="Push"/>. (아직 미통합 — 이음새만)
    ///  · <b>평활화/외삽</b>: 이 클래스 + <see cref="InputSmoother"/> (지연 가리기 = 내 담당).
    ///  · <b>매핑</b>: raw <see cref="ControllerSample"/> → <see cref="HexInput"/> 변환은 제어 스킴(게임플레이)이
    ///    <see cref="Map"/>으로 채운다. 비어 있으면 컨텍스트만 채운 빈 입력(안전).</para>
    ///
    /// <para><see cref="Active"/> = 지금 살아 있는 네트워크 소스. 네트워크 수신부는 여기로 Push하면 된다.</para>
    /// </summary>
    public sealed class NetworkHexInputSource : IHexInputSource
    {
        /// <summary>현재 활성 네트워크 소스(가장 최근 생성). SYB 네트워크 수신부의 Push 대상.</summary>
        public static NetworkHexInputSource Active { get; private set; }

        public readonly InputSmoother Smoother = new InputSmoother();

        /// <summary>평활화된 컨트롤러 상태 → 게임 입력 매핑(제어 스킴 §2.5). 게임플레이가 채운다.</summary>
        public System.Func<ControllerSample, ControlContext, HexInput> Map;

        public NetworkHexInputSource() { Active = this; }

        /// <summary>SYB 네트워크가 패킷 도착 시 호출. <paramref name="arrivalTime"/> = 로컬 수신 시각(초).</summary>
        public void Push(double arrivalTime, in ControllerSample sample) => Smoother.Push(arrivalTime, sample);

        public HexInput Poll(ControlContext context)
        {
            HexInput cmd = HexInput.Empty;
            cmd.context = context;
            if (!Smoother.TrySample(Time.unscaledTimeAsDouble, out ControllerSample s)) return cmd;
            if (Map == null) return cmd;   // 매핑 미구현 — 컨텍스트만 채운 빈 입력(안전)
            HexInput mapped = Map(s, context);
            mapped.context = context;
            return mapped;
        }
    }
}
