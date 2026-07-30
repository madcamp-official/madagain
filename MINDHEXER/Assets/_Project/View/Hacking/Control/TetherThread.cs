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
        [Tooltip("실 두께(m). 실은 얇아야 실로 보인다 — 2~4mm.")]
        public float thickness = 0.003f;

        [Header("엉덩이에서 뻗는 구간")]
        [Tooltip("방적돌기 축 방향으로 곧게 나가는 첫 구간 길이(m).\n" +
                 "★ 엉덩이가 ±35° 클램프에 걸려 대상을 못 겨눠도, 실은 반드시 엉덩이 축으로 먼저 나간다.\n" +
                 "그 뒤에 대상 쪽으로 휘므로 '실이 꺾인 것'으로 읽힌다 — 클램프가 결함이 아니라 물성이 된다.")]
        public float stubLength = 0.08f;

        [Header("자유 구간 (거미 → 첫 땀)")]
        [Tooltip("느슨할 때 아래로 처지는 최대량(m). 조이면 0이 되며 직선이 된다.")]
        public float sagMax = 0.12f;

        [Tooltip("처짐을 표현할 중간 점 개수. 늘려도 비용은 미미하나 3이면 충분하다.")]
        [Range(1, 6)] public int sagSegments = 3;

        [Header("땀")]
        [Tooltip("표면 위로 솟는 높이(m). 조이면 줄어들어 '눌려 붙은' 모양이 된다.")]
        public float loopOut = 0.02f;

        [Tooltip("표면 안으로 들어가는 깊이(m). 얇은 대상에서는 두께에 맞춰 자동으로 줄어든다.")]
        public float stitchDepth = 0.05f;

        [Tooltip("한 땀이 표면을 따라 벌어지는 폭의 절반(m). 들어간 곳과 나온 곳 사이 간격.")]
        public float stitchHalfSpan = 0.03f;

        [Tooltip("조였을 때 솟은 높이에 곱할 비율. 낮을수록 세게 조인 티가 난다.")]
        [Range(0.05f, 1f)] public float tightLoopScale = 0.35f;

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
                         float progress, float tension, Color color)
        {
            if (!Ensure()) return;

            _pts.Clear();
            progress = Mathf.Clamp01(progress);
            tension = Mathf.Clamp01(tension);

            int siteCount = (sites != null && picked != null) ? picked.Count : 0;

            // ① 방적돌기 + 엉덩이 축으로 곧게 나가는 구간
            if (spinneretDir.sqrMagnitude < 1e-6f) spinneretDir = Vector3.forward;
            spinneretDir = spinneretDir.normalized;
            _pts.Add(origin);
            Vector3 stubEnd = origin + spinneretDir * stubLength;
            _pts.Add(stubEnd);

            // ② 자유 구간 — 첫 땀의 진입점(없으면 대상 지점)까지, 느슨하면 처진다
            Vector3 firstAnchor = fallbackEnd;
            if (siteCount > 0)
            {
                ResolveStitch(sites, picked, 0, siteCount, out Vector3 a0, out _, out _);
                firstAnchor = a0;
            }
            AppendSag(stubEnd, firstAnchor, tension);

            // ③ 땀 — 완성된 것과, 지금 박히는 중인 것 하나
            if (siteCount > 0)
            {
                float exact = progress * siteCount;
                int done = Mathf.Clamp(Mathf.FloorToInt(exact), 0, siteCount);
                float frac = Mathf.Clamp01(exact - done);

                for (int i = 0; i < done; i++)
                {
                    ResolveStitch(sites, picked, i, siteCount, out Vector3 a, out Vector3 o, out Vector3 b);
                    _pts.Add(a);
                    _pts.Add(Vector3.Lerp(a, o, LoopScale(tension)));
                    _pts.Add(b);
                }

                // 박히는 중인 땀 — 솟은 부분이 표면 아래에서 위로 올라오며 "뚫고 나온다"
                if (done < siteCount && frac > 0.001f)
                {
                    ResolveStitch(sites, picked, done, siteCount, out Vector3 a, out Vector3 o, out Vector3 b);
                    _pts.Add(a);
                    _pts.Add(Vector3.Lerp(a, Vector3.Lerp(a, o, LoopScale(tension)), frac));
                    _pts.Add(Vector3.Lerp(a, b, frac));
                }
            }

            // 자유 구간의 끝점과 첫 땀의 진입점은 같은 자리다. 인접 중복점은 LineRenderer에서
            // 방향이 정의되지 않아 그 구간이 깜빡인다 — 지운다.
            for (int i = _pts.Count - 1; i > 0; i--)
                if ((_pts[i] - _pts[i - 1]).sqrMagnitude < 1e-10f) _pts.RemoveAt(i);

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

            if (_mat != null)
            {
                // URP/Unlit은 _BaseColor를 쓴다. 빌트인 호환을 위해 _Color도 같이 넣는다.
                if (_mat.HasProperty(BaseColorId)) _mat.SetColor(BaseColorId, color);
                if (_mat.HasProperty(ColorId)) _mat.SetColor(ColorId, color);
            }
        }

        float LoopScale(float tension) => Mathf.Lerp(1f, tightLoopScale, tension);

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
