using System;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 파손 부위 하나의 정의. 어느 본이 떨어져 나가고, 그 전선이 어떻게 늘어질지.
    /// 본 이름은 <b>부분 일치</b>로 찾으므로 모델마다 접두어가 달라도(mixamorig: 등) 동작한다.
    /// </summary>
    [Serializable]
    public class DamagedPart
    {
        [Tooltip("떨어져 나갈 본 이름(부분 일치). 예: LeftArm, Head, RightForeArm")]
        public string boneName = "LeftArm";

        [Tooltip("여러 부위 중 뽑힐 가중치. 클수록 자주 선택됨")]
        public float weight = 1f;

        [Header("전선")]
        [Tooltip("전선 길이(m). 대롱거리는 정도 — 바닥에 끌리지 않는다")]
        public float length = 0.45f;
        [Tooltip("전선 마디 수. 많을수록 부드럽지만 비용 증가")]
        [Range(3, 24)] public int particles = 7;
        [Tooltip("뿌리 굵기(m)")]
        public float rootRadius = 0.016f;
        [Tooltip("끝 굵기(m) — 뿌리보다 가늘게")]
        public float tipRadius = 0.008f;

        [Header("흔들림")]
        [Tooltip("속도 감쇠. 낮을수록 빨리 잦아든다(덜 출렁임)")]
        [Range(0.80f, 0.999f)] public float damping = 0.90f;
        [Tooltip("중력(m/s²). 음수가 아래")]
        public float gravity = -9.8f;
        [Tooltip("매달린 부위의 무게. 클수록 끝이 처지고 오래 흔들린다")]
        [Range(0f, 8f)] public float tipWeight = 1.2f;
        [Tooltip("줄 뻣뻣함(제약 반복 횟수). 1=느슨해서 잘 흔들림, 4=막대처럼 굳음")]
        [Range(1, 4)] public int stiffness = 2;
        [Tooltip("[미사용] 지형 충돌을 없애서 더는 쓰이지 않는다 — 자기 몸통만 밀어낸다")]
        [Range(0f, 1f)] public float groundFriction = 0f;

        [Header("연출")]
        [Tooltip("끝에서 스파크를 튀긴다")]
        public bool sparks = true;
        [Tooltip("스파크 발생 간격(초). 불규칙하게 튀도록 ±50% 흔들린다")]
        public float sparkInterval = 0.9f;
    }

    /// <summary>
    /// 몹 한 종류의 파손 설정. 코드 수정 없이 인스펙터에서 부위를 몇 개든 추가한다.
    /// 만들기: Project 우클릭 → Create → 몹 → 파손 프로파일
    /// 두는 곳: Assets/_Project/Prefabs/Resources/MobDamage/ (이름은 아래 <see cref="targetKind"/>와 무관하게 자유)
    /// </summary>
    [CreateAssetMenu(fileName = "MobDamage_", menuName = "몹/파손 프로파일")]
    public class MobDamageProfile : ScriptableObject
    {
        public enum Kind { Melee, Ranged, Charge, Flying }

        [Tooltip("이 프로파일을 적용할 몹 종류")]
        public Kind targetKind = Kind.Melee;

        [Tooltip("이 몹이 파손된 개체로 생성될 확률")]
        [Range(0f, 1f)] public float chance = 0.35f;

        [Tooltip("한 개체에 동시에 적용할 최대 부위 수")]
        [Range(1, 6)] public int maxParts = 2;

        [Tooltip("파손 가능 부위 — 몇 개든 추가 가능")]
        public DamagedPart[] parts = new DamagedPart[0];

        /// <summary>가중치로 부위를 중복 없이 최대 n개 뽑는다. rng는 호출자가 소유(결정성 통제).</summary>
        public void Pick(System.Random rng, int n, System.Collections.Generic.List<DamagedPart> outParts)
        {
            outParts.Clear();
            if (parts == null || parts.Length == 0) return;

            var pool = new System.Collections.Generic.List<DamagedPart>(parts);
            n = Mathf.Min(n, Mathf.Min(maxParts, pool.Count));
            for (int k = 0; k < n; k++)
            {
                float total = 0f;
                foreach (var p in pool) total += Mathf.Max(0f, p.weight);
                if (total <= 0f) break;

                float r = (float)rng.NextDouble() * total, acc = 0f;
                int hit = pool.Count - 1;
                for (int i = 0; i < pool.Count; i++)
                {
                    acc += Mathf.Max(0f, pool[i].weight);
                    if (r <= acc) { hit = i; break; }
                }
                outParts.Add(pool[hit]);
                pool.RemoveAt(hit);      // 같은 부위가 두 번 뽑히지 않게
            }
        }
    }
}
