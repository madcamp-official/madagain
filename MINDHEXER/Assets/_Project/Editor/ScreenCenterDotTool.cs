using UnityEngine;
using UnityEditor;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// 화면 중앙 점 표시 on/off. Game 뷰(컴포넌트)와 Scene 뷰(오버레이) 양쪽을 다룬다.
    /// 1인칭 구도를 잡을 때 "화면 정중앙이 어디인가"를 눈으로 확인하는 용도.
    /// </summary>
    [InitializeOnLoad]
    public static class ScreenCenterDotTool
    {
        const string GameMenu  = "Tools/뷰모델/화면 중앙 점 (Game 뷰)";
        const string SceneMenu = "Tools/뷰모델/화면 중앙 점 (Scene 뷰)";
        const string SceneKey  = "Precog_SceneCenterDot";
        const string HolderName = "[ScreenCenterDot]";

        static bool SceneOn
        {
            get => EditorPrefs.GetBool(SceneKey, false);
            set => EditorPrefs.SetBool(SceneKey, value);
        }

        static ScreenCenterDotTool()
        {
            SceneView.duringSceneGui -= OnSceneGui;
            SceneView.duringSceneGui += OnSceneGui;
        }

        // ── Game 뷰: 컴포넌트 토글 ──
        [MenuItem(GameMenu)]
        static void ToggleGame()
        {
            var found = Object.FindFirstObjectByType<ScreenCenterDot>();
            if (found != null)
            {
                Undo.DestroyObjectImmediate(found.gameObject);
                Debug.Log("[중앙 점] Game 뷰 표시 끔");
            }
            else
            {
                var go = new GameObject(HolderName);
                Undo.RegisterCreatedObjectUndo(go, "중앙 점 켜기");
                go.AddComponent<ScreenCenterDot>();
                Selection.activeGameObject = go;
                Debug.Log("[중앙 점] Game 뷰 표시 켬 — 크기·색·십자선·3분할선은 Inspector에서 조절");
            }
        }

        [MenuItem(GameMenu, true)]
        static bool ToggleGameValidate()
        {
            Menu.SetChecked(GameMenu, Object.FindFirstObjectByType<ScreenCenterDot>() != null);
            return true;
        }

        // ── Scene 뷰: 오버레이 토글 ──
        [MenuItem(SceneMenu)]
        static void ToggleScene()
        {
            SceneOn = !SceneOn;
            SceneView.RepaintAll();
            Debug.Log(SceneOn ? "[중앙 점] Scene 뷰 표시 켬" : "[중앙 점] Scene 뷰 표시 끔");
        }

        [MenuItem(SceneMenu, true)]
        static bool ToggleSceneValidate()
        {
            Menu.SetChecked(SceneMenu, SceneOn);
            return true;
        }

        static void OnSceneGui(SceneView sv)
        {
            if (!SceneOn) return;
            Handles.BeginGUI();
            var r = sv.position;
            float cx = r.width * 0.5f, cy = r.height * 0.5f;

            Color prev = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.45f);
            EditorGUI.DrawRect(new Rect(cx - 14f, cy - 0.5f, 28f, 1f), new Color(1f, 1f, 1f, 0.45f));
            EditorGUI.DrawRect(new Rect(cx - 0.5f, cy - 14f, 1f, 28f), new Color(1f, 1f, 1f, 0.45f));
            EditorGUI.DrawRect(new Rect(cx - 2f, cy - 2f, 4f, 4f), new Color(1f, 0.2f, 0.2f, 0.95f));
            GUI.color = prev;
            Handles.EndGUI();
        }
    }
}
