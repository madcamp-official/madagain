using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>Scene 창 디버그. 적 위치·향하는 경로점을 그린다.</summary>
    public static class SimGizmos
    {
        public static void Draw(in SimWorld w)
        {
            if (w.enemies == null) return;

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(w.player.pos + Vector3.up * SimConfig.PlayerRadius, SimConfig.PlayerRadius);

            for (int i = 0; i < w.enemyCount; i++)
            {
                ref readonly EnemySim e = ref w.enemies[i];
                if (!e.alive) continue;

                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(e.pos + Vector3.up * SimConfig.EnemyRadius, SimConfig.EnemyRadius);

                if (e.hasWaypoint)
                {
                    Gizmos.color = new Color(1f, 0.5f, 0f);
                    Gizmos.DrawLine(e.pos + Vector3.up * 0.3f, e.waypoint + Vector3.up * 0.3f);
                    Gizmos.DrawWireCube(e.waypoint + Vector3.up * 0.3f, Vector3.one * 0.3f);
                }
            }
        }
    }
}
