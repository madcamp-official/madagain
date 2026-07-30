using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 절차적 카메라 연출 레이어 — 동작 종류별로 이벤트가 갈라져 있다:
    ///  · <see cref="OnJumpLaunch"/> — 도약 발구름: 아래로 훅 가라앉았다 풀리는 다운킥. 도약 높이 비례.
    ///  · <see cref="OnLand"/> — 순수 점프/낙하 착지: <b>착지 순간 수직 속도</b> 비례 침하.
    ///    "얼마나 높이 올라갔나"가 아니라 "얼마나 빨리 떨어지고 있었나"라서, 높은 곳에
    ///    올라갔다고 큰 흔들림이 나오는 일이 없다.
    ///  · <see cref="OnMantleFinish"/> — 잡고 올라가기 완료: 높이 무관 고정 소량의 안착.
    ///  · 당김 롤 스웨이 — 한 팔씩 번갈아 당기는 좌우 교차 기울임. AutoTraversal이 구동.
    ///  · <see cref="OnCarried"/> — 지하철 스웨이. 레일·피스톤·프레서 등 스크립트가 태우는 이동뿐 아니라
    ///    <see cref="FirstPersonPlayer"/>의 "의도치 않은 밀림 감지"(레일에 붙은 오브젝트가 부딪혀 유니티의
    ///    CharacterController 자동 depenetration으로 밀리는 경우 등, 소스 불문)까지 전부 이 한 관문을 지난다.
    ///    옆으로 실려가는 동안 반대로 살짝 버티는 롤, 전진/후진엔 FOV 축소/확대 — 멈추면
    ///    <see cref="PdApproach"/>(댐핑&lt;1)가 반대쪽으로 넘어갔다 흔들리며 정착한다. 물리 충돌처럼
    ///    델타가 프레임마다 들쭉날쭉할 땐 <see cref="carrySmoothTime"/>이 완화하고, 세게 부딪히면
    ///    <see cref="carryImpactFrequency"/>로 반응성을 올려 짧게 처리한다. 실제 게임플레이 위치는
    ///    호출자가 이미 정확히 반영했으므로, 여기선 <b>화면만</b> 잠깐 뒤처졌다 따라잡는 카메라 전용
    ///    지연(<see cref="carryPosLagMax"/>)을 더해 순간 보정이 뚝뚝 끊겨 보이는 걸 흡수한다.
    ///
    /// <para>적용 방식: 위치 오프셋은 LateUpdate에서 직전 프레임 적용분을 되돌리고 새로 더한다
    /// (CharacterController와 안 싸움). 롤은 이 컴포넌트가 회전을 건드리지 않고
    /// <see cref="CurrentRoll"/>만 계산 — FirstPersonPlayer가 시점 회전을 쓸 때 합성한다.</para>
    ///
    /// <para><b>VR</b>: 인위적 롤은 멀미 유발 1순위라 <see cref="vrRollScale"/> 기본 0.
    /// 위치 성분도 <see cref="vrPositionScale"/>로 축소. 실기에서 견딜 만하면 올린다.</para>
    /// </summary>
    [DefaultExecutionOrder(-100)]   // 오프셋 되돌리기가 모든 위치 구동자보다 먼저 돌아야 한다
    public class MotionFeel : MonoBehaviour
    {
        [Header("점프 발구름(다운킥)")]
        [Tooltip("도약 높이 1m당 침하 깊이(m). ※업킥과 크기·지속이 비슷하면 서로 상쇄돼 아무것도 안 느껴진다.")]
        public float launchDipPerMeter = 0.05f;
        public float launchDipMax = 0.12f;
        public float launchDuration = 0.18f;
        public AnimationCurve launchCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 4f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0f, -1.5f, 0f));

        [Header("점프 발구름(업킥) — 위로 '탁' 튀었다 원복")]
        [Tooltip("높이와 무관하게 항상 붙는 킥(m). 기본 0 — 상하 흔들림은 멀미 유발이라 롤 킥을 먼저 쓴다.")]
        public float launchKickBase = 0f;
        [Tooltip("도약 높이 1m당 추가로 위로 튀는 양(m).")]
        public float launchKickPerMeter = 0f;
        public float launchKickMax = 0.26f;
        [Tooltip("킥은 침하보다 짧아야 '탁' 하고 튄다.")]
        public float launchKickDuration = 0.15f;
        public AnimationCurve launchKickCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 9f), new Keyframe(0.22f, 1f), new Keyframe(1f, 0f, -1.8f, 0f));

        [Header("착지 (강도 = 착지 순간 낙하 속도)")]
        [Tooltip("낙하 속도 1m/s당 침하 깊이(m).")]
        public float landDipPerSpeed = 0.012f;
        public float landDipMax = 0.22f;
        [Tooltip("이 속도(m/s) 미만의 착지는 연출 없음. ※접지 중 수직 속도가 -2로 고정되므로 " +
                 "2 이하로 두면 접지가 깜빡일 때마다 연출이 터져 화면이 계속 흔들린다.")]
        public float landMinSpeed = 4.5f;
        public float landDuration = 0.3f;
        public AnimationCurve landCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 5f), new Keyframe(0.25f, 1f), new Keyframe(0.6f, 0.12f), new Keyframe(1f, 0f));

        [Header("착지(업킥) — 충격 뒤 위로 '탁'")]
        [Tooltip("낙하 속도 1m/s당 위로 튀는 양(m). 기본 0 — 롤 킥을 먼저 쓴다.")]
        public float landKickPerSpeed = 0f;
        public float landKickMax = 0.26f;
        public float landKickDuration = 0.18f;
        public AnimationCurve landKickCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 9f), new Keyframe(0.22f, 1f), new Keyframe(1f, 0f, -1.8f, 0f));

        [Header("잡고 올라가기 안착 (높이 무관 고정)")]
        public float settleDip = 0.05f;
        public float settleDuration = 0.22f;
        public AnimationCurve settleCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 4f), new Keyframe(0.4f, 1f), new Keyframe(1f, 0f, -1f, 0f));

        [Header("롤 킥 — 도약·착지 때 좌우로 '파박'")]
        [Tooltip("도약 발구름 시 좌우 기울임 진폭(도). 등반 당김의 교차 기울임과 같은 계열.")]
        public float launchRollDeg = 3.5f;
        [Tooltip("지속(초). 짧을수록 '파박'.")]
        public float launchRollDuration = 0.26f;
        [Tooltip("그 사이 좌우로 오가는 횟수. 1.25면 한 번 크게 갔다 반대로 살짝.")]
        public float launchRollCycles = 1.25f;

        [Tooltip("착지 시 좌우 기울임 진폭(도).")]
        public float landRollDeg = 2.5f;
        public float landRollDuration = 0.3f;
        public float landRollCycles = 1.25f;

        [Header("실려가기 — 지하철 스웨이. 발판·피스톤·프레서 등 강제 이동 전부 공통")]
        [Tooltip("옆으로 실려가는 동안(오른쪽이면) 카메라가 그 반대(왼쪽)로 살짝 버티는 각도 — " +
                 "속도 1m/s당 도(°). 멈추면 스프링이 반대쪽으로 넘어갔다(오른쪽으로 더 꺾임) 흔들리며 정착한다.")]
        public float carryRollGain = 1.1f;
        public float carryRollMax = 4f;

        [Tooltip("전진 실려가는 동안 좁아지는 FOV(도) — 속도 1m/s당. 후진이면 자동으로 반대(넓어짐)로 나온다. " +
                 "멈추면 넘어갔다(더 넓어짐) 흔들리며 정착 — 롤과 완전히 같은 스프링, 축만 다르다.")]
        public float carryFovGain = 0.7f;
        public float carryFovMax = 5f;

        [Tooltip("실려가기 스프링 반응성(완만한 승차 기준). 클수록 빨리 따라붙는다.")]
        public float carryFrequency = 9f;

        [Tooltip("실려가기 감쇠비. 1 미만이어야 정지 시 반대로 넘어갔다 흔들리며 정착한다(지하철 느낌의 핵심).")]
        [Range(0.1f, 1f)] public float carryDamping = 0.35f;

        [Tooltip("입력 속도 스무딩 시간(초). 레일 위 오브젝트에 부딪히는 것처럼 델타가 " +
                 "프레임마다 들쭉날쭉하게 들어올 때(물리 충돌 보정) 그 노이즈가 그대로 롤·FOV에 새어나가 " +
                 "화면이 덜덜 떨리는 걸 막는다. 0이면 끔(원본 그대로).")]
        public float carrySmoothTime = 0.08f;

        [Tooltip("이 속도(m/s)를 넘으면 '갑자기 세게 부딪힌 것'으로 보고 반응성을 " +
                 "carryImpactFrequency까지 올린다 — 완만한 승차보다 짧게 홀드했다 빨리 정착한다.")]
        public float carryImpactSpeed = 5f;

        [Tooltip("강한 충돌일 때 쓰는 반응성. carryFrequency보다 커야 '짧게' 느껴진다.")]
        public float carryImpactFrequency = 18f;

        [Tooltip("카메라 흡수 지연 상한(m). 실제 위치는 안 건드리고 화면만 이만큼 뒤처졌다 따라잡는다.")]
        public float carryPosLagMax = 0.15f;

        [Tooltip("화면이 뒤처진 만큼 따라잡는 속도. 클수록 빨리 따라잡는다(짧은 지연).")]
        public float carryPosLagDecay = 12f;

        PdApproach _carryRoll = new PdApproach();
        PdApproach _carryFov = new PdApproach();
        Vector3 _carryAccum;      // 이번 프레임 들어온 실려가기 델타 합(여러 발판 중첩 대비)
        Vector3 _carrySmoothed;   // 스무딩된 실려가기 속도 — 노이즈 낀 물리 충돌 델타 완화용
        Vector3 _posLag;          // 카메라 전용 흡수 지연(실제 게임플레이 위치는 무관 — 순수 시각용)
        float _baseFov = -1f;
        Camera _cam;

        [Header("VR 감쇠")]
        [Tooltip("VR에서 위치 오프셋에 곱하는 배율.")]
        [Range(0f, 1f)] public float vrPositionScale = 0.25f;
        [Tooltip("VR에서 롤에 곱하는 배율. 인위적 롤 = 멀미 1순위라 기본 0.")]
        [Range(0f, 1f)] public float vrRollScale = 0f;

        /// <summary>이번 프레임의 롤(도).</summary>
        public float CurrentRoll { get; private set; }

        /// <summary>
        /// 이 컴포넌트가 <b>자기 트랜스폼의 회전을 직접 소유</b>하는가.
        ///
        /// <para><c>[CamRig]</c>(카메라의 부모)에 붙었으면 true — 롤을 자기가 적용한다. 그러면
        /// PC(<see cref="MouseLook"/>)든 VR(TrackedPoseDriver)든 <b>시점 드라이버를 건드리지 않고</b>
        /// 롤 연출이 걸린다. 지금까지 VR에서 롤이 아예 안 걸렸던 이유가 이것이다 —
        /// 롤을 합성해 주던 게 <see cref="MouseLook"/> 하나뿐이었는데 VR엔 그게 없다.</para>
        ///
        /// <para><b>기본 false.</b> 자동 판정(예: "카메라가 안 붙어 있으면 리그다")은 위험하다 —
        /// 손으로 조립한 씬에서는 <see cref="MotionFeel"/>이 <b>몸</b>에 직접 붙어 있어서 그 판정이
        /// 참이 되고, 그러면 몸을 굴려 버린다(ViewmodelStudio에서 실제로 걸렸다).
        /// 그래서 <see cref="GameBoot"/>이 <c>[CamRig]</c>에만 명시적으로 켠다.</para>
        /// </summary>
        public bool OwnsRotation => ownsRotation;

        [Tooltip("이 트랜스폼의 회전을 직접 소유한다(롤을 여기에 적용). ★ 전용 카메라 리그([CamRig])에만 " +
                 "켤 것 — 몸이나 카메라에 붙은 경우 켜면 시점 소유자와 싸운다. GameBoot이 자동으로 켠다.")]
        public bool ownsRotation = false;

        /// <summary>
        /// 다른 연출이 얹는 <b>FOV 가산분</b>(도). 빙의 흡입 줌(<see cref="PossessionTransition"/>) 등이 쓴다.
        ///
        /// <para><b>왜 여기로 받는가</b>: 카메라의 FOV와 위치를 실제로 기록하는 곳은 이 컴포넌트
        /// <b>하나뿐</b>이어야 한다. 바깥에서 <c>camera.fieldOfView</c>를 직접 쓰면 이쪽 계산이 매 프레임
        /// 덮어써서 조용히 무효가 된다(카메라 소유권 사고와 같은 종류). 그래서 값만 받아 합산한다.</para>
        ///
        /// <para>쓰는 쪽이 <b>0으로 되돌릴 책임</b>을 진다 — 여기서 자동으로 감쇠시키지 않는다.</para>
        /// </summary>
        [System.NonSerialized] public float ExternalFovOffset;

        /// <summary>다른 연출이 얹는 <b>월드 위치 가산분</b>(m). 빙의 흡입 돌리 등. 규약은 위와 같다.</summary>
        [System.NonSerialized] public Vector3 ExternalPosOffset;

        struct Fx { public bool active; public float amp, dur, t; }
        Fx _launch, _land, _settle;       // 아래로(침하)
        Fx _launchKick, _landKick;        // 위로(킥)

        // 당김 스웨이 상태(AutoTraversal 구동)
        bool _swayActive;
        float _swayCycles, _swayAmp, _swaySign, _swayProgress;

        // 롤 킥 — 감쇠 진동 하나로 좌우를 훑는다. 방향은 번갈아 바뀐다(같은 쪽만 기울면 금방 티가 난다).
        Fx _rollKick;
        float _rollCycles, _rollSign = 1f;

        Vector3 _appliedPos;

        /// <summary>
        /// 두 경로가 부른다 — <see cref="RailPlatform.Carry"/>(발판 위에 탄 것을 실어 나름)와
        /// <see cref="FirstPersonPlayer"/>의 "의도치 않은 밀림 감지"(실제 이동량 − 의도한 이동량 = 물리
        /// 충돌로 밀린 잔차, 소스 불문). 델타만 누적해두고 실제 스프링 목표값 계산은 LateUpdate에서
        /// 한 번에 한다(여러 소스가 겹쳐도 합산). 카메라 흡수 지연도 같이 시작한다 — 실제 게임플레이
        /// 위치는 이미 정확하게 반영됐으니(호출자가 그렇게 함) 이 지연은 순수하게 화면만 부드럽게 하는 용도다.
        /// </summary>
        public void OnCarried(Vector3 worldDelta)
        {
            _carryAccum += worldDelta;
            _posLag -= worldDelta;
            if (_posLag.magnitude > carryPosLagMax) _posLag = _posLag.normalized * carryPosLagMax;
        }

        /// <summary>
        /// 도약 발구름. <paramref name="scale"/>(0~1)는 <b>도약 크기 배율</b>이다 —
        /// 침하·킥은 rise에 비례하지만 <b>롤은 원래 크기와 무관해서</b>, 작은 턱을 넘을 때도
        /// 큰 도약과 똑같은 진폭으로 흔들렸다. AutoTraversal이 이동량으로 배율을 넘긴다.
        /// </summary>
        public void OnJumpLaunch(float rise, float scale = 1f)
        {
            float r = Mathf.Max(0f, rise);
            float s = Mathf.Clamp01(scale);
            if (s <= 0.001f) return;

            Fire(ref _launch, Mathf.Min(launchDipMax, launchDipPerMeter * r) * s, launchDuration);
            Fire(ref _launchKick, Mathf.Min(launchKickMax, launchKickBase + launchKickPerMeter * r) * s, launchKickDuration);
            FireRoll(launchRollDeg * s, launchRollDuration, launchRollCycles);
        }

        /// <summary>
        /// 착지. <paramref name="scale"/>(0~1)는 도약 크기 배율 — <see cref="OnJumpLaunch"/>와 같은 이유로
        /// 롤에 곱한다(침하는 이미 impactSpeed로 스스로 조절되지만, 롤은 그 값과 무관했다).
        /// </summary>
        public void OnLand(float impactSpeed, float scale = 1f)
        {
            if (impactSpeed < landMinSpeed) return;
            float s = Mathf.Clamp01(scale);
            Fire(ref _land, Mathf.Min(landDipMax, landDipPerSpeed * impactSpeed), landDuration);
            Fire(ref _landKick, Mathf.Min(landKickMax, landKickPerSpeed * impactSpeed), landKickDuration);
            FireRoll(landRollDeg * s, landRollDuration, landRollCycles);
        }

        void FireRoll(float deg, float dur, float cycles)
        {
            if (deg <= 0.01f || dur <= 0f) return;
            _rollSign = -_rollSign;                 // 매번 반대쪽부터
            _rollCycles = Mathf.Max(0.5f, cycles);
            Fire(ref _rollKick, deg, dur);
        }

        static void Fire(ref Fx fx, float amp, float dur)
        {
            if (amp <= 0.0001f || dur <= 0f) return;
            fx.active = true; fx.amp = amp; fx.dur = dur; fx.t = 0f;
        }

        public void OnMantleFinish()
        {
            _settle.active = true;
            _settle.amp = settleDip;
            _settle.dur = settleDuration;
            _settle.t = 0f;
        }

        /// <summary>당김 시작. cycles=교차 횟수, sign=첫 기울임 방향(+1/-1).</summary>
        public void BeginPullSway(float cycles, float amplitudeDeg, float sign)
        {
            _swayActive = true;
            _swayCycles = Mathf.Max(0.5f, cycles);
            _swayAmp = amplitudeDeg;
            _swaySign = Mathf.Sign(sign);
            _swayProgress = 0f;
        }

        public void SetPullProgress(float p) { _swayProgress = Mathf.Clamp01(p); }

        public void EndPullSway() { _swayActive = false; }

        void Awake()
        {
            // [CamRig]에 붙으면 카메라는 자식에 있다. 손으로 조립한 씬(몸에 직접 부착)도 그대로 동작한다.
            _cam = GetComponent<Camera>();
            if (_cam == null) _cam = GetComponentInChildren<Camera>();
            if (_cam != null) _baseFov = _cam.fieldOfView;

            // 안전장치 — 시점 드라이버와 <b>같은 오브젝트</b>에서 회전을 소유하면 서로 덮어쓴다.
            // (카메라에 붙는 것 자체는 정상이다. 시점이 부모 [Head]에 있으면 롤은 이쪽 몫이다.)
            if (ownsRotation &&
                (GetComponent<MouseLook>() != null ||
                 GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>() != null))
            {
                ownsRotation = false;
                Debug.LogWarning("[MotionFeel] 시점 드라이버와 같은 오브젝트에서는 회전을 소유할 수 " +
                                 "없습니다 — 서로 덮어씁니다. 껐습니다(롤은 CurrentRoll로만 내보냅니다).", this);
            }
            _carryRoll.frequency = _carryFov.frequency = carryFrequency;
            _carryRoll.damping = _carryFov.damping = carryDamping;
        }

        /// <summary>
        /// 직전 프레임 오프셋 되돌리기 — <b>모든 위치 구동자보다 먼저</b>(실행 순서 -100).
        /// LateUpdate에서 같이 처리하면, 그 사이 AutoTraversal이 위치를 절대값으로 덮어쓴 경우
        /// 이미 사라진 오프셋을 또 빼서 매 프레임 이중 차감 → 화면이 가라앉으며 떨린다.
        /// </summary>
        void Update()
        {
            transform.position -= _appliedPos;
            _appliedPos = Vector3.zero;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;

            float dip = Tick(ref _launch, launchCurve, dt)
                      + Tick(ref _land, landCurve, dt)
                      + Tick(ref _settle, settleCurve, dt);

            // 위로 튀는 성분 — 킥이 침하보다 짧아 '탁' 튀었다 원복하는 순서로 읽힌다.
            float kick = Tick(ref _launchKick, launchKickCurve, dt)
                       + Tick(ref _landKick, landKickCurve, dt);

            // 롤 킥 — 감쇠 진동. sin이 0에서 출발해 빠르게 최고점을 찍으므로 '파박' 하고 튄다.
            float roll = 0f;
            if (_rollKick.active)
            {
                _rollKick.t += dt;
                float u = _rollKick.dur > 0f ? _rollKick.t / _rollKick.dur : 1f;
                if (u >= 1f) _rollKick.active = false;
                else roll += _rollSign * _rollKick.amp
                           * Mathf.Sin(u * _rollCycles * 2f * Mathf.PI) * (1f - u);
            }

            if (_swayActive)
            {
                // sin(교차) × sin(진행 포락선) — 시작·끝에서 0으로 수렴해 뚝 끊기지 않는다.
                float envelope = Mathf.Sin(_swayProgress * Mathf.PI);
                roll += _swaySign * _swayAmp * envelope
                      * Mathf.Sin(_swayProgress * _swayCycles * 2f * Mathf.PI);
            }

            // 실려가기 — 지하철 스웨이. 목표값을 "지금 실려가는 속도"로 매 프레임 다시 세팅하고
            // 항상 Step()한다 — Carry가 안 들어온 프레임엔 목표가 0으로 뚝 떨어지는데, 댐핑<1이라
            // 스프링이 그 급변에 반응해 반대쪽으로 넘어갔다 흔들리며 정착한다(요청하신 "정지 시 더
            // 꺾였다가 제자리로 흔들~"이 이 스프링 하나로 자연히 나온다. 롤·FOV 둘 다 같은 원리라
            // 별도 오버슈트 로직이 없다).
            Vector3 carryVelocity = dt > 1e-5f ? _carryAccum / dt : Vector3.zero;
            _carryAccum = Vector3.zero;

            // 원본(raw) 속도로 "지금 갑자기 세게 부딪혔는지" 판단하고, 실제 목표값 계산에는
            // 스무딩된 값을 써서 물리 충돌 특유의 프레임 단위 노이즈가 떨림으로 새어나가지 않게 한다.
            float rawSpeedMag = carryVelocity.magnitude;
            float smoothT = carrySmoothTime > 0f ? 1f - Mathf.Exp(-dt / carrySmoothTime) : 1f;
            _carrySmoothed = Vector3.Lerp(_carrySmoothed, carryVelocity, smoothT);

            float lateralSpeed = Vector3.Dot(_carrySmoothed, transform.right);     // +면 오른쪽으로 실려감
            float forwardSpeed = Vector3.Dot(_carrySmoothed, transform.forward);   // +면 전진으로 실려감

            // 갑자기 세게 부딪힌 경우 반응성을 올려 짧게 홀드했다 빨리 정착시킨다(완만한 승차와 구분).
            float impactBlend = Mathf.InverseLerp(carryImpactSpeed * 0.5f, carryImpactSpeed, rawSpeedMag);
            float freq = Mathf.Lerp(carryFrequency, carryImpactFrequency, impactBlend);
            _carryRoll.frequency = _carryFov.frequency = freq;
            _carryRoll.damping = _carryFov.damping = carryDamping;

            // 오른쪽으로 실려가면 화면은 반시계 방향으로 버틴다 — Unity Euler Z 부호 기준 +lateralSpeed.
            _carryRoll.Target = Mathf.Clamp(lateralSpeed * carryRollGain, -carryRollMax, carryRollMax);
            _carryFov.Target = Mathf.Clamp(-forwardSpeed * carryFovGain, -carryFovMax, carryFovMax);
            _carryRoll.Step(dt);
            _carryFov.Step(dt);

            bool vr = VrMode.Enabled;
            float posScale = vr ? vrPositionScale : 1f;
            float rollScale = vr ? vrRollScale : 1f;
            CurrentRoll = roll * rollScale + _carryRoll.Value * rollScale;

            if (_cam != null && _baseFov > 0f)
                _cam.fieldOfView = _baseFov + _carryFov.Value * posScale + ExternalFovOffset;

            // 카메라 흡수 지연 — 갑자기 밀린 순간 화면이 살짝 뒤처졌다가 짧게 따라잡는다.
            // 실제 게임플레이 위치(CharacterController)는 호출자가 이미 정확히 반영했으니 안 건드리고,
            // 여기 순수 시각적 오프셋만 지수 감쇠로 원위치(0)로 되돌린다 — 뚝뚝 끊기던 걸 흡수한다.
            _posLag = Vector3.Lerp(_posLag, Vector3.zero, 1f - Mathf.Exp(-carryPosLagDecay * dt));

            // ExternalPosOffset도 _appliedPos에 함께 담는다 — Update의 되돌리기가 같은 값을 빼야
            // 누적되지 않는다. 따로 더하면 되돌리기가 그만큼을 놓쳐 카메라가 계속 밀려난다.
            _appliedPos = Vector3.up * ((kick - dip) * posScale) + _posLag * posScale
                        + ExternalPosOffset * posScale;
            transform.position += _appliedPos;

            // 롤 — [CamRig]에 붙었을 때만 직접 적용한다(이 트랜스폼의 유일한 작성자이므로 대입이 정당).
            // 카메라에 직접 붙은 구형 배치에서는 시점 소유자와 싸우므로 CurrentRoll만 내보내고 만다.
            if (OwnsRotation) transform.localRotation = Quaternion.Euler(0f, 0f, CurrentRoll);
        }

        static float Tick(ref Fx fx, AnimationCurve curve, float dt)
        {
            if (!fx.active) return 0f;
            fx.t += dt;
            float u = fx.dur > 0f ? fx.t / fx.dur : 1f;
            if (u >= 1f) { fx.active = false; return 0f; }
            return fx.amp * Mathf.Max(0f, curve.Evaluate(u));
        }
    }
}
