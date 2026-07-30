using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 해킹 실의 <b>표현만</b> 담당한다 — 점 목록을 만들어 <see cref="LineRenderer"/>에 넣는다.
    /// 해킹 상태는 모른다. 진행도·조임은 <see cref="ControlTether"/>가 넘겨준다.
    ///
    /// <para><b>★ 숨는 구간을 셰이더로 지우지 않는다.</b> 바느질은 절반이 "안 보이는 구간"인데,
    /// 마스크나 스텐실로 잘라내면 패스가 늘고 VR에서 깨진다. 대신 실을 <b>진짜로 표면 안쪽에
    /// 집어넣는다</b> — 깊이 버퍼가 공짜로 가려 준다. 어느 각도에서 봐도 맞고, 대상이 회전해도
    /// 자동으로 맞고, 평범한 불투명 물체 하나라 드로우콜이 1이다.</para>
    ///
    /// <para>실 한 땀의 모양: <c>안(들어감) → 밖(솟음) → 안(들어감)</c> 세 점.
    /// 땀과 땀 사이는 대상 <b>속</b>을 지나므로 저절로 안 보인다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class TetherThread : MonoBehaviour
    {
        [Header("굵기")]
        [Tooltip("실 두께(m). 사실적인 실 굵기(3mm)는 실기에서 전혀 안 보였다 — 게임 안에서\n" +
                 "확실히 읽히려면 이 정도는 되어야 한다(사용자 실측: 3mm의 21배).\n" +
                 "★ 이 값을 키우면 loopOut·stitchDepth도 같이 키워야 한다. 안 그러면 땀이\n" +
                 "  실 자기 굵기에 파묻혀 바느질이 아예 안 보인다.")]
        public float thickness = 0.038f;

        [Header("엉덩이에서 뻗는 구간")]
        [Tooltip("방적돌기 축 방향으로 곧게 나가는 첫 구간 길이(m).\n" +
                 "★ 엉덩이가 ±35° 클램프에 걸려 대상을 못 겨눠도, 실은 반드시 엉덩이 축으로 먼저 나간다.\n" +
                 "그 뒤에 대상 쪽으로 휘므로 '실이 꺾인 것'으로 읽힌다 — 클램프가 결함이 아니라 물성이 된다.")]
        public float stubLength = 0.08f;

        [Header("자유 구간 (거미 → 첫 땀)")]
        [Tooltip("느슨할 때 아래로 처지는 최대량(m). 조이면 0이 되며 팽팽한 직선이 된다.\n" +
                 "몇 미터짜리 구간이라 작게 잡으면 처진 게 안 보인다.")]
        public float sagMax = 0.35f;

        [Tooltip("처짐을 표현할 중간 점 개수. 적으면 곡선이 각져 보인다.")]
        [Range(1, 8)] public int sagSegments = 4;

        [Tooltip("대상에 박힌 뒤(바느질 구간) 실 굵기 비율. 나가는 줄은 굵고, 꿰는 실은 가늘다.")]
        [Range(0.05f, 1f)] public float stitchWidthRatio = 0.3f;

        [Tooltip("느슨할 때 바느질 구간 굵기에 곱할 배율.\n" +
                 "★ 중력 처짐보다 이쪽이 훨씬 잘 읽힌다 — 흑백 화면에서는 위치보다 굵기가 눈에 띈다.\n" +
                 "  실제 줄도 당기면 가늘어지고 늘어지면 굵어진다. 조종하면 1배로 쭉 줄어든다.")]
        [Range(1f, 3f)] public float slackWidthScale = 2f;

        [Header("땀")]
        [Tooltip("표면 위로 솟는 높이(m).\n" +
                 "★ 바느질 구간의 실 <b>반지름</b>보다 확실히 커야 한다. 작으면 솟은 부분이\n" +
                 "  자기 굵기에 파묻혀 그냥 직선으로 보인다(실제로 그랬다).")]
        public float loopOut = 0.06f;

        [Tooltip("표면 안으로 들어가는 깊이(m). 얇은 대상에서는 두께에 맞춰 자동으로 줄어든다.")]
        public float stitchDepth = 0.12f;

        [Tooltip("한 땀이 표면을 따라 벌어지는 폭의 절반(m). 좁으면 굵은 실이 매듭처럼 뭉친다.")]
        public float stitchHalfSpan = 0.10f;

        [Tooltip("조였을 때 솟은 높이에 곱할 비율. 낮을수록 세게 조인 티가 난다.")]
        [Range(0.05f, 1f)] public float tightLoopScale = 0.35f;

        [Tooltip("느슨할 때 땀이 중력으로 아래로 늘어지는 양(m).\n" +
                 "조이면 0이 되어 표면에 붙는다 — 조작을 쉬면 매듭까지 축 늘어져 보여야 한다.")]
        public float slackDroop = 0.05f;

        [Tooltip("땀 하나를 몇 조각으로 쪼개 그릴 것인가.\n" +
                 "★ 1이면 꼭짓점 하나짜리 V가 되어 <b>각진 꺾임</b>일 뿐 곡선으로 안 보인다.\n" +
                 "  실은 뻣뻣한 철사가 아니라 늘어지는 줄이므로 곡선이어야 한다.")]
        [Range(1, 10)] public int loopSegments = 6;

        [Tooltip("팽팽할 때도 남기는 최소 휘어짐 비율. 0이면 완전한 직선이 되어 실 같지 않다.")]
        [Range(0f, 0.5f)] public float minCurve = 0.12f;

        readonly List<Vector3> _pts = new List<Vector3>(32);
        Vector3[] _buf = new Vector3[32];   // 매 프레임 ToArray()를 하면 GC가 쌓인다
        LineRenderer _line;
        Material _mat;
        bool _failed;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>실을 그릴 수 있는 상태인가. 셰이더를 못 찾았으면 false로 굳는다.</summary>
        public bool Ready => _line != null;

        /// <summary>가장 최근에 그린 실의 끝점(월드). 거미가 이쪽으로 엉덩이를 겨눈다.</summary>
        public Vector3 LastEnd { get; private set; }

        // ── 준비 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// <see cref="LineRenderer"/>를 한 번만 만든다.
        ///
        /// <para>⚠️ <b>셰이더를 먼저 찾는다.</b> 예전 구현은 오브젝트를 만든 뒤에 찾다가, 실패하면
        /// 그 오브젝트가 고아로 남아 <b>프레임당 하나씩 누수</b>됐다. 실기에서 렌더러가 12,000개까지
        /// 불어나 60→17fps로 떨어진 적이 있다. 실패는 <see cref="_failed"/>로 굳혀 재시도하지 않는다.</para>
        /// </summary>
        bool Ensure()
        {
            if (_line != null) return true;
            if (_failed) return false;

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                _failed = true;
                Debug.LogError("[TetherThread] 'Universal Render Pipeline/Unlit' 셰이더를 못 찾음 — " +
                               "빌드에선 Always Included Shaders에 넣어야 한다. 실 표시를 건너뛴다.", this);
                return false;
            }

            var go = new GameObject("[TetherThread]");
            go.transform.SetParent(null, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.alignment = LineAlignment.View;      // 실은 얇아서 카메라를 향해 눕는 편이 맞다
            lr.textureMode = LineTextureMode.Stretch;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.numCornerVertices = 0;               // ★ 올리면 꺾이는 점마다 정점이 배로 는다
            lr.numCapVertices = 0;
            lr.widthMultiplier = thickness;

            _mat = new Material(shader);
            lr.sharedMaterial = _mat;

            _line = lr;
            _line.enabled = false;
            return true;
        }

        void OnDestroy()
        {
            if (_line != null) Destroy(_line.gameObject);
            if (_mat != null) Destroy(_mat);
        }

        public void Hide()
        {
            if (_line != null) _line.enabled = false;
        }

        // ── 그리기 ───────────────────────────────────────────────────────────

        /// <summary>
        /// 실 한 줄을 그린다.
        /// </summary>
        /// <param name="origin">방적돌기 위치(월드).</param>
        /// <param name="spinneretDir">엉덩이가 실제로 향하는 방향. 첫 구간이 이쪽으로 나간다.</param>
        /// <param name="sites">땀 자리. null이거나 비면 대상 지점까지 직선만 그린다.</param>
        /// <param name="picked">고른 후보 인덱스.</param>
        /// <param name="fallbackEnd">땀이 없을 때 실이 도달할 지점(대상 피벗).</param>
        /// <param name="progress">0~1. 땀 몇 개가 박혔는가. 소수부는 박히는 중인 땀.</param>
        /// <param name="tension">0~1. 1이면 팽팽하다.</param>
        /// <param name="color">실 색.</param>
        public void Draw(Vector3 origin, Vector3 spinneretDir,
                         StitchSites sites, List<int> picked, Vector3 fallbackEnd,
                         float launch, float progress, float tension, Color color)
        {
            if (!Ensure()) return;

            _pts.Clear();
            progress = Mathf.Clamp01(progress);
            tension = Mathf.Clamp01(tension);

            int siteCount = (sites != null && picked != null) ? picked.Count : 0;

            // ① 방적돌기 + 엉덩이 축으로 곧게 나가는 구간
            if (spinneretDir.sqrMagnitude < 1e-6f) spinneretDir = Vector3.forward;
            spinneretDir = spinneretDir.normalized;
            AddPt(origin);
            Vector3 stubEnd = origin + spinneretDir * stubLength;
            AddPt(stubEnd);

            // ② 자유 구간 — 첫 땀의 진입점(없으면 대상 지점)까지, 느슨하면 처진다
            //
            // ★ launch가 발사다. 실 끝이 방적돌기에서 대상까지 <b>날아가는 게 보여야</b> 한다.
            //   예전엔 완성된 길이로 한 번에 나타나 "탁" 생겨 버렸다.
            Vector3 firstAnchor = fallbackEnd;
            if (siteCount > 0)
            {
                ResolveStitch(sites, picked, 0, siteCount, out Vector3 a0, out _, out _);
                firstAnchor = a0;
            }
            launch = Mathf.Clamp01(launch);
            Vector3 tip = Vector3.Lerp(stubEnd, firstAnchor, launch);
            // ⚠️ 예전엔 여기에 `tension * launch`를 넘겼다. launch는 0에서 시작하므로 곱하면
            //    <b>날아가는 동안 처짐이 최대</b>가 된다 — 주석과 정반대였고, 실이 축 늘어진 채
            //    힘없이 나갔다. 발사 중 팽팽함은 ControlTether가 tension으로 직접 준다.
            AppendSag(stubEnd, tip, tension);

            // ③ 땀 — 완성된 것과, 지금 박히는 중인 것 하나
            //    여기서부터가 '꿰는 실'이라 굵기가 가늘어진다.
            //    ★ 실이 <b>도착한 뒤에야</b> 꿰기 시작한다.
            int stitchStart = _pts.Count;
            if (siteCount > 0 && launch >= 0.999f)
            {
                float exact = progress * siteCount;
                int done = Mathf.Clamp(Mathf.FloorToInt(exact), 0, siteCount);
                float frac = Mathf.Clamp01(exact - done);

                for (int i = 0; i < done; i++)
                {
                    ResolveStitch(sites, picked, i, siteCount, out Vector3 a, out Vector3 o, out Vector3 b);
                    AddPt(a);
                    AppendLoop(a, OuterOf(a, o, tension), b);
                    AddPt(b);
                }

                // ★ 박히는 중인 땀 — 앞 땀의 끝에서 <b>이어져 자라난다</b>.
                //   예전엔 진입점을 통째로 찍고 솟은 점만 올렸다 — 실이 다음 자리로
                //   순간이동한 뒤 거기서 부풀어 오르는 꼴이라 "탁 생긴다"로 보였다.
                if (done < siteCount && frac > 0.001f && _pts.Count > 0)
                {
                    ResolveStitch(sites, picked, done, siteCount, out Vector3 a, out Vector3 o, out Vector3 b);
                    AppendPartial(_pts[_pts.Count - 1], a, OuterOf(a, o, tension), b, frac);
                }
            }

            if (_pts.Count < 2) { _line.enabled = false; return; }
            LastEnd = _pts[_pts.Count - 1];

            // 길이를 정확히 맞춘다 — SetPositions에 더 긴 배열을 주는 동작이 버전마다 미묘하다.
            // 점 개수는 땀이 늘 때만 바뀌므로(해킹 한 번에 대여섯 번) 매 프레임 할당이 아니다.
            if (_buf.Length != _pts.Count) _buf = new Vector3[_pts.Count];
            _pts.CopyTo(_buf, 0);

            _line.enabled = true;
            _line.widthMultiplier = thickness;
            _line.positionCount = _pts.Count;
            _line.SetPositions(_buf);
            ApplyWidthCurve(stitchStart, stitchWidthRatio * Mathf.Lerp(slackWidthScale, 1f, tension));

            if (_mat != null)
            {
                // URP/Unlit은 _BaseColor를 쓴다. 빌트인 호환을 위해 _Color도 같이 넣는다.
                if (_mat.HasProperty(BaseColorId)) _mat.SetColor(BaseColorId, color);
                if (_mat.HasProperty(ColorId)) _mat.SetColor(ColorId, color);
            }
        }

        float LoopScale(float tension) => Mathf.Lerp(1f, tightLoopScale, tension);

        /// <summary>
        /// 땀이 표면 위로 솟은 점. 조이면 낮게 눌리고, 느슨하면 <b>중력으로 아래로 늘어진다</b>.
        /// 자유 구간만 처지고 땀은 그대로면 "위쪽만 늘어진" 어색한 그림이 된다.
        /// </summary>
        Vector3 OuterOf(Vector3 enter, Vector3 outer, float tension)
        {
            Vector3 p = Vector3.Lerp(enter, outer, LoopScale(tension));
            // 완전히 조여도 minCurve만큼은 남긴다 — 실이 자로 그은 듯 곧으면 철사처럼 보인다.
            float slack = Mathf.Max(minCurve, 1f - tension);
            return p + Vector3.down * (slackDroop * slack);
        }

        /// <summary>
        /// 땀 하나를 <b>곡선</b>으로 그린다. <paramref name="apex"/>를 실제로 지나가는 이차 곡선이다.
        ///
        /// <para>예전엔 점 세 개(<c>진입 → 솟음 → 이탈</c>)만 찍었다. 그건 곡선이 아니라
        /// <b>꼭짓점 하나짜리 V</b>라, 가는 실에서는 그냥 꺾인 직선으로 보인다.
        /// 실은 늘어지는 줄이므로 휘어야 한다.</para>
        /// </summary>
        void AppendLoop(Vector3 enter, Vector3 apex, Vector3 exit)
        {
            int segs = Mathf.Max(1, loopSegments);
            if (segs == 1) { AddPt(apex); return; }

            // 이차 베지어는 제어점을 지나가지 않는다 — t=0.5에서 apex를 지나도록 제어점을 역산한다.
            Vector3 ctrl = 2f * apex - (enter + exit) * 0.5f;

            for (int i = 1; i < segs; i++)
            {
                float t = i / (float)segs;
                float u = 1f - t;
                AddPt(u * u * enter + 2f * u * t * ctrl + t * t * exit);
            }
        }

        /// <summary>
        /// 박히는 중인 땀 — <b>완성됐을 때와 똑같은 곡선</b>을 따라 <paramref name="frac"/>만큼만 그린다.
        ///
        /// <para>직선으로 자라다가 완성되는 순간 곡선으로 바뀌면 매 땀마다 튄다.
        /// 그래서 같은 경로를 만들어 두고 길이 비율로 잘라 낸다.</para>
        /// </summary>
        void AppendPartial(Vector3 prev, Vector3 enter, Vector3 apex, Vector3 exit, float frac)
        {
            _tmp.Clear();
            _tmp.Add(prev);
            _tmp.Add(enter);

            int segs = Mathf.Max(1, loopSegments);
            if (segs == 1) _tmp.Add(apex);
            else
            {
                Vector3 ctrl = 2f * apex - (enter + exit) * 0.5f;
                for (int i = 1; i < segs; i++)
                {
                    float t = i / (float)segs;
                    float u = 1f - t;
                    _tmp.Add(u * u * enter + 2f * u * t * ctrl + t * t * exit);
                }
            }
            _tmp.Add(exit);

            float total = 0f;
            for (int i = 1; i < _tmp.Count; i++) total += Vector3.Distance(_tmp[i - 1], _tmp[i]);
            if (total < 1e-5f) return;

            float want = Mathf.Clamp01(frac) * total;
            for (int i = 1; i < _tmp.Count; i++)
            {
                float d = Vector3.Distance(_tmp[i - 1], _tmp[i]);
                if (want <= d) { AddPt(Vector3.Lerp(_tmp[i - 1], _tmp[i], want / Mathf.Max(1e-5f, d))); return; }
                AddPt(_tmp[i]);
                want -= d;
            }
        }

        readonly List<Vector3> _tmp = new List<Vector3>(16);

        /// <summary>인접 중복점은 넣지 않는다 — LineRenderer에서 방향이 정의되지 않아 깜빡인다.</summary>
        void AddPt(Vector3 p)
        {
            if (_pts.Count > 0 && (_pts[_pts.Count - 1] - p).sqrMagnitude < 1e-10f) return;
            _pts.Add(p);
        }

        /// <summary>
        /// 나가는 줄은 굵게, 대상에 박힌 뒤 꿰는 실은 가늘게.
        ///
        /// <para><see cref="LineRenderer.widthCurve"/>는 <b>길이 비율</b>로 평가되므로,
        /// 땀이 시작되는 점이 전체 길이의 몇 %인지를 재서 그 지점에서 굵기를 떨어뜨린다.
        /// 점 개수로 나누면 안 된다 — 자유 구간은 점이 적고 길며, 땀은 점이 많고 짧다.</para>
        /// </summary>
        void ApplyWidthCurve(int stitchStart, float stitchRatio)
        {
            float t = 1f;
            if (stitchStart > 0 && stitchStart < _pts.Count)
            {
                float total = 0f, upTo = 0f;
                for (int i = 1; i < _pts.Count; i++)
                {
                    total += Vector3.Distance(_pts[i - 1], _pts[i]);
                    if (i == stitchStart - 1) upTo = total;   // 자유 구간이 끝나는 지점까지의 길이
                }
                if (total > 1e-5f) t = Mathf.Clamp01(upTo / total);
            }

            // 매 프레임 AnimationCurve를 새로 만들면 GC가 쌓인다. 변화가 클 때만 다시 만든다.
            if (_curve != null && Mathf.Abs(t - _curveT) < 0.01f
                && Mathf.Abs(_curveRatio - stitchRatio) < 0.005f) return;

            _curveT = t;
            _curveRatio = stitchRatio;

            if (t >= 0.999f)
            {
                _curve = AnimationCurve.Constant(0f, 1f, 1f);
            }
            else
            {
                // 굵기가 뚝 떨어지게 — 대상에 닿는 순간이 눈에 보여야 한다.
                _curve = new AnimationCurve(
                    new Keyframe(0f, 1f),
                    new Keyframe(Mathf.Max(0f, t - 0.001f), 1f),
                    new Keyframe(t, stitchRatio),
                    new Keyframe(1f, stitchRatio));
            }
            _line.widthCurve = _curve;
        }

        AnimationCurve _curve;
        float _curveT = -1f, _curveRatio = -1f;

        /// <summary>
        /// 땀 하나의 세 점을 푼다 — 진입(안) / 솟음(밖) / 이탈(안).
        /// 표면을 따라 벌어지는 방향은 <b>다음 땀 쪽</b>으로 잡는다. 그래야 실이
        /// 이어지는 흐름과 어긋나지 않는다.
        /// </summary>
        void ResolveStitch(StitchSites sites, List<int> picked, int i, int count,
                           out Vector3 enter, out Vector3 outer, out Vector3 exit)
        {
            var s = sites.sites[picked[i]];
            Vector3 p = sites.WorldPos(s);
            Vector3 n = sites.WorldNormal(s);

            // 얇은 판(레일·판넬)에서 실이 뒤로 뚫고 나오지 않게 두께의 절반 이내로 제한한다.
            float depth = stitchDepth;
            if (s.thickness > 0.0001f) depth = Mathf.Min(depth, s.thickness * 0.4f);

            // 표면을 따라 벌어질 방향 — 다음 땀(마지막이면 이전 땀) 쪽
            int other = (i + 1 < count) ? i + 1 : i - 1;
            Vector3 along;
            if (other >= 0 && other < count)
            {
                Vector3 q = sites.WorldPos(sites.sites[picked[other]]);
                along = Vector3.ProjectOnPlane(q - p, n);
                if (other < i) along = -along;
            }
            else along = Vector3.zero;

            if (along.sqrMagnitude < 1e-8f)
            {
                along = Vector3.ProjectOnPlane(Vector3.right, n);
                if (along.sqrMagnitude < 1e-8f) along = Vector3.ProjectOnPlane(Vector3.forward, n);
            }
            along = along.normalized * stitchHalfSpan;

            enter = p - n * depth - along;
            outer = p + n * loopOut;
            exit = p - n * depth + along;
        }

        /// <summary>거미에서 첫 땀까지의 자유 구간. 느슨할수록 아래로 처진다.</summary>
        void AppendSag(Vector3 from, Vector3 to, float tension)
        {
            float sag = sagMax * (1f - tension);
            if (sag <= 0.0005f) { _pts.Add(to); return; }

            for (int i = 1; i <= sagSegments; i++)
            {
                float t = i / (float)(sagSegments + 1);
                Vector3 p = Vector3.Lerp(from, to, t);
                p.y -= sag * Mathf.Sin(t * Mathf.PI);   // 양 끝에서 0, 가운데가 가장 처진다
                _pts.Add(p);
            }
            _pts.Add(to);
        }
    }
}
