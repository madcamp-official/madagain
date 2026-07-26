using System.IO;
using UnityEngine;
using UnityEditor;

namespace Game.EditorTools
{
    /// <summary>
    /// 씬에서 맞춘 뷰모델(그립·IK·손가락·칼 배치)을 Resources 프리팹에 반영한다.
    ///
    /// <b>왜 필요한가</b> — SwordView는 씬에 KatanaViewmodel이 없으면
    /// Resources/KatanaViewmodel 프리팹을 인스턴스화한다.
    /// 그런데 작업 씬(slash_anim)의 뷰모델은 프리팹에서 <b>언팩된 독립 오브젝트</b>라
    /// 아무리 튜닝해도 프리팹에 전달되지 않는다. 그래서 그 씬에서만 제대로 보였다.
    ///
    /// 이 툴로 한 번 저장하면 <b>모든 씬에서 동일하게</b> 나온다.
    /// </summary>
    public static class ViewmodelPrefabTool
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/Resources/KatanaViewmodel.prefab";

        [MenuItem("Tools/뷰모델/③ 현재 씬 뷰모델을 프리팹에 저장 (모든 씬에 적용)", false, 3)]
        public static void SaveToPrefab()
        {
            var root = FindViewmodel();
            if (root == null)
            {
                EditorUtility.DisplayDialog("실패",
                    "씬에서 KatanaViewmodel을 찾지 못했습니다.\n" +
                    "작업 씬(slash_anim 등)을 열고 다시 실행하십시오.", "확인");
                return;
            }

            if (!EditorUtility.DisplayDialog("프리팹 덮어쓰기",
                    $"'{root.name}' 의 현재 상태를\n{PrefabPath}\n에 덮어씁니다.\n\n" +
                    "그립·IK·손가락·칼 배치가 모든 씬에 적용됩니다.\n계속할까요?", "저장", "취소"))
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));

            // 씬 오브젝트는 그대로 두고 에셋만 덮어쓴다(연결하지 않음).
            // 연결하면 이후 씬 편집이 "오버라이드"가 되어 매번 Apply를 눌러야 한다.
            var saved = PrefabUtility.SaveAsPrefabAsset(root.gameObject, PrefabPath, out bool ok);
            if (!ok || saved == null)
            {
                EditorUtility.DisplayDialog("실패", "프리팹 저장에 실패했습니다. 콘솔을 확인하십시오.", "확인");
                return;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            int bones = root.GetComponentsInChildren<Transform>(true).Length;
            Debug.Log($"[뷰모델 프리팹] 저장 완료 — {PrefabPath}  (오브젝트 {bones}개)\n" +
                      "  이제 다른 씬에서도 Play 시 이 상태로 생성됩니다. (콘솔 vm 으로 즉시 소환 가능)");
            EditorUtility.DisplayDialog("저장 완료",
                $"오브젝트 {bones}개를 프리팹에 반영했습니다.\n\n" +
                "다른 씬에서 Play하면 이 상태로 나옵니다.\n" +
                "게임 중에는 콘솔 `vm` 으로 즉시 소환·재생성할 수 있습니다.", "확인");

            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        }

        [MenuItem("Tools/뷰모델/④ 프리팹 상태 확인", false, 4)]
        public static void Inspect()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var scene = FindViewmodel();
            string msg =
                $"프리팹: {(asset != null ? $"있음 — 오브젝트 {asset.GetComponentsInChildren<Transform>(true).Length}개" : "없음")}\n" +
                $"씬:     {(scene != null ? $"있음 — 오브젝트 {scene.GetComponentsInChildren<Transform>(true).Length}개" : "없음")}\n\n" +
                (scene != null
                    ? (PrefabUtility.IsPartOfPrefabInstance(scene.gameObject)
                        ? "씬 오브젝트가 프리팹 인스턴스입니다 (Apply 로도 반영 가능)"
                        : "씬 오브젝트가 <b>언팩된 독립 오브젝트</b>입니다.\n→ ③ 툴로 저장해야 프리팹에 반영됩니다.")
                    : "씬에 뷰모델이 없습니다.");
            Debug.Log("[뷰모델 프리팹] " + msg.Replace("<b>", "").Replace("</b>", ""));
            EditorUtility.DisplayDialog("뷰모델 프리팹 상태", msg.Replace("<b>", "").Replace("</b>", ""), "확인");
        }

        static Transform FindViewmodel()
        {
            var cam = Camera.main;
            if (cam != null)
            {
                var t = cam.transform.Find("KatanaViewmodel");
                if (t != null) return t;
            }
            var go = GameObject.Find("KatanaViewmodel");
            return go != null ? go.transform : null;
        }
    }
}
