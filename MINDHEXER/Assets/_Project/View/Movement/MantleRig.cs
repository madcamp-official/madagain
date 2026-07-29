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
            if (_phase == Phase.Idle) return;

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

            if (handIkR != null && handIkR.target != null) handIkR.target.SetPositionAndRotation(posR, rotR);
            if (handIkL != null && handIkL.target != null) handIkL.target.SetPositionAndRotation(posL, rotL);

            // 손가락은 목표값과 시점만 준다 — 스무딩은 FingerPoser.sustainSpeed가 단독 소유한다(§3 규칙2).
            if (fingerR != null) fingerR.SetSustain(grip);
            if (fingerL != null) fingerL.SetSustain(grip);
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

            if (handIkR == null || handIkL == null)
                foreach (var ik in FindObjectsByType<HandIK>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (ik.target == null) continue;   // 타깃 없는 IK는 몰 수 없다
                    if (IsLeft(ik.end, ik.gameObject.name)) { if (handIkL == null) handIkL = ik; }
                    else                                    { if (handIkR == null) handIkR = ik; }
                }

            if (fingerR == null || fingerL == null)
                foreach (var fp in FindObjectsByType<FingerPoser>(FindObjectsInactive.Include, FindObjectsSortMode.None))
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
