using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// 블록아웃 보조: 선택한 오브젝트 표면에 월드 공간 1m 격자 머티리얼을 입힌다.
    /// 에디터 전용 시각 보조 — 게임 로직·예측(Sim)엔 전혀 영향 없음.
    /// 머티리얼이 없으면 자동 생성해 재사용한다.
    /// </summary>
    public static class GridMaterialTool
    {
        const string MatPath = "Assets/_Project/Editor/GridMaterial.mat";

        [MenuItem("Tools/격자 머티리얼 적용 (선택)")]
        static void ApplyToSelection()
        {
            var mat = LoadOrCreate();
            if (mat == null) return;

            int count = 0;
            foreach (var go in Selection.gameObjects)
            {
                var renderers = go.GetComponentsInChildren<MeshRenderer>(true);
                foreach (var r in renderers)
                {
                    Undo.RecordObject(r, "격자 머티리얼 적용");
                    var mats = new Material[r.sharedMaterials.Length == 0 ? 1 : r.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                    r.sharedMaterials = mats;
                    EditorUtility.SetDirty(r);
                    count++;
                }
            }
            Debug.Log($"[격자] {count}개 렌더러에 1m 격자 머티리얼 적용.");
        }

        [MenuItem("Tools/격자 머티리얼 적용 (선택)", true)]
        static bool ValidateApply() => Selection.gameObjects.Length > 0;

        static Material LoadOrCreate()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (mat != null) return mat;

            var shader = Shader.Find("Game/WorldGrid");
            if (shader == null)
            {
                Debug.LogError("[격자] 'Game/WorldGrid' 셰이더를 못 찾음 — WorldGrid.shader 컴파일 확인.");
                return null;
            }
            mat = new Material(shader) { name = "GridMaterial" };
            AssetDatabase.CreateAsset(mat, MatPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[격자] 머티리얼 생성: {MatPath}");
            return mat;
        }
    }
}
