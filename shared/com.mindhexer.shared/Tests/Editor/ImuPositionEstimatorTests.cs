using NUnit.Framework;
using UnityEngine;
using MindHexer.Shared.Input;

namespace MindHexer.Shared.Tests
{
    /// <summary>IMU 위치 추정기(이중 적분 + 감쇠) EditMode 테스트.</summary>
    public sealed class ImuPositionEstimatorTests
    {
        [Test]
        public void Reset_ZeroesState()
        {
            var e = new ImuPositionEstimator();
            e.Integrate(new Vector3(5f, 0f, 0f), 0.1f);
            e.Reset();
            Assert.AreEqual(0f, e.Position.x, 1e-6f);
            Assert.AreEqual(0f, e.Velocity.x, 1e-6f);
        }

        [Test]
        public void ConstantAccel_MovesPositionAlongAxis()
        {
            var e = new ImuPositionEstimator { AccelDeadZone = 0.3f, VelocityDamping = 3f };
            for (int i = 0; i < 5; i++) e.Integrate(new Vector3(2f, 0f, 0f), 0.05f);
            Assert.Greater(e.Position.x, 0f, "가속 방향으로 위치 증가");
            Assert.AreEqual(0f, e.Position.y, 1e-6f);
            Assert.AreEqual(0f, e.Position.z, 1e-6f);
        }

        [Test]
        public void SmallAccel_IsGatedByDeadZone()
        {
            var e = new ImuPositionEstimator { AccelDeadZone = 0.3f };
            for (int i = 0; i < 10; i++) e.Integrate(new Vector3(0.2f, -0.1f, 0.25f), 0.05f);
            Assert.AreEqual(0f, e.Position.x, 1e-6f, "데드존 이하 → 이동 없음");
            Assert.AreEqual(0f, e.Velocity.x, 1e-6f);
        }

        [Test]
        public void VelocityDecays_WhenStill()
        {
            var e = new ImuPositionEstimator { AccelDeadZone = 0.3f, VelocityDamping = 3f };
            // 잠깐 가속해 속도를 만든 뒤
            for (int i = 0; i < 3; i++) e.Integrate(new Vector3(4f, 0f, 0f), 0.05f);
            float vAfterPush = Mathf.Abs(e.Velocity.x);
            Assert.Greater(vAfterPush, 0f);

            // 정지(가속 0)에서 속도가 감쇠하는지
            for (int i = 0; i < 30; i++) e.Integrate(Vector3.zero, 0.05f);
            Assert.Less(Mathf.Abs(e.Velocity.x), vAfterPush * 0.1f, "정지 시 속도 감쇠");
        }
    }
}
