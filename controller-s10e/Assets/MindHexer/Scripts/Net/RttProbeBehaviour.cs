using UnityEngine;
using MindHexer.Shared.Protocol;
using MindHexer.Shared.Net;

namespace MindHexer.Controller.Net
{
    /// <summary>
    /// S10e RTT 프로브 어댑터. 주기적으로 Ping을 보내 왕복시간을 측정한다(SPEC 5.4). 코어는 <see cref="RttProbe"/>.
    /// HUD에 <see cref="LastRttMs"/>/<see cref="AverageRttMs"/> 표시 권장.
    /// </summary>
    public sealed class RttProbeBehaviour : MonoBehaviour
    {
        [Tooltip("S24+ IP. UdpSender와 동일하게 페어링 결과로 설정.")]
        public string TargetIp = "192.168.0.2";

        [Tooltip("Ping 송신 주기(초).")]
        [Range(0.1f, 2f)] public float PingIntervalSeconds = 0.5f;

        private RttProbe _core;
        private float _timer;

        public double LastRttMs => _core?.LastRttMs ?? -1;
        public double AverageRttMs => _core?.AverageRttMs ?? -1;
        public bool MeetsTarget => _core?.MeetsTarget ?? false;

        private void OnEnable()
        {
            _core = new RttProbe();
            _core.Connect(TargetIp, NetworkConstants.UdpRttPort);
        }

        private void OnDisable()
        {
            _core?.Dispose();
            _core = null;
        }

        public void SetTarget(string ip)
        {
            TargetIp = ip;
            _core?.Connect(ip, NetworkConstants.UdpRttPort);
        }

        private void Update()
        {
            if (_core == null) return;
            _timer += Time.deltaTime;
            if (_timer >= PingIntervalSeconds)
            {
                _timer = 0f;
                _core.SendPing();
            }
        }
    }
}
