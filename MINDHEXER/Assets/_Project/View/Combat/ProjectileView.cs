using System.Collections.Generic;
using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 투사체를 구(sphere)로 비추는 거울. ★ 독립 뷰 — Main.Instance.World만 읽음.
    /// 외형은 잠정(그냥 동그라미) — 연출은 다른 곳에서. 콜라이더 없음(판정은 Sim).
    /// </summary>
    public class ProjectileView : MonoBehaviour
    {
        readonly List<Transform> balls = new List<Transform>();
        // [2026-07-22] 투사체가 새로 생기는 순간 = 누군가 총을 쐈다 → 발사음. 지상·공중 원거리 공통.
        readonly List<bool> alivePrev = new List<bool>();

        // [2026-07-22] 투사체가 몸 중앙에서 나오는 걸 총구 쪽으로 보이게 하는 렌더 전용 오프셋.
        //   총(원거리 몹)은 몸통 정면-왼쪽으로 들려 있다(EntityViews.RangedAimYawOffset=45°) — 그래서
        //   총구는 발사 방향 기준 앞 + 왼쪽에 있다. 판정(Sim)은 그대로 두고 <b>보이는 위치만</b> 옮긴다.
        //   상수만큼 평행 이동하므로 탄도 직선성은 유지되고, 히트 판정과의 차이는 무시 가능(~0.4m).
        const float MuzzleForward = 0.42f;   // 발사 방향으로 앞쪽
        const float MuzzleSide    = 0.34f;   // 총을 든 왼쪽으로
        const float MuzzleDown    = 0.12f;   // 눈높이 발사점을 총구 높이로 살짝 내림

        void Update()
        {
            var main = Main.Instance;
            if (main == null) return;
            ref readonly SimWorld w = ref main.World;

            while (balls.Count < w.projectileCount) { balls.Add(MakeBall(balls.Count)); alivePrev.Add(false); }

            for (int i = 0; i < balls.Count; i++)
            {
                bool active = i < w.projectileCount && w.projectiles[i].alive;
                // 비활성→활성 = 이번 프레임에 새로 발사됨 → 발사한 몹 종류에 맞는 레이저음.
                if (active && !alivePrev[i])
                {
                    Vector3 spawn = w.projectiles[i].pos;
                    if (FirerIsFlying(in w, spawn)) CombatAudio.EnemyFireAir(spawn);
                    else                            CombatAudio.EnemyFireGround(spawn);
                }
                alivePrev[i] = active;
                balls[i].gameObject.SetActive(active);
                if (!active) continue;
                Vector3 vel = w.projectiles[i].vel;
                balls[i].position = w.projectiles[i].pos + MuzzleOffset(vel);
                // 길쭉한 직육면체의 긴 축(로컬 +Z)을 진행 방향으로 눕혀 "날아오는" 느낌을 준다.
                if (vel.sqrMagnitude > 1e-6f)
                    balls[i].rotation = Quaternion.LookRotation(vel);
            }
        }

        /// <summary>이 투사체를 쏜 몹이 공중몹인가 — 발사 지점에서 가장 가까운 적으로 되짚는다
        /// (투사체는 방금 그 몹 눈높이에서 생겼으므로 가장 가까운 적 = 발사한 몹).</summary>
        static bool FirerIsFlying(in SimWorld w, Vector3 projPos)
        {
            int best = -1; float bestSq = float.MaxValue;
            for (int i = 0; i < w.enemyCount; i++)
            {
                if (!w.enemies[i].alive) continue;
                float sq = (w.enemies[i].pos - projPos).sqrMagnitude;
                if (sq >= bestSq) continue;
                bestSq = sq; best = i;
            }
            return best >= 0 && w.enemies[best].ai.mobility == MobilityType.Flying;
        }

        /// <summary>발사 방향(투사체 속도)에서 총구 위치로 가는 렌더 오프셋. 속도가 0이면 오프셋 없음.</summary>
        static Vector3 MuzzleOffset(Vector3 vel)
        {
            Vector3 f = vel; f.y = 0f;
            if (f.sqrMagnitude < 1e-6f) return Vector3.zero;
            f.Normalize();
            Vector3 left = new Vector3(-f.z, 0f, f.x);   // 발사 방향의 왼쪽(총을 든 쪽)
            return f * MuzzleForward + left * MuzzleSide + Vector3.down * MuzzleDown;
        }

        Transform MakeBall(int idx)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Projectile_{idx}";
            Object.Destroy(go.GetComponent<Collider>());
            // 진행 방향(로컬 +Z)으로 길쭉한 직육면체 — 단면은 얇게, 길이는 길게.
            float d = AIConfig.ProjectileRadius * 2f;
            go.transform.localScale = new Vector3(d * 0.7f, d * 0.7f, d * 3f);
            go.GetComponent<Renderer>().material = Mat(new Color(1f, 0.18f, 0.12f));  // 빨강
            return go.transform;
        }

        static Material Mat(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            var m = new Material(sh); m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class ProjectileViewBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<ProjectileView>() == null)
                new GameObject("[ProjectileView]").AddComponent<ProjectileView>();
        }
    }
}
