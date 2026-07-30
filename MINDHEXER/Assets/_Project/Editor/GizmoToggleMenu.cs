using UnityEditor;
using UnityEngine;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// 씬 뷰를 덮는 진단용 기즈모를 <b>메뉴에서 끈다</b>. (Tools/기즈모)
    ///
    /// <para><b>왜 필요한가</b> — 레벨에 <see cref="ClimbLedge"/>가 수백 개라 "설 바닥 없음" 라벨과
    /// 선이 화면을 통째로 덮는다. 배치·조명 작업이 사실상 불가능해진다. 유니티 기본 Gizmos 창에서도
    /// 끌 수 있지만 스크립트가 많아 찾기 번거롭고, 자주 껐다 켜는 것이라 단축 경로를 둔다.</para>
    ///
    /// <para><b>표시만 사라진다.</b> 판정·동작에는 아무 영향이 없다.
    /// 설정은 <see cref="EditorPrefs"/>에 남아 에디터를 껐다 켜도 유지된다.</para>
    /// </summary>
    static class GizmoToggleMenu
    {
        const string ClimbPath = "Tools/기즈모/등반 표시 (설 바닥 없음)";

        [MenuItem(ClimbPath, false, 1)]
        static void ToggleClimb() => ClimbLedge.ShowGizmos = !ClimbLedge.ShowGizmos;

        [MenuItem(ClimbPath, true)]
        static bool ToggleClimbValidate()
        {
            Menu.SetChecked(ClimbPath, ClimbLedge.ShowGizmos);   // 체크 표시 = 켜짐
            return true;
        }

        [MenuItem("Tools/기즈모/전부 끄기", false, 20)]
        static void AllOff()
        {
            ClimbLedge.ShowGizmos = false;
            Debug.Log("[기즈모] 진단 표시를 전부 껐습니다. Tools/기즈모 에서 다시 켤 수 있습니다.");
        }

        [MenuItem("Tools/기즈모/전부 켜기", false, 21)]
        static void AllOn()
        {
            ClimbLedge.ShowGizmos = true;
            Debug.Log("[기즈모] 진단 표시를 전부 켰습니다.");
        }
    }
}
