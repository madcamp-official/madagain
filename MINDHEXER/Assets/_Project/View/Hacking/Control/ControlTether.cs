using System.Collections.Generic;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 해킹 실 — 거미 방적돌기와 대상을 잇는 줄. (기초_설계안 §6.2 마리오네트)
    ///
    /// <para>이 컴포넌트는 <b>상태와 진행도</b>만 갖는다. 실을 실제로 그리는 것은
    /// <see cref="TetherThread"/>, 어디를 찌를지는 <see cref="StitchSites"/>가 안다.
    /// 셋을 나눈 이유는 표현을 갈아엎을 때 해킹 흐름이 딸려 들어오지 않게 하기 위함이다.</para>
    ///
    /// <para><b>연출 요약</b> — 실은 대상 표면 <b>여기저기를 무작위로 들쑤신다</b>. 처음엔 한 땀도
    /// 없다가 패턴 획을 하나 이을 때마다 한 땀씩 는다. 장악하면 팽팽하게 조이고, 풀면 마지막에
    /// 찌른 땀부터 <b>역순으로</b> 하나씩 빠진다. 이미 해킹했던 것을 다시 잡을 때는 같은 연출이
    /// <b>타이머로</b> 빠르게 재생된다 — 연출을 두 벌 만들지 않는다.</para>
    ///
    /// <para><b>공개 API는 예전 그대로다</b>(<see cref="Active"/>·<see cref="StartPoint"/>·
    /// <see cref="EndPoint"/>·<see cref="UpdateTether"/>·<see cref="originOverride"/>).
    /// <c>HackDriver</c>·<c>SpiderRig</c>가 이미 쓰고 있어 시그니처를 바꾸면 다른 세션이 깨진다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class ControlTether : MonoBehaviour
    {
        [Header("색")]
        [Tooltip("해킹 시도 중(패턴 그리는 중) 실 색 — 초록(§7).")]
        public Color hackingColor = new Color(0.4f, 1f, 0.3f, 1f);

        [Tooltip("조종 중(장악 성공) 실 색 — 파랑(§7 내 것).")]
        public Color controlColor = new Color(0.3f, 0.8f, 1f, 1f);

        [Header("시작점")]
        [Tooltip("실이 시작하는 지점의 카메라 기준 오프셋(오른손 위 펫 거미 자리, §2.6).\n" +
                 "★ 거미가 씬에 있으면 originOverride가 이 값을 대신한다 — 이건 거미가 없을 때의 임시 자리다.")]
        public Vector3 handOffset = new Vector3(0.35f, -0.35f, 0.5f);

        [Tooltip("실이 실제로 나오는 지점(거미 방적돌기). SpiderRig가 손목에 있을 때만 채워 넣는다.\n" +
                 "거미가 대상으로 날아가 붙은 뒤에는 비워야 한다 — 안 그러면 실 길이가 0이 된다.\n" +
                 "비어 있으면 from + handOffset 을 쓴다(기존 동작).")]
        public Transform originOverride;

        [Tooltip("방적돌기가 향하는 축(originOverride 로컬). 실의 첫 구간이 이 방향으로 나간다.\n" +
                 "비면 originOverride의 −Z를 쓴다(SpiderRig의 spinneretAxis 기본값과 같다).")]
        public Vector3 spinneretAxisLocal = new Vector3(0f, 0f, -1f);

        [Header("땀")]
        [Tooltip("한 번의 해킹에 찌르는 땀 개수. 패턴 선 개수와 맞춘다(전 대상 5선으로 통일됨).")]
        [Range(1, 12)] public int stitchCount = 5;

        [Header("타이밍")]
        [Tooltip("획 하나를 이었을 때 그 땀이 박히는 시간(초). 짧을수록 '탁' 하고 박힌다.\n" +
                 "0이면 순간이동이라 딱딱하고, 길면 늘어진다.")]
        public float stitchSnapTime = 0.08f;

        [Tooltip("이미 해킹했던 것을 다시 잡을 때 5땀을 전부 박는 데 걸리는 시간(초). 쓰윽.")]
        public float fastStitchTime = 0.35f;

        [Tooltip("해제할 때 땀이 역순으로 전부 빠지는 시간(초).")]
        public float retractTime = 0.5f;

        [Header("조임")]
        [Tooltip("해킹 시도 중 팽팽함. 낮게 둬야 처짐이 보이고, 장악 순간의 대비가 산다.")]
        [Range(0f, 1f)] public float looseTension = 0.15f;

        [Tooltip("조임이 바뀌는 데 걸리는 시간(초).")]
        public float tensionTime = 0.25f;

        [Header("참조 (비우면 찾는다)")]
        public TetherThread thread;
        public PatternMinigame minigame;

        // ── 공개 상태 (기존 API 유지) ────────────────────────────────────────

        /// <summary>지금 실이 보이는가. 펫 거미가 "발사 자세"를 잡는 신호로 쓴다(§2.6).</summary>
        public bool Active { get; private set; }

        /// <summary>실이 시작하는 지점(월드). 거미가 여기서 실을 뽑는다.</summary>
        public Vector3 StartPoint { get; private set; }

        /// <summary>실 끝(대상) 지점(월드).</summary>
        public Vector3 EndPoint { get; private set; }

        /// <summary>
        /// ★ 엉덩이가 향해야 할 <b>월드 방향</b>. <c>SpiderRig</c>가 이걸 읽어 자기 뼈에
        /// ±35° 클램프를 걸어 적용한다.
        ///
        /// <para>클램프 값은 거미 메시가 안 찢어지는 한계라 <b>거미 쪽 지식</b>이므로 여기서
        /// 자르지 않는다. 여기는 "어디를 향하고 싶은가"만 말하고, 얼마나 돌릴 수 있는지는
        /// 거미가 정한다. 그래서 두 세션이 같은 파일을 안 건드린다.</para>
        ///
        /// <para>실이 없을 때는 <see cref="Vector3.zero"/>다.</para>
        /// </summary>
        public Vector3 AimDirection { get; private set; }

        /// <summary>0~1. 땀이 몇 개나 박혔는가.</summary>
        public float Progress => _progress;

        /// <summary>0~1. 1이면 팽팽하다.</summary>
        public float Tension => _tension;

        // ── 내부 ─────────────────────────────────────────────────────────────

        readonly List<int> _picked = new List<int>(8);
        StitchSites _sites;
        Hackable _boundTarget;          // 지금 땀 자리를 뽑아 둔 대상
        int _seed;
        bool _wasHacking;               // 직전 프레임이 패턴 그리는 중이었나 (재조종 판별)
        float _progress, _progressTarget;
        float _tension, _tensionTarget;
        float _fastTimer = -1f;         // ≥0이면 재조종 타이머가 도는 중
        bool _warnedNotBaked;
        Vector3 _lastTargetPos;         // 대상이 사라진 뒤에도 회수 연출을 그리려면 필요하다
        Color _lastColor;               // 패턴 취소(초록)와 조종 해제(파랑)의 회수 색이 달라야 한다

        void Awake()
        {
            if (thread == null)
            {
                thread = GetComponent<TetherThread>();
                if (thread == null) thread = gameObject.AddComponent<TetherThread>();
            }
        }

        /// <summary>매 프레임 호출. target이 null이면 실을 감춘다. captured=true면 파랑, false면 초록.</summary>
        public void UpdateTether(Transform from, Transform target, bool captured)
        {
            float dt = Time.deltaTime;

            // ── 대상이 바뀌었나 ──────────────────────────────────────────────
            // ★ 대상이 null이 되어도 땀 자리를 지우지 않는다. 해제 연출(역순 풀림)이
            //   그 자리를 계속 써야 하기 때문이다. 완전히 회수된 뒤에 비운다.
            Hackable hk = target != null ? target.GetComponentInParent<Hackable>() : null;
            if (hk != null && hk != _boundTarget)
            {
                _boundTarget = hk;
                _progress = 0f;
                _fastTimer = -1f;
                BindSites(hk);
            }

            if (from == null || target == null)
            {
                // 회수 — 마지막에 찌른 땀부터 역순으로 빠진 뒤에 감춘다.
                _progressTarget = 0f;
                Step(dt, retract: true);
                if (_progress <= 0.001f)
                {
                    Active = false;
                    AimDirection = Vector3.zero;
                    if (thread != null) thread.Hide();
                    _wasHacking = false;
                    _boundTarget = null;
                    _sites = null;
                    _picked.Clear();
                    return;
                }
                DrawRetract(from);
                return;
            }

            _lastTargetPos = target.position;

            bool hacking = !captured;

            // ── 진행도를 무엇이 미는가 ───────────────────────────────────────
            if (hacking)
            {
                // 패턴 획이 진행도를 민다. StrokeCount는 정수라 폴링이 안전하다.
                int strokes = 0;
                var mg = ResolveMinigame();
                if (mg != null && mg.Input != null) strokes = mg.Input.StrokeCount;
                _progressTarget = stitchCount > 0 ? Mathf.Clamp01(strokes / (float)stitchCount) : 0f;
                _fastTimer = -1f;
            }
            else
            {
                if (_wasHacking)
                {
                    // 패턴을 막 끝냈다 — 이미 거의 다 박혀 있다. 마무리만.
                    _progressTarget = 1f;
                }
                else
                {
                    // ★ 재조종 — 같은 연출을 타이머로 빠르게 재생한다.
                    if (_fastTimer < 0f) _fastTimer = 0f;
                    _fastTimer += dt;
                    _progressTarget = fastStitchTime > 0.0001f
                        ? Mathf.Clamp01(_fastTimer / fastStitchTime)
                        : 1f;
                }
            }

            _tensionTarget = hacking ? looseTension : 1f;
            _wasHacking = hacking;

            Step(dt, retract: false);
            DrawWith(from, target, captured);
        }

        void Step(float dt, bool retract)
        {
            // 박힐 때는 짧게 '탁', 회수할 때는 정해진 시간에 걸쳐 스르르.
            if (retract)
            {
                float rate = retractTime > 0.0001f ? dt / retractTime : 1f;
                _progress = Mathf.MoveTowards(_progress, _progressTarget, rate);
            }
            else
            {
                _progress = stitchSnapTime > 0.0001f
                    ? Mathf.Lerp(_progress, _progressTarget, 1f - Mathf.Exp(-dt / stitchSnapTime))
                    : _progressTarget;
            }

            _tension = tensionTime > 0.0001f
                ? Mathf.Lerp(_tension, _tensionTarget, 1f - Mathf.Exp(-dt / tensionTime))
                : _tensionTarget;
        }

        /// <summary>대상이 사라진 뒤의 회수 연출. 마지막으로 알던 자리를 계속 쓴다.</summary>
        void DrawRetract(Transform from)
        {
            Render(OriginOf(from), _lastTargetPos, _lastColor);
        }

        void DrawWith(Transform from, Transform target, bool captured)
        {
            _lastColor = captured ? controlColor : hackingColor;
            Render(OriginOf(from), target.position, _lastColor);
        }

        Vector3 OriginOf(Transform from)
        {
            if (originOverride != null) return originOverride.position;
            return from != null ? from.TransformPoint(handOffset) : transform.position;
        }

        void Render(Vector3 a, Vector3 fallbackEnd, Color color)
        {
            Active = true;
            StartPoint = a;

            if (thread == null) { EndPoint = fallbackEnd; return; }

            Vector3 dir = SpinneretDir(a, fallbackEnd);
            thread.Draw(a, dir, _sites, _picked, fallbackEnd,
                        _progress, _tension, color);

            EndPoint = thread.Ready ? thread.LastEnd : fallbackEnd;

            // 엉덩이는 '실이 실제로 가는 쪽'을 향해야 한다 — 첫 땀이 있으면 그쪽, 없으면 대상.
            Vector3 aimAt = fallbackEnd;
            if (_sites != null && _picked.Count > 0) aimAt = _sites.WorldPos(_sites.sites[_picked[0]]);
            Vector3 aim = aimAt - a;
            AimDirection = aim.sqrMagnitude > 1e-6f ? aim.normalized : Vector3.zero;
        }

        /// <summary>
        /// 엉덩이가 실제로 향하는 방향. <c>SpiderRig</c>가 클램프를 걸어 뼈를 돌려 두었으므로
        /// 그 결과(방적돌기 트랜스폼의 축)를 그대로 읽는다. 그래야 <b>실과 엉덩이 각도가 항상
        /// 일치</b>한다 — 클램프에 걸렸을 때도 실이 엉덩이 축으로 먼저 나간다.
        /// </summary>
        Vector3 SpinneretDir(Vector3 origin, Vector3 fallbackEnd)
        {
            if (originOverride != null && spinneretAxisLocal.sqrMagnitude > 1e-6f)
            {
                Vector3 d = originOverride.TransformDirection(spinneretAxisLocal);
                if (d.sqrMagnitude > 1e-6f) return d.normalized;
            }
            Vector3 f = fallbackEnd - origin;
            return f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
        }

        PatternMinigame ResolveMinigame()
        {
            if (minigame == null) minigame = FindFirstObjectByType<PatternMinigame>();
            return minigame;
        }

        // ── 땀 자리 결정 ─────────────────────────────────────────────────────

        void BindSites(Hackable hk)
        {
            _picked.Clear();
            _sites = null;
            if (hk == null) return;

            // 베이커가 Hackable과 같은 오브젝트에 붙이므로 한 단계로 끝난다.
            // ★ 위로 올라가며 찾지 않는다 — 다른 물체 밑에 낀 대상이 남의 땀 자리를 집어온다.
            _sites = hk.GetComponent<StitchSites>();
            if (_sites == null || !_sites.IsBaked) _sites = BuildFallback(hk);
            if (_sites == null) return;

            // 시드는 해킹마다 새로 뽑는다 — 같은 대상이어도 매번 다른 자리를 찌른다.
            // 다만 한 번 뽑으면 그 해킹이 끝날 때까지 고정이다(안 그러면 실이 발작한다).
            _seed = Random.Range(int.MinValue, int.MaxValue);

            var cam = Camera.main;
            Vector3 viewer = cam != null ? cam.transform.position : transform.position;
            _sites.Pick(stitchCount, viewer, _seed, _picked);
        }

        /// <summary>
        /// 굽는 걸 잊은 대상을 위한 폴백 — <b>콜라이더</b>에 레이캐스트해 자리를 만든다.
        ///
        /// <para>박스 콜라이더면 실제 메시보다 큰 상자에 박히므로 어설프다. 그래도 실이 아예
        /// 안 나오는 것보다 낫고, 경고가 남아 굽는 걸 잊었다는 걸 알 수 있다.</para>
        /// </summary>
        StitchSites BuildFallback(Hackable hk)
        {
            if (!_warnedNotBaked)
            {
                _warnedNotBaked = true;
                Debug.LogWarning($"[ControlTether] '{hk.name}'에 구운 땀 자리가 없습니다 — " +
                                 "콜라이더 레이캐스트로 대신합니다(부정확). " +
                                 "Tools/해킹/땀 자리 굽기 를 돌리십시오.", hk);
            }

            var col = hk.GetComponentInChildren<Collider>();
            if (col == null) return null;

            var holder = hk.gameObject.AddComponent<StitchSites>();
            Bounds b = col.bounds;
            Vector3 center = b.center;
            float radius = Mathf.Max(b.extents.magnitude, 0.05f);

            const int candidates = 24;
            for (int i = 0; i < candidates; i++)
            {
                // 구 표면에 고르게 흩뿌린다(황금각 나선). 무작위보다 뭉침이 적다.
                float t = (i + 0.5f) / candidates;
                float y = 1f - 2f * t;
                float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
                float phi = i * 2.39996323f;
                Vector3 d = new Vector3(Mathf.Cos(phi) * r, y, Mathf.Sin(phi) * r);

                Vector3 from = center + d * radius * 1.5f;
                if (!col.Raycast(new Ray(from, -d), out RaycastHit hit, radius * 3f)) continue;

                // 반대편에서도 쏴 두께를 잰다 — 얇은 판에서 실이 뒤로 뚫고 나오지 않게.
                float thick = 0f;
                Vector3 back = hit.point - d * radius * 3f;
                if (col.Raycast(new Ray(back, d), out RaycastHit far, radius * 3f))
                    thick = Vector3.Distance(hit.point, far.point);

                holder.sites.Add(new StitchSites.Site
                {
                    localPos = holder.transform.InverseTransformPoint(hit.point),
                    localNormal = holder.transform.InverseTransformDirection(hit.normal),
                    thickness = thick,
                    boneIndex = -1,
                });
            }

            holder.bakedCount = holder.sites.Count;
            return holder.IsBaked ? holder : null;
        }
    }
}
