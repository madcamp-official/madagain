using UnityEngine;
using Game.Sim;

namespace Game.Bridge
{
    /// <summary>
    /// ICollision 구현 = 유니티 정적 충돌 질의. 물리엔진을 짜는 게 아니라 CapsuleCast/Raycast를 부른다.
    /// 정적 지형이라 같은 입력 → 같은 결과(결정론). 예측이 복제 상태로 물어도 안전.
    /// </summary>
    public class PhysicsCollision : ICollision
    {
        readonly int mask;

        public PhysicsCollision(int layerMask) { mask = layerMask; }

        public CastHit CapsuleCast(Vector3 bottom, Vector3 top, float radius, Vector3 dir, float maxDist)
        {
            if (Physics.CapsuleCast(bottom, top, radius, dir, out var hit, maxDist,
                                    mask, QueryTriggerInteraction.Ignore))
                return new CastHit { hit = true, distance = hit.distance, normal = hit.normal };
            return default;
        }

        public CastHit Raycast(Vector3 origin, Vector3 dir, float maxDist)
        {
            if (Physics.Raycast(origin, dir, out var hit, maxDist, mask, QueryTriggerInteraction.Ignore))
                return new CastHit { hit = true, distance = hit.distance, normal = hit.normal };
            return default;
        }

        public bool SampleGround(Vector3 feet, float maxDown, out float groundY)
        {
            Vector3 origin = feet + Vector3.up * 0.5f;
            if (Physics.Raycast(origin, Vector3.down, out var hit, maxDown + 0.5f,
                                mask, QueryTriggerInteraction.Ignore))
            {
                groundY = hit.point.y;
                return true;
            }
            groundY = 0f;
            return false;
        }

        // >>> [예측 세션 추가] 아래 두 메서드는 프로젝트 병합 때 예측 쪽(prediction-foundation)의
        // 독립 추가가 자동으로 합쳐진 것 — 기존 메서드는 안 건드림. Ports.cs의 ICollision에
        // 같은 이름으로 선언돼 있다.
        public bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance <= 1e-5f) return true;
            return !Physics.Raycast(from, delta / distance, distance, mask, QueryTriggerInteraction.Ignore);
        }

        public bool CanOccupyCapsule(Vector3 feet, float radius, float height)
        {
            CharacterMotor.Capsule(feet, radius, height, out Vector3 bottom, out Vector3 top);
            return !Physics.CheckCapsule(bottom, top, radius, mask, QueryTriggerInteraction.Ignore);
        }
        // <<< [예측 세션 추가 끝]

        // ── 겹침 해소(depenetration) ──
        // 스윕은 "이미 겹친 콜라이더"를 무시하므로, 한 번 지오메트리 안에 들어가면 그 벽이
        // 사라진 것처럼 통과해 버린다. 매 틱 파고든 만큼 밀어내 그 상태 자체를 없앤다.
        const int MaxOverlaps = 16;
        readonly Collider[] overlapBuf = new Collider[MaxOverlaps];
        CapsuleCollider proxy;   // ComputePenetration은 Collider 인스턴스가 필요해 프록시를 쓴다

        public Vector3 Depenetrate(Vector3 feet, float radius, float height)
        {
            CharacterMotor.Capsule(feet, radius, height, out Vector3 bottom, out Vector3 top);
            int n = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, overlapBuf,
                                                   mask, QueryTriggerInteraction.Ignore);
            if (n <= 0) return Vector3.zero;

            // 결정론: OverlapCapsule의 결과 순서는 보장되지 않는다.
            // 그래서 정렬하는 대신 <b>순서에 무관한 계산</b>을 쓴다 — 모든 침투를 '원래 위치' 기준으로
            // 재고 그냥 더한다. 덧셈은 교환법칙이 성립하므로 어떤 순서로 와도 결과가 같다
            // (예측 포크와 실제 재생이 항상 일치). 누적 방식은 순서에 따라 값이 달라져 못 쓴다.
            //
            // 대가: 구석에서 두 벽이 각각 온전히 밀어 살짝 과보정될 수 있다. 상한(모터 쪽 1m)으로
            // 튐을 막고, 남은 오차는 다음 틱에 다시 줄어들어 실용상 문제가 없다.
            CapsuleCollider self = Proxy(radius, height);
            Vector3 total = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                Collider c = overlapBuf[i];
                if (c == null || c == self) continue;
                if (Physics.ComputePenetration(self, feet, Quaternion.identity,
                                               c, c.transform.position, c.transform.rotation,
                                               out Vector3 dir, out float dist))
                    total += dir * dist;
            }
            return total;
        }

        /// <summary>
        /// 침투 계산용 캡슐 프록시. Ignore Raycast(레이어 2)에 두어 우리 질의(DefaultRaycastLayers)에
        /// 절대 안 잡히게 한다 — 안 그러면 자기 자신이 벽으로 잡힌다.
        /// </summary>
        CapsuleCollider Proxy(float radius, float height)
        {
            if (proxy == null)
            {
                var go = new GameObject("[DepenetrateProxy]") { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
                proxy = go.AddComponent<CapsuleCollider>();
            }
            proxy.direction = 1;                                   // Y축 캡슐
            proxy.radius = radius;
            proxy.height = Mathf.Max(height, radius * 2f);         // 유니티 규칙: height >= 2r
            proxy.center = new Vector3(0f, proxy.height * 0.5f, 0f);  // 기준점 = 발밑
            return proxy;
        }
    }
}
