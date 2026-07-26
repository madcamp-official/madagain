using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// [2026-07-22] 우클릭(런지) 사거리에 들어온 적에게 <b>흰색 실루엣 테두리</b>를 씌운다.
    /// ★ 순수 연출·독립 뷰 — Main.Instance.World/Views만 읽고 Sim/전투는 안 건드린다.
    ///
    /// 방식: 각 몹 스킨드 메시를 복제한 렌더러에 인버티드 헐 아웃라인 머티리얼(Game/EnemyOutline)을
    ///   물려두고, 타깃일 때만 켠다. 복제 렌더러는 원본과 본을 공유하므로 똑같이 움직인다.
    ///   ※ 이 컴포넌트가 실패해도 몹 본체 렌더링에는 영향이 없다(완전 분리).
    ///
    /// 규칙:
    ///   · 런지 스택이 남아 있을 때만(우클릭 기회 없으면 표시 안 함).
    ///   · 사거리·조준 원뿔은 Sim의 IsLungeable을 수평면에서 근사(LOS 생략 — 연출이므로).
    ///   · 예측(미리보기·실행) 중에는 전부 끈다 — 예측엔 자체 표적 표시가 있어 겹치면 헷갈린다.
    /// </summary>
    public class LungeTargetHighlight : MonoBehaviour
    {
        /// <summary>아웃라인 두께(오브젝트 공간). 모델 스케일에 따라 체감이 달라 콘솔/인스펙터로 조절.</summary>
        public static float Width = 0.01f;   // 흰 테두리 두께(월드 m) — 얇게(0.03→0.01)
        static readonly Color OutlineColor = Color.white;

        static Material outlineMat;
        static bool matTried;

        // 몹 인덱스별 아웃라인 복제 렌더러 캐시(어떤 뷰로 만들었는지 함께 기록해 교체 시 재생성).
        struct Cache { public Transform builtFor; public List<GameObject> dups; }
        readonly Dictionary<int, Cache> cache = new Dictionary<int, Cache>();

        void Update()
        {
            var main = Main.Instance;
            if (main == null) return;

            // 예측 중이거나 스택이 없으면 전부 끈다.
            ref readonly SimWorld w = ref main.World;
            bool suppressed = main.PredictionActive || w.player.combat.lungeStacks <= 0;

            Material mat = OutlineMaterial();
            if (mat == null) return;   // 셰이더 없음 — 조용히 비활성(본체엔 영향 없음)
            mat.SetFloat("_OutlineWidth", Width);
            mat.SetColor("_OutlineColor", OutlineColor);

            var views = main.Views;
            if (views == null) return;
            IReadOnlyList<Transform> list = views.EnemyViews;

            // 지금 우클릭하면 실제로 맞는 <b>단 하나의</b> 대상만 윤곽선을 켠다 — Sim의 대상 선택
            // 로직(FindLungeTarget)을 그대로 써서 "보이는 대상 = 실제 맞는 대상"이 일치한다.
            int targetIndex = -1;
            if (!suppressed)
            {
                int targetId = PlayerCombat.FindLungeTarget(in w, in w.player, main.Services);
                if (targetId >= 0) targetIndex = PlayerCombat.FindEnemyIndex(in w, targetId);
            }

            for (int i = 0; i < list.Count; i++)
            {
                Transform view = list[i];
                bool on = view != null && view.gameObject.activeSelf && i == targetIndex;
                SetOutline(i, view, mat, on);
            }
        }

        void SetOutline(int index, Transform view, Material mat, bool on)
        {
            if (!cache.TryGetValue(index, out Cache c) || c.builtFor != view || c.dups == null)
            {
                c = Build(view, mat);
                cache[index] = c;
            }
            if (c.dups == null) return;
            for (int k = 0; k < c.dups.Count; k++)
                if (c.dups[k] != null && c.dups[k].activeSelf != on)
                    c.dups[k].SetActive(on);
        }

        /// <summary>뷰의 모든 스킨드 메시를 복제해 아웃라인 렌더러를 만든다(비활성 상태로).</summary>
        static Cache Build(Transform view, Material mat)
        {
            var dups = new List<GameObject>();
            if (view == null) return new Cache { builtFor = view, dups = dups };

            var skins = view.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var src in skins)
            {
                if (src == null || src.sharedMesh == null) continue;
                var go = new GameObject("LungeOutline");
                Transform t = go.transform;
                t.SetParent(src.transform.parent, false);
                t.localPosition = src.transform.localPosition;
                t.localRotation = src.transform.localRotation;
                t.localScale    = src.transform.localScale;

                var dup = go.AddComponent<SkinnedMeshRenderer>();
                dup.sharedMesh  = src.sharedMesh;
                dup.bones       = src.bones;
                dup.rootBone    = src.rootBone;
                dup.localBounds = src.localBounds;
                dup.sharedMaterial = mat;
                dup.shadowCastingMode = ShadowCastingMode.Off;
                dup.receiveShadows = false;

                go.SetActive(false);
                dups.Add(go);
            }
            return new Cache { builtFor = view, dups = dups };
        }

        static Material OutlineMaterial()
        {
            if (matTried) return outlineMat;
            matTried = true;
            Shader sh = Shader.Find("Game/EnemyOutline");
            if (sh == null)
            {
                Debug.LogWarning("[런지 윤곽] 셰이더 'Game/EnemyOutline'을 못 찾음 — 윤곽 표시를 건너뜁니다.");
                return null;
            }
            outlineMat = new Material(sh) { name = "EnemyOutline(Runtime)" };
            return outlineMat;
        }
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class LungeTargetHighlightBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<LungeTargetHighlight>() == null)
                new GameObject("[LungeTargetHighlight]").AddComponent<LungeTargetHighlight>();
        }
    }
}
