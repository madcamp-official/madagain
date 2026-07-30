using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Game.View;

namespace Game.EditorTools
{
    /// <summary>
    /// 에디터에서 UI 컴포넌트를 매 틱 구동한다.
    ///
    /// <para><b>왜 필요한가</b> — <c>[ExecuteAlways]</c>의 에디터 틱은 씬 뷰 리페인트 등에 의존해
    /// 확실하지 않다. UI는 <b>저작이 전부 에디터에서 이뤄지는데</b> 틱이 안 돌면 메시가 비어
    /// 아무것도 안 보이고, 등장/소멸 애니메이션도 미리볼 수 없다. <c>EditorApplication.update</c>에서
    /// 직접 불러 결정적으로 만든다.</para>
    ///
    /// <para>Play 중에는 컴포넌트가 스스로 돌므로 아무것도 하지 않는다.</para>
    /// </summary>
    [InitializeOnLoad]
    static class UiEditorDriver
    {
        static readonly List<HackPanel> _panels = new List<HackPanel>();
        static readonly List<VrUiSpace> _spaces = new List<VrUiSpace>();
        static readonly List<VrUiFollow> _follows = new List<VrUiFollow>();
        static int _tick;

        static UiEditorDriver()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        static void Update()
        {
            if (Application.isPlaying) return;

            // 매 틱 씬 전수 검색은 낭비다 — 주기적으로만 다시 훑는다.
            if ((++_tick & 31) == 0 || _panels.Count + _spaces.Count + _follows.Count == 0)
            {
                _panels.Clear();
                _panels.AddRange(Object.FindObjectsByType<HackPanel>(FindObjectsSortMode.None));
                _spaces.Clear();
                _spaces.AddRange(Object.FindObjectsByType<VrUiSpace>(FindObjectsSortMode.None));
                _follows.Clear();
                _follows.AddRange(Object.FindObjectsByType<VrUiFollow>(FindObjectsSortMode.None));
            }

            // ★ 추종을 패널보다 먼저 돌린다 — 패널이 추종 루트의 위치를 읽어 꼭짓점을 만든다.
            //   이게 없으면 에디터에서 카메라를 돌려도 패널이 따라오지 않아 '선이 그대로'로 보인다.
            for (int i = 0; i < _follows.Count; i++)
                if (_follows[i] != null) _follows[i].TickFollow();

            for (int i = 0; i < _spaces.Count; i++)
                if (_spaces[i] != null) _spaces[i].Refresh();

            bool animating = false;
            for (int i = 0; i < _panels.Count; i++)
            {
                HackPanel p = _panels[i];
                if (p == null) continue;
                p.Tick();
                animating = true;
            }

            // 메시가 바뀌었으니 씬 뷰를 다시 그려야 눈에 보인다.
            if (animating) SceneView.RepaintAll();
        }
    }
}
