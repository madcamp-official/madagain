using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 플레이스홀더 루트 생성기 — 전투 미포함, "이동 경로"만 만든다.
    /// 사정권 내 살아있는 적을 3가지 순서(근접순/원거리순/스윕)로 이어 NavMesh 경로를 뽑는다.
    /// 실제 전투 봇(Sim/Prediction)이 준비되면 이 자리를 그 산출물로 교체하면 된다.
    /// (연출은 PredictedRoute 계약만 소비하므로 여기 로직이 바뀌어도 연출은 그대로다.)
    /// </summary>
    public static class RoutePreviewStub
    {
        public static List<PredictedRoute> Build(in SimWorld w, float range, Color[] colors)
        {
            Vector3 pp = w.player.pos;

            // 사정권 내 살아있는 적 위치만 로컬로 추출(이후 ref 없이 순수 계산)
            var es = new List<Vector3>();
            for (int i = 0; i < w.enemyCount; i++)
            {
                ref readonly EnemySim e = ref w.enemies[i];
                if (!e.alive) continue;
                Vector3 d = e.pos - pp; d.y = 0f;
                if (d.sqrMagnitude <= range * range) es.Add(e.pos);
            }

            var routes = new List<PredictedRoute>
            {
                BuildRoute(pp, OrderNearest(pp, es),  colors[0 % colors.Length]),
                BuildRoute(pp, OrderByDistDesc(pp, es), colors[1 % colors.Length]),
                BuildRoute(pp, OrderBySweep(pp, es),  colors[2 % colors.Length]),
            };
            return routes;
        }

        // ── 순서 휴리스틱 3종 (지금은 이동만 다양화. 스킬/전투는 실제 봇에서) ──
        static List<Vector3> OrderNearest(Vector3 start, List<Vector3> es)
        {
            var remaining = new List<Vector3>(es);
            var order = new List<Vector3>();
            Vector3 cur = start;
            while (remaining.Count > 0)
            {
                int best = 0; float bd = float.MaxValue;
                for (int k = 0; k < remaining.Count; k++)
                {
                    float d = (remaining[k] - cur).sqrMagnitude;
                    if (d < bd) { bd = d; best = k; }
                }
                cur = remaining[best];
                order.Add(cur);
                remaining.RemoveAt(best);
            }
            return order;
        }

        static List<Vector3> OrderByDistDesc(Vector3 start, List<Vector3> es)
        {
            var order = new List<Vector3>(es);
            order.Sort((a, b) => (b - start).sqrMagnitude.CompareTo((a - start).sqrMagnitude));
            return order;
        }

        static List<Vector3> OrderBySweep(Vector3 start, List<Vector3> es)
        {
            var order = new List<Vector3>(es);
            order.Sort((a, b) => Ang(start, a).CompareTo(Ang(start, b)));
            return order;
        }
        static float Ang(Vector3 c, Vector3 p) => Mathf.Atan2(p.z - c.z, p.x - c.x);

        // ── 순서대로 NavMesh 경로 이어붙이기 ──
        static PredictedRoute BuildRoute(Vector3 start, List<Vector3> targets, Color color)
        {
            var r = new PredictedRoute { color = color };
            r.path.Add(start);
            Vector3 cur = start;
            float len = 0f;
            foreach (var t in targets)
            {
                len += AppendPath(r.path, cur, t);
                r.kills.Add(t);
                cur = t;
            }
            r.seconds = len / Mathf.Max(0.01f, SimConfig.PlayerMoveSpeed);
            return r;
        }

        static readonly NavMeshPath scratch = new NavMeshPath();

        static float AppendPath(List<Vector3> path, Vector3 from, Vector3 to)
        {
            Vector3 a = Snap(from), b = Snap(to);
            if (NavMesh.CalculatePath(a, b, NavMesh.AllAreas, scratch) && scratch.corners.Length > 1)
            {
                float len = 0f;
                for (int k = 1; k < scratch.corners.Length; k++)
                {
                    path.Add(scratch.corners[k]);
                    len += Vector3.Distance(scratch.corners[k - 1], scratch.corners[k]);
                }
                return len;
            }
            path.Add(to);   // 폴백: 경로 실패 시 직선
            return Vector3.Distance(from, to);
        }

        static Vector3 Snap(Vector3 p)
            => NavMesh.SamplePosition(p, out var h, 4f, NavMesh.AllAreas) ? h.position : p;
    }
}
