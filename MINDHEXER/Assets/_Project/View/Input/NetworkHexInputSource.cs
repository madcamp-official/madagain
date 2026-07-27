using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// VR/네트워크 입력 소스 — S10e 컨트롤러 샘플을 <b>지연 가리기 층</b>(<see cref="InputSmoother"/>)으로
    /// 평활화한 뒤, 제어 스킴 매핑(§2.5)을 거쳐 <see cref="HexInput"/>으로 낸다.
    ///
    /// <para>역할 경계:
    ///  · <b>원시 샘플 주입</b>: SYB 네트워크 수신부가 패킷 도착 시 <see cref="Push"/>. (아직 미통합 — 이음새만)
    ///  · <b>age 추정 + 외삽</b>: 이 클래스 + <see cref="LatencyEstimator"/> + <see cref="InputSmoother"/>.
    ///    보간 버퍼(의도적 지연)는 제거됐다 — 연속값만 age만큼 앞으로 외삽하고, 이산값은 즉시 반영한다.
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

        /// <summary>패킷별 age(지터) 추정 — 시계 동기화 없이(A′). 송신 시각을 같이 받을 때만 갱신된다.</summary>
        public readonly LatencyEstimator Latency = new LatencyEstimator();

        /// <summary>가장 최근 패킷의 age(초). 이동 적분기(FirstPersonPlayer.InputAge)로 넘겨 지연을 보상한다.</summary>
        public float LatestAge { get; private set; }

        /// <summary>평활화된 컨트롤러 상태 → 게임 입력 매핑(제어 스킴 §2.5). 게임플레이가 채운다.</summary>
        public System.Func<ControllerSample, ControlContext, HexInput> Map;

        public NetworkHexInputSource() { Active = this; }

        /// <summary>
        /// SYB 네트워크가 패킷 도착 시 호출. <paramref name="arrivalTime"/> = 로컬 <b>단조</b> 수신 시각(초).
        /// age를 못 구하므로 지연 보상은 0이 된다 — 가능하면 송신 시각을 함께 넘기는 오버로드를 쓸 것.
        /// </summary>
        public void Push(double arrivalTime, in ControllerSample sample) => Smoother.Push(arrivalTime, sample);

        /// <summary>
        /// 송신 시각을 함께 받는 경로(권장). 두 시각은 <b>각자의 단조 시계</b> 기준이면 되고
        /// 서로 맞출 필요가 없다 — <see cref="LatencyEstimator"/>가 상대 방식(A′)으로 age를 뽑는다.
        /// </summary>
        public void Push(double arrivalTime, double remoteSendTime, in ControllerSample sample)
        {
            LatestAge = (float)Latency.Observe(arrivalTime, remoteSendTime);
            Smoother.Push(arrivalTime, sample);
        }

        public HexInput Poll(ControlContext context)
        {
            HexInput cmd = HexInput.Empty;
            cmd.context = context;

            // 연속값은 age만큼 외삽해 '현재'를 만들고, 이산값(터치 down/up)은 최신 그대로 쓴다.
            if (!Smoother.TrySample(LatestAge, out ControllerSample s)) return cmd;
            if (Smoother.TryLatest(out ControllerSample raw)) s.touchPhase = raw.touchPhase;

            if (Map == null) return cmd;   // 매핑 미구현 — 컨텍스트만 채운 빈 입력(안전)
            HexInput mapped = Map(s, context);
            mapped.context = context;
            return mapped;
        }
    }
}
