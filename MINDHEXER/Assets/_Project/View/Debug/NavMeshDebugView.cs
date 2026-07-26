using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace Game.View
{
    /// <summary>
    /// NavMesh 시각화(디버그). ★ 읽기 전용 — 게임 로직에 일절 영향 없음.
    ///
    /// 이 프로젝트의 NavMesh는 <b>런타임에 생성</b>된다(MapBuilder가 NavMeshSurface.BuildNavMesh 호출).
    /// 그래서 에디터의 기본 NavMesh 표시로는 보이지 않고, "링크 끝점이 정말 걸을 수 있는 면 위인지"를
    /// 눈으로 확인할 방법이 없었다. 이걸 Play 중 Scene 뷰에서 보여준다.
    ///
    /// 표시 내용
    ///  · NavMesh 삼각형 — <b>Area별로 다른 색</b>(Walkable / Leap / LeapTraversal 등)
    ///  · 층이동 마커 끝점 — 면 위면 초록, 면 밖이면 <b>빨강 + 가장 가까운 면까지 선</b>
    ///
    /// Gizmos가 켜져 있어야 보인다(Scene 뷰 상단 Gizmos 토글).
    /// </summary>
    [DisallowMultipleComponent]
    public class NavMeshDebugView : MonoBehaviour
    {
        [Header("표시")]
        public bool  showMesh = true;
        [Tooltip("면을 반투명하게 채운다(끄면 테두리 선만)")]
        public bool  fillFaces = true;
        [Range(0.05f, 1f)] public float fillAlpha = 0.35f;
        public bool  showLinkEndpoints = true;
        [Tooltip("면을 다시 읽는 주기(초). 런타임 생성이라 처음엔 비어 있을 수 있다")]
        public float refreshInterval = 1f;
        [Tooltip("멀리 있는 삼각형은 건너뛰어 가볍게(0 = 전부)")]
        public float maxDrawDistance = 60f;

        NavMeshTriangulation tri;
        float nextRefresh;
        bool  hasData;

        // 면 채우기용 캐시. Gizmos.DrawMesh는 메시 하나에 색 하나라 Area별로 메시를 나눠 만든다.
        // 매 갱신마다 다시 만들면 낭비라, 삼각화 크기가 바뀔 때만 재생성한다.
        readonly List<int>  fillAreas  = new List<int>();
        readonly List<Mesh> fillMeshes = new List<Mesh>();
        int lastVertCount, lastIndexCount;

        // ── Game 뷰에도 그리기 ──
        // Gizmos는 Scene 뷰(및 Game 뷰 Gizmos 토글 ON)에서만 보인다. 그것 때문에 "안 보인다"가 되기 쉬워
        // GL로도 직접 그린다 → Play 중 Game 뷰에서 토글 없이 보인다.
        static Material lineMat;

        static void EnsureMat()
        {
            if (lineMat != null) return;
            var sh = Shader.Find("Hidden/Internal-Colored");
            if (sh == null) return;
            lineMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            lineMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            lineMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            lineMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            lineMat.SetInt("_ZWrite", 0);
        }

        void OnRenderObject()
        {
            // 에디터에선 Update가 안 돌아 데이터가 비어 있을 수 있으므로 여기서도 확보한다.
            if (!hasData && Time.realtimeSinceStartup >= nextRefresh)
            {
                nextRefresh = Time.realtimeSinceStartup + Mathf.Max(0.1f, refreshInterval);
                Refresh();
            }
            if (!showMesh || !hasData) return;
            // 도메인 리로드·NavMesh 소거 등으로 hasData(true)와 tri(내용 비움)가 어긋나면 아래 인덱싱이 NRE.
            if (tri.indices == null || tri.vertices == null || tri.areas == null) return;
            EnsureMat();
            if (lineMat == null) return;

            Vector3 eye = ViewerPos();
            float maxSq = maxDrawDistance > 0f ? maxDrawDistance * maxDrawDistance : float.MaxValue;
            Vector3 up = Vector3.up * 0.03f;
            int triCount = tri.indices.Length / 3;

            lineMat.SetPass(0);
            GL.PushMatrix();

            // ① 면 채우기(반투명) — 선보다 먼저 그려 테두리가 위에 얹히게
            if (fillFaces)
            {
                GL.Begin(GL.TRIANGLES);
                for (int t = 0; t < triCount; t++)
                {
                    Vector3 a = tri.vertices[tri.indices[t * 3]];
                    if ((a - eye).sqrMagnitude > maxSq) continue;
                    Vector3 b = tri.vertices[tri.indices[t * 3 + 1]];
                    Vector3 c = tri.vertices[tri.indices[t * 3 + 2]];
                    Color col = AreaColor(tri.areas[t]);
                    col.a = fillAlpha;
                    GL.Color(col);
                    GL.Vertex(a + up); GL.Vertex(b + up); GL.Vertex(c + up);
                }
                GL.End();
            }

            // ② 테두리
            GL.Begin(GL.LINES);
            for (int t = 0; t < triCount; t++)
            {
                Vector3 a = tri.vertices[tri.indices[t * 3]];
                if ((a - eye).sqrMagnitude > maxSq) continue;
                Vector3 b = tri.vertices[tri.indices[t * 3 + 1]];
                Vector3 c = tri.vertices[tri.indices[t * 3 + 2]];
                GL.Color(AreaColor(tri.areas[t]));
                GL.Vertex(a + up); GL.Vertex(b + up);
                GL.Vertex(b + up); GL.Vertex(c + up);
                GL.Vertex(c + up); GL.Vertex(a + up);
            }
            GL.End();
            GL.PopMatrix();
        }

        void Update()
        {
            // GL 경로는 Gizmos 콜백에 의존하지 않으므로 갱신을 여기서도 돌린다.
            if (Time.realtimeSinceStartup < nextRefresh) return;
            nextRefresh = Time.realtimeSinceStartup + Mathf.Max(0.1f, refreshInterval);
            Refresh();
        }

        void Refresh()
        {
            tri = NavMesh.CalculateTriangulation();
            // 세 배열이 전부 있고 길이가 맞아야 안전하다. 에디터에선 NavMesh가 아직 없어 비어 있을 수 있다.
            hasData = tri.indices  != null && tri.indices.Length >= 3
                   && tri.vertices != null && tri.vertices.Length > 0
                   && tri.areas    != null && tri.areas.Length >= tri.indices.Length / 3;

            int v  = hasData ? tri.vertices.Length : 0;
            int ix = hasData ? tri.indices.Length  : 0;
            if (v != lastVertCount || ix != lastIndexCount)
            { lastVertCount = v; lastIndexCount = ix; RebuildFillMeshes(); }
        }

        /// <summary>Area별로 삼각형을 묶어 채움용 메시를 만든다(색이 메시 단위라 나눠야 한다).</summary>
        void RebuildFillMeshes()
        {
            foreach (var m in fillMeshes) Kill(m);
            fillMeshes.Clear();
            fillAreas.Clear();
            if (!hasData) return;

            var byArea = new Dictionary<int, List<int>>();
            int triCount = tri.indices.Length / 3;
            for (int t = 0; t < triCount; t++)
            {
                int area = tri.areas[t];
                if (!byArea.TryGetValue(area, out var list)) { list = new List<int>(); byArea[area] = list; }
                list.Add(tri.indices[t * 3]);
                list.Add(tri.indices[t * 3 + 1]);
                list.Add(tri.indices[t * 3 + 2]);
            }

            foreach (var kv in byArea)
            {
                var mesh = new Mesh
                {
                    name = "NavFill_" + kv.Key,
                    hideFlags = HideFlags.HideAndDontSave,
                    indexFormat = UnityEngine.Rendering.IndexFormat.UInt32,
                };
                mesh.vertices = tri.vertices;      // 정점은 공유(안 쓰는 정점이 있어도 무해)
                mesh.SetTriangles(kv.Value, 0);
                mesh.RecalculateBounds();
                fillAreas.Add(kv.Key);
                fillMeshes.Add(mesh);
            }
        }

        void OnDisable()
        {
            foreach (var m in fillMeshes) Kill(m);
            fillMeshes.Clear();
            fillAreas.Clear();
            lastVertCount = lastIndexCount = -1;
        }

        static void Kill(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        void OnDrawGizmos()
        {
            if (Time.realtimeSinceStartup >= nextRefresh)
            {
                nextRefresh = Time.realtimeSinceStartup + Mathf.Max(0.1f, refreshInterval);
                Refresh();
            }

            if (showMesh && hasData)
            {
                if (fillFaces) DrawFill();   // 면 먼저 → 테두리가 위에 얹힘
                DrawMesh();
            }
            if (showLinkEndpoints) DrawLinkEndpoints();
        }

        /// <summary>Area별 채움 메시를 반투명으로 그린다.</summary>
        void DrawFill()
        {
            Matrix4x4 saved = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.Translate(Vector3.up * 0.02f);   // z-fighting 방지
            for (int i = 0; i < fillMeshes.Count; i++)
            {
                if (fillMeshes[i] == null) continue;
                Color c = AreaColor(fillAreas[i]);
                c.a = fillAlpha;
                Gizmos.color = c;
                Gizmos.DrawMesh(fillMeshes[i]);
            }
            Gizmos.matrix = saved;
        }

        void DrawMesh()
        {
            Vector3 eye = ViewerPos();
            int triCount = tri.indices.Length / 3;
            float maxSq = maxDrawDistance > 0f ? maxDrawDistance * maxDrawDistance : float.MaxValue;

            for (int t = 0; t < triCount; t++)
            {
                Vector3 a = tri.vertices[tri.indices[t * 3]];
                Vector3 b = tri.vertices[tri.indices[t * 3 + 1]];
                Vector3 c = tri.vertices[tri.indices[t * 3 + 2]];
                if ((a - eye).sqrMagnitude > maxSq) continue;

                Gizmos.color = AreaColor(tri.areas[t]);
                // 살짝 띄워 z-fighting 방지
                Vector3 up = Vector3.up * 0.03f;
                Gizmos.DrawLine(a + up, b + up);
                Gizmos.DrawLine(b + up, c + up);
                Gizmos.DrawLine(c + up, a + up);
            }
        }

        /// <summary>마커 끝점이 실제로 NavMesh 위인지 — 링크가 안 붙는 원인을 눈으로 잡는다.</summary>
        void DrawLinkEndpoints()
        {
            var links = FindObjectsByType<TraversalLink>(FindObjectsSortMode.None);
            foreach (var l in links)
            {
                if (l == null) continue;
                DrawEndpoint(l.PointA);
                DrawEndpoint(l.PointB);
            }
        }

        static void DrawEndpoint(Vector3 p)
        {
            bool on = NavMesh.SamplePosition(p, out NavMeshHit hit, 0.6f, NavMesh.AllAreas);
            Gizmos.color = on ? new Color(0.3f, 1f, 0.4f, 0.95f) : new Color(1f, 0.25f, 0.25f, 0.95f);
            Gizmos.DrawWireSphere(p, 0.35f);
            if (!on && NavMesh.SamplePosition(p, out hit, 5f, NavMesh.AllAreas))
            {
                // 면 밖이면 가장 가까운 면까지 선을 그어 "얼마나 벗어났는지" 보여준다
                Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.9f);
                Gizmos.DrawLine(p, hit.position);
                Gizmos.DrawWireSphere(hit.position, 0.2f);
            }
        }

        /// <summary>Area 인덱스별 색. 0=Walkable, 1=NotWalkable, 2=Jump, 그 외는 커스텀(Leap 등).</summary>
        static Color AreaColor(int area)
        {
            switch (area)
            {
                case 0:  return new Color(0.25f, 0.65f, 1f, 0.75f);   // Walkable — 파랑
                case 1:  return new Color(0.5f, 0.5f, 0.5f, 0.5f);    // NotWalkable — 회색
                case 2:  return new Color(1f, 0.85f, 0.2f, 0.85f);    // Jump — 노랑
                default:
                    // 커스텀 Area(Leap / LeapTraversal 등)는 인덱스로 색을 갈라 구분
                    float h = (area * 0.37f) % 1f;
                    return Color.HSVToRGB(h, 0.75f, 1f) * new Color(1f, 1f, 1f, 0.85f);
            }
        }

        /// <summary>F2로 켜고 끈다(Play 중). 메뉴로도 생성 가능.</summary>
        public static NavMeshDebugView Toggle()
        {
            var v = FindFirstObjectByType<NavMeshDebugView>();
            if (v != null) { Destroy(v.gameObject); return null; }
            var go = new GameObject("[NavMeshDebugView]");
            return go.AddComponent<NavMeshDebugView>();
        }

        static Vector3 ViewerPos()
        {
            var main = Main.Instance;
            if (main != null && main.Cam != null) return main.Cam.transform.position;
#if UNITY_EDITOR
            var sv = UnityEditor.SceneView.lastActiveSceneView;
            if (sv != null && sv.camera != null) return sv.camera.transform.position;
#endif
            return Vector3.zero;
        }
    }
}
