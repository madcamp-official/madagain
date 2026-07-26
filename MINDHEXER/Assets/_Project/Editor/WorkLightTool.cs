using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// 맵 작업용 임시 조명. 어두운 맵에서 형태가 안 보일 때 켠다.
    ///
    /// ★ 씬 데이터를 건드리지 않는다.
    ///   생성 오브젝트에 <b>HideFlags.DontSave</b>만 준다(HideInHierarchy·NotEditable은 주지 않는다)
    ///   → 씬에 직렬화되지 않아 실수로 저장·커밋되지 않으면서도,
    ///     Hierarchy에 그대로 보이고 <b>언제든 선택해서 Delete로 지울 수 있다.</b>
    ///   → Play를 누르면 씬이 다시 로드되며 저절로 사라진다(게임은 실제 조명으로 돈다).
    ///
    /// RenderSettings(환경광)를 올리는 방식은 쓰지 않았다 — 그건 씬에 저장되는 실제 렌더 설정이라
    /// 실수로 커밋되면 게임 조명이 밝아진 채로 남는다.
    /// </summary>
    public static class WorkLightTool
    {
        const string RootName = "[작업용 조명]";

        [MenuItem("Tools/작업용 조명 켜기·끄기", false, 1)]
        static void Toggle()
        {
            int removed = RemoveAll();
            if (removed > 0)
            {
                Debug.Log($"[작업용 조명] 껐습니다. ({removed}개 제거)");
                SceneView.RepaintAll();
                return;
            }

            var root = new GameObject(RootName) { hideFlags = HideFlags.DontSave };
            // 세 방향에서 비춰 그늘지는 면이 없게. 그림자는 끈다(형태 파악이 목적).
            AddLight(root, new Vector3(50f, -30f, 0f), 1.1f);   // 주광
            AddLight(root, new Vector3(30f, 150f, 0f), 0.45f);  // 반대편 채움
            AddLight(root, new Vector3(-35f, 60f, 0f), 0.35f);  // 아래쪽 채움

            Debug.Log("[작업용 조명] 켰습니다.\n" +
                      "  · 씬에 저장되지 않습니다(DontSave). Play하면 사라집니다.\n" +
                      "  · 끄는 법: 같은 메뉴 / Tools > 작업용 조명 전부 제거 / Hierarchy에서 선택 후 Delete\n" +
                      "  · Scene 뷰 툴바의 전구 아이콘을 끄면 더 밝게 볼 수 있습니다.");
            SceneView.RepaintAll();
        }

        [MenuItem("Tools/작업용 조명 전부 제거", false, 2)]
        static void RemoveAllMenu()
        {
            int n = RemoveAll();
            Debug.Log(n > 0 ? $"[작업용 조명] {n}개 제거했습니다." : "[작업용 조명] 제거할 것이 없습니다.");
            SceneView.RepaintAll();
        }

        /// <summary>
        /// 이름이 같은 작업용 조명 루트를 전부 찾아 지운다(비활성 포함). 중복 생성돼도 확실히 정리된다.
        ///
        /// ★ <b>FindObjectsByType을 쓰면 안 된다.</b> 그쪽은 <see cref="HideFlags.DontSave"/>가 붙은
        ///   오브젝트를 반환하지 않는데, 이 툴이 만드는 것이 정확히 그 플래그다 → 자기가 만든 것을
        ///   영원히 못 찾아 "제거할 것이 없습니다"만 찍히고 토글할 때마다 조명이 하나씩 더 쌓였다.
        ///   Resources.FindObjectsOfTypeAll은 숨김·DontSave까지 포함해서 돌려준다.
        /// </summary>
        static int RemoveAll()
        {
            int n = 0;
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null) continue;                      // 부모가 먼저 지워진 경우
                if (go.name != RootName) continue;
                if (EditorUtility.IsPersistent(go)) continue;   // 프리팹 에셋은 건드리지 않는다
                Object.DestroyImmediate(go);
                n++;
            }
            return n;
        }

        static void AddLight(GameObject parent, Vector3 euler, float intensity)
        {
            var go = new GameObject("WorkLight") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(parent.transform, false);
            go.transform.rotation = Quaternion.Euler(euler);

            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            l.color = Color.white;
        }
    }
}
