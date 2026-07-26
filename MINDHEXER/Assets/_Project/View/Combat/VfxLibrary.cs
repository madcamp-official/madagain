using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// VFX 프리팹 등록소. Resources/VFX/ 폴더의 프리팹을 이름으로 소환한다.
    /// ★ 코드 수정 없이 폴더에 프리팹을 넣기만 하면 콘솔(`vfx &lt;이름&gt;`)에서 바로 테스트된다.
    ///
    /// 경로 예: Assets/_Project/Prefabs/Resources/VFX/Hit_Impact.prefab → 이름 "Hit_Impact"
    /// </summary>
    public static class VfxLibrary
    {
        const string Folder = "VFX";
        const float  DefaultLifetime = 5f;   // 파티클 길이를 못 구할 때 자동 파괴까지

        static Dictionary<string, GameObject> cache;

        static void EnsureLoaded()
        {
            if (cache != null) return;
            cache = new Dictionary<string, GameObject>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var go in Resources.LoadAll<GameObject>(Folder))
                if (go != null) cache[go.name] = go;
        }

        /// <summary>폴더를 다시 읽는다(에디터에서 프리팹 추가 후).</summary>
        public static void Reload() { cache = null; EnsureLoaded(); }

        public static List<string> Names()
        {
            EnsureLoaded();
            var list = new List<string>(cache.Keys);
            list.Sort();
            return list;
        }

        /// <summary>이름으로 소환. 실패 시 null.</summary>
        public static GameObject Play(string name, Vector3 pos, Quaternion rot)
        {
            EnsureLoaded();
            if (!cache.TryGetValue(name, out var prefab) || prefab == null) return null;

            var inst = Object.Instantiate(prefab, pos, rot);
            Object.Destroy(inst, LifetimeOf(inst));
            return inst;
        }

        /// <summary>파티클 길이 기반 수명 추정(없으면 기본값).</summary>
        static float LifetimeOf(GameObject go)
        {
            float longest = 0f;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>())
            {
                var m = ps.main;
                float t = m.duration + m.startLifetime.constantMax;
                if (t > longest) longest = t;
            }
            return longest > 0.01f ? longest + 0.5f : DefaultLifetime;
        }
    }
}
