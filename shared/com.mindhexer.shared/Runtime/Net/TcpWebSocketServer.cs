using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using MindHexer.Shared.Protocol;

namespace MindHexer.Shared.Net
{
    /// <summary>
    /// 외부 라이브러리 없는 WebSocket 서버(RFC 6455). TcpListener 기반이라 WebSocketSharp DLL 없이
    /// S24+(헤드셋)와 PC 수신기가 동일하게 서버를 띄울 수 있다. 텍스트 프레임만 처리(작은 단일 프레임 JSON).
    /// 연결마다 <see cref="IEventChannel"/>을 제공 → <see cref="PairingServer"/>가 그대로 붙는다.
    ///
    /// 콜백(Received/Closed)은 **연결별 백그라운드 스레드**에서 올라온다. Unity에서 쓰면 어댑터가
    /// MainThreadDispatcher로 메인 스레드에 넘겨야 한다.
    /// </summary>
    public sealed class TcpWebSocketServer
    {
        private const string MagicGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        private readonly int _port;
        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;

        /// <summary>새 클라이언트가 WS 핸드셰이크를 마쳤을 때. (clientId, 채널)</summary>
        public event Action<string, IEventChannel> ClientConnected;

        public bool IsRunning => _running;

        public TcpWebSocketServer(int port) => _port = port;
        public TcpWebSocketServer() : this(NetworkConstants.WebSocketPort) { }

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
            _acceptThread = null;
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
                ClientConnected?.Invoke(id, conn);
                conn.ReadLoop();
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
                // WS 업그레이드가 아니면(브라우저 접속 등) 도달성 확인용 HTTP 200을 돌려준다.
                string body = "MindHexer WebSocket server alive. Connect via WebSocket to " + NetworkConstants.WebSocketPath + ".";
                byte[] b = Encoding.UTF8.GetBytes(body);
                string http = "HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: " + b.Length + "\r\nConnection: close\r\n\r\n";
                byte[] head = Encoding.ASCII.GetBytes(http);
                stream.Write(head, 0, head.Length);
                stream.Write(b, 0, b.Length);
                stream.Flush();
                return false;
            }

            string accept;
            using (var sha1 = SHA1.Create())
                accept = Convert.ToBase64String(sha1.ComputeHash(Encoding.ASCII.GetBytes(key + MagicGuid)));

            string resp = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n";
            byte[] respBytes = Encoding.ASCII.GetBytes(resp);
            stream.Write(respBytes, 0, respBytes.Length);
            return true;
        }

        private static string ReadHttpHeaders(NetworkStream stream)
        {
            var sb = new StringBuilder();
            var buf = new byte[1];
            int consecutive = 0, total = 0;
            while (total++ < 16384)
            {
                int n = stream.Read(buf, 0, 1);
                if (n == 0) return null;
                char ch = (char)buf[0];
                sb.Append(ch);
                if (ch == '\n') { consecutive++; if (consecutive == 2) return sb.ToString(); }
                else if (ch != '\r') consecutive = 0;
            }
            return null;
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

            public void Close()
            {
                if (_closed) return;
                try { SendControl(0x8, Array.Empty<byte>()); } catch { }
                MarkClosed();
                try { _client.Close(); } catch { }
            }

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
                            case 0x1: Received?.Invoke(Encoding.UTF8.GetString(payload)); break; // text
                            case 0x8: SendControl(0x8, Array.Empty<byte>()); MarkClosed(); return; // close
                            case 0x9: SendControl(0xA, payload); break; // ping → pong
                        }
                    }
                }
                catch { }
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

            private void MarkClosed()
            {
                if (_closed) return;
                _closed = true;
                Closed?.Invoke();
            }
        }
    }
}
