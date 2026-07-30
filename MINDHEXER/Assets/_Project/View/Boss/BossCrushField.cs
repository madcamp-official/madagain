using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 보스가 지나는 자리의 <b>소품을 물리로 풀어 날려 버린다</b>. (기초_설계안 §0.4)
    ///
    /// <para>보스는 스케일이 50이라 몸이 닿는 것을 하나하나 배치해 둘 수 없다. 그래서 진행 경로를
    /// 통째로 훑어 걸리는 것을 <b>부모에서 떼고 Rigidbody를 붙여</b> 밀어낸다. 부수는 게 아니라
    /// <b>고정을 푸는 것</b>이라 별도 파괴 메시가 필요 없다.</para>
    ///
    /// <para><b>★ 대상은 반드시 한정해야 한다.</b> 씬 렌더러가 2만 개가 넘는다. 무제한으로 걸면
    /// <list type="number">
    /// <item>바닥·벽까지 날아가 보스도 플레이어도 허공에 남는다</item>
    /// <item>Rigidbody가 한꺼번에 수백 개 생겨 프레임이 죽는다</item>
    /// <item>구조가 무너져 플레이어가 지나갈 길이 사라진다</item>
    /// </list>
    /// 그래서 <see cref="breakableMask"/>(소품 레이어)로 거르고, 살아 있는 파편 수에
    /// <see cref="maxDebris"/> 상한을 둔다. 상한을 넘으면 <b>가장 오래된 것부터 치운다</b> —
    /// 새 파편을 막으면 보스 앞의 것이 안 부서져 더 어색하다.</para>
    ///
    /// <para><b>매 프레임 훑지 않는다.</b> OverlapBox는 넓을수록 비싼데 보스는 느리다.
    /// <see cref="scanInterval"/>마다 한 번이면 충분하고, 그 사이 이동분은 상자 길이로 덮는다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class BossCrushField : MonoBehaviour
    {
        [Header("범위 (보스 로컬 기준)")]
        [Tooltip("훑을 상자의 중심(로컬 m). 보스 발밑~상체를 덮게 잡는다.")]
        public Vector3 center = new Vector3(0f, 20f, 10f);

        [Tooltip("훑을 상자의 크기(로컬 m). 앞쪽(z)은 스캔 간격 동안 이동하는 거리보다 길어야 " +
                 "빈틈이 안 생긴다.")]
        public Vector3 size = new Vector3(60f, 40f, 40f);

        [Tooltip("★ 부술 것의 레이어. <b>소품만</b> 넣을 것 — 바닥·벽이 들어가면 스테이지가 무너진다.")]
        public LayerMask breakableMask;

        [Header("빈도")]
        [Tooltip("훑는 간격(초). 보스가 느려서 매 프레임 할 이유가 없다.")]
        public float scanInterval = 0.12f;

        [Header("날리기")]
        [Tooltip("파편에 주는 속도(m/s). 보스 진행 방향 + 위쪽으로 섞어 준다.")]
        public float launchSpeed = 18f;

        [Tooltip("위로 섞는 비율. 0이면 순수 전방, 1이면 절반쯤 위로 뜬다.")]
        [Range(0f, 1f)] public float upBias = 0.35f;

        [Tooltip("회전 세기(rad/s). 0이면 안 돈다.")]
        public float spin = 3f;

        [Tooltip("파편 질량. 실제 부피를 몰라 균일하게 준다 — 무거우면 안 날아간다.")]
        public float debrisMass = 20f;

        [Header("정리")]
        [Tooltip("파편이 사라지기까지(초). 0 이하면 안 지운다.")]
        public float debrisLife = 6f;

        [Tooltip("동시에 살아 있을 수 있는 파편 수. 넘으면 오래된 것부터 치운다.")]
        public int maxDebris = 60;

        [Header("디버그")]
        [Tooltip("훑는 상자를 씬 뷰에 그린다.")]
        public bool drawGizmo = true;

        float _nextScan;

        // 이미 처리한 것 — 같은 것을 두 번 날리면 힘이 겹쳐 튕겨 나간다.
        readonly HashSet<Transform> _taken = new HashSet<Transform>();
        readonly Queue<GameObject> _debris = new Queue<GameObject>();

        static readonly Collider[] Buf = new Collider[128];

        void Update()
        {
            // 추격 중에만 부순다 — 전반부에 보스가 서 있기만 해도 주변이 날아가면 안 된다.
            if (!BossChaseState.Active) return;
            if (Time.time < _nextScan) return;
            _nextScan = Time.time + Mathf.Max(0.02f, scanInterval);
            Scan();
        }

        void Scan()
        {
            Vector3 c = transform.TransformPoint(center);
            Vector3 half = Vector3.Scale(size, transform.lossyScale) * 0.5f;

            int n = Physics.OverlapBoxNonAlloc(c, half, Buf, transform.rotation,
                                               breakableMask, QueryTriggerInteraction.Ignore);
            if (n == 0) return;

            Vector3 dir = transform.forward;
            Vector3 launch = (dir + Vector3.up * upBias).normalized * launchSpeed;

            for (int i = 0; i < n; i++)
            {
                var col = Buf[i];
                if (col == null) continue;

                // 보스 자신은 제외. 레이어를 잘못 잡았을 때 자기 몸을 날려 버리는 사고를 막는다.
                if (col.transform.IsChildOf(transform)) continue;

                Transform t = col.transform;
                if (!_taken.Add(t)) continue;

                Release(t, launch);
            }
        }

        /// <summary>고정을 풀고 날린다.</summary>
        void Release(Transform t, Vector3 launch)
        {
            // ① 부모에서 분리. 안 떼면 부모(레일·플랫폼 등)가 계속 좌표를 덮어써 물리가 안 먹는다.
            t.SetParent(null, true);

            // ② 파편이 플레이어를 막으면 안 된다 — 길을 뚫으러 부수는 건데 잔해가 새 벽이 되면 최악이다.
            foreach (var c in t.GetComponentsInChildren<Collider>(true))
                c.isTrigger = true;

            // ③ 물리
            var rb = t.GetComponent<Rigidbody>();
            if (rb == null) rb = t.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.useGravity = true;
            rb.mass = Mathf.Max(0.01f, debrisMass);
            rb.linearVelocity = launch;
            if (spin > 0f)
                rb.angularVelocity = Random.onUnitSphere * spin;

            _debris.Enqueue(t.gameObject);
            if (debrisLife > 0f) Destroy(t.gameObject, debrisLife);

            // ④ 상한 — 오래된 것부터 치운다. 새 파편을 막으면 보스 앞의 것이 안 부서져 더 어색하다.
            while (_debris.Count > Mathf.Max(1, maxDebris))
            {
                var old = _debris.Dequeue();
                if (old != null) Destroy(old);
            }
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (!drawGizmo) return;
            Gizmos.color = new Color(1f, 0.3f, 0.1f, 0.25f);
            Gizmos.matrix = Matrix4x4.TRS(transform.TransformPoint(center), transform.rotation,
                                          Vector3.Scale(size, transform.lossyScale));
            Gizmos.DrawCube(Vector3.zero, Vector3.one);
            Gizmos.color = new Color(1f, 0.3f, 0.1f, 0.9f);
            Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        }
#endif
    }
}
