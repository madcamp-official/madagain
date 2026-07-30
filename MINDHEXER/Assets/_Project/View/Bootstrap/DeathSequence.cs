using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 사망 → 부활 연출. <b>원인 무관 하나로 통일</b>한다(프레스·보스·낙사 전부 같은 연출).
    /// (사망_부활_연출_설계 §1)
    ///
    /// <code>
    /// 0.00        입력 동결. 즉시 암전 → ★ 이 순간 리셋(암전 중이라 튀는 것이 안 보인다)
    /// 0.00~1.50   암전 유지
    /// 1.50~1.70   치지직 아주 크게. 이 사이에 검정이 걷힌다
    ///      1.70   기상 시작(1.5초)
    /// </code>
    ///
    /// <para><b>리셋은 이번 작업에 없다.</b> <see cref="OnResetPoint"/>가 그 자리에 남겨 둔 훅이고,
    /// 스테이지를 실제로 이을 때 계층 범위 리셋(설계 §4)을 여기에 연결한다. 지금은 아무도 구독하지
    /// 않아 아무 일도 일어나지 않는다 — 그게 의도한 상태다.</para>
    ///
    /// <para><b>진입점을 <see cref="GameOverManager"/>에서 옮기지 않는다.</b> 이미
    /// <c>PressCrusher</c>·<c>StageEntranceFlow</c>가 <c>GameOverManager.Trigger</c>를 부르고 있으므로,
    /// 그쪽을 고치는 대신 <see cref="Play"/>를 밖에서 부를 수 있게 열어 둔다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class DeathSequence : MonoBehaviour
    {
        [Header("암전")]
        [Tooltip("암전을 유지하는 시간(초). 리셋은 이 구간이 시작될 때 일어난다.")]
        public float blackTime = 1.5f;

        [Header("치지직")]
        [Tooltip("치지직이 지속되는 시간(초). 짧고 강하게.")]
        public float glitchTime = 0.2f;

        [Tooltip("치지직 최대 강도. 1이면 화면이 가로선으로 뒤덮인다.")]
        [Range(0f, 1f)] public float glitchPeak = 1f;

        [Tooltip("치지직 강도 곡선. 기본은 바로 최대로 치고 끝에 살짝 빠진다.")]
        public AnimationCurve glitchCurve = new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(0.75f, 1f), new Keyframe(1f, 0.4f));

        [Tooltip("치지직 구간에서 검정이 걷히는 비율(0~1 중 어느 지점에 다 걷히는지).\n" +
                 "0.5면 치지직 절반쯤에 검정이 완전히 사라진다 — 치지직 사이로 세상이 돌아온다.")]
        [Range(0.05f, 1f)] public float blackClearAt = 0.5f;

        [Header("참조 (비우면 자동 생성·탐색)")]
        public ScreenVeil veil;
        public WakeUpSequence wakeUp;
        public FirstPersonPlayer body;

        /// <summary>
        /// 암전이 완전히 덮인 순간. <b>여기서 판을 초기화하고 플레이어를 부활 지점으로 옮긴다.</b>
        /// 화면이 검으므로 순간이동이 보이지 않는다.
        /// </summary>
        public event System.Action OnResetPoint;

        /// <summary>연출이 전부 끝난 순간(조작이 풀린 뒤).</summary>
        public event System.Action OnDone;

        /// <summary>지금 사망 연출 중인가. 중복 사망을 막는 데 쓴다.</summary>
        public bool Playing { get; private set; }

        enum Phase { Idle, Black, Glitch, Wake }
        Phase _phase = Phase.Idle;
        float _t;

        static DeathSequence _instance;

        /// <summary>어디서든 이거 하나만 부르면 된다. 없으면 스스로 만든다.</summary>
        public static DeathSequence Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<DeathSequence>();
                    if (_instance == null)
                        _instance = new GameObject("[DeathSequence]").AddComponent<DeathSequence>();
                }
                return _instance;
            }
        }

        void Awake()
        {
            if (_instance == null) _instance = this;
            Resolve();
        }

        void Resolve()
        {
            if (veil == null) veil = GetComponent<ScreenVeil>() ?? gameObject.AddComponent<ScreenVeil>();
            if (wakeUp == null) wakeUp = FindAnyObjectByType<WakeUpSequence>();
            if (wakeUp == null) wakeUp = gameObject.AddComponent<WakeUpSequence>();
            if (body == null) body = FirstPersonPlayer.Instance;
        }

        /// <summary>사망 연출 시작. 이미 진행 중이면 무시한다(같은 프레임에 여러 번 깔리는 경우).</summary>
        public void Play()
        {
            if (Playing) return;
            Resolve();

            Playing = true;
            _phase = Phase.Black;
            _t = 0f;

            // ExternalMotion = 이동·중력을 넘겨받아 그 자리에 세운다(GameOverManager와 같은 방식).
            if (body != null) { body.LookFrozen = true; body.ExternalMotion = true; }

            if (veil != null)
            {
                veil.black = 1f;      // 즉시 암전 — 죽는 순간을 질질 끌지 않는다
                veil.glitch = 0f;
            }

            // 화면이 완전히 검은 지금 판을 초기화한다 — 순간이동·리셋이 튀어도 안 보인다.
            OnResetPoint?.Invoke();
        }

        /// <summary>
        /// 게임 시작 인트로 — 검정에서 시작해 페이드하며 기상한다. 사망 연출의 뒷부분만 쓴다.
        /// </summary>
        public void PlayIntro()
        {
            if (Playing) return;
            Resolve();

            Playing = true;
            _phase = Phase.Glitch;   // 인트로는 암전 유지를 건너뛰고 치지직부터 — 뒷부분만 쓴다
            _t = 0f;

            // ExternalMotion = 이동·중력을 넘겨받아 그 자리에 세운다(GameOverManager와 같은 방식).
            if (body != null) { body.LookFrozen = true; body.ExternalMotion = true; }
            if (veil != null) { veil.black = 1f; veil.glitch = 0f; }
            if (wakeUp != null) wakeUp.Play();
        }

        void Update()
        {
            if (!Playing) return;

            // 동결을 매 프레임 다시 주장한다. 연출 중에 남이 이 플래그를 되돌릴 수 있기 때문이다 —
            // 예를 들어 DevConsole은 닫힐 때 ExternalMotion을 열기 전 값으로 복원한다. 연출이
            // 도는 동안은 이쪽이 권한을 갖는 게 맞다.
            if (body != null) { body.LookFrozen = true; body.ExternalMotion = true; }

            _t += Time.deltaTime;

            switch (_phase)
            {
                case Phase.Black: TickBlack(); break;
                case Phase.Glitch: TickGlitch(); break;
                case Phase.Wake: TickWake(); break;
            }
        }

        /// <summary>그냥 검은 화면을 유지한다. 리셋은 이미 <see cref="Play"/>에서 끝났다.</summary>
        void TickBlack()
        {
            if (_t < blackTime) return;

            _phase = Phase.Glitch;
            _t = 0f;
            if (wakeUp != null) wakeUp.Play();   // 치지직과 겹쳐 기상이 시작된다
        }

        /// <summary>치지직이 크게 터지는 동안 검정이 걷힌다 — 치지직 사이로 세상이 돌아온다.</summary>
        void TickGlitch()
        {
            float u = glitchTime > 1e-4f ? Mathf.Clamp01(_t / glitchTime) : 1f;

            if (veil != null)
            {
                veil.glitch = glitchPeak * (glitchCurve != null ? glitchCurve.Evaluate(u) : 1f);
                veil.black = 1f - Mathf.Clamp01(u / blackClearAt);
            }

            if (u < 1f) return;

            if (veil != null) { veil.glitch = 0f; veil.black = 0f; }
            _phase = Phase.Wake;
            _t = 0f;
        }

        /// <summary>
        /// 기상 <b>본편</b>이 끝날 때까지 기다린다. 그 뒤의 조작 여운(감도 회복)은
        /// <see cref="WakeUpSequence"/>가 혼자 이어 간다 — 여기서 계속 붙잡고 있으면 아래
        /// 매 프레임 동결 재주장이 여운을 못 느끼게 막는다.
        /// </summary>
        void TickWake()
        {
            if (wakeUp != null && !wakeUp.MainDone) return;

            Playing = false;
            _phase = Phase.Idle;

            if (veil != null) veil.Clear();
            if (body != null) { body.LookFrozen = false; body.ExternalMotion = false; }

            OnDone?.Invoke();
        }
    }
}
