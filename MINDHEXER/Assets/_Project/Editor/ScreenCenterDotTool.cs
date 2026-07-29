using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// 화면 중앙 점 표시를 켜고 끈다. 메뉴에 체크 표시가 뜬다.
    /// 1인칭 구도를 잡을 때 기준선이 필요하므로 씬 저장 없이 즉시 토글되게 둔다.
    /// </summary>
    public static class ScreenCenterDotTool
    {
        // ★ 상수 이름을 Menu로 두면 UnityEditor.Menu 클래스를 가려 Menu.SetChecked가 안 잡힌다.
        const string MenuPath = "Tools/뷰모델/화면 중앙 점 표시";
        const string ObjName = "[ScreenCenterDot]";

        static ScreenCenterDot Find() => Object.FindFirstObjectByType<ScreenCenterDot>();

        [MenuItem(MenuPath, false, 30)]
        static void Toggle()
        {
            var dot = Find();
            if (dot == null)
            {
                var go = new GameObject(ObjName);
                go.AddComponent<ScreenCenterDot>();
                Undo.RegisterCreatedObjectUndo(go, "중앙 점 표시");
                EditorSceneManager.MarkSceneDirty(go.scene);
                Debug.Log("[중앙 점] 표시 켬");
            }
            else
            {
                Undo.DestroyObjectImmediate(dot.gameObject);
                Debug.Log("[중앙 점] 표시 끔");
            }
            SceneView.RepaintAll();
        }

        [MenuItem(MenuPath, true)]
        static bool Validate()
        {
            UnityEditor.Menu.SetChecked(MenuPath, Find() != null);
            return true;
        }
    }
}
