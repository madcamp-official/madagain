using UnityEngine;

namespace Game.Sim
{
    /// <summary>전투 판정 헬퍼(구·부채꼴·선분). 좌표 계산만 — 결정론적, 빠름. ★ combat 소유.</summary>
    public static class CombatHit
    {
        /// <summary>부채꼴 안인가: 수평 거리 + 정면 각도.</summary>
        public static bool InCone(Vector3 origin, float yaw, Vector3 target, float range, float halfAngleDeg)
        {
            Vector3 to = target - origin; to.y = 0f;
            if (to.sqrMagnitude > range * range) return false;
            Vector3 fwd = new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));
            to.Normalize();
            return Vector3.Dot(fwd, to) >= Mathf.Cos(halfAngleDeg * Mathf.Deg2Rad);
        }

        /// <summary>yaw·pitch를 3D 시선 벡터로. pitch는 아래를 볼 때 +(유니티 관례).</summary>
        public static Vector3 LookDir(float yaw, float pitch)
        {
            float y = yaw * Mathf.Deg2Rad, p = pitch * Mathf.Deg2Rad;
            float cp = Mathf.Cos(p);
            return new Vector3(Mathf.Sin(y) * cp, -Mathf.Sin(p), Mathf.Cos(y) * cp);
        }

        /// <summary>
        /// 구 vs 직립 캡슐(적). 오버워치식 근접 판정 — 시선 앞에 구를 놓고 겹침만 본다.
        /// 각도 개념이 없어 "부채꼴 밖이라 안 맞음"이 생기지 않고, 피치가 그대로 반영된다.
        ///
        /// 적은 발밑 footPos에서 위로 height, 반지름 radius인 수직 캡슐로 본다.
        /// (오버워치도 넘어지든 말든 캡슐은 항상 수직이다)
        /// </summary>
        public static bool SphereHitsCapsule(Vector3 sphereCenter, float sphereRadius,
                                             Vector3 footPos, float radius, float height)
        {
            // 캡슐 심(axis)은 발밑+radius ~ 머리-radius 구간. 그 선분에서 가장 가까운 점까지의 거리로 판정.
            float half = Mathf.Max(0f, height * 0.5f - radius);
            Vector3 mid = footPos + Vector3.up * (height * 0.5f);
            float dy = Mathf.Clamp(sphereCenter.y - mid.y, -half, half);
            Vector3 closest = new Vector3(mid.x, mid.y + dy, mid.z);
            float r = sphereRadius + radius;
            return (sphereCenter - closest).sqrMagnitude <= r * r;
        }

        /// <summary>선분(돌진 경로) vs 구 (질풍참 관통용 — Phase 2).</summary>
        public static bool SegmentHitsSphere(Vector3 p0, Vector3 p1, Vector3 c, float radius)
        {
            Vector3 d = p1 - p0;
            float len2 = d.sqrMagnitude;
            float t = len2 > 1e-6f ? Mathf.Clamp01(Vector3.Dot(c - p0, d) / len2) : 0f;
            Vector3 closest = p0 + t * d;
            return (c - closest).sqrMagnitude <= radius * radius;
        }
    }
}
