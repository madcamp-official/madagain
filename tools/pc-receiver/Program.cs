using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using MindHexer.Shared.Protocol;
using MindHexer.Shared.Events;
using MindHexer.Shared.Net;

namespace MindHexer.PcReceiver
{
    /// <summary>
    /// PC에서 S24+ 역할을 대신하는 6DoF 수신/측정 도구.
    /// UDP InputPacket 수신 + WebSocket 서버(페어링) + 디스커버리 브로드캐스트 + RTT 응답을 모두 띄운다.
    /// 실제 S10e가 이 PC에 페어링하면 6DoF 스트림이 콘솔에 실시간 표시된다.
    ///
    /// 실행:  dotnet run                (라이브 수신, Ctrl+C 종료)
    ///        dotnet run -- --selftest  (폰 없이 PC 내부 자가검증)
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            foreach (var a in args)
                if (a == "--selftest") return SelfTest().GetAwaiter().GetResult();
            RunLive();
            return 0;
        }

        // ---------- 라이브 수신 ----------

        private static void RunLive()
        {
            string localIp = LocalIPv4.Resolve();

            var pairing = new PairingServer(NetworkConstants.ProtocolVersion);
            pairing.ClientPaired += id => Console.WriteLine($"\n[페어링됨] client={id} → 이제 S10e가 6DoF UDP 스트리밍을 시작합니다.\n");

            var ws = new TcpWebSocketServer(NetworkConstants.WebSocketPort);
            ws.ClientConnected += (id, ch) =>
            {
                Console.WriteLine($"[WS 연결] {id}");
                pairing.Register(id, ch);
            };
            ws.Start();

            var stats = new PacketStats();
            var rx = new InputStreamReceiver(NetworkConstants.UdpInputPort);
            rx.PacketReceived += stats.OnPacket; // 수신 스레드
            rx.Start();

            var rtt = new RttResponder(NetworkConstants.UdpRttPort);
            rtt.Start();

            var discovery = new DiscoveryBroadcaster(localIp, NetworkConstants.WebSocketPort);
            discovery.Start();

            PrintBanner(localIp);

            var stop = new ManualResetEventSlim(false);
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Set(); };

            long lastCount = 0;
            var swReport = System.Diagnostics.Stopwatch.StartNew();
            while (!stop.IsSet)
            {
                stop.Wait(500);
                double dt = swReport.Elapsed.TotalSeconds;
                swReport.Restart();

                long total = rx.AcceptedCount;
                double rate = dt > 0 ? (total - lastCount) / dt : 0;
                lastCount = total;

                var snap = stats.Snapshot();
                string line = snap.HasData
                    ? $"pkts={total} rate={rate,5:0.0}/s loss={snap.Lost} | " +
                      $"move={snap.Move} pos={snap.Position} rotQ={snap.Rotation} |q|={snap.RotMagnitude:0.###} " +
                      $"touch={snap.Phase}@{snap.NormalizedPos}"
                    : $"pkts=0 (S10e 페어링/스트리밍 대기 중… paired={pairing.PairedCount})";
                Console.WriteLine(line);
            }

            Console.WriteLine("\n종료 중…");
            discovery.Dispose(); rtt.Dispose(); rx.Dispose(); ws.Stop();
        }

        private static void PrintBanner(string localIp)
        {
            Console.WriteLine("================ MINDHEXER PC 수신기 (S24+ 시뮬레이터) ================");
            Console.WriteLine($" 주 IP(인터넷쪽): {localIp}");
            Console.WriteLine(" 이 PC의 모든 IPv4 (폰이 붙은 네트워크의 IP를 고르세요):");
            var all = LocalIPv4.AllIPv4();
            if (all.Count == 0) Console.WriteLine("   (열거 실패 — 위 주 IP 사용)");
            foreach (var (iface, ip) in all)
            {
                string hint = ip.StartsWith("192.168.137.") ? "  ← Windows 모바일 핫스팟(폰이 여기 붙음)"
                            : ip.StartsWith("10.") || ip.StartsWith("172.") ? "  (캠퍼스/관리망일 수 있음: 단말격리 주의)"
                            : "";
                Console.WriteLine($"   - {ip,-15} [{iface}]{hint}");
            }
            Console.WriteLine("----------------------------------------------------------------------");
            Console.WriteLine($" UDP InputPort   : {NetworkConstants.UdpInputPort}  (S10e→PC 6DoF 스트림)");
            Console.WriteLine($" UDP RttPort     : {NetworkConstants.UdpRttPort}  (RTT Ping/Pong)");
            Console.WriteLine($" UDP Discovery   : {NetworkConstants.UdpDiscoveryPort} (PC→서브넷 브로드캐스트)");
            Console.WriteLine($" WebSocket 포트  : {NetworkConstants.WebSocketPort}  (경로 {NetworkConstants.WebSocketPath})");
            Console.WriteLine($" ProtocolVersion : {NetworkConstants.ProtocolVersion} (80B v3: 6DoF+이동축)");
            Console.WriteLine("----------------------------------------------------------------------");
            Console.WriteLine(" 서비스는 0.0.0.0(모든 인터페이스)에 바인딩되어 어느 IP로도 수신됩니다.");
            Console.WriteLine(" S10e 연결(수동 권장): 폰 HUD의 IP 입력란에 폰과 같은 서브넷의 위 IP를 넣고 [연결].");
            Console.WriteLine("   · PC 모바일 핫스팟 사용 시 → 192.168.137.1 사용");
            Console.WriteLine("   · 도달성 확인: 폰 브라우저로 http://<그 IP>:" + NetworkConstants.WebSocketPort + "/ 접속 → alive 페이지");
            Console.WriteLine(" 방화벽에서 위 UDP 포트(인바운드)와 TCP WebSocket 포트를 허용하세요.");
            Console.WriteLine(" (Ctrl+C 종료)\n");
        }

        // ---------- 자가검증 ----------

        private static async Task<int> SelfTest()
        {
            Console.WriteLine("== PC 수신기 자가검증 (폰 없이 내부 루프) ==\n");
            int pass = 0, fail = 0;
            void Check(bool c, string label) { if (c) { pass++; Console.WriteLine($"  [PASS] {label}"); } else { fail++; Console.WriteLine($"  [FAIL] {label}"); } }

            const int wsPort = 47812, udpPort = 47810;

            var pairing = new PairingServer(NetworkConstants.ProtocolVersion);
            bool serverPaired = false;
            pairing.ClientPaired += _ => serverPaired = true;

            var ws = new TcpWebSocketServer(wsPort);
            ws.ClientConnected += (id, ch) => pairing.Register(id, ch);
            ws.Start();

            InputPacket? got = null;
            var rx = new InputStreamReceiver(udpPort);
            rx.PacketReceived += p => got = p;
            rx.Start();

            Thread.Sleep(100);

            // 표준 ClientWebSocket으로 접속 → 공유 TcpWebSocketServer 핸드셰이크/프레이밍 검증
            using var cw = new ClientWebSocket();
            await cw.ConnectAsync(new Uri($"ws://127.0.0.1:{wsPort}{NetworkConstants.WebSocketPath}"), CancellationToken.None);
            Check(cw.State == WebSocketState.Open, "WebSocket 핸드셰이크 성공");

            // PairRequest 송신 → PairAck 기대
            await SendText(cw, EventMessage.PairRequest(NetworkConstants.ProtocolVersion, "selftest").Encode());
            string reply = await ReceiveText(cw);
            bool ok = EventMessage.TryDecode(reply, out var ack);
            Check(ok && ack.Type == EventType.PairAck, $"PairAck 수신 ({reply})");
            Check(serverPaired, "서버 페어링 상태 반영");

            // UDP로 6DoF InputPacket 송신 → 수신 확인
            var tx = new InputStreamSender();
            tx.Connect("127.0.0.1", udpPort);
            var pos = new Vector3(0.12f, -0.34f, 1.56f);
            var rot = new Quaternion(0.1f, 0.2f, 0.3f, 0.9f);
            tx.Send(TouchPhaseCode.Move, 3, new Vector2(0.4f, 0.6f), pos, rot, Vector3.zero, new Vector2(0.7f, -0.3f), 1234);
            for (int i = 0; i < 100 && got == null; i++) Thread.Sleep(5);

            Check(got != null, "UDP 6DoF 패킷 수신");
            if (got != null)
            {
                var p = got.Value;
                Check(p.Position.x == pos.x && p.Position.y == pos.y && p.Position.z == pos.z, "Position 일치");
                Check(p.Rotation.x == rot.x && p.Rotation.w == rot.w, "Rotation 일치");
                Check(p.MoveAxis.x == 0.7f && p.MoveAxis.y == -0.3f, "MoveAxis(조이스틱) 일치");
                Check(p.TouchId == 3 && p.Phase == TouchPhaseCode.Move, "터치 필드 일치");
            }

            await cw.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            tx.Dispose(); rx.Dispose(); ws.Stop();

            Console.WriteLine($"\n== 결과: {pass} passed, {fail} failed ==");
            return fail == 0 ? 0 : 1;
        }

        private static async Task SendText(ClientWebSocket cw, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            await cw.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        private static async Task<string> ReceiveText(ClientWebSocket cw)
        {
            var buf = new byte[4096];
            var r = await cw.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);
            return Encoding.UTF8.GetString(buf, 0, r.Count);
        }

        // ---------- 통계 ----------

        private sealed class PacketStats
        {
            private readonly object _lock = new object();
            private bool _has;
            private uint _first, _last;
            private long _accepted;
            private InputPacket _latest;

            public void OnPacket(InputPacket p)
            {
                lock (_lock)
                {
                    if (!_has) { _first = p.Sequence; _has = true; }
                    _last = p.Sequence;
                    _accepted++;
                    _latest = p;
                }
            }

            public Snap Snapshot()
            {
                lock (_lock)
                {
                    if (!_has) return new Snap { HasData = false };
                    long span = (long)_last - _first + 1;
                    long lost = span - _accepted; // 수신 스레드 기준 근사 손실
                    var q = _latest.Rotation;
                    double mag = Math.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
                    return new Snap
                    {
                        HasData = true,
                        Lost = lost < 0 ? 0 : lost,
                        Position = _latest.Position.ToString(),
                        Rotation = q.ToString(),
                        RotMagnitude = mag,
                        Phase = _latest.Phase.ToString(),
                        NormalizedPos = _latest.NormalizedPos.ToString(),
                        Move = _latest.MoveAxis.ToString()
                    };
                }
            }

            public struct Snap
            {
                public bool HasData;
                public long Lost;
                public string Position, Rotation, Phase, NormalizedPos, Move;
                public double RotMagnitude;
            }
        }
    }
}
