using System;
using UnityEngine;

namespace MindHexer.Shared.Protocol
{
    /// <summary>
    /// InputPacket ↔ 고정 길이 바이트 배열 (역)직렬화. <b>리틀 엔디언, v3 = 128바이트.</b>
    /// 송신(S10e)과 수신이 동일 코드를 쓰므로 필드 불일치가 원천 차단된다.
    ///
    /// <para><b>와이어 포맷</b>
    /// <code>
    /// off  size  field
    ///   0     4  magic 'MHX2'
    ///   4     1  version
    ///   5     1  touchCount
    ///   6     2  payloadLen  — 이 패킷의 전체 바이트 수
    ///   8     4  sessionId
    ///  12     4  sequence
    ///  16     8  timestampMs
    ///  24     1  trackingState
    ///  25     3  (pad)
    ///  28    12  position      x,y,z
    ///  40    16  rotation      x,y,z,w
    ///  56    12  acceleration  x,y,z
    ///  68     4  screenWidth
    ///  72     4  screenHeight
    ///  76     4  dpi
    ///  80    16  safeArea      x,y,w,h
    ///  96    16  touch[0]      id(4) phase(1) pad(3) u(4) v(4)
    /// 112    16  touch[1]
    /// 128        total
    /// </code></para>
    ///
    /// <para><b>왜 길이 필드를 두는가</b> — 나중에 필드를 덧붙여도 구버전 수신부가 아는 만큼만 읽고
    /// 뒷부분을 무시하면 되므로, 양쪽을 동시에 고치지 않아도 된다. 컨트롤러는 실기 빌드·설치가
    /// 필요해 한 번 고칠 때마다 비싸다.</para>
    ///
    /// <para><c>BitConverter</c>의 바이트 배열 변환은 플랫폼 엔디언에 의존하므로 조립은 직접 한다.
    /// float↔비트 변환만 <c>SingleToInt32Bits</c>를 쓴다 — unsafe가 필요 없어서, 이 코드를
    /// unsafe 미허용 어셈블리(MINDHEXER의 Game.View 등)에 그대로 가져다 써도 컴파일된다.</para>
    /// </summary>
    public static class PacketSerializer
    {
        // 각 필드의 시작 오프셋. 위 표와 1:1.
        const int OffMagic = 0;
        const int OffVersion = 4;
        const int OffTouchCount = 5;
        const int OffPayloadLen = 6;
        const int OffSessionId = 8;
        const int OffSequence = 12;
        const int OffTimestamp = 16;
        const int OffTracking = 24;
        const int OffPosition = 28;
        const int OffRotation = 40;
        const int OffAccel = 56;
        const int OffScreenW = 68;
        const int OffScreenH = 72;
        const int OffDpi = 76;
        const int OffSafeArea = 80;
        const int OffTouch0 = 96;
        const int TouchStride = 16;

        /// <summary>수신 측이 최소한 읽어야 하는 바이트 수. 이보다 짧으면 해석 불가.</summary>
        public const int MinReadableSize = 128;

        /// <summary>InputPacket을 제공된 버퍼에 기록. 버퍼 길이는 InputPacketSize 이상이어야 함.</summary>
        public static void Serialize(in InputPacket p, byte[] buffer)
        {
            if (buffer == null || buffer.Length < NetworkConstants.InputPacketSize)
                throw new ArgumentException("buffer must be >= " + NetworkConstants.InputPacketSize + " bytes");

            Array.Clear(buffer, 0, NetworkConstants.InputPacketSize);   // pad 바이트를 0으로

            WriteUInt(buffer, OffMagic, NetworkConstants.InputPacketMagic);
            buffer[OffVersion] = NetworkConstants.ProtocolVersion;
            buffer[OffTouchCount] = (byte)Mathf.Clamp(p.TouchCount, 0, NetworkConstants.MaxTouches);
            WriteUShort(buffer, OffPayloadLen, (ushort)NetworkConstants.InputPacketSize);

            WriteUInt(buffer, OffSessionId, p.SessionId);
            WriteUInt(buffer, OffSequence, p.Sequence);
            WriteLong(buffer, OffTimestamp, p.TimestampMs);
            buffer[OffTracking] = (byte)p.Tracking;

            WriteVector3(buffer, OffPosition, p.Position);
            WriteFloat(buffer, OffRotation + 0, p.Rotation.x);
            WriteFloat(buffer, OffRotation + 4, p.Rotation.y);
            WriteFloat(buffer, OffRotation + 8, p.Rotation.z);
            WriteFloat(buffer, OffRotation + 12, p.Rotation.w);
            WriteVector3(buffer, OffAccel, p.Acceleration);

            WriteInt(buffer, OffScreenW, p.ScreenWidth);
            WriteInt(buffer, OffScreenH, p.ScreenHeight);
            WriteFloat(buffer, OffDpi, p.Dpi);
            WriteFloat(buffer, OffSafeArea + 0, p.SafeArea.x);
            WriteFloat(buffer, OffSafeArea + 4, p.SafeArea.y);
            WriteFloat(buffer, OffSafeArea + 8, p.SafeArea.width);
            WriteFloat(buffer, OffSafeArea + 12, p.SafeArea.height);

            for (int i = 0; i < NetworkConstants.MaxTouches; i++)
            {
                int o = OffTouch0 + i * TouchStride;
                TouchSample t = p.GetTouch(i);
                WriteInt(buffer, o, t.Id);
                buffer[o + 4] = (byte)t.Phase;
                WriteFloat(buffer, o + 8, t.Normalized.x);
                WriteFloat(buffer, o + 12, t.Normalized.y);
            }
        }

        /// <summary>새 버퍼를 할당해 직렬화(편의 오버로드). 핫패스에서는 버퍼 재사용 버전을 쓸 것.</summary>
        public static byte[] Serialize(in InputPacket p)
        {
            var buffer = new byte[NetworkConstants.InputPacketSize];
            Serialize(in p, buffer);
            return buffer;
        }

        /// <summary>
        /// 바이트를 InputPacket으로 역직렬화. 매직/길이 검증 실패 시 false(패킷 폐기).
        /// 알려진 필드만 읽고 뒤에 붙은 미지의 바이트는 무시한다.
        /// </summary>
        public static bool TryDeserialize(byte[] buffer, int length, out InputPacket packet)
        {
            packet = default;
            if (buffer == null || length < MinReadableSize) return false;
            if (ReadUInt(buffer, OffMagic) != NetworkConstants.InputPacketMagic) return false;

            // 선언된 길이가 실제 수신 길이보다 크면 잘린 패킷이다 → 폐기.
            int declared = ReadUShort(buffer, OffPayloadLen);
            if (declared > length) return false;

            packet.SessionId = ReadUInt(buffer, OffSessionId);
            packet.Sequence = ReadUInt(buffer, OffSequence);
            packet.TimestampMs = ReadLong(buffer, OffTimestamp);
            packet.Tracking = (TrackingStateCode)buffer[OffTracking];

            packet.Position = ReadVector3(buffer, OffPosition);
            packet.Rotation = new Quaternion(
                ReadFloat(buffer, OffRotation + 0),
                ReadFloat(buffer, OffRotation + 4),
                ReadFloat(buffer, OffRotation + 8),
                ReadFloat(buffer, OffRotation + 12));
            packet.Acceleration = ReadVector3(buffer, OffAccel);

            packet.ScreenWidth = ReadInt(buffer, OffScreenW);
            packet.ScreenHeight = ReadInt(buffer, OffScreenH);
            packet.Dpi = ReadFloat(buffer, OffDpi);
            packet.SafeArea = new Rect(
                ReadFloat(buffer, OffSafeArea + 0),
                ReadFloat(buffer, OffSafeArea + 4),
                ReadFloat(buffer, OffSafeArea + 8),
                ReadFloat(buffer, OffSafeArea + 12));

            packet.TouchCount = Mathf.Clamp(buffer[OffTouchCount], 0, NetworkConstants.MaxTouches);
            for (int i = 0; i < NetworkConstants.MaxTouches; i++)
            {
                int o = OffTouch0 + i * TouchStride;
                packet.SetTouch(i, new TouchSample
                {
                    Id = ReadInt(buffer, o),
                    Phase = (TouchPhaseCode)buffer[o + 4],
                    Normalized = new Vector2(ReadFloat(buffer, o + 8), ReadFloat(buffer, o + 12))
                });
            }
            return true;
        }

        // ── 리틀 엔디언 primitive 헬퍼 ────────────────────────────────────

        static void WriteUShort(byte[] b, int o, ushort v)
        {
            b[o] = (byte)(v & 0xFF);
            b[o + 1] = (byte)((v >> 8) & 0xFF);
        }

        static ushort ReadUShort(byte[] b, int o) { return (ushort)(b[o] | (b[o + 1] << 8)); }

        static void WriteUInt(byte[] b, int o, uint v)
        {
            b[o] = (byte)(v & 0xFF);
            b[o + 1] = (byte)((v >> 8) & 0xFF);
            b[o + 2] = (byte)((v >> 16) & 0xFF);
            b[o + 3] = (byte)((v >> 24) & 0xFF);
        }

        static uint ReadUInt(byte[] b, int o)
        {
            return (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
        }

        static void WriteInt(byte[] b, int o, int v) { WriteUInt(b, o, unchecked((uint)v)); }
        static int ReadInt(byte[] b, int o) { return unchecked((int)ReadUInt(b, o)); }

        static void WriteLong(byte[] b, int o, long v)
        {
            ulong u = unchecked((ulong)v);
            for (int i = 0; i < 8; i++) b[o + i] = (byte)((u >> (8 * i)) & 0xFF);
        }

        static long ReadLong(byte[] b, int o)
        {
            ulong u = 0;
            for (int i = 0; i < 8; i++) u |= (ulong)b[o + i] << (8 * i);
            return unchecked((long)u);
        }

        static void WriteFloat(byte[] b, int o, float v)
        {
            WriteUInt(b, o, unchecked((uint)BitConverter.SingleToInt32Bits(v)));
        }

        static float ReadFloat(byte[] b, int o)
        {
            return BitConverter.Int32BitsToSingle(unchecked((int)ReadUInt(b, o)));
        }

        static void WriteVector3(byte[] b, int o, Vector3 v)
        {
            WriteFloat(b, o + 0, v.x);
            WriteFloat(b, o + 4, v.y);
            WriteFloat(b, o + 8, v.z);
        }

        static Vector3 ReadVector3(byte[] b, int o)
        {
            return new Vector3(ReadFloat(b, o + 0), ReadFloat(b, o + 4), ReadFloat(b, o + 8));
        }
    }
}
