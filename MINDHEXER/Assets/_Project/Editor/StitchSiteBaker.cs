using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// 해킹 실이 찌를 <b>땀 자리 후보</b>를 실제 메시에서 뽑아 <see cref="StitchSites"/>에 굽는다.
    ///
    /// <para><b>왜 에디터에서 하나</b> — 런타임 레이캐스트는 콜라이더에 맞는다. 터렛처럼 박스
    /// 콜라이더 하나로 감싼 대상은 상자가 실제 메시보다 커서 실이 허공을 찌른다. 여기서는
    /// 콜라이더를 무시하고 <b>메시 삼각형에 직접</b> 쏘므로 정확하다. 비용은 에디터에서만 든다.</para>
    ///
    /// <para>후보를 넉넉히(기본 40개) 구워 두고 런타임에 5개를 뽑는다 — 조합이 수십만 가지라
    /// <b>매번 다른 자리</b>를 찌른다. 손으로 심으면 늘 같은 자리다.</para>
    ///
    /// 사용: 대상(프리팹 에셋 또는 씬 오브젝트)을 선택하고 <c>Tools/해킹/땀 자리 굽기</c>.
    /// </summary>
    public static class StitchSiteBaker
    {
        const int DefaultCount = 40;

        [MenuItem("Tools/해킹/땀 자리 굽기", false, 20)]
        static void BakeSelection()
        {
            var objs = Selection.objects;
            if (objs == null || objs.Length == 0)
            {
                EditorUtility.DisplayDialog("땀 자리 굽기", "대상을 선택하십시오(프리팹 또는 씬 오브젝트).", "확인");
                return;
            }

            int ok = 0, fail = 0;
            foreach (var o in objs)
            {
                var go = o as GameObject;
                if (go == null) { fail++; continue; }

                string path = AssetDatabase.GetAssetPath(go);
                bool isPrefabAsset = !string.IsNullOrEmpty(path) && path.EndsWith(".prefab");

                if (isPrefabAsset)
                {
                    var root = PrefabUtility.LoadPrefabContents(path);
                    bool done = Bake(root, DefaultCount);
                    if (done) PrefabUtility.SaveAsPrefabAsset(root, path);
                    PrefabUtility.UnloadPrefabContents(root);
                    if (done) ok++; else fail++;
                }
                else
                {
                    if (Bake(go, DefaultCount))
                    {
                        EditorUtility.SetDirty(go);
                        ok++;
                    }
                    else fail++;
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[땀 자리 굽기] 성공 {ok}건 / 실패 {fail}건");
        }

        [MenuItem("Tools/해킹/땀 자리 지우기", false, 21)]
        static void ClearSelection()
        {
            foreach (var o in Selection.objects)
            {
                var go = o as GameObject;
                if (go == null) continue;
                var s = go.GetComponentInChildren<StitchSites>();
                if (s == null) continue;
                Undo.RecordObject(s, "땀 자리 지우기");
                s.sites.Clear();
                s.bakedCount = 0;
                EditorUtility.SetDirty(s);
            }
        }

        // ── 굽기 본체 ────────────────────────────────────────────────────────

        struct Tri
        {
            public Vector3 a, b, c;     // 월드
            public Vector3 n;           // 기하 법선 — 오프셋에는 스무딩 법선보다 이게 맞다
            public float area;
            public int bone;            // −1 = 정적
        }

        public static bool Bake(GameObject root, int count)
        {
            var bones = new List<Transform>();
            var tris = new List<Tri>(4096);

            CollectStatic(root, tris);
            CollectSkinned(root, tris, bones);

            if (tris.Count == 0)
            {
                Debug.LogWarning($"[땀 자리 굽기] '{root.name}'에서 메시를 못 찾았습니다.", root);
                return false;
            }

            // 면적 누적 — 큰 면에 더 많이 떨어지게 해야 고르게 흩어진다.
            var cum = new float[tris.Count];
            float total = 0f;
            for (int i = 0; i < tris.Count; i++) { total += tris[i].area; cum[i] = total; }
            if (total <= 1e-8f)
            {
                Debug.LogWarning($"[땀 자리 굽기] '{root.name}'의 메시 면적이 0입니다.", root);
                return false;
            }

            // ★ Hackable과 <b>같은 오브젝트</b>에 붙인다.
            // 프리팹 루트에 붙이면 Hackable이 자식일 때 런타임 조회가 위로 올라가야 하는데,
            // 그러면 다른 물체 밑에 낀 대상이 <b>남의 땀 자리를 집어온다</b>(실제로 발생).
            // 같은 자리에 두면 조회가 한 단계로 끝나고 오인이 원리적으로 불가능하다.
            var hk = root.GetComponentInChildren<Hackable>(true);
            var host = hk != null ? hk.gameObject : root;

            // 예전 판이 루트에 붙여 둔 것 등, host가 아닌 곳의 StitchSites는 걷어낸다.
            // 남겨 두면 조회가 어느 쪽을 집을지 모호해진다.
            foreach (var stray in root.GetComponentsInChildren<StitchSites>(true))
                if (stray != null && stray.gameObject != host) Object.DestroyImmediate(stray, true);

            var sites = host.GetComponent<StitchSites>();
            if (sites == null) sites = host.AddComponent<StitchSites>();
            sites.sites.Clear();
            sites.bones = bones.ToArray();

            // 결정적 시드 — 같은 모델을 다시 구우면 같은 결과가 나와야 비교가 된다.
            var rng = new System.Random(root.name.GetHashCode());

            int guard = count * 40;
            while (sites.sites.Count < count && guard-- > 0)
            {
                int ti = PickTriangle(cum, total, (float)rng.NextDouble());
                Tri t = tris[ti];

                // 삼각형 안 균등 샘플
                float u = (float)rng.NextDouble();
                float v = (float)rng.NextDouble();
                if (u + v > 1f) { u = 1f - u; v = 1f - v; }
                Vector3 p = t.a + (t.b - t.a) * u + (t.c - t.a) * v;

                float thick = MeasureThickness(tris, p, t.n);

                Transform space = (t.bone >= 0 && t.bone < bones.Count && bones[t.bone] != null)
                                ? bones[t.bone] : host.transform;

                sites.sites.Add(new StitchSites.Site
                {
                    localPos = space.InverseTransformPoint(p),
                    localNormal = space.InverseTransformDirection(t.n),
                    thickness = thick,
                    boneIndex = (space == host.transform) ? -1 : t.bone,
                });
            }

            sites.bakedCount = sites.sites.Count;
            EditorUtility.SetDirty(sites);
            Debug.Log($"[땀 자리 굽기] '{root.name}' — 후보 {sites.sites.Count}개 " +
                      $"(삼각형 {tris.Count}, 본 {bones.Count})", root);
            return sites.sites.Count > 0;
        }

        static int PickTriangle(float[] cum, float total, float r01)
        {
            float x = r01 * total;
            int lo = 0, hi = cum.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (cum[mid] < x) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        /// <summary>
        /// 이 렌더러를 구워도 되는가.
        ///
        /// <para><b>Play에 안 보이는 것에 땀을 박으면 실이 허공을 찌른다.</b> 실제로
        /// <c>RotationPlatform</c>의 방향 표시기(<see cref="EditorOnlyVisual"/>)가 걸려들었다 —
        /// 그건 authoring용이라 Play 시작과 함께 꺼진다.</para>
        /// </summary>
        static bool Usable(Component c)
        {
            if (c == null) return false;
            if (!c.gameObject.activeInHierarchy) return false;
            if (c.GetComponentInParent<EditorOnlyVisual>() != null) return false;

            var r = c.GetComponent<Renderer>();
            return r != null && r.enabled;
        }

        /// <summary>
        /// ⚠️ <b>Read/Write Enabled를 요구하지 않는다.</b> 임포트 설정이 꺼져 있으면
        /// <c>isReadable</c>이 false지만, <b>에디터에서는</b> 원본 데이터가 살아 있어 대개 읽힌다.
        /// 여기서 걸러내 버리면 구매 에셋(Synty·TallCity)·Tripo 모델이 통째로 대상에서 빠진다 —
        /// 그것들이 정확히 우리가 구워야 할 것들이다. 실패하면 그때 알려 준다.
        /// </summary>
        static void CollectStatic(GameObject root, List<Tri> tris)
        {
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                if (mesh == null || !Usable(mf)) continue;
                try { AddMesh(mesh, mf.transform.localToWorldMatrix, null, tris); }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[땀 자리 굽기] '{mesh.name}'를 읽지 못했습니다 — " +
                                     $"임포트 설정에서 Read/Write Enabled를 켜십시오. ({e.GetType().Name})", mf);
                }
            }
        }

        static void CollectSkinned(GameObject root, List<Tri> tris, List<Transform> bones)
        {
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var shared = smr.sharedMesh;
                if (shared == null || !Usable(smr)) continue;

                // 본을 <b>중복 없이</b> 모은다. 파츠가 쪼개진 모델은 스킨드 렌더러 수십 개가
                // 같은 스켈레톤을 공유하므로, 렌더러마다 통째로 이어붙이면 본 배열이 수천 개로
                // 불어나 프리팹에 그만큼의 트랜스폼 참조가 직렬화된다(실제로 7,670개가 나왔다).
                var rb = smr.bones;
                var remap = new int[rb == null ? 0 : rb.Length];
                for (int i = 0; i < remap.Length; i++)
                {
                    int at = bones.IndexOf(rb[i]);
                    if (at < 0) { at = bones.Count; bones.Add(rb[i]); }
                    remap[i] = at;
                }

                // 현재(바인드) 자세로 구운 정점을 쓴다. 정점 순서가 sharedMesh와 같아
                // boneWeights를 그대로 대응시킬 수 있다.
                var baked = new Mesh();
                try
                {
                    smr.BakeMesh(baked, true);

                    // 각 정점을 가장 가중치가 큰 본에 매단다 — 경비병이 걸어도 땀이 따라간다.
                    int[] vertBone = null;
                    var bw = shared.boneWeights;
                    if (bw != null && bw.Length == baked.vertexCount && remap.Length > 0)
                    {
                        vertBone = new int[bw.Length];
                        for (int i = 0; i < bw.Length; i++)
                        {
                            int bi = bw[i].boneIndex0;
                            vertBone[i] = (bi >= 0 && bi < remap.Length) ? remap[bi] : -1;
                        }
                    }

                    AddMesh(baked, smr.transform.localToWorldMatrix, vertBone, tris);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[땀 자리 굽기] '{smr.name}'의 스킨드 메시를 읽지 못했습니다 — " +
                                     $"임포트 설정에서 Read/Write Enabled를 켜십시오. ({e.GetType().Name})", smr);
                }
                Object.DestroyImmediate(baked);
            }
        }

        static void AddMesh(Mesh mesh, Matrix4x4 l2w, int[] vertBone, List<Tri> tris)
        {
            var verts = mesh.vertices;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                var idx = mesh.GetTriangles(sub);
                for (int i = 0; i + 2 < idx.Length; i += 3)
                {
                    Vector3 a = l2w.MultiplyPoint3x4(verts[idx[i]]);
                    Vector3 b = l2w.MultiplyPoint3x4(verts[idx[i + 1]]);
                    Vector3 c = l2w.MultiplyPoint3x4(verts[idx[i + 2]]);

                    Vector3 cross = Vector3.Cross(b - a, c - a);
                    float area = cross.magnitude * 0.5f;
                    if (area < 1e-9f) continue;

                    tris.Add(new Tri
                    {
                        a = a, b = b, c = c,
                        n = cross.normalized,
                        area = area,
                        bone = vertBone != null ? vertBone[idx[i]] : -1,
                    });
                }
            }
        }

        /// <summary>
        /// 표면점에서 <b>안쪽으로</b> 쏴 반대편 표면까지의 거리를 잰다.
        /// 이 값이 있어야 레일·판넬처럼 얇은 대상에서 실이 뒤로 뚫고 나오지 않는다.
        /// 반대편을 못 찾으면 0(제한 없음)을 돌려준다.
        /// </summary>
        static float MeasureThickness(List<Tri> tris, Vector3 p, Vector3 n)
        {
            Vector3 o = p - n * 1e-4f;
            Vector3 d = -n;
            float best = float.MaxValue;

            for (int i = 0; i < tris.Count; i++)
            {
                if (RayTri(o, d, tris[i], out float t) && t > 1e-4f && t < best) best = t;
            }
            return best == float.MaxValue ? 0f : best;
        }

        /// <summary>Möller–Trumbore. 양면 판정(대상 내부에서 쏘므로 뒷면에도 맞아야 한다).</summary>
        static bool RayTri(Vector3 o, Vector3 d, in Tri tri, out float t)
        {
            t = 0f;
            Vector3 e1 = tri.b - tri.a;
            Vector3 e2 = tri.c - tri.a;
            Vector3 h = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, h);
            if (Mathf.Abs(det) < 1e-9f) return false;

            float inv = 1f / det;
            Vector3 s = o - tri.a;
            float u = Vector3.Dot(s, h) * inv;
            if (u < 0f || u > 1f) return false;

            Vector3 q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(d, q) * inv;
            if (v < 0f || u + v > 1f) return false;

            t = Vector3.Dot(e2, q) * inv;
            return t > 0f;
        }
    }
}
