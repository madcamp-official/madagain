using UnityEngine;

namespace Game.Sim
{
    /// <summary>
    /// 캐릭터 이동의 공통 뼈대. 벽 충돌은 ICollision(유니티 CapsuleCast)에 묻고,
    /// "막히면 벽면을 따라 미끄러진다"는 슬라이드 로직만 여기서 한다.
    /// 물리엔진을 짜는 게 아니라 질의를 조합하는 것.
    /// </summary>
    public static class CharacterMotor
    {
        const float Skin = 0.03f;
        const float MaxSubStep = 0.1f;   // 서브스텝 상한(m). 이보다 얇은 벽만 관통 위험 → 0.1이면 컨테이너(0.2) 안전

        /// <summary>
        /// 수평 이동. 이동량을 작은 서브스텝(≤MaxSubStep)으로 쪼개 각 스텝마다 스윕+슬라이드 →
        /// 고속(대시)에도 캡슐이 얇은 벽을 뛰어넘거나 파고들지 못해 관통을 막는다.
        /// ★ 플레이어·몹 모두 이 함수를 거치므로 한 곳 수정으로 전부 적용된다.
        /// </summary>
        public static Vector3 MoveHorizontal(ICollision col, Vector3 pos, Vector3 delta,
                                             float radius, float height)
        {
            delta.y = 0f;
            float dist = delta.magnitude;
            if (dist < 1e-6f) return pos;

            int steps = Mathf.Clamp(Mathf.CeilToInt(dist / MaxSubStep), 1, 16);
            Vector3 stepDelta = delta / steps;
            for (int s = 0; s < steps; s++)
                pos = MoveHorizontalStep(col, pos, stepDelta, radius, height);
            return pos;
        }

        /// <summary>한 서브스텝의 수평 이동 + 벽 슬라이드(최대 3회). 새 위치 반환.</summary>
        static Vector3 MoveHorizontalStep(ICollision col, Vector3 pos, Vector3 delta,
                                          float radius, float height)
        {
            for (int iter = 0; iter < 3 && delta.sqrMagnitude > 1e-8f; iter++)
            {
                float dist = delta.magnitude;
                Vector3 dir = delta / dist;
                Capsule(pos, radius, height, out var b, out var t);
                CastHit hit = col.CapsuleCast(b, t, radius, dir, dist + Skin);

                // 안 막히거나, 걸을 수 있는 경사(윗면)면 그대로 진행 → 높이는 지면 스냅이 처리
                if (!hit.hit || hit.normal.y > 0.6f) { pos += delta; break; }

                // 낮은 턱이면 올라타기(step-up): 턱 높이만큼 위에서 앞이 비었으면 올려서 전진.
                // NavMesh가 잇는 작은 턱을 모터도 넘게 해 "경로는 있는데 몸이 낌" 방지. 지면 스냅이 안착.
                Vector3 up = Vector3.up * SimConfig.StepHeight;
                Capsule(pos + up, radius, height, out var bUp, out var tUp);
                if (!col.CapsuleCast(bUp, tUp, radius, dir, dist + Skin).hit)
                { pos += up + delta; break; }

                float travel = Mathf.Max(0f, hit.distance - Skin);
                pos += dir * travel;

                Vector3 remaining = dir * (dist - travel);
                Vector3 n = hit.normal; n.y = 0f;
                if (n.sqrMagnitude < 1e-6f) break;
                n.Normalize();
                delta = remaining - Vector3.Dot(remaining, n) * n;  // 벽면에 투영 = 슬라이드
            }
            return pos;
        }

        /// <summary>
        /// 중력 적용 + 낙하/착지. grounded 갱신.
        /// radius>0이면 상승 중 위쪽 캡슐 스윕으로 천장 충돌까지 처리(아래에서 위로 뚫기 방지).
        /// radius를 안 넘기면(기존 호출) 천장 검사 생략 — 종전 동작 유지.
        /// </summary>
        public static void ResolveVertical(ICollision col, ref Vector3 pos, ref Vector3 vel,
                                           float dt, out bool grounded,
                                           float radius = 0f, float height = 0f)
        {
            vel.y += SimConfig.Gravity * dt;
            float dy = vel.y * dt;

            // 상승 중 천장 충돌: 위로 캡슐을 쏴서 막히면 천장 바로 아래서 멈추고 상승속도 제거.
            if (dy > 0f && radius > 0f)
            {
                Capsule(pos, radius, height, out var b, out var t);
                CastHit up = col.CapsuleCast(b, t, radius, Vector3.up, dy + Skin);
                if (up.hit)
                {
                    dy = Mathf.Max(0f, up.distance - Skin);
                    vel.y = 0f;
                }
            }
            pos.y += dy;

            if (col.SampleGround(pos, 100f, out float gy) && pos.y <= gy + 0.02f)
            {
                pos.y = gy;
                vel.y = 0f;
                grounded = true;
            }
            else grounded = false;
        }

        /// <summary>
        /// 겹침 해소 — 지오메트리에 파고들었으면 그만큼 밀어낸다.
        ///
        /// 스윕(CapsuleCast)만으로는 관통을 못 막는다. 유니티 스윕은 <b>시작 시점에 이미 겹친
        /// 콜라이더를 무시</b>하므로, 한 번 벽 안에 들어가면 그 벽이 없는 것처럼 통과하고
        /// 스스로 빠져나올 방법도 없다. 매 틱 이걸 돌려 "낀 상태"를 없애면 관통이 원천 차단된다.
        ///
        /// 한 틱에 밀 수 있는 양을 제한해, 계산이 튀어도 캐릭터가 순간이동하지 않게 한다.
        /// </summary>
        public static void ResolveOverlap(ICollision col, ref Vector3 pos, float radius, float height)
        {
            Vector3 push = col.Depenetrate(pos, radius, height);
            float sq = push.sqrMagnitude;
            if (sq < 1e-8f) return;
            if (sq > MaxDepenetration * MaxDepenetration) push = push / Mathf.Sqrt(sq) * MaxDepenetration;
            pos += push;
        }

        const float MaxDepenetration = 1f;   // 한 틱 밀어내기 상한(m) — 튐 방지

        public static void Capsule(Vector3 feet, float radius, float height,
                                   out Vector3 bottom, out Vector3 top)
        {
            bottom = feet + Vector3.up * radius;
            top    = feet + Vector3.up * (height - radius);
        }
    }
}
