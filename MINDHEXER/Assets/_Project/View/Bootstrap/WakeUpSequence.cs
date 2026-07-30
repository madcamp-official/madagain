using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 기상 연출 — <b>아주 높은 곳에서 바닥에 착지한 순간</b>. 1초 컷.
    /// 게임 시작 인트로와 부활이 이걸 같이 쓴다. (사망_부활_연출_설계 §3)
    ///
    /// <para>실제로 떨어지지는 않는다. <b>착지 순간부터</b>를 보여 준다.</para>
    ///
    /// <para><b>① 충격 → ② 눌림 → ③ 복귀 → ④ 정착</b>의 4단이고, ③에서 구동 방식이 바뀐다.
    /// <code>
    /// ① 0.00~0.05  순간 급강하 + 고주파 흔들림 + FOV 펀치 + 롤 킥
    /// ② 0.05~0.20  최저점에서 머문다        ← ★ 이 hold가 무게를 만든다
    /// ③ 0.20~      스프링에 인계해 튀어 오른다
    /// ④      ~1.00 2차 진동이 잦아든다
    /// </code></para>
    ///
    /// <para><b>왜 곡선과 스프링을 둘 다 쓰는가</b>
    /// <list type="bullet">
    /// <item><b>스프링은 hold를 못 만든다.</b> 목표를 주면 곧장 그리로 간다. ②의 "눌린 채 멈춤"은
    ///       손으로 잡은 곡선만 만들 수 있다. 스프링 하나로 했더니 트램폴린처럼 밋밋했다.</item>
    /// <item><b>곡선은 잔여 진동을 못 만든다.</b> ④를 키프레임으로 잡으면 프레임률에 따라 성격이
    ///       변하고 손으로 잡을 양이 많다.</item>
    /// </list>
    /// 그래서 ③ 진입 순간 <b>그때의 침하값을 스프링에 넘기고</b>(<c>SnapTo</c> + 목표 0) 손을 뗀다.
    /// 눌린 상태에서는 수직 속도가 0에 가까우므로 속도까지 넘길 필요가 없다 — 다리를 펴며
    /// 정지 상태에서 밀어 올리는 것이 실제 순서다.</para>
    ///
    /// <para><b>기존 <see cref="MotionFeel.OnLand"/>를 쓰지 않는다.</b> 그건 평범한 점프 착지용이라
    /// <c>landDipMax</c>가 <b>0.22m</b>로 묶여 있고 롤이 2.5°이며, 단조 곡선이라 <b>머무는 구간이 없다.</b>
    /// 배율로 키워도 hold가 없어 트램폴린이 된다.</para>
    ///
    /// <para><b>VR 처리가 채널 선택으로 해결된다</b> — 침하·흔들림을
    /// <see cref="MotionFeel.ExternalPosOffset"/>에 넣으면 <c>vrPositionScale</c>(기본 0.25)을 타서
    /// 자동으로 25%가 되고, 롤은 <see cref="MotionFeel.ExternalRoll"/>이 <c>vrRollScale</c>(기본 0)을
    /// 타서 사라진다. 수직 가속은 VR 멀미의 주범이라 이게 중요하다. FOV 펀치만 직접 끈다
    /// (VR에서 FOV를 건드리면 렌즈 왜곡과 안 맞는다).</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class WakeUpSequence : MonoBehaviour
    {
        [Header("① 충격")]
        [Tooltip("머리가 최저점까지 내려가는 시간(초). 길면 '내려앉음'이 아니라 '주저앉음'이 된다.")]
        public float impactTime = 0.05f;

        [Tooltip("최저점 깊이(m). 기존 점프 착지 상한(0.22)의 4~5배여야 높은 곳이 읽힌다.")]
        public float dipDepth = 1f;

        [Tooltip("급강하 모양. 기본은 앞이 아주 빠른 곡선 — 충격은 순간이어야 한다.")]
        public AnimationCurve impactCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 6f), new Keyframe(1f, 1f, 0.2f, 0.2f));

        [Header("① 충격 — 흔들림·FOV·롤")]
        [Tooltip("고주파 흔들림 지속(초). '쿵'의 정체다. 짧아야 한다.")]
        public float shakeTime = 0.07f;

        [Tooltip("흔들림 진폭(m).")]
        public float shakeAmplitude = 0.05f;

        [Tooltip("흔들림 빈도(Hz). 높아야 '진동'이 아니라 '충격'으로 읽힌다.")]
        public float shakeFrequency = 38f;

        [Tooltip("FOV 펀치(도). 충격에 순간 넓어졌다 돌아온다. ★ VR에서는 자동으로 0이 된다.")]
        public float fovPunch = 8f;

        [Tooltip("FOV가 돌아오는 시간(초).")]
        public float fovPunchTime = 0.25f;

        [Tooltip("롤 킥(도) — 한쪽 발이 먼저 닿으니 좌우 비대칭이다.")]
        public float rollDeg = 10f;

        [Tooltip("롤 킥이 잦아드는 시간(초).")]
        public float rollTime = 0.45f;

        [Tooltip("롤 킥이 좌우로 오가는 횟수.")]
        public float rollCycles = 1.75f;

        [Tooltip("먼저 닿는 발 쪽. −1이면 반대.")]
        public float rollSign = 1f;

        [Header("② 눌림 (★ 무게를 만드는 구간)")]
        [Tooltip("최저점에서 머무는 시간(초). 0이면 무게가 사라진다.")]
        public float holdTime = 0.15f;

        [Header("③④ 복귀·정착")]
        [Tooltip("복귀 스프링 진동수(Hz).")]
        public float springFrequency = 7f;

        [Tooltip("복귀 스프링 감쇠비. ★ 1보다 낮아야 지나쳤다 되돌아온다 = 반동.")]
        [Range(0.05f, 1.5f)] public float springDamping = 0.45f;

        [Header("눈꺼풀 — 충격에 번쩍 뜬다")]
        [Tooltip("가로 = 컷 진행(0~1), 세로 = 뜬 정도.")]
        public AnimationCurve eyelidCurve = new AnimationCurve(
            new Keyframe(0f,    0f),
            new Keyframe(0.10f, 0.95f),   // 충격에 번쩍
            new Keyframe(0.22f, 0.60f),   // 한 번 찡그림
            new Keyframe(0.40f, 1f),
            new Keyframe(1f,    1f));

        [Tooltip("다 뜬 뒤에도 잠깐 남는 코너 어둠. 여운 동안 걷힌다.")]
        [Range(0f, 1f)] public float residualVignette = 0.35f;

        [Header("시선 (PC 전용 — VR은 HMD가 소유한다)")]
        [Tooltip("충격 순간 아래를 보는 각(도).")]
        public float startPitchDown = 30f;

        [Tooltip("고개가 들리는 데 걸리는 시간(초). 몸보다 늦게 들려야 무겁다.")]
        public float pitchRecoverTime = 0.4f;

        [Header("팔 — 짚었다 제자리로")]
        public float armDelay = 0.04f;
        public float armRiseTime = 0.45f;

        [Tooltip("팔의 시작 오프셋(m, 음수 = 화면 아래).")]
        public float armDrop = -0.3f;

        [Header("길이")]
        [Tooltip("컷 길이(초). 여기서 조작이 풀린다.")]
        public float mainTime = 1f;

        [Tooltip("컷 뒤 감도·잔여 비네트가 돌아오는 시간(초).")]
        public float recoverTime = 0.5f;

        [Range(0.05f, 1f)] public float recoverStartScale = 0.5f;

        [Header("참조 (비우면 자동 탐색)")]
        public ScreenVeil veil;
        public MouseLook look;
        public FirstPersonPlayer body;
        public MotionFeel feel;

        [Tooltip("뷰모델 루트. 비우면 이름 'Viewmodel'로 찾는다.")]
        public Transform viewmodelRoot;

        /// <summary>연출이 진행 중인가(여운 포함).</summary>
        public bool Playing { get; private set; }

        /// <summary>컷이 끝났는가. 여기서 조작이 풀린다 — 여운은 움직일 수 있어야 느껴진다.</summary>
        public bool MainDone => !Playing || _t >= mainTime;

        readonly PdApproach _spring = new PdApproach();
        readonly PdApproach _pitch = new PdApproach();

        System.Action _onDone;
        float _t, _seed;
        float _targetYaw, _armHomeY;
        bool _armHomeCached, _handedOff;

        /// <summary>②가 끝나 스프링에 넘기는 시각.</summary>
        float HandoffAt => impactTime + holdTime;

        float Total => mainTime + recoverTime;

        void Awake() => Resolve();

        void Resolve()
        {
            if (veil == null) veil = FindAnyObjectByType<ScreenVeil>();
            if (look == null) look = FindAnyObjectByType<MouseLook>();
            if (body == null) body = FirstPersonPlayer.Instance;
            if (feel == null) feel = FindAnyObjectByType<MotionFeel>();
            if (viewmodelRoot == null)
            {
                var t = FindByName(ViewmodelCamera.ViewmodelRootName);
                if (t != null) viewmodelRoot = t;
            }
        }

        static Transform FindByName(string n)
        {
            var all = FindObjectsByType<Transform>(FindObjectsInactive.Include);
            for (int i = 0; i < all.Length; i++)
                if (all[i].name == n) return all[i];
            return null;
        }

        /// <summary>연출 시작. 이미 진행 중이면 무시한다(겹쳐 부르면 오프셋이 두 겹으로 쌓인다).</summary>
        public void Play(System.Action onDone = null)
        {
            if (Playing) return;
            Resolve();

            _onDone = onDone;
            _t = 0f;
            _seed = Random.Range(0f, 500f);   // 흔들림이 매번 똑같지 않게. 성격은 값이 정한다
            _handedOff = false;
            Playing = true;

            if (body != null) body.LookFrozen = true;

            // 침하·흔들림·롤·FOV가 전부 MotionFeel 채널을 지난다. 없으면 카메라 연출이 통째로
            // 사라지는데 화면상 아무 표시가 없어 "왜 안 보이지"로 시간을 버린다. 그래서 알린다.
            if (feel == null)
                Debug.LogWarning("[기상] MotionFeel을 찾지 못해 카메라 연출(침하·흔들림·롤·FOV)이 " +
                                 "전부 빠집니다. 눈꺼풀과 팔만 동작합니다.", this);

            _spring.frequency = springFrequency;
            _spring.damping = springDamping;

            if (look != null)
            {
                _targetYaw = look.Yaw;   // 부활 지점이 정한 방향을 뒤집지 않는다
                _pitch.frequency = 1f / Mathf.Max(0.02f, pitchRecoverTime);
                _pitch.damping = 0.65f;
                _pitch.SnapTo(startPitchDown);
                _pitch.Target = 0f;
                look.SetLook(_targetYaw, startPitchDown);
            }

            CacheArmHome();
            Apply(0f);
        }

        /// <summary>즉시 끝내고 모든 오프셋을 원복한다(스킵·강제 종료).</summary>
        public void Finish()
        {
            if (!Playing) return;
            Playing = false;

            if (feel != null)
            {
                feel.ExternalPosOffset = Vector3.zero;
                feel.ExternalFovOffset = 0f;
                feel.ExternalRoll = 0f;
            }
            if (veil != null) { veil.eyelidOpen = 1f; veil.eyelidVignette = 0f; }
            if (look != null) { look.SetLook(_targetYaw, 0f); look.SensScale = 1f; }
            if (viewmodelRoot != null && _armHomeCached)
            {
                Vector3 lp = viewmodelRoot.localPosition;
                lp.y = _armHomeY;
                viewmodelRoot.localPosition = lp;
            }
            if (body != null) body.LookFrozen = false;

            var cb = _onDone; _onDone = null;
            cb?.Invoke();
        }

        /// <summary>
        /// 뷰모델 원래 높이를 최초 1회만 기억한다. 연출 중에 잡으면 내려간 위치가 원래 위치가 된다
        /// (보스 머리 자세에서 겪은 사고와 같은 종류).
        /// </summary>
        void CacheArmHome()
        {
            if (_armHomeCached || viewmodelRoot == null) return;
            _armHomeY = viewmodelRoot.localPosition.y;
            _armHomeCached = true;
        }

        void LateUpdate()
        {
            if (!Playing) return;
            _t += Time.deltaTime;
            Apply(Time.deltaTime);
            if (_t >= Total) Finish();
        }

        void Apply(float dt)
        {
            float t = _t;
            bool main = t < mainTime;
            float u = mainTime > 1e-4f ? Mathf.Clamp01(t / mainTime) : 1f;

            // ── ①② 곡선 → ③④ 스프링 ──
            float dip = DipAt(t, dt);

            // ── ① 고주파 흔들림 ──
            Vector3 shake = Vector3.zero;
            if (t < shakeTime && shakeTime > 1e-4f)
            {
                float decay = 1f - t / shakeTime;
                float amp = shakeAmplitude * decay * decay;   // 끝에서 빠르게 잦아든다
                shake = new Vector3(Noise(0.13f), Noise(7.31f) * 0.6f, Noise(19.7f)) * amp;
            }

            // ── ① 롤 킥 — 감쇠 진동. 한쪽 발이 먼저 닿으니 한쪽으로 먼저 꺾인다 ──
            float roll = 0f;
            if (t < rollTime && rollTime > 1e-4f)
            {
                float ru = t / rollTime;
                roll = Mathf.Sign(rollSign) * rollDeg
                     * Mathf.Sin(ru * rollCycles * Mathf.PI * 2f) * (1f - ru) * (1f - ru);
            }

            // ── ① FOV 펀치 ──
            // VR에서는 끈다 — FOV를 건드리면 카드보드 렌즈 왜곡 보정과 안 맞아 멀미가 심해진다.
            float fov = 0f;
            if (!VrMode.Enabled && t < fovPunchTime && fovPunchTime > 1e-4f)
            {
                float fu = t / fovPunchTime;
                fov = fovPunch * (1f - fu) * (1f - fu);
            }

            if (feel != null)
            {
                // ★ ExternalPosOffset을 쓰는 이유: 이 채널이 vrPositionScale(기본 0.25)을 타서
                //   VR에서 수직 진폭이 자동으로 25%가 된다. 수직 가속은 VR 멀미의 주범이다.
                feel.ExternalPosOffset = new Vector3(shake.x, dip + shake.y, shake.z);
                feel.ExternalRoll = roll;          // vrRollScale(기본 0)로 VR에서 자동 제거
                feel.ExternalFovOffset = fov;
            }

            // ── 눈꺼풀 ──
            if (veil != null)
            {
                if (main)
                {
                    veil.eyelidOpen = eyelidCurve != null ? Mathf.Clamp01(eyelidCurve.Evaluate(u)) : u;
                    veil.eyelidVignette = residualVignette;
                }
                else
                {
                    float r = recoverTime > 1e-4f ? Mathf.Clamp01((t - mainTime) / recoverTime) : 1f;
                    veil.eyelidOpen = 1f;
                    veil.eyelidVignette = Mathf.Lerp(residualVignette, 0f, r);
                }
            }

            // ── 시선 (PC만) ──
            // VR에는 MouseLook을 아예 붙이지 않으므로 look이 null이고 이 블록이 자동으로 빠진다.
            if (look != null)
            {
                if (main)
                {
                    _pitch.Step(dt);
                    look.SetLook(_targetYaw, _pitch.Value);
                    look.SensScale = recoverStartScale;
                }
                else
                {
                    // ★ 여기서는 SetLook을 부르지 않는다 — 조작이 풀렸으므로 계속 쓰면 입력과 싸운다.
                    float r = recoverTime > 1e-4f ? Mathf.Clamp01((t - mainTime) / recoverTime) : 1f;
                    look.SensScale = Mathf.Lerp(recoverStartScale, 1f, r * r);
                }
            }

            if (!main && body != null && body.LookFrozen) body.LookFrozen = false;

            // ── 팔 ──
            if (viewmodelRoot == null || !_armHomeCached) return;

            float armT = t - armDelay;
            float au = armRiseTime > 1e-4f ? Mathf.Clamp01(armT / armRiseTime) : 1f;
            if (armT < 0f) au = 0f;

            Vector3 vp = viewmodelRoot.localPosition;
            vp.y = _armHomeY + Mathf.LerpUnclamped(armDrop, 0f, au * au * (3f - 2f * au));
            viewmodelRoot.localPosition = vp;
        }

        /// <summary>
        /// ①② 구간은 곡선이 침하를 직접 지정하고, ③ 진입 순간 스프링에 넘긴 뒤 손을 뗀다.
        ///
        /// <para>넘길 때 속도를 함께 주지 않는다(<c>SnapTo</c>가 0으로 만든다) — 눌린 상태에서는
        /// 수직 속도가 0에 가깝고, 거기서 다리를 펴며 밀어 올리는 것이 실제 순서다.</para>
        /// </summary>
        float DipAt(float t, float dt)
        {
            float depth = -Mathf.Abs(dipDepth);

            if (t < impactTime && impactTime > 1e-4f)
            {
                float iu = Mathf.Clamp01(t / impactTime);
                float k = impactCurve != null ? impactCurve.Evaluate(iu) : iu;
                return depth * k;                      // ① 급강하
            }

            if (t < HandoffAt) return depth;           // ② 최저점에서 머문다

            if (!_handedOff)                           // ③ 인계 — 한 번만
            {
                _handedOff = true;
                _spring.SnapTo(depth);
                _spring.Target = 0f;
            }

            _spring.Step(dt);                          // ③④ 스프링이 튀어 올리고 정착시킨다
            return _spring.Value;
        }

        /// <summary>−1~+1 Perlin. 레인을 다르게 줘 축끼리 상관되지 않게 한다.</summary>
        float Noise(float lane) =>
            (Mathf.PerlinNoise((_t + _seed) * shakeFrequency, lane) - 0.5f) * 2f;
    }
}
