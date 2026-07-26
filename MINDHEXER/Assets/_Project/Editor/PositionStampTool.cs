using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// 씬 뷰 카메라가 있는 위치를 찍어 콘솔·클립보드로 내보낸다(텔레포트 좌표 잡기용).
    /// 씬 뷰를 원하는 지점으로 이동시킨 뒤(WASD 비행/포커스) 메뉴 실행 → 그 카메라 좌표를 복사.
    /// </summary>
    static class PositionStampTool
    {
        // 단축키: Ctrl(%) + Shift(#) + P
        [MenuItem("Tools/위치 찍기/씬 카메라 위치 (클립보드 복사) %#p")]
        static void StampSceneCamera()
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv == null || sv.camera == null)
            { Debug.LogWarning("[위치찍기] 활성 씬 뷰가 없습니다. Scene 창을 한 번 클릭하세요."); return; }

            Vector3 cam   = sv.camera.transform.position;   // 카메라(눈) 위치
            Vector3 pivot = sv.pivot;                        // 카메라가 보는 중심점

            string s = $"new Vector3({cam.x:0.##}f, {cam.y:0.##}f, {cam.z:0.##}f)";
            EditorGUIUtility.systemCopyBuffer = s;

            // 이 위치가 어느 아레나 존 안인지
            string arena = "(존 밖/불명)";
            foreach (var r in UnityEngine.Object.FindObjectsByType<Game.View.ArenaRoom>(FindObjectsSortMode.None))
                if (r.zone != null && (r.zone.ClosestPoint(cam) - cam).sqrMagnitude < 1e-4f)
                { arena = r.transform.root.name; break; }

            Debug.Log($"[위치찍기] 씬 카메라 위치 = {cam:F2}\n" +
                      $"  → 클립보드 복사됨: {s}\n" +
                      $"  (카메라가 보는 중심점 pivot = {pivot:F2})\n" +
                      $"  소속 아레나: {arena}");
        }
    }
}
