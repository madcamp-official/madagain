using System;

namespace MindHexer.Shared.Protocol
{
    /// <summary>
    /// RTT 측정용 Ping/Pong 패킷. S10e가 보내고 S24+가 바이트 그대로 에코한다. (SPEC 5.4)
    /// OriginTimestamp는 S10e 자신의 Stopwatch 틱 → 시계 동기화 없이 왕복시간 계산 가능.
    /// 고정 길이 16바이트, 리틀 엔디언.
    /// </summary>
    public struct RttPacket
    {
        public uint Nonce;             // 손실/짝맞춤 통계용.
        public long OriginTimestamp;   // 송신 시점 Stopwatch.GetTimestamp() 틱.

        public static byte[] Serialize(in RttPacket p)
        {
            var b = new byte[NetworkConstants.RttPacketSize];
            Serialize(in p, b);
            return b;
        }

        public static void Serialize(in RttPacket p, byte[] b)
        {
            if (b == null || b.Length < NetworkConstants.RttPacketSize)
                throw new ArgumentException($"buffer must be >= {NetworkConstants.RttPacketSize} bytes");
            int o = 0;
            WriteUInt(b, ref o, NetworkConstants.RttMagic);
            WriteUInt(b, ref o, p.Nonce);
            WriteLong(b, ref o, p.OriginTimestamp);
        }

        public static bool TryDeserialize(byte[] b, int length, out RttPacket p)
        {
            p = default;
            if (b == null || length < NetworkConstants.RttPacketSize) return false;
            int o = 0;
            if (ReadUInt(b, ref o) != NetworkConstants.RttMagic) return false;
            p.Nonce = ReadUInt(b, ref o);
            p.OriginTimestamp = ReadLong(b, ref o);
            return true;
        }

        private static void WriteUInt(byte[] b, ref int o, uint v)
        {
            b[o++] = (byte)(v & 0xFF);
            b[o++] = (byte)((v >> 8) & 0xFF);
            b[o++] = (byte)((v >> 16) & 0xFF);
            b[o++] = (byte)((v >> 24) & 0xFF);
        }

        private static void WriteLong(byte[] b, ref int o, long v)
        {
            ulong u = unchecked((ulong)v);
            for (int i = 0; i < 8; i++) b[o++] = (byte)((u >> (8 * i)) & 0xFF);
        }

        private static uint ReadUInt(byte[] b, ref int o)
        {
            uint v = (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
            o += 4;
            return v;
        }

        private static long ReadLong(byte[] b, ref int o)
        {
            ulong u = 0;
            for (int i = 0; i < 8; i++) u |= (ulong)b[o + i] << (8 * i);
            o += 8;
            return unchecked((long)u);
        }
    }
}
