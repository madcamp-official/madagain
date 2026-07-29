using UnityEngine;
using UnityEditor;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// 손 IK를 좌·우 따로 켜고 끈다. 메뉴에 체크 표시가 뜬다. (Precog에서 포팅, 변경 없음 — 이미 일반적)
    ///
    /// 쓰임새:
    ///   · 기본 자세를 잡을 땐 IK를 꺼야 한다(켜져 있으면 매 프레임 덮어써서 조정이 안 먹는다)
    ///   · 오른손 IK = 포즈 잡는 보조. 애니메이션 찍을 땐 끔
    ///   · 왼손 IK = 대상을 계속 따라가야 하는 손 — 보통 켜둠
    /// </summary>
    public static class HandIKToggleTool
    {
        const string MenuR   = "Tools/뷰모델/IK 오른손 켜기·끄기 %#r";
        const string MenuL   = "Tools/뷰모델/IK 왼손 켜기·끄기 %#l";
        const string MenuOff = "Tools/뷰모델/IK 양손 끄기 %#o";

        static bool IsLeft(HandIK ik)
        {
            if (ik.end != null && ik.end.name.IndexOf("Left", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (ik.end != null && ik.end.name.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return ik.gameObject.name.EndsWith("_L");
        }

        static HandIK Find(bool left)
        {
            foreach (var ik in Object.FindObjectsByType<HandIK>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (IsLeft(ik) == left) return ik;
            return null;
        }

        static void Toggle(bool left, string label)
        {
            var ik = Find(left);
            if (ik == null) { Debug.LogWarning($"[IK] {label} HandIK를 못 찾았습니다."); return; }
            Undo.RecordObject(ik, "IK 토글");
            ik.weight = ik.weight > 0.001f ? 0f : 1f;
            EditorUtility.SetDirty(ik);
            SceneView.RepaintAll();
            Debug.Log($"[IK] {label} = {(ik.weight > 0f ? "켜짐" : "꺼짐")}" +
                      (ik.weight > 0f && ik.IsOutOfReach() ? "   ★목표가 사정거리 밖" : ""));
        }

        [MenuItem(MenuR)]           static void ToggleR() => Toggle(false, "오른손");
        [MenuItem(MenuR, true)]     static bool ValidR()
        {
            var ik = Find(false);
            Menu.SetChecked(MenuR, ik != null && ik.weight > 0.001f);
            return ik != null;
        }

        [MenuItem(MenuL)]           static void ToggleL() => Toggle(true, "왼손");
        [MenuItem(MenuL, true)]     static bool ValidL()
        {
            var ik = Find(true);
            Menu.SetChecked(MenuL, ik != null && ik.weight > 0.001f);
            return ik != null;
        }

        [MenuItem(MenuOff)]
        static void AllOff()
        {
            int n = 0;
            foreach (var ik in Object.FindObjectsByType<HandIK>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Undo.RecordObject(ik, "IK 끄기");
                ik.weight = 0f;
                EditorUtility.SetDirty(ik);
                n++;
            }
            SceneView.RepaintAll();
            Debug.Log($"[IK] 전부 끔 ({n}개). 이제 팔을 손으로 조정해도 안 덮어써집니다.");
        }
    }
}
