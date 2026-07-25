using System;
using UnityEngine;

namespace MindHexer.Shared.Protocol
{
    /// <summary>
    /// InputPacket ↔ 고정 길이 바이트 배열 (역)직렬화. 리틀 엔디언, 60바이트.
    /// 송신(S10e)과 수신(S24+)이 동일 코드를 사용하므로 필드 불일치가 원천 차단된다.
    /// NETWORK_PROTOCOL.md 와이어 포맷과 1:1 대응.
    /// </summary>
    public static class PacketSerializer
    {
        /// <summary>InputPacket을 제공된 버퍼에 기록. 버퍼 길이는 InputPacketSize 이상이어야 함.</summary>
        public static void Serialize(in InputPacket p, byte[] buffer)
        {
            if (buffer == null || buffer.Length < NetworkConstants.InputPacketSize)
                throw new ArgumentException($"buffer must be >= {NetworkConstants.InputPacketSize} bytes");

            int o = 0;
            WriteUInt(buffer, ref o, NetworkConstants.InputPacketMagic); // 0
            WriteUInt(buffer, ref o, p.Sequence);                        // 4
            WriteLong(buffer, ref o, p.TimestampMs);                     // 8
            WriteInt(buffer, ref o, p.TouchId);                          // 16
            buffer[o++] = (byte)p.Phase;                                 // 20
            buffer[o++] = 0; buffer[o++] = 0; buffer[o++] = 0;           // 21 padding
            WriteFloat(buffer, ref o, p.NormalizedPos.x);                // 24
            WriteFloat(buffer, ref o, p.NormalizedPos.y);                // 28
            WriteFloat(buffer, ref o, p.Position.x);                     // 32
            WriteFloat(buffer, ref o, p.Position.y);                     // 36
            WriteFloat(buffer, ref o, p.Position.z);                     // 40
            WriteFloat(buffer, ref o, p.Rotation.x);                     // 44
            WriteFloat(buffer, ref o, p.Rotation.y);                     // 48
            WriteFloat(buffer, ref o, p.Rotation.z);                     // 52
            WriteFloat(buffer, ref o, p.Rotation.w);                     // 56
            WriteFloat(buffer, ref o, p.Acceleration.x);                 // 60
            WriteFloat(buffer, ref o, p.Acceleration.y);                 // 64
            WriteFloat(buffer, ref o, p.Acceleration.z);                 // 68
            WriteFloat(buffer, ref o, p.MoveAxis.x);                     // 72
            WriteFloat(buffer, ref o, p.MoveAxis.y);                     // 76
            // total 80
        }

        /// <summary>새 버퍼를 할당해 직렬화(편의 오버로드). 핫패스에서는 버퍼 재사용 버전을 쓸 것.</summary>
        public static byte[] Serialize(in InputPacket p)
        {
            var buffer = new byte[NetworkConstants.InputPacketSize];
            Serialize(in p, buffer);
            return buffer;
        }

        /// <summary>
        /// 바이트를 InputPacket으로 역직렬화. 매직/길이 검증 실패 시 false 반환(패킷 폐기).
        /// </summary>
        public static bool TryDeserialize(byte[] buffer, int length, out InputPacket packet)
        {
            packet = default;
            if (buffer == null || length < NetworkConstants.InputPacketSize)
                return false;

            int o = 0;
            uint magic = ReadUInt(buffer, ref o);
            if (magic != NetworkConstants.InputPacketMagic)
                return false;

            packet.Sequence = ReadUInt(buffer, ref o);
            packet.TimestampMs = ReadLong(buffer, ref o);
            packet.TouchId = ReadInt(buffer, ref o);
            packet.Phase = (TouchPhaseCode)buffer[o++];
            o += 3; // padding
            float ux = ReadFloat(buffer, ref o);
            float uy = ReadFloat(buffer, ref o);
            packet.NormalizedPos = new Vector2(ux, uy);
            float posx = ReadFloat(buffer, ref o);
            float posy = ReadFloat(buffer, ref o);
            float posz = ReadFloat(buffer, ref o);
            packet.Position = new Vector3(posx, posy, posz);
            float qx = ReadFloat(buffer, ref o);
            float qy = ReadFloat(buffer, ref o);
            float qz = ReadFloat(buffer, ref o);
            float qw = ReadFloat(buffer, ref o);
            packet.Rotation = new Quaternion(qx, qy, qz, qw);
            float ax = ReadFloat(buffer, ref o);
            float ay = ReadFloat(buffer, ref o);
            float az = ReadFloat(buffer, ref o);
            packet.Acceleration = new Vector3(ax, ay, az);
            float mvx = ReadFloat(buffer, ref o);
            float mvy = ReadFloat(buffer, ref o);
            packet.MoveAxis = new Vector2(mvx, mvy);
            return true;
        }

        // ---- 리틀 엔디언 primitive 헬퍼 (BitConverter는 플랫폼 엔디언 의존이라 명시 구현) ----

        private static void WriteUInt(byte[] b, ref int o, uint v)
        {
            b[o++] = (byte)(v & 0xFF);
            b[o++] = (byte)((v >> 8) & 0xFF);
            b[o++] = (byte)((v >> 16) & 0xFF);
            b[o++] = (byte)((v >> 24) & 0xFF);
        }

        private static void WriteInt(byte[] b, ref int o, int v) => WriteUInt(b, ref o, unchecked((uint)v));

        private static void WriteLong(byte[] b, ref int o, long v)
        {
            ulong u = unchecked((ulong)v);
            for (int i = 0; i < 8; i++) b[o++] = (byte)((u >> (8 * i)) & 0xFF);
        }

        private static unsafe void WriteFloat(byte[] b, ref int o, float v)
        {
            uint u = *(uint*)&v;
            WriteUInt(b, ref o, u);
        }

        private static uint ReadUInt(byte[] b, ref int o)
        {
            uint v = (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
            o += 4;
            return v;
        }

        private static int ReadInt(byte[] b, ref int o) => unchecked((int)ReadUInt(b, ref o));

        private static long ReadLong(byte[] b, ref int o)
        {
            ulong u = 0;
            for (int i = 0; i < 8; i++) u |= (ulong)b[o + i] << (8 * i);
            o += 8;
            return unchecked((long)u);
        }

        private static unsafe float ReadFloat(byte[] b, ref int o)
        {
            uint u = ReadUInt(b, ref o);
            return *(float*)&u;
        }
    }
}
