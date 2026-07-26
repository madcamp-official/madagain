using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// 블록아웃 보조: 맵 배치용 부속(바닥·벽·블록·경사로·기둥·엄폐)을 현재 씬에 생성한다.
    /// 콜라이더 포함(프리미티브 기본) + 격자 머티리얼 자동 적용. Undo 지원, 생성 즉시 선택.
    /// 게임 스케일 1u = 1m (플레이어 키 1.15). 경사로는 회전 판(매끄러움, <45° = NavMesh 걸림).
    /// </summary>
    public static class MapPartsTool
    {
        const string GridMatPath = "Assets/_Project/Editor/GridMaterial.mat";
        const string MarkerMatPath = "Assets/_Project/View/Spawn/SpawnMarkerMat.mat";
        const string MarkerShaderName = "Precog/SpawnMarkerDir";

        [MenuItem("Tools/맵 부속/바닥 타일 (10x0.5x10)")]
        static void Floor() => Box("Floor", new Vector3(10f, 0.5f, 10f));

        [MenuItem("Tools/맵 부속/벽 (8x4x0.5)")]
        static void Wall() => Box("Wall", new Vector3(8f, 4f, 0.5f));

        [MenuItem("Tools/맵 부속/블록·발판 (3x3x3)")]
        static void Block() => Box("Block", new Vector3(3f, 3f, 3f));

        [MenuItem("Tools/맵 부속/얇은 발판 (6x0.4x6)")]
        static void Platform() => Box("Platform", new Vector3(6f, 0.4f, 6f));

        [MenuItem("Tools/맵 부속/기둥 (1.5x5x1.5)")]
        static void Pillar() => Box("Pillar", new Vector3(1.5f, 5f, 1.5f));

        [MenuItem("Tools/맵 부속/엄폐 블록 (3x1.2x1.5)")]
        static void Cover() => Box("Cover", new Vector3(3f, 1.2f, 1.5f));

        [MenuItem("Tools/맵 부속/경사로 22° (오름2 런5)")]
        static void Ramp22() => Ramp("Ramp_22", rise: 2f, run: 5f, width: 4f, thickness: 0.5f);

        [MenuItem("Tools/맵 부속/경사로 27° (오름3 런6)")]
        static void Ramp27() => Ramp("Ramp_27", rise: 3f, run: 6f, width: 4f, thickness: 0.5f);

        [MenuItem("Tools/맵 부속/컨테이너 앞뒤개방 (2.6x2.8x6)")]
        static void Container()
        {
            // 속 빈 통: 바닥·천장·좌우벽 4장을 '하나의 메시'로 결합 → 단일 부속(자식 없음).
            // 앞뒤(±Z) 개방 → 관통. 배치 후 회전하면 방향 자유.
            var boxes = ContainerBoxes();

            // 박스별로 단위큐브 정점을 복사·배치하고, 각 정점의 원래 단위좌표(±0.5)를 UV2에 저장.
            // → 셰이더가 _EDGE_FROM_UV로 판마다 모서리 외곽선을 낸다(결합 메시라도 외곽선 정상).
            var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh unit = temp.GetComponent<MeshFilter>().sharedMesh;
            Vector3[] uVerts = unit.vertices;   // ±0.5 단위좌표
            Vector3[] uNorms = unit.normals;
            int[]     uTris  = unit.triangles;
            Object.DestroyImmediate(temp);

            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var edge  = new List<Vector3>();    // UV2: 박스별 단위좌표(외곽선용)
            var tris  = new List<int>();
            foreach (var b in boxes)
            {
                var M = Matrix4x4.TRS(b.pos, Quaternion.identity, b.size);
                int baseIdx = verts.Count;
                for (int k = 0; k < uVerts.Length; k++)
                {
                    verts.Add(M.MultiplyPoint3x4(uVerts[k]));   // 스케일·이동 적용된 실제 위치
                    norms.Add(uNorms[k]);                       // 축정렬 박스라 방향 그대로
                    edge.Add(uVerts[k]);                        // 원래 ±0.5
                }
                for (int k = 0; k < uTris.Length; k++) tris.Add(baseIdx + uTris[k]);
            }

            var mesh = new Mesh { name = "ContainerMesh" };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(2, edge);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            var go = new GameObject("Container");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = GridMatEdgeUV();
            // 콜리전은 판별 BoxCollider 4개(결합 MeshCollider 금지).
            // 이유: 하나의 결합 콜라이더면 안쪽에서 바닥에 얹히는 순간 캡슐이 그 콜라이더와 겹쳐
            // CapsuleCast가 '겹친 콜라이더는 무시' → 옆벽까지 통째로 무시돼 관통했다.
            // 판을 분리하면 바닥 박스만 겹치고 옆벽 박스는 계속 유효 → 정상 차단.
            foreach (var b in boxes)
            {
                var bc = go.AddComponent<BoxCollider>();
                bc.center = b.pos;
                bc.size   = b.size;
            }
            go.transform.position = SpawnPos();

            Undo.RegisterCreatedObjectUndo(go, "맵 부속 생성");
            Selection.activeGameObject = go;
        }

        /// <summary>컨테이너 4판(바닥·천장·좌벽·우벽)의 로컬 center/size. 생성·수리가 공유한다.</summary>
        static (Vector3 pos, Vector3 size)[] ContainerBoxes()
        {
            const float length = 6f, width = 2.6f, height = 2.8f, thk = 0.2f;
            float wallH = height - thk;                 // 천장 아래까지
            float wx    = width * 0.5f - thk * 0.5f;    // 좌우 벽 안쪽면이 폭 경계에 맞음
            return new (Vector3, Vector3)[]
            {
                (new Vector3(0f, -thk * 0.5f, 0f),         new Vector3(width, thk, length)),  // 바닥(윗면 flush)
                (new Vector3(0f, height - thk * 0.5f, 0f), new Vector3(width, thk, length)),  // 천장(위=발판)
                (new Vector3(-wx, wallH * 0.5f, 0f),       new Vector3(thk, wallH, length)),  // 좌벽
                (new Vector3( wx, wallH * 0.5f, 0f),       new Vector3(thk, wallH, length)),  // 우벽
            };
        }

        [MenuItem("Tools/맵 부속/컨테이너 콜라이더 수리 (Mesh→Box)")]
        static void RepairContainerColliders()
        {
            // 이미 배치된 컨테이너의 결합 MeshCollider를 판별 BoxCollider 4개로 교체.
            // Transform(위치·회전·스케일)·렌더 메시는 안 건드림 → 보이는 모양·크기 그대로.
            var boxes = ContainerBoxes();
            int n = 0;
            for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true))
                    {
                        if (mc == null || mc.sharedMesh == null || mc.sharedMesh.name != "ContainerMesh") continue;
                        var go = mc.gameObject;
                        Undo.DestroyObjectImmediate(mc);
                        foreach (var b in boxes)
                        {
                            var bc = Undo.AddComponent<BoxCollider>(go);
                            bc.center = b.pos;
                            bc.size   = b.size;
                        }
                        n++;
                    }
            }
            if (n == 0) Debug.LogWarning("[맵 부속] 수리 대상(ContainerMesh MeshCollider)을 못 찾았습니다. 이미 수리됐거나 씬이 안 열렸을 수 있습니다.");
            else Debug.Log($"[맵 부속] 컨테이너 콜라이더 수리: {n}개 (MeshCollider → BoxCollider 4개, 크기·위치 유지). 씬 저장 필요.");
        }

        // ── 스폰 마커 방향표시 ──
        [MenuItem("Tools/맵 부속/선택 큐브에 방향표시 적용")]
        static void ApplyMarkerMaterial()
        {
            // 선택한 오브젝트들의 MeshRenderer 머티리얼만 방향표시 머티리얼로 교체.
            // Transform(위치·회전·스케일)·콜라이더는 일절 안 건드림 → 기존 크기 그대로 유지.
            var mat = SpawnMarkerMat();
            if (mat == null) return;
            // 선택 오브젝트 + 그 자식들까지 (부모 _SpawnBox 하나만 선택해도 하위 마커 전부 적용).
            var done = new HashSet<MeshRenderer>();
            foreach (var go in Selection.gameObjects)
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                    if (done.Add(r))
                    {
                        Undo.RecordObject(r, "방향표시 적용");
                        r.sharedMaterial = mat;
                    }
            Debug.Log($"[맵 부속] 방향표시 머티리얼 적용: {done.Count}개 (자식 포함, 크기·위치 유지). 노란 면(로컬 +Z)이 몹 출구 방향.");
        }

        [MenuItem("Tools/맵 부속/_SpawnBox 전체 방향표시 적용")]
        static void ApplyMarkerToAllSpawnBoxes()
        {
            // 선택과 무관하게: 열린 씬에서 이름이 '_SpawnBox'인 오브젝트를 찾아
            // 그 하위(손자·증손자 포함) 모든 MeshRenderer에 방향표시 머티리얼 적용.
            var mat = SpawnMarkerMat();
            if (mat == null) return;
            var done = new HashSet<MeshRenderer>();
            int boxes = 0;
            for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                        if (tr.name == "_SpawnBox")
                        {
                            boxes++;
                            foreach (var r in tr.GetComponentsInChildren<MeshRenderer>(true))
                                if (done.Add(r))
                                {
                                    Undo.RecordObject(r, "방향표시 적용");
                                    r.sharedMaterial = mat;
                                }
                        }
            }
            if (boxes == 0)
                Debug.LogWarning("[맵 부속] '_SpawnBox' 이름의 오브젝트를 못 찾았습니다. 열린 씬·이름을 확인하십시오.");
            else
                Debug.Log($"[맵 부속] _SpawnBox {boxes}개, 하위 MeshRenderer {done.Count}개에 방향표시 적용(크기 유지). 노란 면(+Z)=출구.");
        }

        [MenuItem("Tools/맵 부속/스폰 마커 큐브 생성")]
        static void SpawnMarkerCube()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "SpawnMarker";
            go.transform.position = SpawnPos() + Vector3.up * 0.5f;
            go.transform.localScale = new Vector3(2f, 2f, 1f);   // 기존 마커 규격(얇은 판, Z가 두께=출구 방향)
            go.GetComponent<MeshRenderer>().sharedMaterial = SpawnMarkerMat();
            // 마커는 장애물이 아니므로 콜라이더 제거(이동·NavMesh 방해 방지).
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);

            Undo.RegisterCreatedObjectUndo(go, "스폰 마커 생성");
            Selection.activeGameObject = go;
        }

        /// <summary>방향표시 머티리얼을 로드하거나 없으면 셰이더로 생성해 에셋으로 저장.</summary>
        static Material SpawnMarkerMat()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MarkerMatPath);
            if (mat != null) return mat;

            var sh = Shader.Find(MarkerShaderName);
            if (sh == null)
            {
                Debug.LogError($"[맵 부속] 셰이더 '{MarkerShaderName}'를 찾지 못했습니다. " +
                               "SpawnMarkerDir.shader 임포트/컴파일을 확인하십시오.");
                return null;
            }
            mat = new Material(sh) { name = "SpawnMarkerMat" };
            AssetDatabase.CreateAsset(mat, MarkerMatPath);
            AssetDatabase.SaveAssets();
            return mat;
        }

        // ── 생성 헬퍼 ──
        /// <summary>
        /// 그리드 머티리얼 인스턴스 + 외곽선을 UV2로 계산(_EDGE_FROM_UV). 결합 메시는 positionOS가
        /// ±0.5 밖이라, 박스별 단위좌표를 넣은 UV2로 판마다 모서리 외곽선을 낸다. 없으면 Lit 폴백.
        /// </summary>
        static Material GridMatEdgeUV()
        {
            var baseMat = AssetDatabase.LoadAssetAtPath<Material>(GridMatPath);
            if (baseMat == null) return LitMat(new Color(0.55f, 0.55f, 0.58f));
            var m = new Material(baseMat);   // 그리드 색·격자 설정 복사
            m.SetFloat("_EdgeFromUV", 1f);
            m.EnableKeyword("_EDGE_FROM_UV");
            return m;
        }

        /// <summary>URP Lit 단색 머티리얼(그리드 머티리얼 없을 때 폴백).</summary>
        static Material LitMat(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }


        static void Box(string name, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = SpawnPos();
            go.transform.localScale = size;
            Finish(go);
        }

        /// <summary>경사로: 판을 X축 기준 -θ 회전 → +Z 끝이 rise만큼 올라간다. 윗면이 걷는 면.</summary>
        static void Ramp(string name, float rise, float run, float width, float thickness)
        {
            float length = Mathf.Sqrt(run * run + rise * rise);   // 경사면 길이
            float theta  = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            // 낮은 끝을 원점 근처에, 높은 끝을 +Z로. 중심은 (0, rise/2, run/2).
            go.transform.position = SpawnPos() + new Vector3(0f, rise * 0.5f, run * 0.5f);
            go.transform.rotation = Quaternion.Euler(-theta, 0f, 0f);
            go.transform.localScale = new Vector3(width, thickness, length);
            Finish(go);
        }

        static Vector3 SpawnPos()
        {
            // 씬 뷰 화면 중앙(피벗)에 생성 — 안 보이는 원점에 안 묻히게.
            var sv = SceneView.lastActiveSceneView;
            if (sv != null) { Vector3 p = sv.pivot; p.y = 0f; return p; }
            return Vector3.zero;
        }

        static void Finish(GameObject go)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(GridMatPath);
            if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;

            Undo.RegisterCreatedObjectUndo(go, "맵 부속 생성");
            Selection.activeGameObject = go;
        }
    }
}
