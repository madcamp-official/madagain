using UnityEditor;
using UnityEngine;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// 팬마다 스폰 실체화 재생 박스(<see cref="SpawnRevealVolume"/>)를 생성/갱신하는 설정 창.
    /// 박스 크기와 팬 입에서 얼마나 아래로 내릴지를 조절할 수 있다.
    ///
    /// ★ 박스는 <b>팬에 자식으로 붙이지 않는다.</b> 팬은 런타임에 FanSpawnActor가 오르내리므로
    ///   자식이면 박스도 따라 움직여 몹 경로와 어긋난다. 몹은 고정 링크(휴지 상태 입 근처)에서 나오니
    ///   박스도 휴지 상태 팬 입 위치(+하강량)에 고정으로 둔다(팬의 부모 아래).
    ///
    /// 다시 실행하면 이름으로 찾아 위치·크기만 갱신한다(중복 생성 없음).
    /// </summary>
    public class FanRevealBoxTool : EditorWindow
    {
        const string BoxPrefix = "[SpawnRevealBox]";
        const string PrefSize  = "FanRevealBox.size";
        const string PrefDown  = "FanRevealBox.down";

        Vector3 boxSize = new Vector3(2.5f, 3f, 2.5f);
        float   downOffset = 1.0f;   // 팬 입에서 아래로 내리는 양(m). 클수록 더 낮은 곳에서 재생.

        [MenuItem("Tools/Fan 재생 박스 설정...", false, 40)]
        static void Open()
        {
            var w = GetWindow<FanRevealBoxTool>(true, "Fan 재생 박스", true);
            w.minSize = new Vector2(340f, 200f);
        }

        void OnEnable()
        {
            boxSize.x  = EditorPrefs.GetFloat(PrefSize + ".x", boxSize.x);
            boxSize.y  = EditorPrefs.GetFloat(PrefSize + ".y", boxSize.y);
            boxSize.z  = EditorPrefs.GetFloat(PrefSize + ".z", boxSize.z);
            downOffset = EditorPrefs.GetFloat(PrefDown, downOffset);
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "팬 입 위치(휴지 상태)에서 아래로 '하강량'만큼 내린 곳에 재생 박스를 둡니다.\n" +
                "몹이 이 박스를 지나는 순간 X-ray 실체화가 시작됩니다. 박스 전엔 숨겨져 있습니다.",
                MessageType.Info);

            EditorGUILayout.Space();
            boxSize    = EditorGUILayout.Vector3Field("박스 크기 (m)", boxSize);
            downOffset = EditorGUILayout.FloatField("하강량 (m, 아래로)", downOffset);

            boxSize.x = Mathf.Max(0.1f, boxSize.x);
            boxSize.y = Mathf.Max(0.1f, boxSize.y);
            boxSize.z = Mathf.Max(0.1f, boxSize.z);

            EditorGUILayout.Space();
            if (GUILayout.Button("팬마다 생성·갱신", GUILayout.Height(30f)))
                Apply();

            EditorGUILayout.Space();
            if (GUILayout.Button("모든 재생 박스 제거"))
                RemoveAll();
        }

        void Save()
        {
            EditorPrefs.SetFloat(PrefSize + ".x", boxSize.x);
            EditorPrefs.SetFloat(PrefSize + ".y", boxSize.y);
            EditorPrefs.SetFloat(PrefSize + ".z", boxSize.z);
            EditorPrefs.SetFloat(PrefDown, downOffset);
        }

        void Apply()
        {
            Save();
            var fans = Object.FindObjectsByType<FanSpawn>(FindObjectsSortMode.None);
            if (fans.Length == 0) { Debug.LogWarning("[재생박스] 씬에 FanSpawn이 없습니다."); return; }

            var existing = Object.FindObjectsByType<SpawnRevealVolume>(FindObjectsSortMode.None);
            int made = 0, updated = 0;

            foreach (var fan in fans)
            {
                string bn = BoxPrefix + " " + fan.name;

                GameObject go = null;
                foreach (var b in existing)
                    if (b != null && b.name == bn) { go = b.gameObject; break; }

                if (go != null) updated++;
                else
                {
                    go = new GameObject(bn);
                    Undo.RegisterCreatedObjectUndo(go, "Create SpawnRevealBox");
                    made++;
                }

                // 실제 스폰 지점(드롭 링크 PointA)들에 맞춰 박스 위치/크기를 잡는다 — 없으면 팬 입 폴백.
                // 몹은 팬 입이 아니라 링크 PointA에서 나와 수직 낙하하므로, 여기에 맞춰야 확실히 걸린다.
                Vector3 center = fan.Mouth + Vector3.down * downOffset;
                Vector3 sizeUsed = boxSize;
                if (fan.dropLinks != null)
                {
                    Vector3 min = Vector3.zero, max = Vector3.zero; int n = 0;
                    foreach (var l in fan.dropLinks)
                    {
                        if (l == null) continue;
                        Vector3 p = l.PointA;
                        if (n == 0) { min = p; max = p; } else { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
                        n++;
                    }
                    if (n > 0)
                    {
                        Vector3 mid = (min + max) * 0.5f;
                        sizeUsed.x = Mathf.Max(boxSize.x, (max.x - min.x) + 1.0f);   // 스폰 지점 퍼짐을 덮게
                        sizeUsed.z = Mathf.Max(boxSize.z, (max.z - min.z) + 1.0f);
                        center = new Vector3(mid.x, min.y - downOffset, mid.z);      // 최저 스폰점에서 하강량만큼 아래
                    }
                }

                var vol = go.GetComponent<SpawnRevealVolume>();
                if (vol == null) vol = Undo.AddComponent<SpawnRevealVolume>(go);

                Undo.RecordObject(vol, "Configure SpawnRevealBox");
                vol.size = sizeUsed;

                Undo.SetTransformParent(go.transform, fan.transform.parent, "Parent SpawnRevealBox");
                Undo.RecordObject(go.transform, "Place SpawnRevealBox");
                go.transform.position = center;
                go.transform.rotation = Quaternion.identity;
            }

            Debug.Log($"[재생박스] 완료 — 새로 {made}개, 갱신 {updated}개 (팬 {fans.Length}개). " +
                      $"크기={boxSize} 하강={downOffset}m.");
        }

        void RemoveAll()
        {
            var all = Object.FindObjectsByType<SpawnRevealVolume>(FindObjectsSortMode.None);
            int n = 0;
            foreach (var b in all)
            {
                if (b == null || !b.name.StartsWith(BoxPrefix)) continue;
                Undo.DestroyObjectImmediate(b.gameObject);
                n++;
            }
            Debug.Log(n > 0 ? $"[재생박스] {n}개 제거했습니다." : "[재생박스] 제거할 것이 없습니다.");
        }
    }
}
