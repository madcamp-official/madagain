using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// 뷰모델 카메라 분리 설치 — 팔·칼이 근평면에 잘려 뚫려 보이는 문제와
    /// 벽 관통 문제를 함께 해결한다.
    ///
    /// 하는 일
    ///   ① 'Viewmodel' 레이어를 없으면 만든다 (ProjectSettings/TagManager 직접 편집)
    ///   ② 씬의 KatanaViewmodel 전체를 그 레이어로 옮긴다
    ///   ③ 씬에 [ViewmodelCamera]를 놓는다 (런타임에 오버레이 카메라를 구성)
    /// </summary>
    public static class ViewmodelCameraTool
    {
        [MenuItem("Tools/뷰모델/① 뷰모델 카메라 설치 (팔 뚫림·벽 관통 해결)", false, 1)]
        public static void Install()
        {
            int layer = EnsureLayer(ViewmodelCamera.DefaultLayer);
            if (layer < 0)
            {
                EditorUtility.DisplayDialog("실패",
                    "빈 레이어 슬롯이 없습니다.\nProject Settings ▸ Tags and Layers 에서 하나를 비우고 다시 실행하십시오.", "확인");
                return;
            }

            int moved = 0;
            var root = FindViewmodel();
            if (root != null)
            {
                Undo.RegisterFullObjectHierarchyUndo(root.gameObject, "뷰모델 레이어");
                moved = SetLayerRecursive(root, layer);
                EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            }

            var vc = Object.FindFirstObjectByType<ViewmodelCamera>();
            bool isNew = vc == null;
            if (isNew)
            {
                var go = new GameObject("[ViewmodelCamera]");
                vc = go.AddComponent<ViewmodelCamera>();
                Undo.RegisterCreatedObjectUndo(go, "뷰모델 카메라 설치");
                EditorSceneManager.MarkSceneDirty(go.scene);
            }
            else Undo.RecordObject(vc, "뷰모델 카메라 기본값");

            vc.layerName = ViewmodelCamera.DefaultLayer;
            // 이미 씬에 있던 컴포넌트는 직렬화된 옛 값을 들고 있다 —
            // 코드 기본값이 자동 반영되지 않으므로 여기서 명시적으로 맞춘다.
            vc.ResetToDefaults();
            EditorUtility.SetDirty(vc);
            EditorSceneManager.MarkSceneDirty(vc.gameObject.scene);

            EditorUtility.DisplayDialog("설치 완료",
                $"레이어 '{ViewmodelCamera.DefaultLayer}' (index {layer}) 준비됨\n" +
                (root != null ? $"뷰모델 오브젝트 {moved}개를 그 레이어로 옮겼습니다.\n" : "※ 씬에 KatanaViewmodel이 없어 레이어 이동은 건너뛰었습니다.\n   (Play 중 자동으로 처리됩니다)\n") +
                "\nPlay를 누르면 뷰모델 전용 오버레이 카메라가 구성됩니다.\n" +
                "근평면·후퇴량은 게임 중 F2에서 조절하십시오.", "확인");

            Debug.Log($"[뷰모델 카메라] 레이어 {layer} · 오브젝트 {moved}개 이동 · [ViewmodelCamera] 배치 완료");
        }

        [MenuItem("Tools/뷰모델/② 뷰모델 카메라 제거 (원상복구)", false, 2)]
        public static void Uninstall()
        {
            var root = FindViewmodel();
            if (root != null)
            {
                Undo.RegisterFullObjectHierarchyUndo(root.gameObject, "뷰모델 레이어 복구");
                SetLayerRecursive(root, 0);       // Default
                EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            }
            var vc = Object.FindFirstObjectByType<ViewmodelCamera>();
            if (vc != null) Undo.DestroyObjectImmediate(vc.gameObject);

            var cam = Camera.main;
            if (cam != null)
            {
                var t = cam.transform.Find(ViewmodelCamera.CamObjectName);
                if (t != null) Undo.DestroyObjectImmediate(t.gameObject);
                cam.cullingMask = ~0;
            }
            Debug.Log("[뷰모델 카메라] 제거 — 레이어를 Default로 되돌리고 오버레이 카메라를 삭제했습니다.");
        }

        // ── 레이어 생성 ──
        /// <summary>이름의 레이어를 찾고, 없으면 빈 슬롯(8~31)에 만든다. 실패 시 -1.</summary>
        public static int EnsureLayer(string name)
        {
            int existing = LayerMask.NameToLayer(name);
            if (existing >= 0) return existing;

            var asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (asset == null || asset.Length == 0) return -1;

            var so = new SerializedObject(asset[0]);
            var layers = so.FindProperty("layers");
            if (layers == null) return -1;

            // 0~7은 유니티 예약. 8부터 빈 칸을 찾는다.
            for (int i = 8; i < layers.arraySize; i++)
            {
                var el = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(el.stringValue))
                {
                    el.stringValue = name;
                    so.ApplyModifiedProperties();
                    AssetDatabase.SaveAssets();
                    Debug.Log($"[뷰모델 카메라] 레이어 '{name}' 를 index {i} 에 생성했습니다.");
                    return i;
                }
            }
            return -1;
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

        static int SetLayerRecursive(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            int n = 1;
            for (int i = 0; i < root.childCount; i++) n += SetLayerRecursive(root.GetChild(i), layer);
            return n;
        }
    }
}
