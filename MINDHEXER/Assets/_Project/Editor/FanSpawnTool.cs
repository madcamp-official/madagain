using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// Fan을 스폰 출구로 설정하는 툴. (설계 §4-①)
    ///  · 선택한 Fan들에 FanSpawn 컴포넌트 부착 + 출구 방향(exitEuler) 일괄 조절.
    ///  · 그 Fan들을 지정 ArenaWaves / 지정 웨이브의 PipeEmission.marker로 등록.
    ///
    /// 드롭 링크 배치는 별도 툴(⑤ Fan 드롭 링크 자동배치)에서 한다.
    /// </summary>
    public class FanSpawnTool : EditorWindow
    {
        Vector3 exitEuler = new Vector3(0f, 90f, 90f);
        float mouthGap = 0.4f;          // 입을 팬 바운즈 밖으로 밀어내는 거리(개구부 방향)
        bool attachToSelected = false;  // true면 Fan 이름 검색 없이 '선택한 오브젝트 그대로'에 부착
        ArenaWaves waves;
        int waveIndex;
        float startDelay = 1.0f;   // "준비" 시간
        float interval   = 0.5f;

        [MenuItem("Tools/스폰/Fan 스폰 설정")]
        static void Open() => GetWindow<FanSpawnTool>("Fan 스폰 설정").minSize = new Vector2(340, 320);

        void OnGUI()
        {
            int sel = Selection.gameObjects.Length;
            EditorGUILayout.HelpBox(
                "씬에서 Fan들을 선택하고 아래 버튼으로 설정하십시오.\n" +
                "① FanSpawn 부착·방향  ② 웨이브에 스폰 출구로 등록", MessageType.Info);
            EditorGUILayout.LabelField("선택된 오브젝트", sel > 0 ? sel + "개" : "없음");

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("① FanSpawn 부착 + 방향", EditorStyles.boldLabel);
            exitEuler = EditorGUILayout.Vector3Field("출구 방향(오일러)", exitEuler);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("X +90")) exitEuler.x = Mathf.Repeat(exitEuler.x + 90f, 360f);
                if (GUILayout.Button("Y +90")) exitEuler.y = Mathf.Repeat(exitEuler.y + 90f, 360f);
                if (GUILayout.Button("Z +90")) exitEuler.z = Mathf.Repeat(exitEuler.z + 90f, 360f);
            }
            mouthGap = EditorGUILayout.Slider("입 거리(개구부 밖으로, m)", mouthGap, -3f, 5f);
            attachToSelected = EditorGUILayout.Toggle("선택 그대로 부착(Fan 이름 검색 끔)", attachToSelected);
            EditorGUILayout.HelpBox("입이 엉뚱하면: '선택 그대로 부착'을 켜고 실제 팬 오브젝트를 직접 선택하거나, " +
                "부착 후 씬에서 입 핸들(주황)을 드래그해 미세조정하십시오.", MessageType.None);
            using (new EditorGUI.DisabledScope(sel == 0))
                if (GUILayout.Button("선택 Fan에 FanSpawn 부착 + 방향 적용", GUILayout.Height(24)))
                    AttachFanSpawn();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("② 웨이브에 스폰 출구로 등록", EditorStyles.boldLabel);
            waves = (ArenaWaves)EditorGUILayout.ObjectField("ArenaWaves", waves, typeof(ArenaWaves), true);
            if (waves == null && GUILayout.Button("씬에서 ArenaWaves 찾기"))
                waves = Object.FindFirstObjectByType<ArenaWaves>();

            if (waves != null)
            {
                int waveCount = waves.waves != null ? waves.waves.Length : 0;
                waveIndex = EditorGUILayout.IntSlider("웨이브 번호(0부터)", waveIndex, 0, Mathf.Max(0, waveCount - 1));
                EditorGUILayout.LabelField(" ", waveCount == 0 ? "웨이브가 없습니다 — 먼저 하나 만드십시오" : $"웨이브 {waveCount}개");
                startDelay = EditorGUILayout.FloatField("준비 시간 startDelay(초)", startDelay);
                interval   = EditorGUILayout.FloatField("기본 간격 interval(초)", interval);

                using (new EditorGUI.DisabledScope(sel == 0 || waveCount == 0))
                    if (GUILayout.Button("선택 Fan을 이 웨이브 스폰 출구로 등록", GUILayout.Height(24)))
                        RegisterToWave();
            }
        }

        void AttachFanSpawn()
        {
            int n = 0;
            foreach (var go in Selection.gameObjects)
            {
                // 선택이 _Art 래퍼 그룹이면 안쪽 실제 Fan에 붙인다(그룹에 붙이면 mouth가 엉뚱해진다).
                // attachToSelected면 이름 검색 없이 선택 오브젝트에 그대로 붙인다.
                GameObject target = attachToSelected ? go : ResolveFan(go);
                var fs = target.GetComponent<FanSpawn>();
                if (fs == null) fs = Undo.AddComponent<FanSpawn>(target);
                Undo.RecordObject(fs, "FanSpawn 방향");
                fs.exitEuler = exitEuler;
                // 입 = 팬의 '개구부 방향'으로 바운즈 밖 + 여유(0.4). 방향은 (팬 회전 × exitEuler)라
                // 팬을 어떻게 돌리든/뒤집든 입이 개구부를 따라 자동으로 옮겨진다(월드 아래 고정 폐기).
                if (LodBounds(target, out Bounds b))
                {
                    Vector3 openDir = ((target.transform.rotation * Quaternion.Euler(exitEuler)) * Vector3.forward).normalized;
                    // 월드 AABB의 openDir 방향 반경 = |축|·half 투영합.
                    float extent = Mathf.Abs(openDir.x) * b.extents.x
                                 + Mathf.Abs(openDir.y) * b.extents.y
                                 + Mathf.Abs(openDir.z) * b.extents.z;
                    Vector3 mouthWorld = b.center + openDir * (extent + mouthGap);
                    fs.mouthLocal = target.transform.InverseTransformPoint(mouthWorld);
                }
                EditorUtility.SetDirty(fs);
                n++;
            }
            Debug.Log($"[Fan 스폰] {n}개에 FanSpawn 부착 + 방향 {exitEuler} 적용(입=개구부 방향 자동, 팬 회전 따라감).");
        }

        /// <summary>선택이 _Art 그룹이면 그 안의 'Fan'을, 아니면 자기 자신을 돌려준다.</summary>
        static GameObject ResolveFan(GameObject go)
        {
            if (go.name == "Fan") return go;
            var t = go.transform.Find("Fan");
            if (t != null) return t.gameObject;
            // 깊이 탐색(그룹 → Fan)
            foreach (Transform c in go.GetComponentsInChildren<Transform>(true))
                if (c.name == "Fan") return c.gameObject;
            return go;   // Fan을 못 찾으면 그대로(경고 없이 원래 동작)
        }

        /// <summary>LOD0 렌더 바운즈(시각적 크기·중심).</summary>
        static bool LodBounds(GameObject go, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                string n = r.name.ToLowerInvariant();
                if (n.Contains("lod1") || n.Contains("lod2") || n.Contains("_collider")) continue;
                if (!any) { bounds = r.bounds; any = true; } else bounds.Encapsulate(r.bounds);
            }
            return any;
        }

        void RegisterToWave()
        {
            if (waves == null || waves.waves == null || waveIndex >= waves.waves.Length) return;
            var wave = waves.waves[waveIndex];
            var list = new List<PipeEmission>(wave.pipes ?? new PipeEmission[0]);

            int added = 0, skipped = 0;
            foreach (var go in Selection.gameObjects)
            {
                Transform t = go.transform;
                // 이미 이 웨이브에 등록돼 있으면 건너뜀(중복 방지)
                if (list.Any(p => p != null && p.marker == t)) { skipped++; continue; }
                list.Add(new PipeEmission
                {
                    marker = t,
                    startDelay = startDelay,
                    interval = interval,
                    mobs = new MobEmit[0],   // 엔트리는 나중에 채움(공백/몹)
                });
                added++;
            }
            Undo.RecordObject(waves, "웨이브에 Fan 등록");
            wave.pipes = list.ToArray();
            EditorUtility.SetDirty(waves);
            Debug.Log($"[Fan 스폰] 웨이브 {waveIndex}에 Fan {added}개 등록(중복 {skipped}개 제외). " +
                      "엔트리(몹/공백)는 ArenaWaves Inspector 또는 후속 작업에서 채우십시오.");
        }
    }
}
