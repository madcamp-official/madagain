using UnityEngine;

namespace Game.Sim
{
    public static class CombatMath
    {
        public static Vector3 Forward(float yaw)
            => new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));

        public static Vector3 FlatDirection(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            d.y = 0f;
            return d.sqrMagnitude > 1e-8f ? d.normalized : Vector3.forward;
        }

        public static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public static bool InCone(Vector3 origin, Vector3 forward, Vector3 target, float range, float halfAngleDeg)
        {
            Vector3 to = target - origin;
            to.y = 0f;
            float sqr = to.sqrMagnitude;
            if (sqr > range * range || sqr <= 1e-8f) return false;
            float minimumDot = Mathf.Cos(halfAngleDeg * Mathf.Deg2Rad);
            return Vector3.Dot(forward, to / Mathf.Sqrt(sqr)) >= minimumDot;
        }
    }
}
