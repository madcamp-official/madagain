using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>테스트용 웨이브 구성을 씬의 _SpawnBox 마커로 자동 생성한다(손으로 채우기 전 동작 확인용).</summary>
    public static class ArenaWavesTestSetup
    {
        [MenuItem("Tools/맵 부속/테스트 웨이브 생성")]
        static void CreateTestWaves()
        {
            Transform box = FindByName("_SpawnBox");
            if (box == null)
            {
                Debug.LogError("[테스트 웨이브] '_SpawnBox'를 찾지 못했습니다. 아레나 씬을 열고 다시 실행하십시오.");
                return;
            }

            var markers = new List<Transform>();
            foreach (var r in box.GetComponentsInChildren<MeshRenderer>(true)) markers.Add(r.transform);
            if (markers.Count == 0)
            {
                Debug.LogError("[테스트 웨이브] _SpawnBox 하위에 마커가 없습니다.");
                return;
            }

            ArenaWaves aw = Object.FindFirstObjectByType<ArenaWaves>();
            if (aw != null) Undo.RecordObject(aw, "테스트 웨이브 생성");
            else
            {
                var go = new GameObject("_Waves");
                go.transform.SetParent(box.parent, false);
                aw = go.AddComponent<ArenaWaves>();
                Undo.RegisterCreatedObjectUndo(go, "테스트 웨이브 생성");
            }

            // 테스트 구성: 웨이브마다 **모든 큐브가 1마리씩**, 종류는 전 종류를 순환 배정.
            // 전멸 조건으로 둬서 웨이브가 겹치지 않게 한다(동시 최대 = 마커 수, MaxEnemies 초과 방지).
            // 대형(Large)은 테스트에서 제외.
            var kindList = new List<MobKind>();
            foreach (MobKind k in System.Enum.GetValues(typeof(MobKind)))
                if (k != MobKind.Large) kindList.Add(k);
            MobKind[] kinds = kindList.ToArray();

            const int   MobsPerPipe = 2;      // 배관당 방출 수(≥2여야 간격이 의미를 가짐)
            const float PipeInterval = 1.5f;  // 배관 안 방출 간격(초)

            aw.waves = new Wave[WaveTable.Length];   // 구성표 행 수 = 웨이브 수(제한 없음)
            int lastPipeCount = 0, lastTotal = 0;
            for (int w = 0; w < aw.waves.Length; w++)
            {
                // 웨이브마다 구성이 다르다 → 그 웨이브의 쿼터 합으로 총 마리 수·배관 수를 낸다.
                int MobsPerWave = 0;
                foreach (MobKind k in kinds) MobsPerWave += QuotaOf(w, k);
                // 배관당 수로 안 나눠떨어져도 누락되지 않게 올림 — 마지막 배관이 나머지를 맡는다.
                int pipeCount = Mathf.Clamp(Mathf.CeilToInt(MobsPerWave / (float)MobsPerPipe), 1, markers.Count);
                lastPipeCount = pipeCount; lastTotal = MobsPerWave;

                MobKind[] pool = BuildPool(MobsPerWave, kinds, w);
                var pipes = new PipeEmission[pipeCount];
                int cursor = 0;
                for (int i = 0; i < pipeCount; i++)
                {
                    int take = Mathf.Min(MobsPerPipe, MobsPerWave - cursor);   // 마지막 배관은 나머지만
                    var mobs = new MobEmit[Mathf.Max(0, take)];
                    for (int k = 0; k < take; k++)
                        mobs[k] = new MobEmit { kind = pool[cursor++], intervalOverride = -1f };

                    pipes[i] = new PipeEmission
                    {
                        // 마커를 고르게 골라 아레나 전체에 퍼지게 한다.
                        marker = markers[(int)((long)i * markers.Count / pipeCount)],
                        startDelay = 0f,            // 모든 배관 동시 시작
                        interval = PipeInterval,    // 배관 안에서 1.5초 간격
                        mobs = mobs,
                    };
                }
                aw.waves[w] = new Wave
                {
                    name = $"W{w + 1} 배관 {pipeCount}개 · {MobsPerWave}마리",
                    startDelay = 1f,
                    pipes = pipes,
                    advance = WaveAdvanceMode.KillAll,
                    advanceValue = 0f,
                };
            }

            EditorUtility.SetDirty(aw);
            Selection.activeGameObject = aw.gameObject;
            Debug.Log($"[테스트 웨이브] 3웨이브 생성 완료 → '{aw.name}'. (마지막 웨이브: 배관 {lastPipeCount}개 · {lastTotal}마리) " +
                      "플레이 후 콘솔(`)에서 'wave start' 로 실행하십시오.");
        }

        /// <summary>
        /// ★ 웨이브 구성표 — **여기 한 곳만 고치면 된다.**
        /// 행 하나 = 웨이브 하나. **행을 추가하면 웨이브가 늘어난다**(개수 제한 없음).
        /// 적지 않은 종류는 0마리. 종류: Grunt(근) Pinky(돌) Soldier(원) Caco(공)
        ///                          GruntT(근층) SoldierT(원층) Large(대)
        /// </summary>
        static readonly (MobKind kind, int count)[][] WaveTable =
        {
            // W1 — 근 7 · 근층 7 · 돌 6
            new[]
            {
                (MobKind.Grunt, 7), (MobKind.GruntT, 7), (MobKind.Pinky, 6),
            },
            // W2 — 근 4 · 근층 4 · 돌 2 · 원 3 · 원층 3 · 공 4
            new[]
            {
                (MobKind.Grunt, 4), (MobKind.GruntT, 4), (MobKind.Pinky, 2),
                (MobKind.Soldier, 3), (MobKind.SoldierT, 3), (MobKind.Caco, 4),
            },
            // W3 — 공 7
            new[]
            {
                (MobKind.Caco, 7),
            },
            // ↓ 웨이브를 더 넣으려면 이 아래에 행을 추가하십시오.
            // new[] { (MobKind.Grunt, 10), (MobKind.Large, 2) },
        };

        /// <summary>구성표에서 해당 웨이브·종류의 마리 수를 읽는다. 없으면 0.</summary>
        static int QuotaOf(int wave, MobKind k)
        {
            if (wave < 0 || wave >= WaveTable.Length) return 0;
            foreach (var row in WaveTable[wave]) if (row.kind == k) return row.count;
            return 0;
        }

        /// <summary>
        /// 지정 마리 수(QuotaOf)대로 종류 목록을 만든다.
        /// 남은 수가 가장 많은 종류를 계속 뽑아 고르게 섞는다(Random 미사용, 결정론).
        /// </summary>
        static MobKind[] BuildPool(int total, MobKind[] kinds, int wave)
        {
            var remain = new int[kinds.Length];
            for (int i = 0; i < kinds.Length; i++) remain[i] = QuotaOf(wave, kinds[i]);

            var pool = new MobKind[total];
            for (int n = 0; n < total; n++)
            {
                int best = -1;
                for (int i = 0; i < kinds.Length; i++)
                {
                    int idx = (i + wave) % kinds.Length;   // 웨이브마다 동점 처리 순서를 옮겨 배치를 다르게
                    if (remain[idx] <= 0) continue;
                    if (best < 0 || remain[idx] > remain[best]) best = idx;
                }
                if (best < 0) { pool[n] = kinds[0]; continue; }
                pool[n] = kinds[best];
                remain[best]--;
            }
            return pool;
        }

        /// <summary>배관 하나 생성 — 지정 마커에서 주어진 순서대로 몹을 뱉는다.</summary>
        static PipeEmission Pipe(List<Transform> markers, int markerIndex, float startDelay, float interval,
                                 params MobKind[] kinds)
        {
            var mobs = new MobEmit[kinds.Length];
            for (int i = 0; i < kinds.Length; i++)
                mobs[i] = new MobEmit { kind = kinds[i], intervalOverride = -1f };   // 음수 = 배관 기본 간격
            return new PipeEmission
            {
                marker = markers[markerIndex % markers.Count],
                startDelay = startDelay,
                interval = interval,
                mobs = mobs,
            };
        }

        /// <summary>
        /// 스폰 마커 이름을 현재 좌표로 재생성한다 — 웨이브 목록에서 "어느 큐브인지" 바로 알아보기 위함.
        /// 형식: {그룹약자}_{x}_{y}_{z} (예: Ceil_0_19_-12, WL_-14_6_3)
        /// 큐브를 옮긴 뒤 다시 실행하면 이름이 갱신된다(좌표 stale 문제 해소).
        /// ※ 웨이브 설정은 오브젝트 참조라 이름을 바꿔도 깨지지 않는다.
        /// </summary>
        [MenuItem("Tools/맵 부속/스폰 마커 이름 좌표로 재생성")]
        static void RenameMarkersByCoord()
        {
            Transform box = FindByName("_SpawnBox");
            if (box == null)
            {
                Debug.LogError("[마커 이름] '_SpawnBox'를 찾지 못했습니다.");
                return;
            }

            var used = new HashSet<string>();
            int n = 0;
            foreach (var r in box.GetComponentsInChildren<MeshRenderer>(true))
            {
                Transform t = r.transform;
                string prefix = GroupPrefix(t, box);
                Vector3 p = t.position;
                string baseName = $"{prefix}_{Mathf.RoundToInt(p.x)}_{Mathf.RoundToInt(p.y)}_{Mathf.RoundToInt(p.z)}";

                string final = baseName;
                int dup = 2;
                while (!used.Add(final)) final = $"{baseName}#{dup++}";   // 같은 칸 중복 방지

                Undo.RecordObject(t.gameObject, "마커 이름 재생성");
                t.gameObject.name = final;
                n++;
            }
            Debug.Log($"[마커 이름] {n}개 이름을 좌표로 재생성했습니다. 큐브를 옮긴 뒤 다시 실행하면 갱신됩니다.");
        }

        /// <summary>마커가 속한 그룹(_Ceiling 등)에서 짧은 접두사를 만든다.</summary>
        static string GroupPrefix(Transform marker, Transform box)
        {
            // _SpawnBox 바로 아래 그룹까지 거슬러 올라간다.
            Transform g = marker;
            while (g.parent != null && g.parent != box) g = g.parent;
            string raw = (g.parent == box) ? g.name : "SP";

            switch (raw)
            {
                case "_Ceiling":     return "Ceil";
                case "_Wall_left":   return "WL";
                case "_Wall_right":  return "WR";
                case "_Wall_front":  return "WF";
                case "_Wall_back":   return "WB";
                default:
                    string s = raw.TrimStart('_').Replace("Wall_", "W");
                    return s.Length > 4 ? s.Substring(0, 4) : s;
            }
        }

        internal static Transform FindByName(string wanted)
        {
            for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                        if (t.name == wanted) return t;
            }
            return null;
        }
    }

    /// <summary>
    /// ArenaWaves 저작 보조. 씬에서 마커 큐브를 다중 선택한 뒤 버튼 한 번으로 해당 웨이브의
    /// **배관**으로 추가한다. 배관마다 뱉을 몹 목록은 Inspector에서 개별로 커스터마이즈한다.
    /// </summary>
    [CustomEditor(typeof(ArenaWaves))]
    public class ArenaWavesEditor : Editor
    {
        int     targetWave = 0;
        MobKind seedKind   = MobKind.Grunt;
        int     seedCount  = 1;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var aw = (ArenaWaves)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("저작 도구", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "씬에서 마커 큐브를 선택하고 아래 버튼을 누르면 해당 웨이브의 '배관'으로 추가됩니다.\n" +
                "추가 후 각 배관의 Mobs 목록에서 큐브별로 종류·순서·간격을 자유롭게 고치십시오.",
                MessageType.Info);

            int waveCount = aw.waves != null ? aw.waves.Length : 0;
            targetWave = EditorGUILayout.IntField("웨이브 번호 (0부터)", targetWave);
            seedKind   = (MobKind)EditorGUILayout.EnumPopup("초기 몹 종류", seedKind);
            seedCount  = Mathf.Max(0, EditorGUILayout.IntField("배관당 초기 마리 수", seedCount));

            List<Transform> picked = PickedMarkers(aw);
            bool waveOk = targetWave >= 0 && targetWave < waveCount;

            if (waveCount == 0)
                EditorGUILayout.HelpBox("웨이브가 없습니다. 위 Waves 목록에 웨이브를 먼저 추가하십시오.", MessageType.Warning);
            else if (!waveOk)
                EditorGUILayout.HelpBox($"웨이브 번호는 0 ~ {waveCount - 1} 범위여야 합니다.", MessageType.Warning);

            using (new EditorGUI.DisabledScope(!waveOk || picked.Count == 0))
            {
                if (GUILayout.Button($"선택한 마커 {picked.Count}개를 웨이브 {targetWave}에 배관으로 추가"))
                    AddPipes(aw, targetWave, picked, seedKind, seedCount);
            }

            using (new EditorGUI.DisabledScope(!waveOk))
            {
                if (GUILayout.Button($"웨이브 {targetWave} 배관 전부 비우기"))
                    ClearWave(aw, targetWave);
            }

            if (waveCount > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("요약", EditorStyles.boldLabel);
                for (int i = 0; i < waveCount; i++)
                {
                    Wave w = aw.waves[i];
                    string label = string.IsNullOrEmpty(w?.name) ? $"웨이브 {i}" : $"웨이브 {i} ({w.name})";
                    EditorGUILayout.LabelField(label, $"배관 {aw.PipeCountOf(i)}개 · 총 {aw.SpawnCountOf(i)}마리");
                }
            }
        }

        static List<Transform> PickedMarkers(ArenaWaves aw)
        {
            var list = new List<Transform>();
            foreach (Transform t in Selection.transforms)
                if (t != null && t != aw.transform) list.Add(t);
            return list;
        }

        static void AddPipes(ArenaWaves aw, int index, List<Transform> markers, MobKind kind, int count)
        {
            Undo.RecordObject(aw, "웨이브에 배관 추가");

            Wave wave = aw.waves[index];
            var pipes = new List<PipeEmission>(wave.pipes ?? new PipeEmission[0]);
            foreach (Transform t in markers)
            {
                var mobs = new MobEmit[count];
                for (int i = 0; i < count; i++)
                    mobs[i] = new MobEmit { kind = kind, intervalOverride = -1f };
                pipes.Add(new PipeEmission { marker = t, startDelay = 0f, interval = 0.5f, mobs = mobs });
            }
            wave.pipes = pipes.ToArray();

            EditorUtility.SetDirty(aw);
            Debug.Log($"[ArenaWaves] 웨이브 {index}에 배관 {markers.Count}개 추가(각 {count}마리 {kind}). " +
                      $"현재 배관 {wave.pipes.Length}개 · 총 {wave.TotalMobs()}마리.");
        }

        static void ClearWave(ArenaWaves aw, int index)
        {
            Undo.RecordObject(aw, "웨이브 배관 비우기");
            aw.waves[index].pipes = new PipeEmission[0];
            EditorUtility.SetDirty(aw);
            Debug.Log($"[ArenaWaves] 웨이브 {index} 배관을 비웠습니다.");
        }
    }
}
