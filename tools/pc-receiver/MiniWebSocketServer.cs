using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using MindHexer.Shared.Net;
using MindHexer.Shared.Protocol;

namespace MindHexer.PcReceiver
{
    /// <summary>
    /// 외부 라이브러리 없는 최소 WebSocket 서버(RFC 6455). TcpListener 기반이라 Windows에서
    /// 관리자 권한/urlacl 없이 0.0.0.0 바인딩 가능 → 실제 S10e(NativeWebSocket)가 바로 접속/페어링.
    /// 텍스트 프레임만 처리(우리 메시지는 작은 단일 프레임 JSON). 연결마다 <see cref="IEventChannel"/> 제공.
    /// </summary>
    public sealed class MiniWebSocketServer
    {
        private const string MagicGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        private readonly int _port;
        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;

        /// <summary>새 클라이언트가 WebSocket 핸드셰이크를 마쳤을 때. (clientId, 채널)</summary>
        public event Action<string, IEventChannel> ClientConnected;

        public MiniWebSocketServer(int port) => _port = port;

        public void Start()
        {
            if (_running) return;
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            _running = true;
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "MHX-WsAccept" };
            _acceptThread.Start();
        }

        public void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { /* ignore */ }
            _acceptThread?.Join(300);
            _listener = null;
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client;
                try { client = _listener.AcceptTcpClient(); }
                catch (SocketException) when (!_running) { break; }
                catch (Exception) { continue; }

                var t = new Thread(() => HandleClient(client)) { IsBackground = true, Name = "MHX-WsConn" };
                t.Start();
            }
        }

        private void HandleClient(TcpClient client)
        {
            string id = client.Client.RemoteEndPoint?.ToString() ?? Guid.NewGuid().ToString();
            NetworkStream stream = client.GetStream();
            try
            {
                if (!Handshake(stream)) { client.Close(); return; }

                var conn = new WsConnection(client, stream);
                ClientConnected?.Invoke(id, conn); // PairingServer가 여기서 Register(구독)
                conn.ReadLoop();                   // 블로킹 수신 루프
            }
            catch (Exception) { /* 연결 오류 → 종료 */ }
            finally { try { client.Close(); } catch { } }
        }

        // ---- 핸드셰이크 ----

        private static bool Handshake(NetworkStream stream)
        {
            string request = ReadHttpHeaders(stream);
            if (request == null) return false;

            string key = null;
            foreach (var line in request.Split('\n'))
            {
                int c = line.IndexOf(':');
                if (c <= 0) continue;
                if (line.Substring(0, c).Trim().Equals("Sec-WebSocket-Key", StringComparison.OrdinalIgnoreCase))
                    key = line.Substring(c + 1).Trim();
            }
            if (string.IsNullOrEmpty(key))
            {
                // WebSocket 업그레이드가 아니면(예: 폰 브라우저로 접속) 도달성 확인용 HTTP 200을 돌려준다.
                // → 폰 브라우저에서 http://<PC IP>:45712/ 열어 이 페이지가 보이면 폰→PC TCP 경로 정상.
                string body = "MindHexer pc-receiver alive. WebSocket으로 " +
                              NetworkConstants.WebSocketPath + " 에 접속하면 페어링됩니다.";
                byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
                string http = "HTTP/1.1 200 OK\r\n" +
                              "Content-Type: text/plain; charset=utf-8\r\n" +
                              "Content-Length: " + bodyBytes.Length + "\r\n" +
                              "Connection: close\r\n\r\n";
                byte[] head = Encoding.ASCII.GetBytes(http);
                stream.Write(head, 0, head.Length);
                stream.Write(bodyBytes, 0, bodyBytes.Length);
                stream.Flush();
                return false;
            }

            string accept;
            using (var sha1 = SHA1.Create())
                accept = Convert.ToBase64String(sha1.ComputeHash(Encoding.ASCII.GetBytes(key + MagicGuid)));

            string resp =
                "HTTP/1.1 101 Switching Protocols\r\n" +
                "Upgrade: websocket\r\n" +
                "Connection: Upgrade\r\n" +
                "Sec-WebSocket-Accept: " + accept + "\r\n\r\n";
            byte[] respBytes = Encoding.ASCII.GetBytes(resp);
            stream.Write(respBytes, 0, respBytes.Length);
            return true;
        }

        private static string ReadHttpHeaders(NetworkStream stream)
        {
            var sb = new StringBuilder();
            var buf = new byte[1];
            int consecutive = 0;
            int total = 0;
            while (total++ < 16384)
            {
                int n = stream.Read(buf, 0, 1);
                if (n == 0) return null;
                char c = (char)buf[0];
                sb.Append(c);
                if (c == '\n') { consecutive++; if (consecutive == 2) return sb.ToString(); }
                else if (c != '\r') consecutive = 0;
            }
            return null; // 헤더 과대
        }

        /// <summary>WebSocket 연결 하나 = 하나의 IEventChannel.</summary>
        private sealed class WsConnection : IEventChannel
        {
            private readonly TcpClient _client;
            private readonly NetworkStream _stream;
            private readonly object _writeLock = new object();
            private volatile bool _closed;

            public event Action<string> Received;
            public event Action Closed;

            public WsConnection(TcpClient client, NetworkStream stream)
            {
                _client = client;
                _stream = stream;
            }

            // ---- 송신 (서버→클라, 마스크 없음) ----
            public void Send(string json)
            {
                if (_closed) return;
                byte[] payload = Encoding.UTF8.GetBytes(json);
                byte[] header;
                if (payload.Length <= 125)
                    header = new byte[] { 0x81, (byte)payload.Length };
                else if (payload.Length <= 0xFFFF)
                    header = new byte[] { 0x81, 126, (byte)(payload.Length >> 8), (byte)(payload.Length & 0xFF) };
                else
                {
                    long len = payload.Length;
                    header = new byte[10];
                    header[0] = 0x81; header[1] = 127;
                    for (int i = 0; i < 8; i++) header[9 - i] = (byte)((len >> (8 * i)) & 0xFF);
                }
                try
                {
                    lock (_writeLock)
                    {
                        _stream.Write(header, 0, header.Length);
                        _stream.Write(payload, 0, payload.Length);
                        _stream.Flush();
                    }
                }
                catch { MarkClosed(); }
            }

            // ---- 수신 루프 (클라→서버, 항상 마스크됨) ----
            public void ReadLoop()
            {
                try
                {
                    while (!_closed)
                    {
                        var frame = ReadFrame();
                        if (frame == null) break;
                        var (opcode, payload) = frame.Value;
                        switch (opcode)
                        {
                            case 0x1: // text
                                Received?.Invoke(Encoding.UTF8.GetString(payload));
                                break;
                            case 0x8: // close
                                SendClose();
                                MarkClosed();
                                return;
                            case 0x9: // ping → pong
                                SendControl(0xA, payload);
                                break;
                            // 0xA pong, 0x2 binary 등은 무시
                        }
                    }
                }
                catch { /* 연결 오류 */ }
                finally { MarkClosed(); }
            }

            private (byte opcode, byte[] payload)? ReadFrame()
            {
                byte[] h2 = ReadExact(2);
                if (h2 == null) return null;
                byte opcode = (byte)(h2[0] & 0x0F);
                bool masked = (h2[1] & 0x80) != 0;
                long len = h2[1] & 0x7F;

                if (len == 126)
                {
                    byte[] ext = ReadExact(2);
                    if (ext == null) return null;
                    len = (ext[0] << 8) | ext[1];
                }
                else if (len == 127)
                {
                    byte[] ext = ReadExact(8);
                    if (ext == null) return null;
                    len = 0;
                    for (int i = 0; i < 8; i++) len = (len << 8) | ext[i];
                }

                byte[] mask = null;
                if (masked) { mask = ReadExact(4); if (mask == null) return null; }

                byte[] payload = len == 0 ? Array.Empty<byte>() : ReadExact((int)len);
                if (payload == null && len != 0) return null;
                if (masked && payload != null)
                    for (int i = 0; i < payload.Length; i++) payload[i] ^= mask[i % 4];

                return (opcode, payload ?? Array.Empty<byte>());
            }

            private byte[] ReadExact(int n)
            {
                var buf = new byte[n];
                int off = 0;
                while (off < n)
                {
                    int r = _stream.Read(buf, off, n - off);
                    if (r <= 0) return null;
                    off += r;
                }
                return buf;
            }

            private void SendControl(byte opcode, byte[] payload)
            {
                if (_closed) return;
                int len = payload?.Length ?? 0;
                var frame = new byte[2 + len];
                frame[0] = (byte)(0x80 | opcode);
                frame[1] = (byte)len;
                if (len > 0) Buffer.BlockCopy(payload, 0, frame, 2, len);
                try { lock (_writeLock) { _stream.Write(frame, 0, frame.Length); _stream.Flush(); } }
                catch { MarkClosed(); }
            }

            private void SendClose() => SendControl(0x8, Array.Empty<byte>());

            private void MarkClosed()
            {
                if (_closed) return;
                _closed = true;
                Closed?.Invoke();
            }
        }
    }
}
