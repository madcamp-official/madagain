using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 1단계 전선 테스트 — 모델 파손 방식을 정하기 전에 흔들림·지형 충돌·굵기를 먼저 확인한다.
    /// 살아있는 몹의 지정 본에 전선을 강제로 매달아 본다(부위 절단은 하지 않는다).
    /// 콘솔: wire / wire off / wire &lt;본이름&gt; / wire len 3 …
    /// </summary>
    public class WireTester : MonoBehaviour
    {
        public static WireTester Instance { get; private set; }

        public readonly DamagedPart cfg = new DamagedPart();
        public string boneName = "LeftArm";

        readonly List<DanglingWire> live = new List<DanglingWire>();
        bool on;

        void Awake() { Instance = this; }

        public bool IsOn => on;
        public int Count => live.Count;

        /// <summary>살아있는 몹 전부의 해당 본에 전선을 매단다.</summary>
        public int Attach()
        {
            Clear();
            on = true;
            int made = 0;

            foreach (var go in GameObject.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (!go.name.StartsWith("Enemy_") || !go.gameObject.activeInHierarchy) continue;

                Transform bone = FindBone(go, boneName);
                if (bone == null) continue;

                var wgo = new GameObject("DanglingWire");
                wgo.transform.SetParent(go, false);
                var w = wgo.AddComponent<DanglingWire>();
                w.Init(bone, null, cfg, WireMaterials.Wire);
                live.Add(w);
                made++;
            }
            return made;
        }

        public void Clear()
        {
            foreach (var w in live) if (w != null) Destroy(w.gameObject);
            live.Clear();
            on = false;
        }

        /// <summary>설정을 바꾼 뒤 다시 매단다(길이·마디 수는 재생성이 필요하다).</summary>
        public int Rebuild() => on ? Attach() : 0;

        static Transform FindBone(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.IndexOf(name, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return t;
            return null;
        }

        /// <summary>이 몹 계층에서 쓸 수 있는 본 이름 목록(콘솔 안내용).</summary>
        public static List<string> ListBones(int max = 40)
        {
            var outp = new List<string>();
            foreach (var go in GameObject.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (!go.name.StartsWith("Enemy_") || !go.gameObject.activeInHierarchy) continue;
                foreach (var t in go.GetComponentsInChildren<Transform>(true))
                    if (outp.Count < max && !outp.Contains(t.name)) outp.Add(t.name);
                break;   // 한 마리만
            }
            return outp;
        }
    }

    public static class WireTesterBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<WireTester>() == null)
                new GameObject("[WireTester]").AddComponent<WireTester>();
        }
    }
}
