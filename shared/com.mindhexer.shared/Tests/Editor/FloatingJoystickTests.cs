using NUnit.Framework;
using UnityEngine;
using MindHexer.Shared.Input;

namespace MindHexer.Shared.Tests
{
    /// <summary>플로팅 조이스틱(브롤스타즈식) 코어 EditMode 테스트.</summary>
    public sealed class FloatingJoystickTests
    {
        private const float Eps = 1e-4f;

        [Test]
        public void Press_SetsCenterAndZeroValue()
        {
            var j = new FloatingJoystick(radius: 100f, deadZone: 0f);
            j.Press(100f, 100f);
            Assert.IsTrue(j.Active);
            Assert.AreEqual(100f, j.Center.x, Eps);
            Assert.AreEqual(100f, j.Center.y, Eps);
            Assert.AreEqual(0f, j.Magnitude, Eps);
        }

        [Test]
        public void Drag_WithinRadius_NormalizesValue()
        {
            var j = new FloatingJoystick(radius: 100f, deadZone: 0f);
            j.Press(100f, 100f);
            j.Drag(150f, 100f); // dx=50 → x=0.5
            Assert.AreEqual(0.5f, j.Value.x, Eps);
            Assert.AreEqual(0f, j.Value.y, Eps);
        }

        [Test]
        public void Drag_BeyondRadius_ClampsToUnitAndKnobToRim()
        {
            var j = new FloatingJoystick(radius: 100f, deadZone: 0f);
            j.Press(100f, 100f);
            j.Drag(100f, 400f); // dy=300 > radius 100
            Assert.AreEqual(0f, j.Value.x, Eps);
            Assert.AreEqual(1f, j.Value.y, Eps, "위쪽 최대 세기 1");
            Assert.AreEqual(1f, j.Magnitude, Eps);
            // 노브는 링(중심+반지름)에 클램프, 중심은 고정.
            Assert.AreEqual(100f, j.Knob.x, Eps);
            Assert.AreEqual(200f, j.Knob.y, Eps);
            Assert.AreEqual(100f, j.Center.y, Eps, "중심 고정");
        }

        [Test]
        public void Release_ResetsValue_AndNextPressReCenters()
        {
            var j = new FloatingJoystick(radius: 100f, deadZone: 0f);
            j.Press(100f, 100f);
            j.Drag(100f, 200f);
            j.Release();
            Assert.IsFalse(j.Active);
            Assert.AreEqual(0f, j.Magnitude, Eps);

            // 브롤스타즈 핵심: 떼었다가 다시 누른 새 위치가 새 중심.
            j.Press(500f, 500f);
            Assert.IsTrue(j.Active);
            Assert.AreEqual(500f, j.Center.x, Eps);
            Assert.AreEqual(500f, j.Center.y, Eps);
        }

        [Test]
        public void DeadZone_KillsSmallInput_AndRescalesRest()
        {
            var j = new FloatingJoystick(radius: 100f, deadZone: 0.2f);
            j.Press(100f, 100f);

            j.Drag(100f, 110f); // mag 0.1 < 0.2 → 0
            Assert.AreEqual(0f, j.Magnitude, Eps);

            j.Drag(100f, 150f); // mag 0.5 → (0.5-0.2)/0.8 = 0.375
            Assert.AreEqual(0.375f, j.Value.y, Eps);
            Assert.AreEqual(0f, j.Value.x, Eps);
        }

        [Test]
        public void FollowOnOverflow_DragsCenter()
        {
            var j = new FloatingJoystick(radius: 100f, deadZone: 0f, followOnOverflow: true);
            j.Press(100f, 100f);
            j.Drag(100f, 300f); // dist 200 > 100 → center follows by 100 → (100,200)
            Assert.AreEqual(100f, j.Center.x, Eps);
            Assert.AreEqual(200f, j.Center.y, Eps);
            Assert.AreEqual(1f, j.Value.y, Eps);
            Assert.AreEqual(300f, j.Knob.y, Eps);
        }
    }
}
