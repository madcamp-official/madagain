using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// 파손 변형 프리팹 생성기.
    ///
    /// 런타임에 메시를 자르지 않고 <b>에디터에서 미리 구워둔다</b>:
    ///   · 메시 읽기 권한(isReadable)이 필요 없다 → 런타임 메모리 두 배가 안 됨
    ///   · 스폰 시 프리팹만 고르면 끝 → 런타임 비용 0
    ///   · 결과를 눈으로 확인하고 이상하면 고칠 수 있다
    ///
    /// 조합 수가 적어(부위 3개 → 7가지) 디스크 부담도 없다.
    /// </summary>
    public static class DamageVariantTool
    {
        const string OutDir     = "Assets/_Project/Prefabs/Resources/Enemies/Damaged";
        const string MeshDir    = "Assets/_Project/Prefabs/Resources/Enemies/Damaged/Meshes";
        const int    MaxParts   = 2;    // 개체당 동시 파손 최대

        /// <summary>몹별 파손 가능 부위. 사장님 확정 사항.</summary>
        struct MobDef
        {
            public string prefab;       // Resources 경로
            public string[] parts;      // 떼어낼 본 이름(부분 일치)
        }

        static readonly MobDef[] Mobs =
        {
            // 근접·원거리는 오른손에 무기를 든다 — 오른쪽 계열은 절대 건드리지 않는다.
            new MobDef { prefab = "Enemies/MeleeEnemy",  parts = new[] { "Head", "LeftShoulder", "LeftForeArm" } },
            new MobDef { prefab = "Enemies/RangedEnemy", parts = new[] { "Head", "LeftShoulder", "LeftForeArm" } },
            // 돌진몹은 무기가 없어 제약 없음
            new MobDef { prefab = "Enemies/ChargeEnemy", parts = new[] { "Head", "LeftShoulder", "RightShoulder" } },
            // 비행몹도 인간형 리그라 동일하게 적용된다
            new MobDef { prefab = "Enemies/FlyingEnemy", parts = new[] { "Head", "LeftShoulder", "RightShoulder" } },
        };

        [MenuItem("Tools/몹/파손 변형 생성")]
        public static void Build()
        {
            Directory.CreateDirectory(OutDir);
            Directory.CreateDirectory(MeshDir);

            int made = 0, failed = 0;
            var log = new System.Text.StringBuilder();

            foreach (var mob in Mobs)
            {
                var src = Resources.Load<GameObject>(mob.prefab);
                if (src == null) { log.Append($"★ 프리팹 없음: {mob.prefab}\n"); failed++; continue; }

                string baseName = Path.GetFileName(mob.prefab);
                var combos = Combinations(mob.parts, MaxParts);
                log.Append($"── {baseName}: 부위 {mob.parts.Length}개 → 조합 {combos.Count}개\n");

                for (int ci = 0; ci < combos.Count; ci++)
                {
                    string variantName = $"{baseName}_d{ci + 1:00}";
                    if (BuildVariant(src, combos[ci], variantName, log)) made++;
                    else failed++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[파손 변형] 생성 {made}개 · 실패 {failed}개\n{log}");
        }

        /// <summary>부위 목록에서 1~max개를 고르는 모든 조합(빈 조합 제외).</summary>
        static List<string[]> Combinations(string[] parts, int max)
        {
            var outp = new List<string[]>();
            int n = parts.Length;
            for (int mask = 1; mask < (1 << n); mask++)
            {
                int cnt = 0;
                for (int b = 0; b < n; b++) if ((mask & (1 << b)) != 0) cnt++;
                if (cnt > max) continue;
                var list = new List<string>();
                for (int b = 0; b < n; b++) if ((mask & (1 << b)) != 0) list.Add(parts[b]);
                outp.Add(list.ToArray());
            }
            return outp;
        }

        static bool BuildVariant(GameObject srcPrefab, string[] partNames, string variantName,
                                 System.Text.StringBuilder log)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(srcPrefab);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.name = variantName;

            try
            {
                var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();
                if (smr == null || smr.sharedMesh == null)
                { log.Append($"  ★ {variantName}: SkinnedMeshRenderer 없음\n"); Object.DestroyImmediate(inst); return false; }

                var mesh = smr.sharedMesh;
                if (!mesh.isReadable)
                { log.Append($"  ★ {variantName}: 메시 읽기 불가 — FBX 임포트에서 Read/Write 를 켜십시오\n");
                  Object.DestroyImmediate(inst); return false; }

                // ── 떼어낼 본 인덱스 모으기(자식 본까지 포함) ──
                var removeIdx = new HashSet<int>();
                var sockets   = new List<Transform>();
                foreach (var pn in partNames)
                {
                    var root = FindBone(smr.bones, pn);
                    if (root == null) { log.Append($"  ★ {variantName}: 본 '{pn}' 없음\n"); continue; }
                    sockets.Add(root);
                    // 그 본과 모든 자손 — LeftShoulder를 떼면 팔·손이 전부 딸려 온다
                    for (int i = 0; i < smr.bones.Length; i++)
                        if (smr.bones[i] != null && IsDescendantOrSelf(smr.bones[i], root)) removeIdx.Add(i);
                }
                if (removeIdx.Count == 0)
                { log.Append($"  ★ {variantName}: 떼어낼 본 없음\n"); Object.DestroyImmediate(inst); return false; }

                var split = MeshSplitter.Split(mesh, removeIdx);
                if (split == null)
                { log.Append($"  ★ {variantName}: 분할 실패(해당 삼각형 0개)\n"); Object.DestroyImmediate(inst); return false; }

                // ★ 절단면 캡은 만들지 않는다.
                //   평평한 원반이 장갑 표면 위에 얹혀 오히려 눈에 거슬렸다.
                //   로봇이라 뚫린 단면이 "기계 내부"로 읽혀 그대로 두는 편이 자연스럽다.

                // ── 자산 저장 ──
                string keptPath = $"{MeshDir}/{variantName}_body.asset";
                string partPath = $"{MeshDir}/{variantName}_part.asset";
                AssetDatabase.CreateAsset(split.kept, keptPath);
                AssetDatabase.CreateAsset(split.removed, partPath);

                smr.sharedMesh = split.kept;

                // 떨어진 부위 정보 — 런타임이 전선을 걸 때 쓴다
                var info = inst.AddComponent<DamagedPartInfo>();
                info.socketBoneName = partNames[0];
                info.partMesh       = split.removed;
                info.allParts       = partNames;

                string prefabPath = $"{OutDir}/{variantName}.prefab";
                PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
                log.Append($"  ✔ {variantName}  [{string.Join(",", partNames)}]  남김 {split.keptTris} / 떨어짐 {split.removedTris} 삼각형\n");
                Object.DestroyImmediate(inst);
                return true;
            }
            catch (System.Exception e)
            {
                log.Append($"  ★ {variantName}: {e.Message}\n");
                if (inst != null) Object.DestroyImmediate(inst);
                return false;
            }
        }

        static Transform FindBone(Transform[] bones, string name)
        {
            foreach (var b in bones)
                if (b != null && b.name.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return b;
            return null;
        }

        static bool IsDescendantOrSelf(Transform t, Transform root)
        {
            while (t != null) { if (t == root) return true; t = t.parent; }
            return false;
        }

        [MenuItem("Tools/몹/파손 변형 삭제")]
        public static void Clear()
        {
            if (!EditorUtility.DisplayDialog("파손 변형 삭제",
                $"{OutDir} 아래 전부 지웁니다.", "삭제", "취소")) return;
            AssetDatabase.DeleteAsset(OutDir);
            AssetDatabase.Refresh();
            Debug.Log("[파손 변형] 삭제 완료");
        }
    }
}
