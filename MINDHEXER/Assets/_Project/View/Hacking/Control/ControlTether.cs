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
        // ★ §7의 초록(가능)/파랑(장악) 색 구분은 이 게임에서 성립하지 않는다.
        //   전역 ColorAdjustments가 채도 −100으로 씬을 흑백으로 강제하므로(흑백 아트 방침),
        //   어떤 색을 넣어도 회색으로 나온다. 남은 채널은 <b>밝기</b>뿐이라 상태 구분을
        //   아래 발광 세기로 옮겼다.
        [Tooltip("해킹 시도 중 실 색. 흑백 씬이라 사실상 밝기만 의미가 있다.")]
        public Color hackingColor = Color.white;

        [Tooltip("조종 중(장악 성공) 실 색.")]
        public Color controlColor = Color.white;

        [Header("발광 (Bloom 임계값 1.05를 넘겨야 빛난다)")]
        [Tooltip("해킹 시도 중 밝기. 임계값(1.05)을 갓 넘겨 아주 옅게만 번지게 한다.")]
        public float looseGlow = 1.08f;

        [Tooltip("장악해서 팽팽할 때 밝기. 조일수록 밝아져 '걸렸다'가 읽힌다.")]
        public float tightGlow = 1.3f;

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
        [Tooltip("한 번의 해킹에 찌르는 땀 개수. 패턴 획 하나당 (이 값 ÷ 선 개수)만큼 박힌다.\n" +
                 "선 5개에 40이면 획 한 번에 8땀씩.")]
        [Range(1, 80)] public int stitchCount = 40;

        [Header("타이밍")]
        // 전체 그림: 발사 0.5초 + 획 5개 × 0.5초 = <b>아무리 빨라도 매듭에 3초</b>.
        // 실이 날아가는 것도, 한 땀씩 박히는 것도 눈에 보여야 하므로 입력에 즉시 붙지 않는다.
        [Tooltip("실이 방적돌기에서 대상까지 날아가는 시간(초). 이게 끝나야 꿰기 시작한다.\n" +
                 "발사는 순간적이어야 한다 — 길면 굼떠 보인다.")]
        public float launchTime = 0.2f;

        [Tooltip("패턴 획 하나 분량의 땀이 박히는 시간(초). 획 5개면 총 2.5초.\n" +
                 "★ 개수가 아니라 '획 하나 분량'을 기준으로 잡는다 — 땀 개수를 바꿔도\n" +
                 "  체감 속도가 변하지 않는다.")]
        public float strokeStitchTime = 0.5f;

        [Tooltip("이미 해킹했던 것을 다시 잡을 때 전부 박는 데 걸리는 시간(초). 쓰윽.")]
        public float fastStitchTime = 0.9f;

        [Tooltip("해제 — 전부 풀려 회수되기까지의 시간(초). 개수와 무관하게 <b>항상 이만큼</b>.\n" +
                 "★ 이 동안은 해킹이 막힌다(Busy). 1초는 대상을 갈아탈 때 답답해서 0.3으로 줄였다 — " +
                 "회수는 연출이지 대기 시간이 아니다.")]
        public float retractTime = 0.3f;

        [Header("조임")]
        [Tooltip("해킹 시도 중 팽팽함. 낮게 둬야 처짐이 보이고, 장악 순간의 대비가 산다.")]
        [Range(0f, 1f)] public float looseTension = 0.15f;

        [Tooltip("장악은 했지만 <b>조작을 쉬고 있을 때</b> 팽팽함.\n" +
                 "낮아야 두꺼운 실도 땀도 중력에 축 늘어진 게 보인다. 조작하면 1로 올라가 당겨진다.")]
        [Range(0f, 1f)] public float idleTension = 0.25f;

        [Tooltip("조작이 멈춘 뒤 이만큼 지나야 풀리기 시작한다(초). 0이면 조작 사이사이에 떨린다.")]
        public float slackDelay = 0.15f;

        [Tooltip("대상이 이 속도(m/s) 넘게 움직이면 '조작 중'으로 본다.")]
        public float driveSpeedEpsilon = 0.02f;

        [Tooltip("조임이 바뀌는 데 걸리는 시간(초).")]
        public float tensionTime = 0.06f;

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

        /// <summary>
        /// ★ 실이 <b>아직 회수 중</b>인가. 이 동안은 새 해킹을 받으면 안 된다 —
        /// 실이 풀려 돌아오는 도중에 또 나가면 거미가 실을 두 개 문 꼴이 된다.
        /// <c>HackDriver</c>가 해킹 시작 전에 이 값을 본다.
        /// </summary>
        public bool Busy => _retracting;

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
        Vector3 _prevAnchor;            // 조작 중인지 판정하려고 첫 땀 자리의 이동을 본다
        bool _hasAnchor;
        float _driveHold;
        float _launch;                  // 0~1. 실이 대상까지 날아간 정도
        bool _retracting;

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
                _launch = 0f;       // 대상이 바뀌면 발사부터 다시 보여준다
                _tension = 1f;      // 발사 순간부터 빳빳해야 한다 — 서서히 올라오면 힘이 없다
                _fastTimer = -1f;
                BindSites(hk);
            }

            if (from == null || target == null)
            {
                if (_launch <= 0.001f && _progress <= 0.001f)
                {
                    // 이미 다 회수됐다 — 아무것도 안 한다.
                    Active = false;
                    _retracting = false;
                    AimDirection = Vector3.zero;
                    if (thread != null) thread.Hide();
                    _wasHacking = false;
                    _boundTarget = null;
                    _sites = null;
                    _picked.Clear();
                    _hasAnchor = false;
                    _driveHold = 0f;
                    return;
                }

                // ★ 회수는 개수와 무관하게 <b>항상 retractTime</b>이다.
                //   앞 70%에 땀이 역순으로 빠지고, 남은 30%에 실이 거미에게 돌아온다.
                //   이 동안 Busy가 서서 새 해킹이 막힌다.
                _retracting = true;
                _progressTarget = 0f;
                _tensionTarget = 1f;   // 당겨서 뽑는 것이므로 팽팽하다

                float t = Mathf.Max(0.05f, retractTime);
                _progress = Mathf.MoveTowards(_progress, 0f, dt / (t * 0.7f));
                if (_progress <= 0.001f)
                    _launch = Mathf.MoveTowards(_launch, 0f, dt / (t * 0.3f));

                _tension = Mathf.Lerp(_tension, _tensionTarget, 1f - Mathf.Exp(-dt / tensionTime));
                DrawRetract(from);
                return;
            }

            _retracting = false;
            _lastTargetPos = target.position;

            // ── 발사 — 실이 날아가는 게 보여야 한다 ─────────────────────────
            _launch = Mathf.MoveTowards(_launch, 1f, dt / Mathf.Max(0.01f, launchTime));

            bool hacking = !captured;
            bool arrived = _launch >= 0.999f;   // 실이 도착해야 꿰기 시작한다

            // ── 진행도를 무엇이 미는가 ───────────────────────────────────────
            // ★ 분모는 <b>패턴 선 개수</b>이지 땀 개수가 아니다. 둘이 우연히 같을 때는
            //   구분이 안 되지만, 땀을 늘리는 순간 진행도가 끝까지 안 차게 된다.
            int strokes = 0, lines = 0;
            var mg = ResolveMinigame();
            if (mg != null)
            {
                if (mg.Input != null) strokes = mg.Input.StrokeCount;
                if (mg.Target != null) lines = mg.Target.LineCount;
            }
            if (lines <= 0) lines = 5;   // 전 대상 5선으로 통일됨

            if (hacking)
            {
                _progressTarget = arrived ? Mathf.Clamp01(strokes / (float)lines) : 0f;
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
                    if (!arrived) _fastTimer = 0f;
                    else
                    {
                        if (_fastTimer < 0f) _fastTimer = 0f;
                        _fastTimer += dt;
                    }
                    _progressTarget = fastStitchTime > 0.0001f
                        ? Mathf.Clamp01(_fastTimer / fastStitchTime)
                        : 1f;
                }
            }

            // ── 조작 중인가 ─────────────────────────────────────────────────
            // HackDriver를 건드리지 않고 알아내려면 <b>실이 실제로 붙어 있는 지점</b>이
            // 움직이는지를 보면 된다. 대상 루트가 아니라 첫 땀 자리를 봐야 한다 —
            // 프레스·피스톤은 루트가 가만히 있고 자식 파츠만 움직이기 때문이다.
            Vector3 anchor = (_sites != null && _picked.Count > 0)
                           ? _sites.WorldPos(_sites.sites[_picked[0]])
                           : target.position;
            if (_hasAnchor && dt > 0f)
            {
                float speed = (anchor - _prevAnchor).magnitude / dt;
                if (speed > driveSpeedEpsilon) _driveHold = slackDelay;
                else _driveHold -= dt;
            }
            _prevAnchor = anchor;
            _hasAnchor = true;

            // ★ 발사 중에는 무조건 팽팽하다 — 실이 빳빳하게 쭉 나가서 팍 박혀야 힘이 있다.
            //   늘어지는 건 <b>박힌 뒤</b>다.
            _tensionTarget = !arrived ? 1f
                           : hacking ? looseTension
                                     : (_driveHold > 0f ? 1f : idleTension);

            // 기준은 '획 하나 분량'이다 — 땀 개수를 바꿔도 체감 속도가 안 변한다.
            // 재조종만 예외로 정해진 시간 안에 전부 박는다("쓰윽").
            bool fastPath = !hacking && !_wasHacking;
            float rate = fastPath
                ? 1f / Mathf.Max(0.01f, fastStitchTime)
                : (1f / lines) / Mathf.Max(0.01f, strokeStitchTime);

            _wasHacking = hacking;

            Step(dt, rate);
            DrawWith(from, target, captured);
        }

        /// <param name="rate">초당 진행도 증가량(0~1 기준).</param>
        void Step(float dt, float rate)
        {
            // ★ 일정 속도로 민다. 목표에 즉시 따라붙지 <b>않는다</b> —
            //   획을 이어도 실은 늦게 따라오면서 한 땀씩 박히는 게 보여야 한다.
            //   그 지연이 곧 애니메이션이다.
            _progress = Mathf.MoveTowards(_progress, _progressTarget, Mathf.Max(0.0001f, rate) * dt);

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

            // 조임이 곧 밝기다 — 흑백 씬에서 상태를 알리는 유일한 채널.
            // 알파는 1로 둔다(불투명 큐라 쓰이지 않지만 곱해서 흐트러뜨릴 이유가 없다).
            float g = Mathf.Lerp(looseGlow, tightGlow, _tension);
            var hdr = new Color(color.r * g, color.g * g, color.b * g, 1f);

            Vector3 dir = SpinneretDir(a, fallbackEnd);
            thread.Draw(a, dir, _sites, _picked, fallbackEnd,
                        _launch, _progress, _tension, hdr);

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
            Vector3 aim = cam != null ? cam.transform.forward : transform.forward;

            // ★ 레일 세트는 한 가닥만 — 콜라이더가 세로로 아주 길어서(가동범위를 덮으려고) 기본
            //   stitchCount대로 여러 자리를 한꺼번에 뽑으면 실이 여러 가닥 동시에 튀어나온 것처럼
            //   보인다. 다른 대상(경비병·프레스 등)은 그대로 여러 가닥이 맞다.
            int count = hk.kind == HackableKind.RailCarrier ? 1 : stitchCount;
            _sites.Pick(count, viewer, _seed, _picked);

            // 첫 발사는 조준점 근처에 꽂혀야 "내가 쏜 것"으로 읽힌다. 나머지 순서는 그대로 둔다.
            _sites.SortAimFirst(_picked, viewer, aim);
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
                    spaceIndex = -1,
                });
            }

            holder.bakedCount = holder.sites.Count;
            return holder.IsBaked ? holder : null;
        }
    }
}
