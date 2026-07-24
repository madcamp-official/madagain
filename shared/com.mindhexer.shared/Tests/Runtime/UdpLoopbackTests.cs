using System.Collections;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MindHexer.Shared.Protocol;
using MindHexer.Shared.Net;

namespace MindHexer.Shared.Tests
{
    /// <summary>
    /// 실제 UDP 루프백(127.0.0.1) PlayMode 테스트. Sender/Receiver 코어와 RTT Ping/Pong을 검증한다.
    /// 스레드·소켓을 쓰므로 EditMode가 아닌 PlayMode(UnityTest).
    /// 포트는 다른 테스트/스트림과 겹치지 않도록 4772x 대역 사용.
    /// </summary>
    public sealed class UdpLoopbackTests
    {
        // Environment.TickCount64 기반이 아니라 실제 시간 경과가 필요하므로 코루틴으로 대기.
        private static IEnumerator WaitUntil(System.Func<bool> cond, float timeoutSec)
        {
            var sw = Stopwatch.StartNew();
            while (!cond() && sw.Elapsed.TotalSeconds < timeoutSec)
                yield return null;
        }

        [UnityTest]
        public IEnumerator InputStream_MonotonicPacketsAllAccepted()
        {
            const int port = 47720;
            using var rx = new InputStreamReceiver(port);
            rx.Start();
            yield return new WaitForSeconds(0.05f);

            using var tx = new InputStreamSender();
            tx.Connect("127.0.0.1", port);

            const int n = 50;
            for (int i = 0; i < n; i++)
            {
                tx.Send(TouchPhaseCode.Move, 0, new Vector2(i / 50f, 0.5f),
                        new Vector3(i * 0.01f, 0f, 1f), Quaternion.identity, Vector3.zero, i);
                yield return null;
            }

            yield return WaitUntil(() => rx.AcceptedCount >= n, 3f);
            Assert.AreEqual(n, rx.AcceptedCount, "단조 패킷 전부 수용");
            Assert.AreEqual(0, rx.DiscardedCount, "이 단계 폐기 없음");
            Assert.AreEqual((uint)n, tx.SentCount);
        }

        [UnityTest]
        public IEnumerator InputStream_DiscardsReorderedAndDuplicate()
        {
            const int port = 47721;
            using var rx = new InputStreamReceiver(port);
            rx.Start();
            yield return new WaitForSeconds(0.05f);

            using var tx = new InputStreamSender();
            tx.Connect("127.0.0.1", port);

            InputPacket Mk(uint seq) => new InputPacket
            {
                Sequence = seq, TimestampMs = seq, TouchId = 0, Phase = TouchPhaseCode.Move,
                NormalizedPos = Vector2.zero, Position = Vector3.zero,
                Rotation = Quaternion.identity, Acceleration = Vector3.zero
            };

            tx.SendRaw(Mk(200)); yield return new WaitForSeconds(0.02f); // 수용
            tx.SendRaw(Mk(150)); yield return new WaitForSeconds(0.02f); // 폐기(과거)
            tx.SendRaw(Mk(200)); yield return new WaitForSeconds(0.02f); // 폐기(중복)
            tx.SendRaw(Mk(201)); yield return new WaitForSeconds(0.02f); // 수용

            yield return WaitUntil(() => rx.AcceptedCount >= 2 && rx.DiscardedCount >= 2, 3f);
            Assert.AreEqual(2, rx.AcceptedCount, "수용 2건");
            Assert.AreEqual(2, rx.DiscardedCount, "폐기 2건");

            Assert.IsTrue(rx.TryGetLatest(out var latest));
            Assert.AreEqual(201u, latest.Sequence);
        }

        [UnityTest]
        public IEnumerator Rtt_PingPong_MeasuresRoundTrip()
        {
            const int port = 47722;
            using var responder = new RttResponder(port);
            responder.Start();
            yield return new WaitForSeconds(0.05f);

            using var probe = new RttProbe();
            probe.Connect("127.0.0.1", port);

            const int pings = 10;
            for (int i = 0; i < pings; i++) { probe.SendPing(); yield return new WaitForSeconds(0.02f); }

            yield return WaitUntil(() => probe.ReceivedCount >= pings, 3f);
            Assert.AreEqual(pings, probe.SentCount);
            Assert.AreEqual(pings, probe.ReceivedCount, "Pong 전부 수신");
            Assert.AreEqual(pings, responder.EchoedCount, "응답기 에코");
            Assert.GreaterOrEqual(probe.LastRttMs, 0.0, "RTT 측정값 존재");
            Assert.IsTrue(probe.MeetsTarget, "루프백 RTT 목표 충족");
        }
    }
}
