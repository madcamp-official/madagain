using System.Net;
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
    }
}
