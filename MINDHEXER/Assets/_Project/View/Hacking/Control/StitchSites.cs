using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 해킹 실이 대상을 <b>바느질하듯 들쑤실 자리</b>를 담아 둔다.
    ///
    /// <para><b>왜 미리 굽나</b> — 런타임 레이캐스트는 <b>콜라이더</b>에 맞는다. 터렛처럼
    /// 박스 콜라이더 하나로 감싼 대상은 실제 메시보다 상자가 훨씬 커서, 실이 허공을 찌르는
    /// 모양이 된다. 그래서 <b>에디터에서 진짜 메시에 대고</b> 후보를 뽑아 여기 저장하고,
    /// 런타임에는 그중 몇 개를 골라 쓰기만 한다. 레이캐스트가 0발이 된다.</para>
    ///
    /// <para><b>무작위성은 어디서 오나</b> — 후보를 40개쯤 굽고 그중 5개를 뽑는다.
    /// 조합이 수십만 가지라 <b>매번 다른 자리</b>를 찌른다. 손으로 심으면 늘 같은 자리다.</para>
    ///
    /// <para>굽는 도구: <c>Tools/해킹/땀 자리 굽기</c> (StitchSiteBaker)</para>
    /// </summary>
    public class StitchSites : MonoBehaviour
    {
        /// <summary>구운 후보 하나. 좌표는 <see cref="spaceIndex"/>가 가리키는 공간 기준이다.</summary>
        [System.Serializable]
        public struct Site
        {
            public Vector3 localPos;
            public Vector3 localNormal;

            /// <summary>이 지점에서 반대편 표면까지의 거리(m). 실이 뒤로 뚫고 나오지 않게 깊이를 제한한다.</summary>
            public float thickness;

            /// <summary><see cref="spaces"/> 인덱스. <b>−1이면</b> 이 컴포넌트의 트랜스폼 기준.</summary>
            public int spaceIndex;
        }

        [Header("구운 결과 (StitchSiteBaker가 채운다)")]
        public List<Site> sites = new List<Site>();

        /// <summary>
        /// 땀이 매달릴 트랜스폼 — <b>본만이 아니라 움직이는 파츠도 포함</b>한다.
        ///
        /// <para>★ 이게 루트 하나였을 때 문제가 났다. 피스톤 로드·프레스 헤드·터렛 헤드는
        /// <b>자식 트랜스폼</b>이 움직이는데, 땀을 <see cref="Hackable"/> 루트 기준으로 저장하면
        /// 부품이 움직여도 실이 제자리에 남아 크게 어색하다. 경비병이 멀쩡했던 이유는
        /// 스킨드라 본에 매달렸기 때문이고, 정적 파츠에도 같은 원리를 적용한 것이다.</para>
        /// </summary>
        [Tooltip("땀이 매달릴 트랜스폼(본 또는 움직이는 파츠). 베이커가 채운다.")]
        public Transform[] spaces;

        [Tooltip("구울 때 쓴 후보 개수. 다시 구울 때 참고만 한다.")]
        public int bakedCount;

        public bool IsBaked => sites != null && sites.Count > 0;

        // ── 공간 변환 ────────────────────────────────────────────────────────
        // 정적이면 자기 트랜스폼, 스킨드면 해당 본. 본을 쓰면 경비병이 걸어도 땀이 따라간다.

        Transform SpaceOf(int spaceIndex)
        {
            if (spaceIndex < 0 || spaces == null || spaceIndex >= spaces.Length) return transform;
            var b = spaces[spaceIndex];
            return b != null ? b : transform;
        }

        public Vector3 WorldPos(in Site s) => SpaceOf(s.spaceIndex).TransformPoint(s.localPos);

        public Vector3 WorldNormal(in Site s)
        {
            Vector3 n = SpaceOf(s.spaceIndex).TransformDirection(s.localNormal);
            return n.sqrMagnitude < 1e-8f ? Vector3.up : n.normalized;
        }

        /// <summary>
        /// 후보 중에서 <paramref name="count"/>개를 고른다. 해킹 <b>시작 시 한 번만</b> 부른다 —
        /// 매 프레임 다시 고르면 실이 발작한다.
        ///
        /// <para>고르는 규칙 셋: ① 보는 쪽에 다수를 배정하되 <b>한둘은 반대편</b>에 둬서
        /// 실이 대상 뒤로 사라졌다 나오게 한다(관통이 읽힌다). ② 서로 최소 각거리를 둬서
        /// 한곳에 뭉치지 않게 한다. ③ 시드를 받아 같은 해킹 안에서는 결과가 고정된다.</para>
        /// </summary>
        public void Pick(int count, Vector3 viewerPos, int seed, List<int> result)
        {
            result.Clear();
            if (!IsBaked) return;

            int n = sites.Count;
            count = Mathf.Min(count, n);

            Vector3 center = transform.position;
            var rng = new System.Random(seed);

            // 앞/뒤 구분 — 법선이 보는 쪽을 향하면 앞이다.
            var front = new List<int>();
            var back = new List<int>();
            for (int i = 0; i < n; i++)
            {
                Vector3 toViewer = viewerPos - WorldPos(sites[i]);
                (Vector3.Dot(WorldNormal(sites[i]), toViewer) > 0f ? front : back).Add(i);
            }
            Shuffle(front, rng);
            Shuffle(back, rng);

            // 뒤쪽 배정량 — 다섯 중 하나. 뒤 후보가 없으면 전부 앞에서 채운다.
            int wantBack = Mathf.Clamp(count / 5, 0, back.Count);
            int wantFront = count - wantBack;

            // 개수에 맞춰 좁힌다. 40°는 다섯 땀에는 맞지만 마흔 땀은 구 표면에 그 간격으로
            // 놓을 수가 없어, 조건을 못 맞춘 나머지가 전부 아래 채우기 루프로 새 버린다
            // (= 인덱스 순서대로 박혀 무작위성이 죽는다).
            float minSepDeg = Mathf.Clamp(180f / Mathf.Max(1, count), 5f, 40f);
            TakeSpread(front, wantFront, center, minSepDeg, result);
            TakeSpread(back, wantBack, center, minSepDeg, result);

            // 각거리 조건 때문에 모자라면 남은 것에서 아무거나 채운다.
            // 모양이 조금 뭉쳐도 땀 개수가 줄어드는 것보다 낫다.
            if (result.Count < count)
            {
                for (int i = 0; i < n && result.Count < count; i++)
                    if (!result.Contains(i)) result.Add(i);
            }
        }

        /// <summary>
        /// 고른 것 중 <b>조준점에 가장 가까운</b> 자리를 맨 앞으로 보낸다.
        ///
        /// <para>실은 순서대로 꿰므로 0번이 곧 <b>첫 발사가 꽂히는 곳</b>이다. 무작위로 두면
        /// 조준한 곳과 전혀 다른 데로 날아가 "내가 쏜 것"으로 안 읽힌다. 나머지 순서는
        /// 그대로 두어 이후의 들쑤시는 무작위성은 유지한다.</para>
        /// </summary>
        public void SortAimFirst(List<int> picked, Vector3 eye, Vector3 aimDir)
        {
            if (picked == null || picked.Count < 2) return;
            if (aimDir.sqrMagnitude < 1e-6f) return;
            aimDir = aimDir.normalized;

            int best = 0;
            float bestDot = -2f;
            for (int i = 0; i < picked.Count; i++)
            {
                Vector3 to = WorldPos(sites[picked[i]]) - eye;
                if (to.sqrMagnitude < 1e-8f) continue;
                float d = Vector3.Dot(to.normalized, aimDir);   // 클수록 조준선에 가깝다
                if (d > bestDot) { bestDot = d; best = i; }
            }

            if (best == 0) return;
            (picked[0], picked[best]) = (picked[best], picked[0]);
        }

        /// <summary>대상 중심에서 본 방향이 서로 <paramref name="minSepDeg"/> 이상 떨어지도록 골라 담는다.</summary>
        void TakeSpread(List<int> pool, int want, Vector3 center, float minSepDeg, List<int> result)
        {
            float cosLimit = Mathf.Cos(minSepDeg * Mathf.Deg2Rad);
            int taken = 0;
            for (int p = 0; p < pool.Count && taken < want; p++)
            {
                int idx = pool[p];
                Vector3 dir = (WorldPos(sites[idx]) - center).normalized;

                bool tooClose = false;
                for (int r = 0; r < result.Count; r++)
                {
                    Vector3 other = (WorldPos(sites[result[r]]) - center).normalized;
                    if (Vector3.Dot(dir, other) > cosLimit) { tooClose = true; break; }
                }
                if (tooClose) continue;

                result.Add(idx);
                taken++;
            }
        }

        static void Shuffle(List<int> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
