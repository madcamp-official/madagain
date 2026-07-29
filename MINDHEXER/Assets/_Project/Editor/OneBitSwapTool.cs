using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// 선택한 오브젝트의 머티리얼을 <c>MINDHEXER/OneBit</c>으로 일괄 교체한다(손·거미용).
    ///
    /// <para>Protag 70개 + 거미 57개라 수작업이 불가능하다. <b>원래 셰이더 이름을 JSON에 기록</b>해
    /// 언제든 되돌린다 — 텍스처·색 등 다른 프로퍼티는 건드리지 않으므로 셰이더만 되돌리면 원상복구다.</para>
    ///
    /// <para>기록 파일은 <b>Assets 밖</b>(프로젝트 루트)에 둔다. 안에 두면 에셋으로 임포트돼
    /// 프로젝트 창을 어지럽힌다.</para>
    /// </summary>
    static class OneBitSwapTool
    {
        const string ShaderName = "MINDHEXER/OneBit";

        static string RecordPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "onebit_restore.json"));

        [System.Serializable] class Entry { public string material; public string shader; }
        [System.Serializable] class Record { public List<Entry> entries = new List<Entry>(); }

        [MenuItem("Tools/흑백/1비트 — 선택 오브젝트에 적용", false, 10)]
        static void Apply()
        {
            Shader target = Shader.Find(ShaderName);
            if (target == null)
            {
                EditorUtility.DisplayDialog("1비트", $"셰이더 '{ShaderName}'를 찾을 수 없습니다.", "확인");
                return;
            }

            var mats = Collect(out int rendererCount);
            if (mats.Count == 0)
            {
                EditorUtility.DisplayDialog("1비트",
                    "선택한 오브젝트에서 교체할 머티리얼을 찾지 못했습니다.\n(오브젝트를 선택하고 실행하십시오)", "확인");
                return;
            }

            Record rec = Load();
            int swapped = 0, skipped = 0;
            var skippedNames = new List<string>();

            foreach (Material m in mats)
            {
                if (m == null || m.shader == target) continue;

                string path = AssetDatabase.GetAssetPath(m);

                // fbx 등 모델 안에 박힌 머티리얼은 읽기 전용이라 못 바꾼다 → 추출이 필요하다.
                if (!string.IsNullOrEmpty(path) &&
                    !path.EndsWith(".mat", System.StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    if (skippedNames.Count < 6) skippedNames.Add(m.name);
                    continue;
                }

                // 이미 기록된 것은 덮어쓰지 않는다(두 번 적용해도 원본을 잃지 않게).
                if (!rec.entries.Exists(e => e.material == path))
                    rec.entries.Add(new Entry { material = path, shader = m.shader.name });

                Undo.RecordObject(m, "OneBit swap");
                m.shader = target;
                EditorUtility.SetDirty(m);
                swapped++;
            }

            Save(rec);
            AssetDatabase.SaveAssets();

            string msg = $"렌더러 {rendererCount}개 · 머티리얼 {mats.Count}개 중 {swapped}개 교체.";
            if (skipped > 0)
                msg += $"\n\n건너뜀 {skipped}개 — 모델(fbx) 내장 머티리얼이라 수정할 수 없습니다." +
                       $"\n예: {string.Join(", ", skippedNames)}" +
                       "\n\n임포터에서 Materials → Extract Materials 후 다시 실행하십시오.";
            msg += $"\n\n기록: {RecordPath}";

            Debug.Log("[1비트] " + msg);
            EditorUtility.DisplayDialog("1비트 적용", msg, "확인");
        }

        [MenuItem("Tools/흑백/1비트 — 원래 셰이더로 복구", false, 11)]
        static void Restore()
        {
            if (!File.Exists(RecordPath))
            {
                EditorUtility.DisplayDialog("1비트", "복구 기록이 없습니다.\n" + RecordPath, "확인");
                return;
            }

            Record rec = Load();
            int restored = 0, missing = 0;

            foreach (Entry e in rec.entries)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(e.material);
                Shader s = Shader.Find(e.shader);
                if (m == null || s == null) { missing++; continue; }

                Undo.RecordObject(m, "OneBit restore");
                m.shader = s;
                EditorUtility.SetDirty(m);
                restored++;
            }

            AssetDatabase.SaveAssets();
            File.Delete(RecordPath);

            string msg = $"{restored}개 복구." + (missing > 0 ? $" (찾지 못함 {missing}개)" : "");
            Debug.Log("[1비트] " + msg);
            EditorUtility.DisplayDialog("1비트 복구", msg + "\n기록 파일을 삭제했습니다.", "확인");
        }

        [MenuItem("Tools/흑백/1비트 — 선택 오브젝트에 적용", true)]
        [MenuItem("Tools/흑백/1비트 — 원래 셰이더로 복구", true)]
        static bool Validate() => true;

        // ── 내부 ─────────────────────────────────────────────────────────

        static HashSet<Material> Collect(out int rendererCount)
        {
            var mats = new HashSet<Material>();
            rendererCount = 0;

            foreach (GameObject go in Selection.gameObjects)
            {
                var rends = go.GetComponentsInChildren<Renderer>(true);   // 꺼둔 파츠도 포함
                rendererCount += rends.Length;
                foreach (Renderer r in rends)
                    foreach (Material m in r.sharedMaterials)
                        if (m != null) mats.Add(m);
            }
            return mats;
        }

        static Record Load()
        {
            if (!File.Exists(RecordPath)) return new Record();
            try { return JsonUtility.FromJson<Record>(File.ReadAllText(RecordPath)) ?? new Record(); }
            catch { return new Record(); }
        }

        static void Save(Record rec)
        {
            try { File.WriteAllText(RecordPath, JsonUtility.ToJson(rec, true)); }
            catch (System.Exception e) { Debug.LogWarning("[1비트] 기록 저장 실패: " + e.Message); }
        }
    }
}
