using UnityEditor;
using UnityEngine;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// 시점 리그의 <c>[ExecuteAlways]</c> 컴포넌트를 에디터에서 매 틱 구동한다.
    /// (<see cref="ViewmodelCamera"/>, <see cref="PlayerLightRig"/>)
    ///
    /// <para><b>왜 필요한가</b> — 뷰모델 오버레이나 조명이 Play 중에만 설치되면 <b>자세와 대비를 잡는
    /// 내내</b> 팔이 근평면에 잘리고 벽에 파묻히며, 조명도 실제와 다르게 보인다. 정작 그 작업이
    /// 에디터에서만 이뤄지는데도 그렇다. <c>[ExecuteAlways]</c>를 붙여도 에디터 틱은 씬 뷰 리페인트
    /// 등에 의존해 확실하지 않아(실제로 안 돌았다), <c>EditorApplication.update</c>에서 직접 불러
    /// 결정적으로 만든다.</para>
    ///
    /// <para>생성되는 오브젝트는 전부 <c>HideFlags.DontSave</c>라 씬에 저장되지 않는다.</para>
    /// </summary>
    [InitializeOnLoad]
    static class ViewRigEditorDriver
    {
        static ViewmodelCamera _cam;
        static PlayerLightRig _light;
        static int _tick;

        static ViewRigEditorDriver()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (Application.isPlaying) return;   // Play 중엔 컴포넌트가 스스로 돈다

            bool search = false;
            if (_cam == null || _light == null)
            {
                // 매 틱 씬 전수 검색은 낭비다 — 놓친 게 있을 때만 가끔 찾는다.
                if ((++_tick & 31) != 0) return;
                search = true;
            }

            if (search)
            {
                if (_cam == null)   _cam   = Object.FindFirstObjectByType<ViewmodelCamera>();
                if (_light == null) _light = Object.FindFirstObjectByType<PlayerLightRig>();
            }

            if (_cam != null)   _cam.EnsureInstalled();
            if (_light != null) _light.EnsureInstalled();
        }
    }
}
