using UnityEngine;

namespace Game.View
{
    /// <summary>씬 저작용 참고 표시 — 에디터에서는 보이고, Play 시작하면 렌더러를 끈다.</summary>
    public class EditorOnlyVisual : MonoBehaviour
    {
        void Awake()
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        }
    }
}
