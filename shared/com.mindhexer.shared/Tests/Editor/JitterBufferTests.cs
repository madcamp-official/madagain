using NUnit.Framework;
using UnityEngine;
using MindHexer.Shared.Net;
using MindHexer.Shared.Protocol;

namespace MindHexer.Shared.Tests
{
    /// <summary>지터 버퍼(시간 보간 + 재생 커서 + Slerp) EditMode 테스트.</summary>
    public sealed class JitterBufferTests
    {
        private const float Eps = 1e-3f;

        private static InputPacket Mk(long ts, float x, Quaternion rot)
            => new InputPacket
            {
                TimestampMs = ts,
                Position = new Vector3(x, 0f, 0f),
                NormalizedPos = new Vector2(x, 0f),
                MoveAxis = new Vector2(x, 0f),
                Acceleration = Vector3.zero,
                Rotation = rot
            };

        [Test]
        public void SampleAt_InterpolatesContinuousFieldsLinearly()
        {
            var jb = new JitterBuffer { Adaptive = false, TargetDelayMs = 0f, MinDelayMs = 0f };
            jb.Push(Mk(0, 0f, Quaternion.identity), 0);
            jb.Push(Mk(100, 10f, Quaternion.identity), 1);
            jb.Push(Mk(200, 20f, Quaternion.identity), 2);

            Assert.IsTrue(jb.SampleAt(50, out var a));
            Assert.AreEqual(5f, a.Position.x, Eps, "위치 선형보간");
            Assert.AreEqual(5f, a.MoveAxis.x, Eps, "이동축 선형보간");
            Assert.AreEqual(5f, a.NormalizedPos.x, Eps);

            Assert.IsTrue(jb.SampleAt(150, out var b));
            Assert.AreEqual(15f, b.Position.x, Eps);
        }

        [Test]
        public void SampleAt_HoldsAtEdges()
        {
            var jb = new JitterBuffer { Adaptive = false, TargetDelayMs = 0f, MinDelayMs = 0f };
            jb.Push(Mk(0, 0f, Quaternion.identity), 0);
            jb.Push(Mk(100, 10f, Quaternion.identity), 1);

            Assert.IsTrue(jb.SampleAt(-50, out var lo));
            Assert.AreEqual(0f, lo.Position.x, Eps, "가장 오래된 값 유지");
            Assert.IsTrue(jb.SampleAt(9999, out var hi));
            Assert.AreEqual(10f, hi.Position.x, Eps, "가장 최신 값 유지(언더런)");
        }

        [Test]
        public void Empty_ReturnsFalse()
        {
            var jb = new JitterBuffer();
            Assert.IsFalse(jb.SampleAt(0, out _));
            Assert.IsFalse(jb.TrySample(out _));
        }

        [Test]
        public void Advance_ConvergesToNewestMinusDelay()
        {
            var jb = new JitterBuffer
            {
                Adaptive = false, TargetDelayMs = 50f, MinDelayMs = 0f, MaxDelayMs = 250f, CatchupRate = 8f
            };
            jb.Push(Mk(0, 0f, Quaternion.identity), 0);
            jb.Push(Mk(100, 10f, Quaternion.identity), 1);
            jb.Push(Mk(200, 20f, Quaternion.identity), 2);

            jb.Advance(1000.0); // a = clamp(8 * 1.0) = 1 → 즉시 목표 도달
            Assert.AreEqual(150.0, jb.PlaybackTs, 1e-6, "재생 커서 = newest(200) - delay(50)");

            Assert.IsTrue(jb.TrySample(out var s));
            Assert.AreEqual(15f, s.Position.x, Eps, "150 시점 보간");
        }

        [Test]
        public void Slerp_HalfwayIsUnitAndCorrect()
        {
            // identity → 90° about Y. 중간(45°)은 w=cos(22.5°)=0.92388, y=sin(22.5°)=0.38268.
            var q90 = new Quaternion(0f, 0.70710678f, 0f, 0.70710678f);
            var jb = new JitterBuffer { Adaptive = false, TargetDelayMs = 0f, MinDelayMs = 0f };
            jb.Push(Mk(0, 0f, Quaternion.identity), 0);
            jb.Push(Mk(100, 0f, q90), 1);

            Assert.IsTrue(jb.SampleAt(50, out var r));
            Quaternion q = r.Rotation;
            float mag = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            Assert.AreEqual(1f, mag, 1e-3f, "Slerp 결과는 단위 쿼터니언");
            Assert.AreEqual(0.92388f, q.w, 1e-2f, "45° → w=cos22.5°");
            Assert.AreEqual(0.38268f, q.y, 1e-2f, "45° → y=sin22.5°");
        }

        // ---- 지연 보정(예측 외삽) ----

        [Test]
        public void SampleExtrapolated_PredictsForwardWithVelocity()
        {
            var jb = new JitterBuffer { Adaptive = false, TargetDelayMs = 0f, MinDelayMs = 0f, VelocityWindowMs = 40f };
            jb.Push(Mk(0, 0f, Quaternion.identity), 0);
            jb.Push(Mk(20, 2f, Quaternion.identity), 20);
            jb.Push(Mk(40, 4f, Quaternion.identity), 40); // 속도 0.1 x/ms

            // 최신(40) 너머 60ms 시점 → 예측 x=6
            Assert.IsTrue(jb.SampleExtrapolated(60, out var e));
            Assert.AreEqual(6f, e.Position.x, 1e-3f, "속도 외삽(예측)");
            // 최신 이하 시점은 기존 보간과 동일
            Assert.IsTrue(jb.SampleExtrapolated(30, out var i));
            Assert.AreEqual(3f, i.Position.x, 1e-3f, "과거 시점은 보간");
        }

        [Test]
        public void ClockOffset_TracksMinArrivalMinusSend()
        {
            var jb = new JitterBuffer();
            Assert.IsFalse(jb.HasClock, "수신 전엔 시계 미확보");

            jb.Push(Mk(0, 0f, Quaternion.identity), 1000);   // off 1000
            jb.Push(Mk(20, 2f, Quaternion.identity), 1030);  // off 1010 (지터 +10)
            jb.Push(Mk(40, 4f, Quaternion.identity), 1041);  // off 1001

            Assert.IsTrue(jb.HasClock);
            Assert.AreEqual(1000.0, jb.ClockOffsetMs, 0.2, "최소 오프셋을 추종(지터에 안 끌려감)");
        }

        [Test]
        public void SampleCompensated_UsesClockToPredictNow()
        {
            var jb = new JitterBuffer
            {
                Adaptive = false, TargetDelayMs = 0f, MinDelayMs = 0f,
                LatencyCompensation = true, PredictAheadMs = 0f, MaxExtrapolationMs = 200f, VelocityWindowMs = 40f
            };
            // 오프셋 1000, 지터 0: ts=0,20,40 / arrival=1000,1020,1040
            jb.Push(Mk(0, 0f, Quaternion.identity), 1000);
            jb.Push(Mk(20, 2f, Quaternion.identity), 1020);
            jb.Push(Mk(40, 4f, Quaternion.identity), 1040);

            // 헤드셋 now=1060 → 송신축 60 예측 → x=6, 리드 20
            Assert.IsTrue(jb.SampleCompensated(1060, out var s));
            Assert.AreEqual(6f, s.Position.x, 0.1f, "지금 컨트롤러 위치를 예측");
            Assert.AreEqual(20.0, jb.LastLeadMs, 0.2, "예측 리드 = now − 최신");
        }

        [Test]
        public void SampleCompensated_ClampsToMaxExtrapolation()
        {
            var jb = new JitterBuffer
            {
                Adaptive = false, TargetDelayMs = 0f, MinDelayMs = 0f,
                LatencyCompensation = true, PredictAheadMs = 0f, MaxExtrapolationMs = 30f, VelocityWindowMs = 40f
            };
            jb.Push(Mk(0, 0f, Quaternion.identity), 1000);
            jb.Push(Mk(20, 2f, Quaternion.identity), 1020);
            jb.Push(Mk(40, 4f, Quaternion.identity), 1040);

            // now=1200 → 200ms 앞 예측이지만 상한 30ms로 클램프 → 리드 30
            Assert.IsTrue(jb.SampleCompensated(1200, out _));
            Assert.AreEqual(30.0, jb.LastLeadMs, 1e-6, "외삽 상한 클램프");
        }

        [Test]
        public void SampleCompensated_FallsBackWhenDisabled()
        {
            var jb = new JitterBuffer
            {
                Adaptive = false, TargetDelayMs = 50f, MinDelayMs = 0f, MaxDelayMs = 250f,
                CatchupRate = 8f, LatencyCompensation = false
            };
            jb.Push(Mk(0, 0f, Quaternion.identity), 1000);
            jb.Push(Mk(100, 10f, Quaternion.identity), 1100);
            jb.Push(Mk(200, 20f, Quaternion.identity), 1200);
            jb.Advance(1000.0); // 목표 즉시 도달: newest(200) - delay(50) = 150

            Assert.IsTrue(jb.SampleCompensated(99999, out var s));
            Assert.AreEqual(15f, s.Position.x, Eps, "보정 off → 지연 재생(TrySample)");
            Assert.AreEqual(0.0, jb.LastLeadMs, 1e-9, "off면 예측 리드 0");
        }
    }
}
