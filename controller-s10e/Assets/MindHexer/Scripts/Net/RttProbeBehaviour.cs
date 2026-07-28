using UnityEngine;
using MindHexer.Shared.Protocol;
using MindHexer.Shared.Net;

namespace MindHexer.Controller.Net
{
    /// <summary>
    /// S10e RTT 프로브 어댑터. 코어(<see cref="RttProbe"/>)가 **자동 고빈도 Ping**을 백그라운드에서 보낸다(SPEC 5.4).
    /// 고빈도 Ping은 헤드셋→컨트롤러(되돌아오는) 경로를 계속 깨워 Wi-Fi 절전 지연을 줄인다 → RTT↓.
    /// </summary>
    public sealed class RttProbeBehaviour : MonoBehaviour
    {
        [Tooltip("S24+ IP. UdpSender와 동일하게 페어링 결과로 설정.")]
        public string TargetIp = "192.168.0.2";

        [Tooltip("자동 Ping 간격(ms). 낮을수록 경로가 깨어 있어 RTT↓(20~50 권장). 트래픽은 무시할 수준.")]
        [Range(10, 250)] public int PingIntervalMs = 40;

        private RttProbe _core;

        public double LastRttMs => _core?.LastRttMs ?? -1;
        public double AverageRttMs => _core?.AverageRttMs ?? -1;
        public bool MeetsTarget => _core?.MeetsTarget ?? false;

        private void OnEnable()
        {
            _core = new RttProbe { PingIntervalMs = PingIntervalMs };
            _core.Connect(TargetIp, NetworkConstants.UdpRttPort); // Connect가 수신+자동Ping 스레드 기동
        }

        private void OnDisable()
        {
            _core?.Dispose();
            _core = null;
        }

        public void SetTarget(string ip)
        {
            TargetIp = ip;
            if (_core != null)
            {
                _core.PingIntervalMs = PingIntervalMs;
                _core.Connect(ip, NetworkConstants.UdpRttPort);
            }
        }
    }
}
