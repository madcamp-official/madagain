using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 경비병 파괴 — 원본(스킨드 메시)을 숨기고 미리 구워둔 조각을 흩뿌린다. (기초_설계안 §12.0 소규모)
    ///
    /// <para>조각 10개 정도는 최신 모바일에서 <b>런타임 리지드바디로 충분히 싸다</b>(§12.0).
    /// 베이크는 조각이 수십~수백인 대규모 setpiece의 몫이고, 경비병은 죽는 자세·위치가 매번 달라
    /// 오히려 베이크가 안 맞는다.</para>
    ///
    /// <para><b>충돌 규칙</b>(§12.1): 잔해는 <b>플레이어와 충돌하지 않고 지형과만 충돌</b>한다.
    /// 레이어 매트릭스 대신 <see cref="Physics.IgnoreCollision"/>를 스폰 시 걸어 처리한다 —
    /// 플레이어가 지형과 같은 레이어(Default)에 있어서, 레이어로 끊으면 <b>바닥까지 통과</b>해 버린다.</para>
    ///
    /// <para>해킹당한 뒤의 <b>고장 정지</b>는 파괴가 아니다(§경비병 메커니즘) — 그건 별도 상태다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class GuardDestruction : MonoBehaviour
    {
        [Tooltip("미리 구워둔 조각 프리팹들(GuardDebris/). 각자 Rigidbody+BoxCollider를 갖고 있다.")]
        public GameObject[] debrisPrefabs;

        [Header("흩어짐 — 폭발이 아니라 '무너져 내림'")]
        [Tooltip("바깥으로 밀어내는 세기(m/s). 아주 작게 둔다 — 크면 폭발처럼 튄다.")]
        public float burst = 0.4f;

        [Tooltip("위로 띄우는 정도. 0에 가까울수록 제자리에서 주저앉는다.")]
        public float upBias = 0.1f;

        [Tooltip("회전 임펄스 세기.")]
        public float spin = 1f;

        [Tooltip("이 시간(초) 뒤 조각을 정리한다. 드로우콜 회수 — 방치하면 계속 쌓인다.")]
        public float life = 8f;

        /// <summary>이미 파괴된 개체인가(중복 호출 방지).</summary>
        public bool Destroyed { get; private set; }

        /// <summary>파괴 실행. <paramref name="hitDir"/>는 맞은 방향(월드, 정규화 불필요·0이면 무방향).</summary>
        public void Destruct(Vector3 hitDir)
        {
            if (Destroyed) return;
            Destroyed = true;

            Vector3 center = Center();

            // 원본을 먼저 지운다 — 조각과 겹쳐 보이면 순간 두 겹으로 뜬다.
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = false;
            var anim = GetComponentInChildren<Animator>(true);
            if (anim != null) anim.enabled = false;

            var playerCols = FindPlayerColliders();

            if (debrisPrefabs != null)
            {
                for (int i = 0; i < debrisPrefabs.Length; i++)
                {
                    var p = debrisPrefabs[i];
                    if (p == null) continue;

                    // 조각 메시는 경비병 루트 기준으로 구웠다 → 루트와 같은 자세로 놓으면 원래 실루엣이 된다.
                    var go = Instantiate(p, transform.position, transform.rotation);
                    go.transform.localScale = transform.lossyScale;

                    var rb = go.GetComponent<Rigidbody>();
                    if (rb != null)
                    {
                        Vector3 out3 = go.GetComponent<Renderer>() != null
                            ? go.GetComponent<Renderer>().bounds.center - center
                            : Random.onUnitSphere;
                        if (out3.sqrMagnitude < 1e-4f) out3 = Random.onUnitSphere;
                        out3.Normalize();

                        Vector3 dir = hitDir.sqrMagnitude > 1e-4f ? hitDir.normalized : Vector3.zero;
                        rb.linearVelocity = (out3 + dir) * burst + Vector3.up * upBias;
                        rb.angularVelocity = Random.insideUnitSphere * spin;
                    }

                    // 잔해는 플레이어를 밀거나 끼우지 않는다(§12.1).
                    var col = go.GetComponent<Collider>();
                    if (col != null)
                        for (int k = 0; k < playerCols.Length; k++)
                            if (playerCols[k] != null) Physics.IgnoreCollision(col, playerCols[k], true);

                    if (life > 0f) Destroy(go, life);
                }
            }

            // 껍데기는 조각 수명만큼만 남겨 둔다(참조가 물려 있을 수 있어 즉시 지우지 않는다).
            Destroy(gameObject, Mathf.Max(0.1f, life));
        }

        Vector3 Center()
        {
            var rends = GetComponentsInChildren<Renderer>(true);
            if (rends.Length == 0) return transform.position;
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return b.center;
        }

        /// <summary>플레이어 본체의 콜라이더들. CharacterController도 Collider라 그대로 잡힌다.</summary>
        static Collider[] FindPlayerColliders()
        {
            var fpp = Object.FindFirstObjectByType<FirstPersonPlayer>();
            if (fpp == null) return new Collider[0];
            return fpp.GetComponentsInChildren<Collider>(true);
        }
    }
}
