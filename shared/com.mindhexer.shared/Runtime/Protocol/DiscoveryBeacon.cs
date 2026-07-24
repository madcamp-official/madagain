using System;
using System.Text;

namespace MindHexer.Shared.Protocol
{
    /// <summary>
    /// S24+가 UDP 브로드캐스트로 자신의 서버 접속 정보를 알리는 비콘. (SPEC 2.3-1)
    /// 가변 길이(IP 문자열 포함)라 InputPacket과 달리 간단한 텍스트 포맷을 쓴다:
    ///   "MHXB|{version}|{ip}|{wsPort}"
    /// </summary>
    public static class DiscoveryBeacon
    {
        private const string Prefix = "MHXB";

        public static byte[] Build(string serverIp, int wsPort)
        {
            string s = $"{Prefix}|{NetworkConstants.ProtocolVersion}|{serverIp}|{wsPort}";
            return Encoding.UTF8.GetBytes(s);
        }

        /// <summary>비콘 파싱. 실패 시 false.</summary>
        public static bool TryParse(byte[] data, int length, out string serverIp, out int wsPort, out byte version)
        {
            serverIp = null;
            wsPort = 0;
            version = 0;
            if (data == null || length <= 0) return false;

            string s;
            try { s = Encoding.UTF8.GetString(data, 0, length); }
            catch { return false; }

            var parts = s.Split('|');
            if (parts.Length != 4 || parts[0] != Prefix) return false;
            if (!byte.TryParse(parts[1], out version)) return false;
            serverIp = parts[2];
            if (!int.TryParse(parts[3], out wsPort)) return false;
            return !string.IsNullOrEmpty(serverIp);
        }
    }
}
