using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Game.Sim;

namespace Game.View
{
    /// <summary>맵 생성 결과: 스폰 지점 + 플레이어 시작점. 길찾기는 NavMesh 베이크(런타임).</summary>
    public class MapResult
    {
        public List<Vector3> spawns = new();
        public Vector3 playerSpawn;
        public ArenaMapBake predictionMap;
        public float playerYaw = 180f;   // 시작 시 바라보는 방향(PlayerSpawnPoint의 Y회전). 없으면 남쪽.
    }

    /// <summary>
    /// 코드 아레나(축정렬 박스 + 쐐기 경사로)와 NavMesh 베이크.
    /// 여러 높이의 발판 + 부드러운 경사로 + 벽으로 길찾기 복잡도를 준다.
    /// 길찾기는 런타임 NavMesh. 절벽 낙하는 Phase 2에서 off-mesh link로 추가 예정.
    /// </summary>
    public static class MapBuilder
    {
        public static MapResult BuildCubes()
        {
            var gray  = Mat(new Color(0.55f, 0.55f, 0.58f));   // 바닥·외벽
            var g2    = Mat(new Color(0.45f, 0.48f, 0.55f));   // 2F 발판
            var g3    = Mat(new Color(0.36f, 0.40f, 0.52f));   // 3F 타워·다리
            var gMid  = Mat(new Color(0.50f, 0.50f, 0.46f));   // 반층(3.0)
            var gRamp = Mat(new Color(0.34f, 0.50f, 0.42f));   // 경사로(초록기)
            var gLedge= Mat(new Color(0.60f, 0.52f, 0.40f));   // mantle 계단·엄폐(갈색기)
            var gCont = Mat(new Color(0.16f, 0.38f, 0.68f));   // 컨테이너(파랑)
            var r = new MapResult();
            r.predictionMap = Game.Bridge.GraphPathfinder.CreateArenaBake();

            // 높이: 1F 0 · 반층 3.0 · 2F 4.5 · 3F 9 (단차 1.5배). mantle 1.5(3단=한 층).
            // ── 바닥 + 외벽 (3층 담을 높이 12) ──
            Cube("Floor",  new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f), gray);
            Cube("Wall_N", new Vector3(0f, 6f,  29f), new Vector3(60f, 12f, 2f), gray);
            Cube("Wall_S", new Vector3(0f, 6f, -29f), new Vector3(60f, 12f, 2f), gray);
            Cube("Wall_E", new Vector3( 29f, 6f, 0f), new Vector3(2f, 12f, 60f), gray);
            Cube("Wall_W", new Vector3(-29f, 6f, 0f), new Vector3(2f, 12f, 60f), gray);

            // ── NE: 2F 대형 발판 + 3F 코너 타워 + 램프 + mantle 계단 ──
            Platform("PlatNE2", 4f, 28f, 4f, 28f, 4.5f, g2);       // 2F (타워가 코너 차지 → ㄱ자)
            Platform("PlatNE3", 18f, 28f, 18f, 28f, 9f, g3);       // 3F 타워
            Ramp("Ramp_NE_12", new Vector3(10f, 0f, -2f), new Vector3(10f, 4.5f, 4f), 4f, gRamp);  // 1F→2F 남면
            Ramp("Ramp_NE_23", new Vector3(10f, 4.5f, 20f), new Vector3(18f, 9f, 20f), 4f, gRamp); // 2F→3F 타워 서면
            MantleStair("MS_NE12", 0f, 10f, Vector3.right, 0f, 4.5f, 3f, gLedge);   // 1F→2F 플레이어 전용
            MantleStair("MS_NE23", 14f, 10f, Vector3.right, 4.5f, 9f, 3f, gLedge);  // 2F→3F 플레이어 전용

            // ── SW: 미러(대각 대칭) ──
            Platform("PlatSW2", -28f, -4f, -28f, -4f, 4.5f, g2);
            Platform("PlatSW3", -28f, -18f, -28f, -18f, 9f, g3);
            Ramp("Ramp_SW_12", new Vector3(-10f, 0f, 2f), new Vector3(-10f, 4.5f, -4f), 4f, gRamp);
            Ramp("Ramp_SW_23", new Vector3(-10f, 4.5f, -20f), new Vector3(-18f, 9f, -20f), 4f, gRamp);
            MantleStair("MS_SW12", 0f, -10f, Vector3.left, 0f, 4.5f, 3f, gLedge);
            MantleStair("MS_SW23", -14f, -10f, Vector3.left, 4.5f, 9f, 3f, gLedge);

            // ── 3F 다리(catwalk): 두 타워를 잇는 ㄴ자 통로 + 중앙 노드(기둥 위) ──
            Cube("Bridge_N", new Vector3(-2f, 8.75f, 22f), new Vector3(40f, 0.5f, 4f), g3);   // x[-22,18] z[20,24]
            Cube("Bridge_W", new Vector3(-22f, 8.75f, 0f), new Vector3(4f, 0.5f, 48f), g3);   // x[-24,-20] z[-24,24]
            Cube("Bridge_C", new Vector3(0f, 8.75f, 11f), new Vector3(4f, 0.5f, 18f), g3);    // 중앙 스퍼 z[2,20]
            Cube("Pillar",   new Vector3(0f, 4.5f, 0f),   new Vector3(4f, 9f, 4f), gray);     // 중앙 기둥(0→9, 꼭대기=3F 노드)

            // ── 반층(3.0) 플랫폼: NW·SE 개활지에 "1.5층" 전투 발코니 + 경사로 ──
            Platform("PlatMidNW", -24f, -10f, 10f, 24f, 3f, gMid);
            Ramp("RampMidNW", new Vector3(-6f, 0f, 17f), new Vector3(-10f, 3f, 17f), 4f, gRamp);
            Platform("PlatMidSE", 10f, 24f, -24f, -10f, 3f, gMid);
            Ramp("RampMidSE", new Vector3(6f, 0f, -17f), new Vector3(10f, 3f, -17f), 4f, gRamp);

            // ── 엄폐 턱(0.8, 적 못 넘음) + 잔단차(≤0.4, 적 통과) ──
            Ledge("Cover1", -16f, 0f, -14f, 0.8f, 3f, gLedge);
            Ledge("Cover2", 16f, 0f, 14f, 0.8f, 3f, gLedge);
            Ledge("Step1", 0f, 0f, 14f, 0.3f, 6f, gLedge);
            Ledge("Step2", 0f, 0f, -14f, 0.3f, 6f, gLedge);

            // ── 앞뒤 개방 컨테이너(부속품 예시): 스폰 정면 채널에 1개. 관통·위 발판·엄폐 ──
            Container("Container1", new Vector3(0f, 0f, 6f), 6f, 2.6f, 2.8f, true, gCont);

            Physics.SyncTransforms();

            var surface = new GameObject("NavMeshSurface").AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
            surface.BuildNavMesh();

            var tri = NavMesh.CalculateTriangulation();
            Debug.Log(tri.vertices != null && tri.vertices.Length > 0
                ? $"[Map] 3층 아레나 v2 NavMesh 베이크 — 정점 {tri.vertices.Length}"
                : "[Map] NavMesh 베이크 실패");
            DrawNavMeshOverlay(tri);

            // ── 절벽 낙하 링크(off-mesh link): 경사로 없는 진짜 절벽만. 다리(3F 남쪽 테두리) → 1F 개활.
            //    일방(위→아래). NavMesh가 이 링크로 경로를 태우면 몹이 걸어 나가 자연 낙하. 좌표는 1차 추정 — 테스트로 조정. ──
            DropLink(new Vector3(-8f, 9f, 20f), new Vector3(-8f, 0f, 17f));
            DropLink(new Vector3( 0f, 9f, 20f), new Vector3( 0f, 0f, 17f));
            DropLink(new Vector3( 8f, 9f, 20f), new Vector3( 8f, 0f, 17f));

            // ── 스폰 지점 (1F·반층·2F·3F 다양한 위치·고저차) ──
            r.spawns.Add(new Vector3(0f, 0f, -22f));    // 1F S
            r.spawns.Add(new Vector3(22f, 0f, -6f));    // 1F SE
            r.spawns.Add(new Vector3(-22f, 0f, 6f));    // 1F NW
            r.spawns.Add(new Vector3(6f, 0f, -8f));     // 1F 중앙
            r.spawns.Add(new Vector3(-17f, 3f, 17f));   // 반층 NW
            r.spawns.Add(new Vector3(17f, 3f, -17f));   // 반층 SE
            r.spawns.Add(new Vector3(10f, 4.5f, 10f));  // 2F NE
            r.spawns.Add(new Vector3(-10f, 4.5f, -10f)); // 2F SW
            r.spawns.Add(new Vector3(23f, 9f, 23f));    // 3F NE 타워
            r.spawns.Add(new Vector3(-23f, 9f, -23f));  // 3F SW 타워
            r.playerSpawn = new Vector3(0f, 0f, 10f);   // 1F 중앙 개활지

            Light();
            return r;
        }

        public static MapResult BuildFromScene(Vector3 refPoint)
        {
            Physics.SyncTransforms();

            // 에디터에서 미리 구운 NavMesh(NavMeshSurface + 저장된 데이터)가 있으면 그대로 쓴다 →
            // Play 즉시 시작. 없을 때만 런타임 베이크로 폴백(예전 씬·임시 씬 호환).
            var tri = NavMesh.CalculateTriangulation();
            if (tri.vertices == null || tri.vertices.Length == 0)
            {
                var surface = new GameObject("NavMeshSurface(런타임)").AddComponent<NavMeshSurface>();
                surface.collectObjects = CollectObjects.All;
                surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
                surface.BuildNavMesh();
                tri = NavMesh.CalculateTriangulation();
                Debug.Log(tri.vertices != null && tri.vertices.Length > 0
                    ? $"[Map] 런타임 NavMesh 베이크 — 정점 {tri.vertices.Length}  (Tools/맵 굽기 로 미리 구우면 Play가 즉시 시작됩니다)"
                    : "[Map] NavMesh 베이크 실패 — 콜라이더를 확인하십시오");
            }
            else Debug.Log($"[Map] 미리 구운 NavMesh 사용 — 정점 {tri.vertices.Length}");

            var r = new MapResult();
            ArenaMapAuthoring authored = Object.FindFirstObjectByType<ArenaMapAuthoring>();
            if (authored != null)
                r.predictionMap = authored.BuildBake();

            // 플레이어 시작점: 씬의 PlayerSpawnPoint 우선. 없으면 예전 방식(카메라 위치)으로 폴백.
            var sp = Object.FindFirstObjectByType<PlayerSpawnPoint>();
            Vector3 want = sp != null ? sp.transform.position : refPoint;
            if (sp != null) r.playerYaw = sp.transform.eulerAngles.y;
            else Debug.LogWarning("[Map] PlayerSpawnPoint가 없어 카메라 위치 기준으로 스폰합니다. " +
                                  "빈 오브젝트에 PlayerSpawnPoint를 붙이면 시작 위치·방향이 고정됩니다.");

            if (NavMesh.SamplePosition(want, out var p, 80f, NavMesh.AllAreas))
                r.playerSpawn = p.position;
            else
            {
                r.playerSpawn = want;   // navmesh를 못 찾아도 (0,0,0)으로 떨어지지 않게 지정 좌표 유지
                Debug.LogWarning("[Map] 시작점 근처에 NavMesh가 없습니다 — 좌표를 그대로 사용합니다. 콜라이더/베이크를 확인하십시오.");
            }
            for (int i = 0; i < 5; i++)
            {
                float a = i / 5f * Mathf.PI * 2f;
                Vector3 q = r.playerSpawn + new Vector3(Mathf.Cos(a) * 10f, 0f, Mathf.Sin(a) * 10f);
                if (NavMesh.SamplePosition(q, out var h, 8f, NavMesh.AllAreas)) r.spawns.Add(h.position);
            }
            // 씬 지형 맵은 조명을 직접 배치한다 → 자동 Directional Light를 만들지 않는다.
            // (예전엔 여기서도 Light()를 불렀는데, 그러면 "실내는 조명만으로 침침하게" 같은
            //  어두운 무드를 만들어도 Play 순간 방향광이 생겨 전부 밝아져 버린다.)
            if (Object.FindFirstObjectByType<Light>() == null)
                Debug.LogWarning("[Map] 씬에 Light가 하나도 없습니다 — 화면이 환경광만으로 보입니다. " +
                                 "의도한 것이 아니면 조명을 배치하십시오. (작업 중 임시로 밝게 보려면 Tools/작업용 조명)");
            return r;
        }

        /// <summary>
        /// 절벽 낙하용 off-mesh link(일방, 위→아래). NavMesh가 이 링크로 경로를 태우면
        /// NavMeshPathfinder가 큰 낙차로 감지해 kind=Jump → 몹이 걸어 나가 떨어진다(순간이동 아님).
        /// </summary>
        static void DropLink(Vector3 from, Vector3 to)
        {
            // 테두리 침식으로 링크 끝점이 navmesh에서 떠 등록 안 되는 것 방지 — 양 끝을 navmesh 위로 스냅
            if (NavMesh.SamplePosition(from, out var fHit, 3f, NavMesh.AllAreas)) from = fHit.position;
            if (NavMesh.SamplePosition(to,   out var tHit, 3f, NavMesh.AllAreas)) to   = tHit.position;

            var link = new GameObject("DropLink").AddComponent<NavMeshLink>();
            link.startPoint = from;      // 로컬=월드(트랜스폼 원점·단위)
            link.endPoint = to;
            link.width = 3f;
            link.bidirectional = false;  // 위→아래만(절벽은 못 거슬러 오름)
            link.area = 0;               // Walkable
            link.UpdateLink();

            // 시각화(순수 표시 — 동작 무해): 노란 선 = 드롭 경로, 주황 구슬 = 착지점
            var lr = new GameObject("DropLinkViz").AddComponent<LineRenderer>();
            lr.material = Unlit(new Color(1f, 0.9f, 0.1f));
            lr.widthMultiplier = 0.2f; lr.positionCount = 2;
            lr.SetPosition(0, from); lr.SetPosition(1, to);
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            s.name = "DropLandingMark"; Object.Destroy(s.GetComponent<Collider>());
            s.transform.position = to + Vector3.up * 0.3f; s.transform.localScale = Vector3.one * 0.6f;
            s.GetComponent<Renderer>().material = Unlit(new Color(1f, 0.5f, 0.1f));
        }

        // ── 지형 헬퍼 (전부 축정렬) ──
        static void Platform(string name, float minX, float maxX, float minZ, float maxZ, float topY, Material m)
        {
            Cube(name, new Vector3((minX + maxX) / 2f, topY / 2f, (minZ + maxZ) / 2f),
                 new Vector3(maxX - minX, topY, maxZ - minZ), m);
        }

        /// <summary>
        /// 앞뒤 개방 컨테이너(속 빈 통): 바닥·천장·좌우 벽. 양 끝(뚫린 축)은 개방 → 관통.
        /// 바닥 윗면이 floorCenter.y와 flush(턱 없음). 천장 위는 발판. floorCenter.y를 height씩
        /// 올려 다시 호출하면 위에 적재된다. lengthwiseZ=true면 뚫린 축이 ±Z(벽은 ±X).
        /// 정적 콜라이더라 NavMesh에 자동 반영(몹이 통과·위로 다님). 주름 등 디테일은 아트 단계.
        /// </summary>
        static void Container(string name, Vector3 floorCenter,
                              float length, float width, float height, bool lengthwiseZ, Material m)
        {
            const float thk = 0.2f;                          // 판·벽 두께
            float xSpan = lengthwiseZ ? width  : length;     // 벽이 막는 폭 / 뚫린 길이 정리
            float zSpan = lengthwiseZ ? length : width;
            Vector3 c = floorCenter;

            // 바닥(윗면 flush) · 천장(윗면 = 발판)
            Cube($"{name}_Floor", new Vector3(c.x, c.y - thk * 0.5f, c.z),          new Vector3(xSpan, thk, zSpan), m);
            Cube($"{name}_Roof",  new Vector3(c.x, c.y + height - thk * 0.5f, c.z),  new Vector3(xSpan, thk, zSpan), m);

            // 좌우 벽(뚫린 축과 나란히). 내부 높이 = height - thk(천장 아래까지)
            float wallH = height - thk;
            float wallCy = c.y + wallH * 0.5f;
            if (lengthwiseZ)   // 벽은 ±X, 뚫린 축은 Z
            {
                float wx = xSpan * 0.5f - thk * 0.5f;
                Cube($"{name}_WallL", new Vector3(c.x - wx, wallCy, c.z), new Vector3(thk, wallH, zSpan), m);
                Cube($"{name}_WallR", new Vector3(c.x + wx, wallCy, c.z), new Vector3(thk, wallH, zSpan), m);
            }
            else               // 벽은 ±Z, 뚫린 축은 X
            {
                float wz = zSpan * 0.5f - thk * 0.5f;
                Cube($"{name}_WallB", new Vector3(c.x, wallCy, c.z - wz), new Vector3(xSpan, wallH, thk), m);
                Cube($"{name}_WallF", new Vector3(c.x, wallCy, c.z + wz), new Vector3(xSpan, wallH, thk), m);
            }
        }

        /// <summary>
        /// 턱(정사각 박스). baseY 위에 height 만큼 솟은 솔리드. mantle 턱·엄폐물·잔단차 공용.
        /// height ≤ 0.4 이면 적도 NavMesh step 으로 넘고, 초과면 플레이어 전용(적 못 오름).
        /// </summary>
        static void Ledge(string name, float cx, float baseY, float cz, float height, float side, Material m)
        {
            Cube(name, new Vector3(cx, baseY + height * 0.5f, cz), new Vector3(side, height, side), m);
        }

        /// <summary>
        /// mantle 계단: foot에서 dir로 1.5씩 올라 topY 도달(플레이어 전용, 적은 각 1.5 턱 못 넘음).
        /// 각 단은 바닥(0)까지 솔리드라 baseY 위 발판에 얹으면 노출부만 밟힌다.
        /// </summary>
        static void MantleStair(string name, float footX, float footZ, Vector3 dir,
                                float baseY, float topY, float width, Material m)
        {
            const float stepH = 1.5f, stepD = 1.6f;
            int steps = Mathf.CeilToInt((topY - baseY) / stepH);
            for (int i = 0; i < steps; i++)
            {
                float top = Mathf.Min(topY, baseY + stepH * (i + 1));   // 이 단의 절대 윗면 Y
                Vector3 c = new Vector3(footX, 0f, footZ) + dir * (stepD * (i + 0.5f));
                c.y = top * 0.5f;
                Vector3 s = Mathf.Abs(dir.x) > 0.5f
                    ? new Vector3(stepD, top, width)
                    : new Vector3(width, top, stepD);
                Cube($"{name}_{i}", c, s, m);
            }
        }

        /// <summary>
        /// 경사로(부드러운 빗면). low(바닥,y=0)에서 high(발판 모서리,y=H)로 오르는 솔리드 쐐기.
        /// 회전 큐브가 아니라 6정점 쐐기 메시를 직접 만든다 — 좌표를 정확히 박아 어긋남이 없다.
        /// 빗면 법선은 run 방향과 무관하게 항상 위(+Y) → NavMesh가 빗면 위에 깔린다.
        /// </summary>
        static void Ramp(string name, Vector3 low, Vector3 high, float width, Material m)
        {
            Vector3 runH = new Vector3(high.x - low.x, 0f, high.z - low.z);
            Vector3 perp = new Vector3(-runH.z, 0f, runH.x).normalized * (width * 0.5f);
            Vector3 highBottom = new Vector3(high.x, low.y, high.z);

            Vector3 P0 = low - perp;         // 바닥 낮은쪽 A
            Vector3 P1 = highBottom - perp;  // 바닥 높은쪽 A
            Vector3 P2 = highBottom + perp;  // 바닥 높은쪽 B
            Vector3 P3 = low + perp;         // 바닥 낮은쪽 B
            Vector3 P4 = high - perp;        // 꼭대기 A
            Vector3 P5 = high + perp;        // 꼭대기 B

            var mesh = new Mesh { name = name + "Mesh" };
            mesh.vertices = new[] { P0, P1, P2, P3, P4, P5 };
            mesh.triangles = new[]
            {
                0, 3, 4,  4, 3, 5,   // 빗면(위 향함, 걷는 면)
                0, 2, 1,  0, 3, 2,   // 바닥
                1, 2, 5,  1, 5, 4,   // 높은쪽 수직면
                0, 4, 1,             // 옆면 A
                3, 2, 5,             // 옆면 B
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().material = m;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;   // 정적 non-convex, CapsuleCast·NavMesh용
        }

        static void Light()
        {
            if (Object.FindFirstObjectByType<Light>() == null)
            {
                var l = new GameObject("Directional Light").AddComponent<Light>();
                l.type = LightType.Directional;
                l.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }
        }

        static void DrawNavMeshOverlay(NavMeshTriangulation tri)
        {
            if (tri.vertices == null || tri.vertices.Length == 0) return;
            var mesh = new Mesh { name = "NavMeshOverlay", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = tri.vertices; mesh.triangles = tri.indices; mesh.RecalculateNormals();
            var go = new GameObject("NavMeshOverlay");
            go.transform.position = Vector3.up * 0.06f;
            go.AddComponent<MeshFilter>().mesh = mesh;
            go.AddComponent<MeshRenderer>().material = Unlit(new Color(0.25f, 0.75f, 1f));
        }

        static GameObject Cube(string name, Vector3 c, Vector3 s, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name; go.transform.position = c; go.transform.localScale = s;
            go.GetComponent<Renderer>().material = m;
            return go;
        }

        static Material Mat(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh); m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }
        static Material Unlit(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
            var m = new Material(sh); m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }
    }
}
