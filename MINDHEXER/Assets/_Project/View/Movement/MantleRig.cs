using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 잡고 올라가기 손 리그 — <b>손이 모서리에 월드 고정</b>되고, 기본 손과 부드럽게 교대한다.
    /// 설계: `docs/KJH/design/플레이어_신체표현_설계.md` §4
    ///
    /// <para><b>일반 FPS 뷰모델과 반대</b>: 등반 중 손은 씬 공간의 잡는 지점에 핀 고정되고,
    /// 어깨는 머리(카메라)를 따라온다. 고개를 돌리면 팔이 시야에서 벗어나는데, 그게 정확히
    /// "내 손은 여전히 모서리를 잡고 있다"는 사실적 결과다. 카메라 회전은 일절 건드리지 않는다.</para>
    ///
    /// <para><b>루트를 건드리지 않는다</b>(§4.1). 뷰모델 루트를 내려 숨기는 방식은 기각했다 —
    /// 어깨까지 내려가 등반 중 팔이 이상해지고, 루트는 <see cref="ViewmodelMotion"/>이 소유한다.
    /// 대신 <see cref="HandIK.weight"/>만 쓴다. HandIK가 이미 원래 자세와 IK 해를 Slerp하므로,
    /// 타깃을 <b>park</b>(화면 아래 대기점)에 두고 weight를 움직이면:
    /// <br/>· 0→1 = 손이 기본 자세에서 <b>아래로 내려가 사라짐</b>
    /// <br/>· 1→0 = 손이 아래에서 <b>기본 자세로 올라옴</b>
    /// 손이 안 보이는 바닥 지점에서 시스템이 교대하므로 팝이 원리적으로 없다.</para>
    ///
    /// <para><b>단계</b>(§4.2): Idle → Lowering → Reaching → Holding → Releasing → Raising → Idle.
    /// 진입 `내려감 → 올라옴`, 이탈 `내려감 → 올라옴`으로 대칭이다.</para>
    ///
    /// <para><b>뷰모델이 없으면</b> 예전처럼 캡슐 프리미티브로 어깨→손을 잇는다(임시 표시).</para>
    ///
    /// <para><b>실행 순서</b>: 상태 갱신은 Update(모든 LateUpdate보다 먼저), 타깃 배치는
    /// LateUpdate −40 — ViewmodelMotion(−50)이 루트를 옮긴 뒤, HandIK(0)가 풀기 전.</para>
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class MantleRig : MonoBehaviour
    {
        public enum Phase { Idle, Lowering, Reaching, Holding, Releasing, Raising }

        [Header("어깨 (임시 캡슐용)")]
        [Tooltip("어깨 폭(m). AutoTraversal이 손 앵커 간격을 정할 때도 읽는다.")]
        public float shoulderWidth = 0.42f;

        [Tooltip("머리(카메라)에서 어깨까지 내려가는 거리(m).")]
        public float shoulderDrop = 0.22f;

        [Header("뷰모델 손 (비우면 씬에서 자동 탐색)")]
        [Tooltip("오른손 IK. 여기서 target(HandTarget_R)을 꺼내 쓴다.")]
        public HandIK handIkR;
        public HandIK handIkL;
        public FingerPoser fingerR;
        public FingerPoser fingerL;

        [Header("전환 시간(초)")]
        [Tooltip("기본손이 아래로 사라지는 시간.")]
        public float lowerTime = 0.10f;
        [Tooltip("손이 아래에서 올라와 모서리를 잡는 시간.")]
        public float reachTime = 0.14f;
        [Tooltip("손을 놓고 아래로 사라지는 시간.")]
        public float releaseTime = 0.12f;
        [Tooltip("기본손이 다시 올라오는 시간.")]
        public float raiseTime = 0.16f;

        [Tooltip("예고(Prepare) 없이 Show가 온 경우의 진입 총 시간(초). 벽 앞 바로 잡기는 여유가 0이라 " +
                 "정상 속도로 재생하면 손이 다 올라오기도 전에 몸이 올라가 버린다. 그 경우에만 압축한다.")]
        public float fastEntryTime = 0.12f;

        [Header("1인칭 기본 자세 (카메라 기준 — 매사냥 장갑 자세)")]
        [Tooltip("켜면 Idle에서도 IK로 손을 이 지점에 둔다. 끄면 모델의 쉬는 자세(T포즈)가 그대로 나온다.")]
        public bool driveIdlePose = true;
        // ★ 어깨가 카메라 로컬 (0.23, −0.11, 0.03), 팔 도달거리 0.67m(루트 스케일 2)에서 잡은 값.
        //   목표가 도달거리를 넘으면 IK가 클램프되어 팔이 뻗친 채로 굳으므로 80% 안쪽에 둔다.
        //   GameBoot이 MantleRig을 씬마다 새로 만들므로, 씬 값이 아니라 여기가 기준이 된다.
        //   자세는 <b>오른쪽 아래에서 손이 들어와 손등이 보이는</b> 형태다(가로로 눕히지 않는다).
        [Tooltip("오른손 위치. 팔이 오른쪽 아래에서 들어오도록 화면 오른쪽 아래에 둔다.")]
        public Vector3 idleLocalR = new Vector3(0.13f, -0.20f, 0.42f);
        [Tooltip("왼손 위치. 평소엔 화면 밖에 둔다 — 등반할 때만 들어온다.")]
        public Vector3 idleLocalL = new Vector3(-0.47f, -0.55f, 0.18f);
        // ★ 손 뼈 축을 실측해 역산한 값이다(추측 아님). R_Hand 로컬에서
        //   손가락 방향 (-0.555, -0.751, 0.357) · 손등 법선 (-0.808, 0.384, -0.447)을 재고,
        //   "손가락은 화면 왼쪽·약간 아래, 손등은 카메라 쪽"이 되도록 푼 회전이다.
        //   ⚠ 아직 눈으로 확인하지 않았다 — F6에서 확정할 것.
        [Tooltip("오른손 회전. 손등이 카메라를 보고 손가락이 왼쪽·아래로 가도록 맞춘다.")]
        public Vector3 idleEulerR = new Vector3(344.6f, 312.5f, 339.8f);
        public Vector3 idleEulerL = new Vector3(20f, 30f, 0f);
        [Tooltip("기본 자세에서의 IK 가중치. 1이면 완전히 이 자세, 0이면 모델 쉬는 자세.")]
        [Range(0f, 1f)] public float idleWeight = 1f;
        [Tooltip("기본 자세 손가락의 <b>공통</b> 말림. 아래 손가락별 값이 여기에 더해진다.")]
        [Range(0f, 1f)] public float idleGripR = 0.15f;
        [Range(0f, 1f)] public float idleGripL = 0.15f;

        [Tooltip("손가락별 <b>추가</b> 말림. 다섯이 같은 양으로 말리면 집게처럼 보인다 — " +
                 "실제로 힘을 뺀 손은 검지가 가장 펴지고 새끼로 갈수록 말린다.")]
        public IdleFingerPose idleFingerR = new IdleFingerPose { index = 0f, middle = 0.20f, ring = 0.35f, pinky = 0.45f, thumb = 0.45f, spread = -0.2f };
        public IdleFingerPose idleFingerL = new IdleFingerPose { index = 0f, middle = 0.15f, ring = 0.25f, pinky = 0.35f, thumb = 0.35f, spread = -0.1f };

        /// <summary>
        /// 기본 자세의 손가락별 <b>추가</b> 말림.
        ///
        /// <para><b>왜 단일값으로는 안 되는가</b> — <see cref="FingerPoser"/>의 말림은
        /// <c>clamp01(grip + 손가락별값 + sustainGrip)</c>이다. Idle이 <c>SetSustain</c> 하나만 쓰면
        /// 다섯 손가락이 <b>정확히 같은 각도</b>로 말려 집게가 된다. 힘을 뺀 손은 그렇게 생기지 않았다.</para>
        ///
        /// <para>더하기만 되므로 <see cref="idleGripR"/>을 <b>가장 덜 말린 손가락</b>에 맞추고
        /// 나머지를 여기서 올린다. 등반 중에는 <c>ApplyClimbFingers</c>가 다섯 개를 매 프레임
        /// 전부 덮으므로 이 값이 새어 나가지 않는다.</para>
        /// </summary>
        [System.Serializable]
        public class IdleFingerPose
        {
            [Range(0f, 1f)] public float thumb;
            [Range(0f, 1f)] public float index;
            [Range(0f, 1f)] public float middle;
            [Range(0f, 1f)] public float ring;
            [Range(0f, 1f)] public float pinky;
            [Tooltip("벌림. 음수면 손가락이 모인다.")]
            [Range(-1f, 1f)] public float spread;
        }

        [Header("대기 위치 park (카메라 기준 — 화면 아래)")]
        [Tooltip("오른손 대기 지점. 화면 밖으로 나가야 교대가 안 보인다.")]
        public Vector3 parkLocalR = new Vector3(0.24f, -0.62f, 0.32f);
        public Vector3 parkLocalL = new Vector3(-0.24f, -0.62f, 0.32f);
        [Tooltip("대기 지점에서의 손 회전(카메라 기준).")]
        public Vector3 parkEulerR = new Vector3(45f, 0f, 0f);
        public Vector3 parkEulerL = new Vector3(45f, 0f, 0f);

        [Header("손바닥 보정 (모델 보고 눈으로 맞추는 값)")]
        [Tooltip("모서리를 넘어가는 방향으로 손바닥을 미는 양(m). 손이 모서리에 파묻히면 늘리십시오.")]
        public float palmForwardOffset = 0.02f;

        [Tooltip("모서리 윗면에서 손바닥을 띄우는 양(m). 음수면 파고든다.")]
        public float palmUpOffset = 0.01f;

        [Tooltip("손 뼈 축 보정(도). 리그마다 손 뼈가 보는 방향이 달라 여기서 돌린다. 오른손.")]
        public Vector3 handEulerR = Vector3.zero;
        [Tooltip("왼손. 오른손과 대칭이 아닐 수 있어 따로 둔다.")]
        public Vector3 handEulerL = Vector3.zero;

        [Header("등반 손 절차 동작")]
        [Tooltip("엄지는 반대편에서 물리므로 덜 감긴다.")]
        [Range(0f, 1f)] public float thumbCurlScale = 0.7f;
        [Tooltip("편 손일 때 손가락이 벌어지는 정도. 쥘수록 0으로 모인다.")]
        [Range(-1f, 1f)] public float fingerSpreadOpen = 0.35f;
        [Tooltip("잡는 순간 손목이 꺾이는 각도(°). 모서리를 누르는 반작용.")]
        [Range(0f, 40f)] public float climbWristFlexDeg = 12f;
        [Tooltip("잡는 순간 손목이 비틀리는 각도(°).")]
        [Range(0f, 40f)] public float climbWristRollDeg = 8f;

        [Header("손가락")]
        [Tooltip("모서리를 쥐는 세기(0=편 손, 1=꽉 쥠). 블렌드 속도는 FingerPoser.sustainSpeed가 맡는다.")]
        [Range(0f, 1f)] public float gripAmount = 0.85f;

        [Tooltip("뻗기 진행률이 이 값을 넘으면 쥐기 시작. 늦게 쥘수록 '닿고 나서 쥔다'로 읽힌다.")]
        [Range(0f, 1f)] public float gripCloseAt = 0.6f;

        [Tooltip("놓기 진행률이 이 값을 넘으면 편다. 일찍 펼수록 '놓고 나서 내려온다'로 읽힌다.")]
        [Range(0f, 1f)] public float gripOpenAt = 0.3f;

        [Header("임시 표시 (뷰모델이 없을 때만)")]
        [Tooltip("뷰모델 손을 못 찾았을 때 캡슐로라도 팔을 그린다.")]
        public bool useCapsuleFallback = true;
        public float armThickness = 0.05f;

        [Header("디버그")]
        public bool drawGizmos = true;
        public bool logPhase;

        // ── 상태 ──
        Phase _phase = Phase.Idle;
        float _t;                          // 현재 단계 경과
        bool  _prepared;                   // Prepare()로 미리 내리기 시작했나
        Camera _cam;

        Vector3 _leftHand, _rightHand;     // 모서리 앵커(월드 고정)
        Vector3 _edgeRight, _approach;     // Show에서 한 번 잡아 고정하는 기저
        Quaternion _rotR, _rotL;           // 모서리에서의 손 회전
        float _baseWeightR, _baseWeightL;  // 등반 전 IK 가중치(원복용)
        bool  _resolved, _usingIk;
        bool  _hasAnchors;                 // Show가 와서 앵커·기저가 유효한가
        Transform _idleAnchorR, _idleAnchorL;   // 기본 자세 IK 타깃(카메라 자식, 저장 안 함)
        float _entryScale = 1f;            // 진입 압축 배율(1 = 정상 속도)

        // 압축이 적용된 실제 재생 시간
        float LowerDur => lowerTime * _entryScale;
        float ReachDur => reachTime * _entryScale;

        Transform _capL, _capR;            // 임시 캡슐(필요할 때만)

        public Phase Current => _phase;
        /// <summary>등반 손이 조금이라도 관여 중인가. ViewmodelMotion이 참고할 수 있다.</summary>
        public bool Engaged => _phase != Phase.Idle;

        void Awake() { _cam = Camera.main; }

        void OnDestroy()
        {
            if (_capL != null) Destroy(_capL.gameObject);
            if (_capR != null) Destroy(_capR.gameObject);
        }

        // ── AutoTraversal이 부르는 것 ─────────────────────────────────────

        /// <summary>
        /// 곧 등반한다는 예고. <see cref="Show"/>보다 먼저 오면 그 사이에 기본손을 미리 내려둔다.
        ///
        /// <para><b>왜 필요한가</b>(§4.4): AutoTraversal은 <c>BeginPull()</c>에서 Show를 부르는데,
        /// 그때는 이미 몸이 올라가기 시작하는 시점이라 손이 모서리에 닿기 전에 당기는 모양이 된다.
        /// 도약 경로는 비행 0.18~0.9초의 여유가 있으므로 그 시간에 미리 내린다.</para>
        ///
        /// <para>벽 앞 바로 잡기(directLatchRange)는 여유가 <b>구조적으로 0</b>이라 예고가 와도
        /// 같은 프레임에 Show가 온다. 그 경우 Show가 남은 구간을 압축 재생한다.</para>
        /// </summary>
        public void Prepare()
        {
            if (_phase != Phase.Idle && _phase != Phase.Raising) return;
            Resolve();
            if (!_usingIk) return;          // 캡슐 폴백이면 미리 할 일이 없다

            CaptureBaseWeights();
            _phase = Phase.Lowering;
            _t = 0f;
            _prepared = true;
            _entryScale = 1f;      // 여유가 있으므로 정상 속도
            if (logPhase) Debug.Log("[MantleRig] Prepare — 기본손 내리기 시작");
        }

        /// <summary>손을 잡는 지점(월드)에 핀 고정하고 등반 표시 시작.</summary>
        public void Show(Vector3 leftHand, Vector3 rightHand)
        {
            _leftHand = leftHand;
            _rightHand = rightHand;

            BuildBasis();
            Resolve();
            _hasAnchors = true;

            if (!_usingIk)
            {
                if (!useCapsuleFallback) return;
                EnsureCapsules();
                _capL.gameObject.SetActive(true);
                _capR.gameObject.SetActive(true);
                _phase = Phase.Holding;
                return;
            }

            if (_prepared)
            {
                // 예고를 받아 이미 내리는 중(대개 다 내려간 상태). 정상 속도로 이어간다.
                _prepared = false;
                if (logPhase) Debug.Log("[MantleRig] Show — 예고 있었음, 정상 속도");
            }
            else
            {
                // 여유가 0이었다(벽 앞 바로 잡기). 진입 전체를 fastEntryTime에 맞춰 압축한다.
                CaptureBaseWeights();
                float natural = lowerTime + reachTime;
                _entryScale = natural > 1e-4f ? Mathf.Min(1f, fastEntryTime / natural) : 1f;
                _phase = Phase.Lowering;
                _t = 0f;
                if (logPhase) Debug.Log($"[MantleRig] Show — 예고 없음, 진입 압축 ×{_entryScale:0.00}");
            }
        }

        /// <summary>등반 종료. 손을 놓고 기본손으로 되돌린다.</summary>
        public void Hide()
        {
            if (_capL != null) _capL.gameObject.SetActive(false);
            if (_capR != null) _capR.gameObject.SetActive(false);

            _prepared = false;

            if (!_usingIk) { _phase = Phase.Idle; return; }
            if (_phase == Phase.Idle) return;

            // ★ 예고만 받고 취소된 경우(도약했는데 등반이 아니었다) 앵커가 없다.
            //   그대로 Releasing에 넣으면 LedgePose가 쓰레기 값이라 손이 원점으로 날아간다.
            //   놓을 것이 없으니 바로 올리기로 간다.
            if (!_hasAnchors)
            {
                _phase = Phase.Raising;
                _t = 0f;
                if (logPhase) Debug.Log("[MantleRig] Hide — 앵커 없음(등반 취소), 바로 복귀");
                return;
            }

            _hasAnchors = false;
            _phase = Phase.Releasing;
            _t = 0f;
            if (logPhase) Debug.Log("[MantleRig] Hide — 놓기 시작");
        }

        // ── 상태 진행 (Update — 모든 LateUpdate보다 먼저) ──────────────────

        void Update()
        {
            if (_phase == Phase.Idle) return;
            float dt = Time.deltaTime;
            _t += dt;

            switch (_phase)
            {
                case Phase.Lowering:
                    if (_t < LowerDur) break;
                    // 예고만 받고 아직 Show가 안 왔으면 park에서 <b>대기</b>한다 — 잡을 곳을 모르므로 뻗을 수 없다.
                    if (_prepared) { _t = LowerDur; break; }
                    _phase = Phase.Reaching; _t = 0f; Log();
                    break;

                case Phase.Reaching:
                    if (_t >= ReachDur) { _phase = Phase.Holding; _t = 0f; Log(); }
                    break;

                case Phase.Releasing:
                    if (_t >= releaseTime) { _phase = Phase.Raising; _t = 0f; Log(); }
                    break;

                case Phase.Raising:
                    if (_t >= raiseTime) { _phase = Phase.Idle; _t = 0f; _entryScale = 1f; RestoreWeights(); Log(); }
                    break;
            }
        }

        void Log() { if (logPhase) Debug.Log($"[MantleRig] → {_phase}"); }

        // ── 적용 (LateUpdate −40) ─────────────────────────────────────────

        void LateUpdate()
        {
            if (_phase == Phase.Idle) { ApplyIdlePose(); return; }

            if (!_usingIk) { PlaceCapsules(); return; }

            float blend;
            Vector3 posR, posL;
            Quaternion rotR, rotL;
            float grip;

            switch (_phase)
            {
                case Phase.Lowering:
                    blend = Smooth(LowerDur <= 0f ? 1f : _t / LowerDur);
                    ParkPose(out posR, out posL, out rotR, out rotL);
                    grip = 0f;
                    ApplyWeights(Mathf.Lerp(_baseWeightR, 1f, blend), Mathf.Lerp(_baseWeightL, 1f, blend));
                    break;

                case Phase.Reaching:
                {
                    float u = Smooth(ReachDur <= 0f ? 1f : _t / ReachDur);
                    ParkPose(out Vector3 pR, out Vector3 pL, out Quaternion qR, out Quaternion qL);
                    LedgePose(out Vector3 eR, out Vector3 eL, out Quaternion erR, out Quaternion erL);
                    posR = Vector3.Lerp(pR, eR, u); posL = Vector3.Lerp(pL, eL, u);
                    rotR = Quaternion.Slerp(qR, erR, u); rotL = Quaternion.Slerp(qL, erL, u);
                    grip = u >= gripCloseAt ? gripAmount : 0f;
                    ApplyWeights(1f, 1f);
                    break;
                }

                case Phase.Holding:
                    LedgePose(out posR, out posL, out rotR, out rotL);
                    grip = gripAmount;
                    ApplyWeights(1f, 1f);
                    break;

                case Phase.Releasing:
                {
                    float u = Smooth(releaseTime <= 0f ? 1f : _t / releaseTime);
                    LedgePose(out Vector3 eR, out Vector3 eL, out Quaternion erR, out Quaternion erL);
                    ParkPose(out Vector3 pR, out Vector3 pL, out Quaternion qR, out Quaternion qL);
                    posR = Vector3.Lerp(eR, pR, u); posL = Vector3.Lerp(eL, pL, u);
                    rotR = Quaternion.Slerp(erR, qR, u); rotL = Quaternion.Slerp(erL, qL, u);
                    grip = u >= gripOpenAt ? 0f : gripAmount;
                    ApplyWeights(1f, 1f);
                    break;
                }

                case Phase.Raising:
                default:
                {
                    float u = Smooth(raiseTime <= 0f ? 1f : _t / raiseTime);
                    ParkPose(out posR, out posL, out rotR, out rotL);
                    grip = 0f;
                    ApplyWeights(Mathf.Lerp(1f, _baseWeightR, u), Mathf.Lerp(1f, _baseWeightL, u));
                    break;
                }
            }

            // 손목 꺾임은 목표 회전에 얹는다 — 뼈를 직접 만지면 HandIK와 소유권이 겹친다(§3).
            if (handIkR != null && handIkR.target != null) handIkR.target.SetPositionAndRotation(posR, rotR * WristFlex(grip, true));
            if (handIkL != null && handIkL.target != null) handIkL.target.SetPositionAndRotation(posL, rotL * WristFlex(grip, false));

            // 손가락은 목표값과 시점만 준다 — 스무딩은 FingerPoser.sustainSpeed가 단독 소유한다(§3 규칙2).
            if (fingerR != null) fingerR.SetSustain(grip);
            if (fingerL != null) fingerL.SetSustain(grip);

            // 다섯 손가락이 동시에 같은 양으로 감기면 집게처럼 보인다 — 시차를 준다.
            ApplyClimbHands(grip);
        }

        void ApplyWeights(float wR, float wL)
        {
            if (handIkR != null) handIkR.weight = wR;
            if (handIkL != null) handIkL.weight = wL;
        }

        void CaptureBaseWeights()
        {
            _baseWeightR = handIkR != null ? handIkR.weight : 0f;
            _baseWeightL = handIkL != null ? handIkL.weight : 0f;
        }

        void RestoreWeights()
        {
            ApplyWeights(_baseWeightR, _baseWeightL);
            if (fingerR != null) fingerR.SetSustain(0f);
            if (fingerL != null) fingerL.SetSustain(0f);
        }

        /// <summary>양 끝이 부드러운 smoothstep. 조절할 것은 시간이지 곡선 모양이 아니다(§4.5).</summary>
        static float Smooth(float u) { u = Mathf.Clamp01(u); return u * u * (3f - 2f * u); }

        // ── 자세 계산 ────────────────────────────────────────────────────

        /// <summary>화면 아래 대기 지점(카메라 기준). 카메라가 없으면 플레이어 기준으로 폴백.</summary>
        void ParkPose(out Vector3 posR, out Vector3 posL, out Quaternion rotR, out Quaternion rotL)
        {
            Transform t = _cam != null ? _cam.transform : transform;
            posR = t.TransformPoint(parkLocalR);
            posL = t.TransformPoint(parkLocalL);
            rotR = t.rotation * Quaternion.Euler(parkEulerR);
            rotL = t.rotation * Quaternion.Euler(parkEulerL);
        }

        /// <summary>
        /// 1인칭 기본 자세(카메라 기준). 오른 전완을 가로로 눕혀 <b>매사냥 장갑</b>처럼 두고,
        /// 그 손목 아래에 펫 거미가 얹힌다. 왼손은 화면 밖에서 대기한다.
        ///
        /// <para><b>왜 park 기구를 그대로 쓰나</b> — 등반 상태 기계가 이미 "카메라 로컬 좌표 →
        /// HandIK 타깃 + weight" 경로를 갖고 있다. 같은 기구를 재사용하면 <b>기본↔등반 전환이
        /// 그냥 두 지점 사이 보간</b>이 되어 팝이 생기지 않는다. 전완 뼈를 직접 대입하는 방식은
        /// <see cref="HandIK"/>와 소유권이 겹쳐(설계 §3) 서로 덮어쓴다.</para>
        ///
        /// <para>모델의 쉬는 자세(T포즈)는 1인칭에서 쓸 수 없으므로 <see cref="driveIdlePose"/>가
        /// 켜져 있으면 IK를 상시 1로 두고 이 지점을 푼다.</para>
        /// </summary>
        void ApplyIdlePose()
        {
            if (!driveIdlePose) return;
            if (!_usingIk) { Resolve(); if (!_usingIk) return; }

            Transform t = _cam != null ? _cam.transform : transform;

            // 등반 단계와 <b>같은 방식</b>으로 기존 타깃을 옮긴다(§338행). 타깃이 비어 있을 때만
            // 앵커를 만든다 — 씬에서 지정해 둔 타깃을 덮어쓰면 다른 시스템이 같이 망가진다.
            if (handIkR != null)
            {
                if (handIkR.target == null) { EnsureIdleAnchor(ref _idleAnchorR, "[IdleHandR]", t); handIkR.target = _idleAnchorR; }
                handIkR.target.SetPositionAndRotation(t.TransformPoint(idleLocalR),
                                                      t.rotation * Quaternion.Euler(idleEulerR));
                handIkR.weight = idleWeight;
            }
            if (handIkL != null)
            {
                if (handIkL.target == null) { EnsureIdleAnchor(ref _idleAnchorL, "[IdleHandL]", t); handIkL.target = _idleAnchorL; }
                handIkL.target.SetPositionAndRotation(t.TransformPoint(idleLocalL),
                                                      t.rotation * Quaternion.Euler(idleEulerL));
                handIkL.weight = idleWeight;
            }

            // 손가락 — 거미를 받치는 살짝 편 손. 등반의 꽉 쥠과 달라야 한다.
            // 공통 말림은 sustain으로, 손가락별 차이는 개별 값으로 준다(둘은 더해진다).
            if (fingerR != null) { fingerR.SetSustain(idleGripR); ApplyIdleFingers(fingerR, idleFingerR); }
            if (fingerL != null) { fingerL.SetSustain(idleGripL); ApplyIdleFingers(fingerL, idleFingerL); }
        }

        static void ApplyIdleFingers(FingerPoser f, IdleFingerPose p)
        {
            if (f == null || p == null) return;
            f.thumb  = p.thumb;
            f.index  = p.index;
            f.middle = p.middle;
            f.ring   = p.ring;
            f.pinky  = p.pinky;
            f.spread = p.spread;
        }

        /// <summary>
        /// 등반 중 손가락·손목 절차 동작.
        ///
        /// <para><b>왜 필요한가</b> — 다섯 손가락이 <b>동시에 같은 양</b>으로 감기면 집게처럼 보인다.
        /// 실제로 무언가를 잡을 때는 검지부터 새끼 쪽으로 <b>차례로</b> 닿고, 엄지는 늦게 반대편에서
        /// 물린다. 그 시차만 넣어도 '쥐는 동작'으로 읽힌다.</para>
        ///
        /// <para>손목은 잡는 순간 살짝 꺾인다 — 모서리를 누르는 반작용이다. IK가 손목을 목표 회전에
        /// 맞추므로, 여기서는 <b>목표 회전에 얹는 오프셋</b>으로만 준다(뼈를 직접 만지면 HandIK와
        /// 소유권이 겹친다 — 설계 §3).</para>
        /// </summary>
        void ApplyClimbHands(float grip)
        {
            ApplyClimbFingers(fingerR, grip);
            ApplyClimbFingers(fingerL, grip);
        }

        void ApplyClimbFingers(FingerPoser f, float grip)
        {
            if (f == null) return;

            // 검지 → 중지 → 약지 → 새끼 순으로 늦게 감긴다. 엄지는 가장 늦고 덜 감긴다.
            f.index  = Stagger(grip, 0.00f);
            f.middle = Stagger(grip, 0.06f);
            f.ring   = Stagger(grip, 0.12f);
            f.pinky  = Stagger(grip, 0.18f);
            f.thumb  = Stagger(grip, 0.26f) * thumbCurlScale;

            // 손가락이 감길수록 벌림이 줄어든다 — 편 손은 벌어지고 쥔 손은 모인다.
            f.spread = Mathf.Lerp(fingerSpreadOpen, 0f, grip);
        }

        /// <summary>시차 감기. <paramref name="delay"/>만큼 늦게 시작해 같은 지점에서 끝난다.</summary>
        static float Stagger(float grip, float delay)
        {
            float span = 1f - delay;
            if (span <= 0.001f) return grip;
            return Mathf.Clamp01((grip - delay) / span);
        }

        /// <summary>잡는 순간 손목이 살짝 꺾이는 양(도). 목표 회전에 얹는다.</summary>
        Quaternion WristFlex(float grip, bool right) =>
            Quaternion.Euler((right ? 1f : 1f) * climbWristFlexDeg * grip, 0f, (right ? -1f : 1f) * climbWristRollDeg * grip);

        /// <summary>앵커는 씬에 저장하지 않는다 — 저장되면 씬마다 유령 앵커가 쌓인다.</summary>
        static void EnsureIdleAnchor(ref Transform anchor, string name, Transform parent)
        {
            if (anchor != null) return;
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(parent, false);
            anchor = go.transform;
        }

        /// <summary>모서리에 손바닥이 얹히는 자세. 앵커는 월드 고정이라 고개를 돌려도 안 움직인다.</summary>
        void LedgePose(out Vector3 posR, out Vector3 posL, out Quaternion rotR, out Quaternion rotL)
        {
            Vector3 ofs = _approach * palmForwardOffset + Vector3.up * palmUpOffset;
            posR = _rightHand + ofs;
            posL = _leftHand + ofs;
            rotR = _rotR;
            rotL = _rotL;
        }

        /// <summary>
        /// 두 손 앵커 + 머리 위치만으로 손 회전 기저를 만든다(§4.3). Show에서 <b>한 번만</b>.
        /// 매 프레임 transform.forward로 다시 구하면 고개를 돌릴 때마다 손 회전이 따라 헤엄쳐
        /// 월드 고정의 의미가 사라진다.
        /// </summary>
        void BuildBasis()
        {
            Vector3 span = _rightHand - _leftHand;
            span.y = 0f;
            _edgeRight = span.sqrMagnitude > 1e-6f ? span.normalized : transform.right;

            // 접근 방향 = 머리→모서리(수평). 시선이 아니라 <b>몸이 있는 쪽</b>이라 고개를 돌려도 안 흔들린다.
            Vector3 edgeCenter = (_leftHand + _rightHand) * 0.5f;
            Vector3 toEdge = edgeCenter - transform.position;
            toEdge.y = 0f;

            // 모서리 축 성분을 빼서 직교화 — 손이 모서리에 비스듬히 걸리지 않게.
            Vector3 a = toEdge - _edgeRight * Vector3.Dot(toEdge, _edgeRight);
            if (a.sqrMagnitude < 1e-6f)
            {
                a = transform.forward; a.y = 0f;
                a -= _edgeRight * Vector3.Dot(a, _edgeRight);
            }
            _approach = a.sqrMagnitude > 1e-6f ? a.normalized : Vector3.Cross(Vector3.up, _edgeRight);

            // 손가락은 모서리 너머로(=approach), 손바닥은 아래로.
            Quaternion baseRot = Quaternion.LookRotation(_approach, Vector3.up);
            _rotR = baseRot * Quaternion.Euler(handEulerR);
            _rotL = baseRot * Quaternion.Euler(handEulerL);
        }

        /// <summary>손 IK·손가락을 씬에서 찾는다(인스펙터에 이미 꽂혀 있으면 그대로 둔다).</summary>
        void Resolve()
        {
            if (_resolved && (handIkR != null || handIkL != null)) { _usingIk = true; return; }

            // ★ 반드시 <b>우리 리그 안</b>에서만 찾는다. 예전엔 FindObjectsByType으로 씬 전체를
            //   훑었는데, 그러면 우리 손이 아닌 IK를 집어간다 — 실제로 겪은 버그: 경비병의
            //   다리 IK(GuardTurnStep이 런타임에 만드는 [GuardLegIK_R])가 handIkR로 잡혀
            //   MantleRig가 그 weight를 1로 몰았고, 경비병 오른다리가 걷는 동안 공중의 옛 목표를
            //   붙잡은 채 뻣뻣하게 펴져 있었다. 왼다리가 멀쩡했던 건 IsLeft가 "[GuardLegIK_L]"을
            //   (_L이 아니라 _L]로 끝나서) 왼쪽으로 판정하지 못해 handIkL이 비었기 때문이다.
            //   손 IK는 원래 플레이어 계층(뷰모델) 안에 있으므로 범위를 좁혀도 잃는 것이 없고,
            //   못 찾으면 _usingIk=false로 IK 없는 경로를 타는 것이 맞는 동작이다.
            if (handIkR == null || handIkL == null)
                foreach (var ik in GetComponentsInChildren<HandIK>(true))
                {
                    if (ik.target == null) continue;   // 타깃 없는 IK는 몰 수 없다
                    if (IsLeft(ik.end, ik.gameObject.name)) { if (handIkL == null) handIkL = ik; }
                    else                                    { if (handIkR == null) handIkR = ik; }
                }

            if (fingerR == null || fingerL == null)
                foreach (var fp in GetComponentsInChildren<FingerPoser>(true))
                {
                    Transform root = fp.handRoot != null ? fp.handRoot : fp.transform;
                    if (IsLeft(root, fp.gameObject.name)) { if (fingerL == null) fingerL = fp; }
                    else                                  { if (fingerR == null) fingerR = fp; }
                }

            if (_cam == null) _cam = Camera.main;
            _usingIk = handIkR != null || handIkL != null;
            _resolved = true;
        }

        /// <summary>좌우 판별 — 우리 리그의 "L_"/"R_" 접두사를 먼저 보고, 없으면 Left/Right·_L 접미사로 폴백.</summary>
        static bool IsLeft(Transform bone, string ownerName)
        {
            string n = bone != null ? bone.name : ownerName;
            if (n.StartsWith("L_")) return true;
            if (n.StartsWith("R_")) return false;
            if (n.IndexOf("Left",  System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (n.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;
            return ownerName.EndsWith("_L");
        }

        // ── 임시 캡슐 ────────────────────────────────────────────────────

        void EnsureCapsules()
        {
            if (_capL == null) _capL = CreateArm("[MantleArm L]");
            if (_capR == null) _capR = CreateArm("[MantleArm R]");
        }

        Transform CreateArm(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(null, true);
            go.SetActive(false);
            return go.transform;
        }

        void PlaceCapsules()
        {
            if (_capL == null || _capR == null) return;

            // 어깨 = 머리 아래 + 몸 좌우. 좌우 축은 머리 yaw의 수평 성분(수직으로 보면 폴백).
            Vector3 fwd = transform.forward; fwd.y = 0f;
            Vector3 right = fwd.sqrMagnitude > 1e-4f
                ? Vector3.Cross(Vector3.up, fwd.normalized) * -1f
                : transform.right;
            right.y = 0f;
            if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
            right.Normalize();

            Vector3 baseP = transform.position + Vector3.down * shoulderDrop;
            PlaceArm(_capL, baseP - right * (shoulderWidth * 0.5f), _leftHand);
            PlaceArm(_capR, baseP + right * (shoulderWidth * 0.5f), _rightHand);
        }

        void PlaceArm(Transform arm, Vector3 shoulder, Vector3 hand)
        {
            Vector3 d = hand - shoulder;
            float len = Mathf.Max(0.05f, d.magnitude);
            arm.position = (shoulder + hand) * 0.5f;
            arm.rotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
            arm.localScale = new Vector3(armThickness, len * 0.5f, armThickness);
        }

        // ── 기즈모 ───────────────────────────────────────────────────────

        void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;

            // park 대기점 — 화면 밖에 있는지 확인용
            Transform t = _cam != null ? _cam.transform : (Camera.main != null ? Camera.main.transform : transform);
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(t.TransformPoint(parkLocalR), 0.03f);
            Gizmos.DrawWireSphere(t.TransformPoint(parkLocalL), 0.03f);

            if (_phase == Phase.Idle) return;

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(_leftHand, 0.03f);
            Gizmos.DrawWireSphere(_rightHand, 0.03f);
            Gizmos.DrawLine(_leftHand, _rightHand);

            Vector3 c = (_leftHand + _rightHand) * 0.5f;
            Gizmos.color = Color.green;  Gizmos.DrawRay(c, _approach * 0.25f);    // 손가락이 넘어가는 쪽
            Gizmos.color = Color.red;    Gizmos.DrawRay(c, _edgeRight * 0.25f);   // 모서리 축
        }
    }
}
