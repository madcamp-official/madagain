using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 떨어져 나간 부위를 몸통에 잇는 전선. Verlet 적분 + 거리 제약으로 시뮬레이션한다.
    ///
    /// 물리 엔진(Rigidbody+Joint)을 쓰지 않는 이유:
    ///   - 판정에 영향이 없는 순수 연출이라 물리 솔버가 필요 없다
    ///   - 몹 수십 마리 × 관절 수십 개는 비용이 큰데, Verlet은 배열 연산이라 훨씬 싸다
    ///   - 길이·늘어짐을 코드로 정확히 통제할 수 있다(관절은 튜닝이 까다롭다)
    ///
    /// 지형 충돌은 Sim이 쓰는 것과 같은 마스크로 캐스트한다. 몹·플레이어에는 콜라이더가
    /// 아예 없으므로(EntityViews가 전부 제거함) 자동으로 "지형에만 걸리는" 동작이 된다.
    /// </summary>
    public class DanglingWire : MonoBehaviour
    {
        // ── 시뮬레이션 상태 ──
        Vector3[] cur, prev;
        float segLen;
        DamagedPart cfg;
        Transform socket;      // 몸통 쪽 부착 본
        Transform payload;     // 전선 끝에 매달린 부위(조각)

        // ── 렌더 ──
        Mesh mesh;
        MeshFilter mf;
        MeshRenderer mr;
        Vector3[] verts;
        Vector3[] norms;
        Vector2[] uvs;
        const int Sides = 5;   // 단면 각수 — 5면 원통처럼 보이면서 정점이 적다

        // ── 충돌 ──
        // ★ 지형·다른 몹과는 부딪히지 않는다. 오로지 <b>자기 몸통</b>만 밀어낸다.
        //   짧게 대롱거리는 연출이라 지형 충돌은 얻는 것 없이 비용만 들고,
        //   몹이 붙어 있을 때 남의 몸에 걸리면 오히려 이상해진다.
        //   Physics를 아예 쓰지 않으므로(캐스트 0회) 개체가 많아도 부담이 없다.
        Transform bodyRoot;        // 자기 몸(뷰 루트)
        float bodyRadius = 0.35f;  // 몸통 반지름(월드, Init에서 실측)
        float bodyBottom, bodyTop; // 몸통 캡슐 상하 높이(월드)

        /// <summary>
        /// 자기 몸통을 밀어낼지(다른 몹·지형과는 부딪히지 않는다).
        /// 캡슐 반지름은 <b>소켓 위치를 기준으로</b> 잡는다 — 바운즈 전체로 잡으면
        /// 팔을 벌린 자세에서 반지름이 부풀어 전선이 항상 밀려나 굳어 버린다.
        /// </summary>
        public static bool CollideWithBody = true;

        /// <summary>이 전선이 달린 몹. 사라지면 전선·조각도 같이 정리된다.</summary>
        public Transform owner;
        /// <summary>전선 끝에 매달린 조각(같이 파괴한다).</summary>
        public Transform detachedPart;
        /// <summary>조각 메시가 사는 공간(스킨 렌더러 트랜스폼) — 크기를 여기에 맞춘다.</summary>
        public Transform meshSpace;

        // ── LOD ──
        const float NoCollideDist = 15f;   // 이 밖은 몸통 밀어내기·스파크 생략
        const float FreezeDist    = 25f;   // 이 밖은 시뮬 자체 정지

        float sparkTimer;

        /// <summary>[더 이상 쓰지 않음] 지형 충돌을 없애고 자기 몸통만 밀어내도록 바꿨다.
        /// 기존 호출부 호환을 위해 남겨둔 빈 함수.</summary>
        public static void SetTerrainMask(int mask) { }

        /// <summary>전선을 세운다. socket = 몸통 부착점, payload = 매달릴 조각(없어도 됨).</summary>
        public void Init(Transform socket, Transform payload, DamagedPart cfg, Material wireMat)
        {
            this.socket = socket;
            this.payload = payload;
            this.cfg = cfg;

            int n = Mathf.Max(3, cfg.particles);
            cur = new Vector3[n];
            prev = new Vector3[n];
            segLen = Mathf.Max(0.01f, cfg.length) / (n - 1);

            // 자기 몸통 캡슐 — 전선이 몸을 파고들지 않게 밀어낼 대상.
            // 전선은 부모가 없으므로(스케일 오염 방지) 소켓을 타고 올라가 몸 루트를 찾는다.
            bodyRoot = socket;
            while (bodyRoot != null && bodyRoot.parent != null) bodyRoot = bodyRoot.parent;
            if (bodyRoot == null) bodyRoot = transform;
            var rends = bodyRoot.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                Bounds b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                bodyBottom = b.min.y;
                bodyTop    = b.max.y;

                // ★ 반지름은 <b>소켓이 축에서 얼마나 떨어져 있는지</b>로 정한다.
                //   바운즈 폭으로 잡으면 어깨(축에서 먼 곳)에 달린 전선이 항상 캡슐 안에 들어가
                //   매 프레임 밀려나 굳어 버린다. 소켓보다 안쪽으로만 막으면 그 일이 없다.
                Vector3 axis = bodyRoot.position;
                Vector2 d = new Vector2(socket.position.x - axis.x, socket.position.z - axis.z);
                float socketOut = d.magnitude;
                float fromBounds = Mathf.Min(b.extents.x, b.extents.z) * 0.85f;
                // 소켓보다 확실히 안쪽(85%)이면서, 바운즈 추정보다 크지 않게
                bodyRadius = Mathf.Max(0.02f, Mathf.Min(socketOut * 0.85f, fromBounds));
            }

            // 처음엔 소켓에서 아래로 늘어뜨린 상태로 시작(허공에서 튀어나오지 않게)
            Vector3 p0 = socket != null ? socket.position : transform.position;
            for (int i = 0; i < n; i++)
            {
                cur[i] = p0 + Vector3.down * (segLen * i);
                prev[i] = cur[i];
            }

            BuildMesh(n, wireMat);
        }

        void BuildMesh(int n, Material wireMat)
        {
            // ★ Unity 오브젝트에 ?? 를 쓰면 안 된다. Unity는 ==를 오버로드해 "파괴된 객체"를 null처럼
            //   보이게 하지만 ??는 진짜 참조만 보므로, 둘이 어긋나 null이 그대로 통과한다.
            //   TryGetComponent가 이 문제가 없는 표준 패턴이다.
            if (!TryGetComponent(out mf)) mf = gameObject.AddComponent<MeshFilter>();
            if (!TryGetComponent(out mr)) mr = gameObject.AddComponent<MeshRenderer>();
            if (mf == null || mr == null) { Debug.LogError("[DanglingWire] 렌더 컴포넌트 생성 실패"); return; }

            mesh = new Mesh { name = "WireTube" };
            mesh.MarkDynamic();                    // 매 프레임 갱신 — GPU에 힌트
            mf.sharedMesh = mesh;
            if (wireMat != null) mr.sharedMaterial = wireMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            int vcount = n * Sides;
            verts = new Vector3[vcount];
            norms = new Vector3[vcount];
            uvs   = new Vector2[vcount];

            // 인덱스는 고정 — 매 프레임 정점만 갱신한다
            var tris = new int[(n - 1) * Sides * 6];
            int t = 0;
            for (int i = 0; i < n - 1; i++)
                for (int s = 0; s < Sides; s++)
                {
                    int a = i * Sides + s;
                    int b = i * Sides + (s + 1) % Sides;
                    int c = a + Sides;
                    int d = b + Sides;
                    tris[t++] = a; tris[t++] = c; tris[t++] = b;
                    tris[t++] = b; tris[t++] = c; tris[t++] = d;
                }

            for (int i = 0; i < n; i++)
                for (int s = 0; s < Sides; s++)
                    uvs[i * Sides + s] = new Vector2(s / (float)Sides, i / (float)(n - 1));

            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.uv = uvs;
        }

        void LateUpdate()
        {
            // 몹이 파괴되면 전선·조각도 같이 사라진다(부모가 없으므로 직접 챙긴다)
            if (owner == null && ownerWasSet) { Cleanup(); return; }
            if (owner != null)
            {
                ownerWasSet = true;
                // 죽어서 숨겨진 몹 — 전선도 같이 숨긴다
                bool vis = owner.gameObject.activeInHierarchy;
                if (mr != null && mr.enabled != vis) mr.enabled = vis;
                if (detachedPart != null && detachedPart.gameObject.activeSelf != vis)
                    detachedPart.gameObject.SetActive(vis);
                if (!vis) return;
            }

            // 준비가 안 끝났으면(Init 실패·순서 문제) 아무것도 하지 않는다 —
            // 여기서 걸러야 UpdateMesh가 null 배열을 만지지 않는다.
            if (cur == null || socket == null || mesh == null || verts == null) return;

            var camT = Camera.main != null ? Camera.main.transform : null;
            float dist = camT != null ? Vector3.Distance(camT.position, cur[0]) : 0f;
            if (dist > FreezeDist) return;                 // 멀면 마지막 자세 유지

            float dt = Mathf.Min(Time.deltaTime, 0.05f);   // 프레임 튐에 폭발하지 않게 상한
            Simulate(dt, dist);
            UpdateMesh();
            UpdatePayload();
            if (cfg.sparks) Sparks(dt, dist);
        }

        void Simulate(float dt, float camDist)
        {
            int n = cur.Length;
            float damp = cfg.damping;
            Vector3 g = new Vector3(0f, cfg.gravity, 0f) * (dt * dt);

            // ── ① Verlet 적분 ──
            // ★ 끝 파티클에는 <b>매달린 부위의 무게</b>를 얹는다.
            //   안 그러면 조각이 전선 끝에 순간이동만 할 뿐 관성이 없어 대롱거리지 않는다.
            //   무거운 끝이 아래로 처지고 관성으로 계속 흔들리는 게 "대롱대롱"의 정체다.
            for (int i = 1; i < n; i++)          // 0번은 소켓에 고정이라 건너뜀
            {
                bool tip = (i == n - 1);
                float d = tip ? Mathf.Lerp(damp, 1f, 0.6f) : damp;   // 끝은 덜 감쇠 → 오래 흔들림
                Vector3 gi = tip ? g * (1f + cfg.tipWeight) : g;     // 끝은 더 무겁게

                Vector3 v = (cur[i] - prev[i]) * d;
                prev[i] = cur[i];
                cur[i] += v + gi;
            }

            // ── ② 거리 제약 ──
            // 반복 횟수가 많을수록 줄이 빳빳해진다. 대롱거리려면 오히려 <b>느슨해야</b> 해서
            // 기본 1회만 돈다(3회는 막대처럼 굳는다).
            cur[0] = socket.position;            // 뿌리 고정
            for (int it = 0; it < cfg.stiffness; it++)
            {
                for (int i = 0; i < n - 1; i++)
                {
                    Vector3 d = cur[i + 1] - cur[i];
                    float len = d.magnitude;
                    if (len < 1e-5f) continue;
                    Vector3 corr = d * ((len - segLen) / len);
                    // 0번은 고정이므로 보정을 전부 뒤쪽이 받는다.
                    // 끝은 무거우니 보정을 덜 받는다 — 무게가 제약에 눌려 사라지지 않게.
                    if (i == 0) cur[i + 1] -= corr;
                    else if (i + 1 == n - 1) { cur[i] += corr * 0.75f; cur[i + 1] -= corr * 0.25f; }
                    else { cur[i] += corr * 0.5f; cur[i + 1] -= corr * 0.5f; }
                }
                cur[0] = socket.position;
            }

            // ── ③ 충돌(기본 꺼짐 — CollideWithBody) ──
            if (camDist <= NoCollideDist) Collide(n);
        }

        /// <summary>
        /// 자기 몸통만 밀어낸다. 지형·다른 몹과는 부딪히지 않는다.
        ///
        /// Physics 캐스트를 한 번도 쓰지 않는다 — 몸통을 수직 캡슐로 근사해 거리만 비교하면
        /// 충분하고, 개체가 수십이어도 비용이 사실상 0이다.
        /// </summary>
        void Collide(int n)
        {
            if (!CollideWithBody || bodyRoot == null) return;

            Vector3 axis = bodyRoot.position;   // 몸통 중심축(수직선)
            float r = bodyRadius;

            for (int i = 1; i < n; i++)
            {
                // 캡슐 축 위의 가장 가까운 점 — 높이는 몸통 범위로 자른다
                float y = Mathf.Clamp(cur[i].y, bodyBottom, bodyTop);
                Vector3 onAxis = new Vector3(axis.x, y, axis.z);

                Vector3 d = cur[i] - onAxis;
                d.y = 0f;                       // 수평 방향으로만 밀어낸다
                float dist = d.magnitude;
                if (dist >= r || dist < 1e-5f) continue;

                // 표면 밖으로 밀어냄 + 파고들던 속도 제거(안 하면 계속 비벼댄다)
                Vector3 push = d / dist;
                cur[i] = onAxis + push * r + Vector3.up * (cur[i].y - y);
                Vector3 v = cur[i] - prev[i];
                float into = Vector3.Dot(v, -push);
                if (into > 0f) prev[i] = cur[i] - (v + push * into);
            }
        }

        /// <summary>파티클 경로를 따라 튜브 정점을 다시 만든다. 굵기는 뿌리→끝으로 가늘어진다.</summary>
        void UpdateMesh()
        {
            int n = cur.Length;
            Vector3 up = Vector3.up;

            for (int i = 0; i < n; i++)
            {
                // 진행 방향 — 끝점은 직전 구간 방향을 그대로 쓴다
                Vector3 dir = i < n - 1 ? cur[i + 1] - cur[i] : cur[i] - cur[i - 1];
                if (dir.sqrMagnitude < 1e-8f) dir = Vector3.down;
                dir.Normalize();

                // 진행 방향과 평행하면 기준축을 바꿔 단면이 찌그러지지 않게
                Vector3 refUp = Mathf.Abs(Vector3.Dot(dir, up)) > 0.95f ? Vector3.forward : up;
                Vector3 side = Vector3.Normalize(Vector3.Cross(dir, refUp));
                Vector3 fwd  = Vector3.Cross(side, dir);

                float t = i / (float)(n - 1);
                float rad = Mathf.Lerp(cfg.rootRadius, cfg.tipRadius, t);   // 뿌리 굵고 끝 가늘게

                for (int s = 0; s < Sides; s++)
                {
                    float a = s / (float)Sides * Mathf.PI * 2f;
                    Vector3 off = (side * Mathf.Cos(a) + fwd * Mathf.Sin(a));
                    int vi = i * Sides + s;
                    // 메시는 이 오브젝트 로컬 공간 — 월드 좌표를 로컬로 변환
                    verts[vi] = transform.InverseTransformPoint(cur[i] + off * rad);
                    norms[vi] = transform.InverseTransformDirection(off);
                }
            }

            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.RecalculateBounds();
        }

        /// <summary>매달린 조각을 전선 끝에 붙이고, 전선 방향으로 향하게 한다.</summary>
        void UpdatePayload()
        {
            if (payload == null) return;
            int n = cur.Length;

            // ★ 스킨 메시가 사는 공간의 스케일을 매 프레임 따라간다.
            //   생성 시점(ReplaceView)엔 뷰 스케일이 아직 1이고 Sync가 나중에 개체 크기를 넣으므로,
            //   한 번만 복사하면 조각 크기가 어긋난 채 남는다.
            if (meshSpace != null)
            {
                Vector3 s = meshSpace.lossyScale;
                if ((payload.lossyScale - s).sqrMagnitude > 1e-8f) payload.localScale = s;
            }
            payload.position = cur[n - 1];

            // ★ 조각은 <b>몸 방향</b>을 기준으로 두고, 전선이 기운 만큼만 더 기울인다.
            //   LookRotation만 쓰면 조각의 앞뒤 축이 전선 방향으로 강제로 눕혀져
            //   팔이 옆으로 꺾이거나 뒤집혀 보인다(원본 자세와 무관해진다).
            Vector3 dir = cur[n - 1] - cur[n - 2];
            // 기준 회전도 메시 공간 — 임포트 축변환(모델 자식의 회전)까지 포함해야 방향이 맞는다
            Quaternion baseRot = meshSpace != null ? meshSpace.rotation
                               : owner != null ? owner.rotation : Quaternion.identity;
            if (dir.sqrMagnitude > 1e-6f)
            {
                // 아래로 늘어진 상태(-up)를 기준으로, 실제 전선 방향까지의 회전차만 얹는다
                Quaternion sag = Quaternion.FromToRotation(Vector3.down, dir.normalized);
                payload.rotation = sag * baseRot;
            }
            else payload.rotation = baseRot;
        }

        /// <summary>끝에서 불규칙하게 스파크. 로봇이라 피 대신 이걸로 파손을 표현한다.</summary>
        void Sparks(float dt, float camDist)
        {
            if (camDist > NoCollideDist) return;
            sparkTimer -= dt;
            if (sparkTimer > 0f) return;
            sparkTimer = cfg.sparkInterval * Random.Range(0.5f, 1.5f);
            WireSparks.Emit(cur[cur.Length - 1]);
        }

        bool ownerWasSet;

        /// <summary>몹이 사라졌을 때 전선·조각을 함께 치운다.</summary>
        void Cleanup()
        {
            if (detachedPart != null) Destroy(detachedPart.gameObject);
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }

        /// <summary>현재 전선 끝 위치(스파크·디버그용).</summary>
        public Vector3 TipPosition => cur != null ? cur[cur.Length - 1] : transform.position;
    }
}
