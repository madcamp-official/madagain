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
    }
}
