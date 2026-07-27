using UnityEngine;

namespace MindHexer.Shared.Input
{
    /// <summary>
    /// IMU 선형가속도를 이중 적분해 위치를 추정한다(Unity 비의존, 브링업/폴백용).
    /// ARCore 같은 절대 트래커가 없을 때 "움직이면 반응하는" 위치를 즉시 제공한다.
    ///
    /// **한계**: 순수 IMU 적분은 드리프트가 커서 정확한 절대 위치가 아니다. 정지 시 속도 감쇠(ZUPT-lite)로
    /// 위치를 수렴시키지만 원점 복귀는 보장되지 않는다. 정밀 6DoF는 ARCore(poseSource)를 쓸 것.
    ///
    /// 입력은 **중력이 제거된** 선형가속도(m/s², 디바이스 프레임 권장 — Input.gyro.userAcceleration×9.81).
    /// UnityEngine.Vector3를 데이터 반환용으로만 쓰고 내부는 float 성분 연산(콘솔/EditMode·pc-receiver 호환).
    /// </summary>
    public sealed class ImuPositionEstimator
    {
        /// <summary>정지 시 속도 감쇠(1/초). 클수록 빨리 멈추고 드리프트가 준다.</summary>
        public float VelocityDamping = 3.0f;

        /// <summary>가속도 노이즈 게이트(m/s²). 이보다 작은 성분은 0으로 취급.</summary>
        public float AccelDeadZone = 0.30f;

        private float _vx, _vy, _vz;
        private float _px, _py, _pz;

        public Vector3 Position => new Vector3(_px, _py, _pz);
        public Vector3 Velocity => new Vector3(_vx, _vy, _vz);

        public void Reset()
        {
            _vx = _vy = _vz = 0f;
            _px = _py = _pz = 0f;
        }

        /// <summary>중력 제거된 선형가속도(m/s²)와 dt(초)로 속도·위치를 갱신.</summary>
        public void Integrate(Vector3 accel, float dt)
        {
            if (dt <= 0f) return;

            float ax = Gate(accel.x);
            float ay = Gate(accel.y);
            float az = Gate(accel.z);

            _vx += ax * dt;
            _vy += ay * dt;
            _vz += az * dt;

            float damp = 1f - VelocityDamping * dt;
            if (damp < 0f) damp = 0f;
            _vx *= damp; _vy *= damp; _vz *= damp;

            _px += _vx * dt;
            _py += _vy * dt;
            _pz += _vz * dt;
        }

        private float Gate(float v)
        {
            if (v > AccelDeadZone) return v - AccelDeadZone;
            if (v < -AccelDeadZone) return v + AccelDeadZone;
            return 0f;
        }
    }
}
