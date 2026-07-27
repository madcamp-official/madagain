using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MindHexer.Shared.Net
{
    /// <summary>
    /// 주 아웃바운드 인터페이스의 로컬 IPv4를 추정한다. (디스커버리 비콘에 실을 자신의 IP)
    /// UDP 소켓을 임의 원격지로 "connect"하면 실제 패킷을 보내지 않고도 OS가 사용할
    /// 로컬 엔드포인트를 결정해준다 → 핫스팟/일반 Wi-Fi 모두에서 올바른 인터페이스 선택.
    /// </summary>
    public static class LocalIPv4
    {
        public static string Resolve(string fallback = "127.0.0.1")
        {
            try
            {
                using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                s.Connect("8.8.8.8", 65530); // UDP connect는 패킷을 보내지 않음
                if (s.LocalEndPoint is IPEndPoint ep && ep.Address != null)
                    return ep.Address.ToString();
            }
            catch { /* 오프라인 등 → fallback */ }
            return fallback;
        }

        /// <summary>
        /// 활성 인터페이스의 모든 IPv4를 (인터페이스명, IP)로 열거한다. 루프백 제외.
        /// 다중 네트워크(캠퍼스 Wi-Fi + PC 핫스팟 등)에서 폰이 붙을 IP를 사람이 직접 고를 수 있게 한다.
        /// </summary>
        public static List<(string iface, string ip)> AllIPv4()
        {
            var result = new List<(string, string)>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (IPAddress.IsLoopback(ua.Address)) continue;
                        result.Add((ni.Name, ua.Address.ToString()));
                    }
                }
            }
            catch { /* 권한/플랫폼 이슈 → 빈 목록 */ }
            return result;
        }

        /// <summary>
        /// 기본 게이트웨이 IPv4를 반환(없으면 null). 핫스팟에서는 보통 **호스트(=서버)** 가 게이트웨이다.
        /// (Android/Mono는 GatewayAddresses가 비어 있을 수 있음 → 그때는 null.)
        /// </summary>
        public static string GetGatewayIPv4()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ga in ni.GetIPProperties().GatewayAddresses)
                    {
                        if (ga?.Address == null) continue;
                        if (ga.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string s = ga.Address.ToString();
                        if (!string.IsNullOrEmpty(s) && s != "0.0.0.0") return s;
                    }
                }
            }
            catch { /* 무시 */ }
            return null;
        }

        /// <summary>
        /// 핫스팟 호스트(=대개 서버) IP를 추정한다. 우선순위: 실제 게이트웨이 → 로컬 IP의 서브넷 .1 → fallback.
        /// 헤드셋이 핫스팟을 열고 컨트롤러가 붙은 구성에서, 컨트롤러가 헤드셋 IP를 자동 취득하는 데 쓴다.
        /// </summary>
        public static string GuessServerHost(string fallback = "127.0.0.1")
        {
            string gw = GetGatewayIPv4();
            if (!string.IsNullOrEmpty(gw)) return gw;

            string local = Resolve(null);
            if (!string.IsNullOrEmpty(local))
            {
                int dot = local.LastIndexOf('.');
                if (dot > 0) return local.Substring(0, dot) + ".1"; // 서브넷 호스트 관례(.1)
            }
            return fallback;
        }
    }
}
