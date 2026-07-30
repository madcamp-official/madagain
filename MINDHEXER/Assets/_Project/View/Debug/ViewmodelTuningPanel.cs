using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.View
{
    /// <summary>
    /// F6 1인칭 손·거미 위치 조정 패널.
    ///
    /// <para><b>왜 필요한가</b> — 손 자세와 거미가 얹히는 자리는 계산으로 정할 수 없다.
    /// 모델마다 손 뼈가 보는 축이 다르고(설계 §4.3), 거미 크기·다리 길이에 따라 얹히는 높이가
    /// 달라진다. <b>보면서 밀어 맞추는 수밖에 없다.</b></para>
    ///
    /// <para>조정 대상은 이미 있는 컴포넌트의 필드다 — 이 패널은 값을 들고 있지 않는다.
    /// 저장하면 <see cref="FilePath"/>에 JSON으로 남고, 다음 Play에서 <b>자동으로 불러온다.</b>
    /// (Play를 끄면 리그가 사라지므로 저장하지 않으면 전부 날아간다.)</para>
    ///
    /// <para>F1 이동 · F2 조명 · F3 포즈 · F4 포즈시퀀스 · F5 글리치가 이미 쓰이고 있어 F6이다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class ViewmodelTuningPanel : MonoBehaviour
    {
        const float PanelWidth = 440f;

        bool _open;
        Vector2 _scroll;
        bool _secHand = true, _secSpider = true, _secLegs, _secCam;

        CursorLockMode _prevLock;
        FirstPersonPlayer _fpp;
        bool _prevLookFrozen;

        MantleRig _mantle;
        SpiderRig _spider;
        SpiderLegs _legs;
        ViewmodelCamera _vmCam;
        FingerPoser _fingerR, _fingerL;
        OneBitControl _oneBit;
        MotionFeel _feel;
        ViewmodelMotion _vmMotion;

        public static string FilePath => "Assets/_Project/Poses/viewmodel_tuning.json";

        [System.Serializable]
        class Saved
        {
            public Vector3 idleLocalR, idleLocalL, idleEulerR, idleEulerL;
            public float idleWeight, idleGripR, idleGripL;
            public MantleRig.IdleFingerPose idleFingerR, idleFingerL;
            public Vector3 idleElbowR, idleElbowL;
            public float gripAmount, thumbCurlScale, fingerSpreadOpen, climbWristFlexDeg, climbWristRollDeg;
            public bool climbDrivesFingers;
            public float maxCurlDeg;
            public Vector3 jointWeights;
            // OneBit 플레이어 세트 — Play 중 조정한 값이 Stop에서 날아가지 않도록 여기에 함께 담는다.
            public Vector3 keyDirView;
            public float keyIntensity, keyFloor, obLevels, obInBlack, obInWhite, obDither, obWrap;
            public float feelMaster = -1f;   // -1 = 저장된 적 없음(0이 유효값이라 0을 가드로 못 쓴다)
            // 진폭은 0이 유효값(끔)이므로 주기로 가드한다 — 주기는 0일 수 없다.
            public float fJitterAmp, fJitterHz, fBreathAmp, fBreathHz, fFwdCurl, fFwdSmooth;
            public float handMotionScale = -1f;   // -1 = 저장된 적 없음(0이 유효값)
            public float vmMotionScale = -1f;     // 뷰모델 절차 모션 전체 세기
            public float swayX, swayY, swayHz, swayRoll, swaySmooth;   // swayHz > 0 이 가드
            public Vector3 handEulerR, handEulerL;
            public float gazeAmount, gazeMaxDeg;
            public float anchorRadius, anchorSpread;
            public float nearClip, vrNearClip;
            public bool captured;
        }

        void Awake() { _fpp = FindFirstObjectByType<FirstPersonPlayer>(); }

        void Start() { FindTargets(); Load(); }

        void FindTargets()
        {
            if (_mantle == null) _mantle = FindFirstObjectByType<MantleRig>();
            if (_spider == null) _spider = FindFirstObjectByType<SpiderRig>();
            if (_legs   == null) _legs   = FindFirstObjectByType<SpiderLegs>();
            if (_vmCam  == null) _vmCam  = FindFirstObjectByType<ViewmodelCamera>();
            if (_feel   == null) _feel   = FindFirstObjectByType<MotionFeel>();
            if (_vmMotion == null) _vmMotion = FindFirstObjectByType<ViewmodelMotion>();
            // 손가락 값은 MantleRig이 참조하는 것과 같은 것을 만져야 한다 — 씬에서 따로 찾으면 어긋난다.
            if (_mantle != null) { if (_fingerR == null) _fingerR = _mantle.fingerR; if (_fingerL == null) _fingerL = _mantle.fingerL; }
            // 플레이어 세트를 구동하는 것만 잡는다 — 해킹 대상 컨트롤을 잡으면 엉뚱한 값을 만진다.
            if (_oneBit == null)
                foreach (var c in FindObjectsByType<OneBitControl>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (c.channel == OneBitChannel.Player) { _oneBit = c; break; }
        }

        // ── 흔들림 진단 ────────────────────────────────────────────────────
        // "팔이 흔들린다"의 정체를 가른다. 손이 <b>카메라 기준으로</b> 움직이는지, 아니면 카메라
        // 자체가 월드에서 흔들리고 손은 그냥 따라가는지 — 둘은 고쳐야 할 곳이 완전히 다르다.
        Vector3 _handMin, _handMax;
        float _camYMin, _camYMax;
        float _windowT;
        string _diagHand = "-", _diagCam = "-";

        void SampleShake()
        {
            var cam = Camera.main;
            if (cam == null || _mantle == null || _mantle.handIkR == null || _mantle.handIkR.end == null) return;

            Vector3 local = cam.transform.InverseTransformPoint(_mantle.handIkR.end.position);
            float camY = cam.transform.position.y;

            if (_windowT <= 0f)
            {
                _handMin = _handMax = local;
                _camYMin = _camYMax = camY;
            }
            else
            {
                _handMin = Vector3.Min(_handMin, local); _handMax = Vector3.Max(_handMax, local);
                _camYMin = Mathf.Min(_camYMin, camY);    _camYMax = Mathf.Max(_camYMax, camY);
            }

            _windowT += Time.deltaTime;
            if (_windowT >= 1f)
            {
                Vector3 d = (_handMax - _handMin) * 1000f;   // mm
                _diagHand = $"{d.x:0} / {d.y:0} / {d.z:0} mm";
                _diagCam  = $"{(_camYMax - _camYMin) * 1000f:0} mm";
                _windowT = 0f;
            }
        }

        void Update()
        {
            if (_open) SampleShake();

            var kb = Keyboard.current;
            if (kb == null || !kb.f6Key.wasPressedThisFrame) return;

            _open = !_open;
            if (_open)
            {
                FindTargets();
                _prevLock = Cursor.lockState;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                if (_fpp != null) { _prevLookFrozen = _fpp.LookFrozen; _fpp.LookFrozen = true; }
            }
            else
            {
                Cursor.lockState = _prevLock;
                Cursor.visible = _prevLock != CursorLockMode.Locked;
                if (_fpp != null) _fpp.LookFrozen = _prevLookFrozen;
            }
        }

        void OnGUI()
        {
            if (!_open) return;

            GUILayout.BeginArea(new Rect(Screen.width - PanelWidth - 12f, 12f, PanelWidth, Screen.height - 24f), GUI.skin.box);
            GUILayout.Label("<b>1인칭 손 · 거미 조정 (F6)</b>", Rich());

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("저장")) Save();
            if (GUILayout.Button("불러오기")) Load();
            if (GUILayout.Button("대상 다시 찾기")) FindTargets();
            GUILayout.EndHorizontal();

            _scroll = GUILayout.BeginScrollView(_scroll);
            DrawHand();
            DrawSpider();
            DrawLegs();
            DrawCam();
            GUILayout.EndScrollView();

            GUILayout.Label($"<size=10>{FilePath}</size>", Rich());
            GUILayout.EndArea();
        }

        // ── 손 ──────────────────────────────────────────────────────────
        void DrawHand()
        {
            if (!Section("오른손 · 왼손", ref _secHand)) return;

            // ★ 값이 안 먹는 이유는 대개 "슬라이더가 고장"이 아니라 <b>연결이 없거나 소유권이 넘어간 것</b>이다.
            //   조용히 아무 일도 안 일어나면 원인을 못 찾으므로, 상태를 화면에 드러낸다.
            if (_mantle == null)
            {
                Info("<b>MantleRig이 없습니다.</b> 이 씬에는 플레이어 손 리그가 없습니다 — " +
                     "손은 <b>ViewmodelStudio</b> 씬에만 있습니다. 위 '대상 다시 찾기'를 눌러도 안 잡히면 씬이 잘못된 것입니다.");
                return;
            }
            if (_mantle.handIkR == null || _mantle.fingerR == null)
            {
                Info("<b>손을 못 찾았습니다</b> — HandIK " + (_mantle.handIkR == null ? "없음" : "있음") +
                     " / FingerPoser " + (_mantle.fingerR == null ? "없음" : "있음") +
                     ". 이 상태에서는 아래 값을 바꿔도 <b>아무것도 움직이지 않습니다.</b>");
            }
            if (!_mantle.driveIdlePose)
                Info("<b>'기본 자세를 IK로 잡기'가 꺼져 있습니다</b> — 아래 손 위치·회전 값이 무시됩니다.");
            if (_mantle.handleMode)
                Info("<b>'씬에서 직접 잡기'가 켜져 있습니다</b> — 손·팔꿈치 트랜스폼의 주인이 사람이라서 " +
                     "아래 <b>위치·회전 슬라이더가 동작하지 않습니다.</b> 슬라이더로 맞추시려면 끄십시오.");

            _mantle.driveIdlePose = GUILayout.Toggle(_mantle.driveIdlePose, " 기본 자세를 IK로 잡기 (끄면 모델 T포즈)");
            _mantle.idleWeight = F("IK 가중치", _mantle.idleWeight, 0f, 1f);
            // ★ 여기가 낮으면 아래 값을 아무리 만져도 손이 거의 안 움직인다. 증상이 "슬라이더가 고장",
            //   "공중에서 손이 굳음", "등반 후 자세가 달라짐"으로 제각기 나타나 원인을 찾기 어렵다.
            if (_mantle.idleWeight < 0.9f)
                Info("<b>⚠ IK 가중치가 " + _mantle.idleWeight.ToString("0.##") + "입니다.</b> 손이 IK 목표를 " +
                     Mathf.RoundToInt(_mantle.idleWeight * 100f) + "%만 따르고 나머지는 <b>모델 쉬는 자세</b>로 남습니다. " +
                     "위치·회전을 바꿔도 거의 안 움직이고, 손바닥 방향도 모델 기본값이 이깁니다. " +
                     "특별한 이유가 없으면 <b>1</b>로 두십시오.");

            // 씬에서 직접 끄는 편이 슬라이더 열몇 개를 더듬는 것보다 빠르다.
            bool hm = GUILayout.Toggle(_mantle.handleMode, " <b>씬에서 직접 잡기</b> (기즈모로 끌기)");
            if (hm != _mantle.handleMode) _mantle.handleMode = hm;
            if (_mantle.handleMode)
            {
                Info("Hierarchy에서 <b>[IdleHandR]</b>(손) · <b>[IdleElbowR]</b>(팔꿈치)를 골라 씬 뷰에서 끄십시오. " +
                     "Game 뷰가 실시간으로 따라옵니다. 다 잡으면 아래 버튼을 누르십시오.");
                if (GUILayout.Button("현재 자세를 값으로 굳히기")) { _mantle.CaptureIdleFromScene(); _mantle.handleMode = false; }
            }

            GUILayout.Label("<b>오른손 — 오른쪽 아래에서 들어와 손등이 보인다</b>", Rich());
            _mantle.idleLocalR = V3("  손 위치", _mantle.idleLocalR, -0.8f, 0.8f);
            _mantle.idleEulerR = V3("  손 회전", _mantle.idleEulerR, -180f, 180f);
            _mantle.idleElbowR = V3("  팔꿈치", _mantle.idleElbowR, -1f, 1f);
            _mantle.idleGripR  = F("  공통 말림", _mantle.idleGripR, 0f, 1f);
            Fingers("  R", _mantle.idleFingerR);

            GUILayout.Label("<b>왼손 — 평소엔 화면 밖</b>", Rich());
            _mantle.idleLocalL = V3("  손 위치", _mantle.idleLocalL, -0.8f, 0.8f);
            _mantle.idleEulerL = V3("  손 회전", _mantle.idleEulerL, -180f, 180f);
            _mantle.idleElbowL = V3("  팔꿈치", _mantle.idleElbowL, -1f, 1f);
            _mantle.idleGripL  = F("  공통 말림", _mantle.idleGripL, 0f, 1f);
            Fingers("  L", _mantle.idleFingerL);

            GUILayout.Label("<b>등반 손 (턱을 잡을 때만)</b>", Rich());
            _mantle.climbWristFlexDeg  = F("  손목 꺾임°", _mantle.climbWristFlexDeg, 0f, 40f);
            _mantle.climbWristRollDeg  = F("  손목 롤°", _mantle.climbWristRollDeg, 0f, 40f);
            _mantle.climbDrivesFingers = GUILayout.Toggle(_mantle.climbDrivesFingers, " 손가락도 절차적으로 쥐기");
            if (!_mantle.climbDrivesFingers)
                Info("꺼짐 — 등반 중에도 <b>평상시 손 모양이 그대로</b> 유지됩니다. 아래 값은 동작하지 않습니다.");
            else
            {
                _mantle.gripAmount       = F("  쥐는 세기", _mantle.gripAmount, 0f, 1f);
                _mantle.thumbCurlScale   = F("  엄지 비율", _mantle.thumbCurlScale, 0f, 1f);
                _mantle.fingerSpreadOpen = F("  편 손 벌림", _mantle.fingerSpreadOpen, -1f, 1f);
                if (_fingerR != null)
                {
                    _fingerR.maxCurlDeg   = F("  최대 말림°", _fingerR.maxCurlDeg, 20f, 110f);
                    _fingerR.jointWeights = V3("  마디 비율", _fingerR.jointWeights, 0f, 1.2f);
                    if (_fingerL != null) { _fingerL.maxCurlDeg = _fingerR.maxCurlDeg; _fingerL.jointWeights = _fingerR.jointWeights; }
                }
            }

            // ★ 걸을 때 팔이 흔들리는 것의 <b>실제 출처</b>. 씬에 없고 런타임에 [PlayerBody]에 붙어
            //   인스펙터로는 찾기 어렵다. 뷰모델 루트를 흔들어 어깨가 움직이고 팔 전체가 따라온다.
            if (_vmMotion != null)
            {
                GUILayout.Label("<b>★ 뷰모델 절차 모션 (걷기 흔들림의 출처)</b>", Rich());
                _vmMotion.masterScale = F("  전체 세기", _vmMotion.masterScale, 0f, 1f);
                Info("호흡 · 걸음 bob · 스트레이프 · 공중 · 착지 · 스웨이 <b>여섯 레이어 전부</b>에 곱해집니다. " +
                     "0이면 뷰모델이 완전히 고정됩니다. 이게 걸을 때 팔이 좌우·위아래로 흔들리는 그 값입니다.");
            }
            else Info("<b>ViewmodelMotion을 못 찾았습니다</b> — 걷기 흔들림을 조절할 수 없습니다.");

            GUILayout.Label("<b>★ 걷기 흔들림 (걸을 때 팔이 좌우·위아래로)</b>", Rich());
            _mantle.walkSwayX       = F("  좌우 진폭m", _mantle.walkSwayX, 0f, 0.15f);
            _mantle.walkSwayY       = F("  상하 진폭m", _mantle.walkSwayY, 0f, 0.15f);
            _mantle.walkSwayHz      = F("  걸음 주기Hz", _mantle.walkSwayHz, 0.2f, 3f);
            _mantle.walkSwayRollDeg = F("  기울임°", _mantle.walkSwayRollDeg, 0f, 6f);
            _mantle.walkSwaySmooth  = F("  붙고 잦아드는 시간", _mantle.walkSwaySmooth, 0f, 1f);
            Info("<b>점프와 분리돼 있습니다</b> — 여기를 줄여도 점프에서 손이 내려가는 깊이는 그대로입니다. " +
                 "상하는 좌우의 2배 주기로 돌아 한 걸음마다 한 번 내려앉습니다. 속도에 비례하고 멈추면 잦아듭니다.");

            GUILayout.Label("<b>손 절차 동작 전체 세기</b>", Rich());
            _mantle.handMotionScale = F("  전체", _mantle.handMotionScale, 0f, 1f);
            Info("걷기 흔들림 + 공중 파킹 + 손가락 미세 동작 <b>전부</b>에 곱해집니다. 0이면 손이 시점에 완전히 고정됩니다.\n" +
                 "아래 '절차 연출 세기'는 <b>카메라</b>(딥·킥·롤·FOV)입니다 — 계통이 달라 서로 안 줄입니다.");

            GUILayout.Label("<b>흔들림 진단 (걸으면서 보십시오 — 최근 1초 변동폭)</b>", Rich());
            Info("손 (카메라 기준) : <b>" + _diagHand + "</b>\n카메라 높이 (월드) : <b>" + _diagCam + "</b>");
            // 점프에서 손이 안 내려갈 때 <b>어느 입력이 실패했는지</b> 이 줄이 바로 말해 준다.
            Info("접지 <b>" + (_fpp != null && _fpp.Grounded ? "O" : "X") + "</b>" +
                 "  ·  체공 <b>" + _mantle.DebugAirTime.ToString("0.00") + "s</b>" +
                 " (기준 " + _mantle.airParkDelay.ToString("0.00") + ")" +
                 "  ·  내려간 정도 <b>" + _mantle.DebugAirBlend.ToString("0.00") + "</b>" +
                 "  ·  단계 <b>" + _mantle.Current + "</b>");
            if (_mantle.handIkR != null)
            {
                var ikr = _mantle.handIkR;
                Info("실제 IK 가중치 <b>" + ikr.weight.ToString("0.00") + "</b>" +
                     " (평상시 " + _mantle.idleWeight.ToString("0.00") + " → 파킹하면 1로 올라갑니다)");
                bool swOver = ikr.LastSwingDeg > ikr.wristMaxSwing - 1f;
                bool twOver = ikr.LastTwistDeg > ikr.wristMaxTwist - 1f;
                Info("손목 요구각 — 스윙 <b>" + ikr.LastSwingDeg.ToString("0") + "°</b>/" + ikr.wristMaxSwing.ToString("0") +
                     (swOver ? " <b>한계</b>" : "") +
                     "  ·  트위스트 <b>" + ikr.LastTwistDeg.ToString("0") + "°</b>/" + ikr.wristMaxTwist.ToString("0") +
                     (twOver ? " <b>한계</b>" : ""));
                if (swOver || twOver)
                    Info("<b>손목이 클램프 한계에 붙어 있습니다</b> — 목표 회전이 손 뼈의 쉬는 자세에서 너무 멉니다. " +
                         "한계를 올리는 게 아니라 <b>손 뼈 축 보정(handEulerR)</b>으로 기준을 맞춰야 합니다. 지금 " +
                         _mantle.handEulerR.ToString("0") + " 입니다.");
                _mantle.handEulerR = V3("  R 축 보정", _mantle.handEulerR, -180f, 180f);
            }
            Info("<b>내려간 정도</b>가 1인데도 손이 안 내려가 보이면 IK 가중치를 보십시오 — " +
                 "가중치가 낮으면 목표를 43cm 내려도 손은 그 비율만큼만 따라갑니다. " +
                 "내려간 정도가 <b>0</b>이면 체공이 기준보다 짧거나, 단계가 <b>평상시가 아닌</b> 것입니다.");
            Info("손 값이 <b>거의 0</b>이면 팔은 시점에 붙어 있고 흔들리는 것은 <b>카메라</b>입니다 — " +
                 "그때는 절차 연출이 아니라 <b>카메라 높이</b>를 보십시오. 걸을 때 그 값이 크면 " +
                 "CharacterController가 바닥을 타며 튀는 것이라 절차 세기로는 안 줄어듭니다. " +
                 "반대로 손 값이 크면 무언가가 손을 직접 움직이는 것입니다.");

            GUILayout.Label("<b>손가락 미세 동작 (평상시 · 등반 중에도 유지)</b>", Rich());

            // "손가락이 안 구부러진다"는 두 가지로 갈린다 — 뼈를 못 찾아 <b>아예 안 도는</b> 경우와,
            // 값이 작아 <b>각도가 안 나오는</b> 경우. 화면에 실제 각도를 띄워 그 자리에서 가른다.
            if (_fingerR != null)
            {
                float total = Mathf.Clamp01(_mantle.idleGripR + _mantle.idleFingerR.index);
                Info("오른손 FingerPoser : 뼈 <b>" + _fingerR.FoundBones + "/15</b>" +
                     (_fingerR.Ready ? "" : " <b>(못 찾음 — 값을 넣어도 안 움직입니다)</b>") +
                     "  ·  검지 말림 ≈ <b>" + (total * _fingerR.maxCurlDeg * _fingerR.jointWeights.x).ToString("0.0") + "°</b>" +
                     " (공통 " + _mantle.idleGripR.ToString("0.##") + " + 검지 " + _mantle.idleFingerR.index.ToString("0.##") + ")");
                if (_fingerR.Ready && total < 0.15f)
                    Info("말림 값이 작아 손가락이 거의 펴져 있습니다. <b>공통 말림</b>을 올리십시오 — " +
                         "미세 동작(진폭 0.02 ≈ 1.4°)은 그 위에 얹는 것이라 이것만으로는 안 보입니다.");
            }

            _mantle.fingerJitterAmp   = F("  떨림 진폭", _mantle.fingerJitterAmp, 0f, 0.1f);
            _mantle.fingerJitterHz    = F("  떨림 주기Hz", _mantle.fingerJitterHz, 0.05f, 3f);
            _mantle.fingerBreathAmp   = F("  호흡 진폭", _mantle.fingerBreathAmp, 0f, 0.1f);
            _mantle.fingerBreathHz    = F("  호흡 주기Hz", _mantle.fingerBreathHz, 0.01f, 0.5f);
            _mantle.fingerForwardCurl = F("  전진 추가 말림", _mantle.fingerForwardCurl, 0f, 0.2f);
            _mantle.fingerForwardSmooth = F("  전진 스무딩", _mantle.fingerForwardSmooth, 0f, 1f);
            Info("진폭 0.02 ≈ 1.4°입니다. 손가락마다 위상이 어긋나 있어 함께 움찔거리지 않습니다. " +
                 "전진은 <b>정면으로 갈 때만</b> 걸립니다(뒤·옆 제외). 진폭을 0으로 두면 완전히 꺼집니다.");

            if (_feel != null)
            {
                GUILayout.Label("<b>절차 연출 세기 (흔들림·킥·롤 전체)</b>", Rich());
                _feel.masterScale = F("  전체 세기", _feel.masterScale, 0f, 1f);
                Info("0 = 완전히 끔. 점프 발구름·착지 딥·롤 킥·실려가기 스웨이가 <b>한꺼번에</b> 줄어듭니다. " +
                     "VR 배율과 곱해집니다.");
            }

            // 손은 씬 조명을 안 읽는다(OneBit `_FixedLight`). 그래서 손 보기는 여기서만 정해진다 —
            // 조명 패널(F2)을 만져도 손은 안 변한다. 같은 화면에 둬야 헷갈리지 않는다.
            if (_oneBit != null)
            {
                GUILayout.Label("<b>손 보기 — 고정 키 조명 (씬 조명과 무관)</b>", Rich());
                _oneBit.keyDirView   = V3("  키 방향", _oneBit.keyDirView, -1f, 1f);
                _oneBit.keyIntensity = F("  키 세기", _oneBit.keyIntensity, 0f, 4f);
                _oneBit.keyFloor     = F("  뒷면 바닥", _oneBit.keyFloor, 0f, 1f);
                _oneBit.levels       = F("  계단 수", _oneBit.levels, 2f, 8f);
                _oneBit.inBlack      = F("  검정점", _oneBit.inBlack, 0f, 1f);
                _oneBit.inWhite      = F("  흰색점", _oneBit.inWhite, 0f, 1f);
                _oneBit.dither       = F("  디더", _oneBit.dither, 0f, 1f);
                _oneBit.lightWrap    = F("  랩", _oneBit.lightWrap, 0f, 1f);
                Info("메시 결함을 감추려면 <b>계단 수</b>를 내리십시오(3~4). 밝기를 낮추면 형태까지 같이 사라집니다.");
            }

            // 실제로는 BuildBasis()에서만 쓰인다 — 즉 <b>등반 자세에만</b> 걸린다. 평상시 손 회전은
            // idleEulerR이 통째로 정하므로 이 값의 영향을 받지 않는다. 라벨이 반대로 적혀 있었다.
            GUILayout.Label("<b>손 뼈 축 보정 (등반 자세에만 적용)</b>", Rich());
            _mantle.handEulerR = V3("  R 보정", _mantle.handEulerR, -180f, 180f);
            _mantle.handEulerL = V3("  L 보정", _mantle.handEulerL, -180f, 180f);
            Info("등반 중 <b>손바닥이 위를 보는</b> 문제가 이 값입니다. 멈춰 놓고 보려면 <b>F7</b>에서 만지십시오 — 같은 값입니다.");

            Info("손이 뒤집혀 보이면 <b>회전</b>이 아니라 <b>축 보정</b>을 만지십시오 — 리그마다 손 뼈가 보는 축이 다릅니다.");
        }

        // ── 거미 ────────────────────────────────────────────────────────
        void DrawSpider()
        {
            if (!Section("거미 — 응시", ref _secSpider)) return;
            if (_spider == null) { Info("SpiderRig가 없다."); return; }

            _spider.gazeAmount = F("응시 세기", _spider.gazeAmount, 0f, 1f);
            _spider.gazeMaxDeg = F("최대 각도(°)", _spider.gazeMaxDeg, 0f, 120f);
            _spider.stabilize  = GUILayout.Toggle(_spider.stabilize, " 수평 안정화 (독수리)");

            if (_spider.perchAnchor != null)
            {
                GUILayout.Label("<b>얹히는 자리 (손목 기준 로컬)</b>", Rich());
                Vector3 p = _spider.perchAnchor.localPosition;
                Vector3 np = V3("  위치", p, -0.3f, 0.3f);
                if (np != p) _spider.perchAnchor.localPosition = np;
            }
            else Info("perchAnchor가 비어 있다 — SpiderRig가 R_Hand 아래에 자동 생성한다.");

            Info("각도를 넘으면 거기서 멈춥니다. 그 '못 따라오는' 느낌이 오히려 생물처럼 보입니다.");
        }

        // ── 다리 ────────────────────────────────────────────────────────
        void DrawLegs()
        {
            if (!Section("거미 다리 — 팔에 붙는 지점", ref _secLegs)) return;
            if (_legs == null) { Info("SpiderLegs가 없다."); return; }

            _legs.weight       = F("다리 IK 가중치", _legs.weight, 0f, 1f);
            _legs.anchorRadius = F("팔 둘레 반지름(m)", _legs.anchorRadius, 0.005f, 0.12f);
            _legs.anchorSpread = F("팔 축 간격(m)",    _legs.anchorSpread, 0.005f, 0.2f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("앵커 다시 만들기")) _legs.CreateAnchors();
            if (GUILayout.Button("길이 다시 측정")) _legs.Measure();
            GUILayout.EndHorizontal();

            Info("반지름·간격을 바꾼 뒤에는 <b>앵커 다시 만들기</b>를 눌러야 반영됩니다.");
        }

        // ── 카메라 ──────────────────────────────────────────────────────
        void DrawCam()
        {
            if (!Section("근평면 (손가락 잘림)", ref _secCam)) return;
            if (_vmCam == null) { Info("ViewmodelCamera가 없다."); return; }

            Info(_vmCam.Status);
            _vmCam.nearClip   = F("PC 오버레이 근평면", _vmCam.nearClip,   0.005f, 0.3f);
            _vmCam.vrNearClip = F("VR 메인 근평면",     _vmCam.vrNearClip, 0.01f,  0.3f);
            _vmCam.vrFarClip  = F("VR 원평면",          _vmCam.vrFarClip,  50f,    1000f);

            Info("손가락이 잘리면 근평면을 낮추고, z-fighting이 보이면 <b>VR 원평면</b>을 줄이십시오 " +
                 "— 정밀도는 근/원 <b>비율</b>이 지배합니다.");
            if (GUILayout.Button("뷰모델 카메라 값 저장")) _vmCam.Save();
        }

        // ── 저장 ────────────────────────────────────────────────────────
        public bool Save()
        {
            FindTargets();
            var s = new Saved { captured = true };
            if (_mantle != null)
            {
                s.idleLocalR = _mantle.idleLocalR; s.idleLocalL = _mantle.idleLocalL;
                s.idleEulerR = _mantle.idleEulerR; s.idleEulerL = _mantle.idleEulerL;
                s.idleWeight = _mantle.idleWeight;
                s.idleGripR = _mantle.idleGripR;   s.idleGripL = _mantle.idleGripL;
                s.idleFingerR = _mantle.idleFingerR; s.idleFingerL = _mantle.idleFingerL;
                s.idleElbowR = _mantle.idleElbowR; s.idleElbowL = _mantle.idleElbowL;
                s.gripAmount = _mantle.gripAmount; s.thumbCurlScale = _mantle.thumbCurlScale;
                s.fingerSpreadOpen = _mantle.fingerSpreadOpen;
                s.climbWristFlexDeg = _mantle.climbWristFlexDeg; s.climbWristRollDeg = _mantle.climbWristRollDeg;
                s.climbDrivesFingers = _mantle.climbDrivesFingers;
                s.fJitterAmp = _mantle.fingerJitterAmp; s.fJitterHz = _mantle.fingerJitterHz;
                s.fBreathAmp = _mantle.fingerBreathAmp; s.fBreathHz = _mantle.fingerBreathHz;
                s.fFwdCurl = _mantle.fingerForwardCurl; s.fFwdSmooth = _mantle.fingerForwardSmooth;
                s.handMotionScale = _mantle.handMotionScale;
                s.swayX = _mantle.walkSwayX; s.swayY = _mantle.walkSwayY; s.swayHz = _mantle.walkSwayHz;
                s.swayRoll = _mantle.walkSwayRollDeg; s.swaySmooth = _mantle.walkSwaySmooth;
                s.handEulerR = _mantle.handEulerR; s.handEulerL = _mantle.handEulerL;
            }
            if (_fingerR != null) { s.maxCurlDeg = _fingerR.maxCurlDeg; s.jointWeights = _fingerR.jointWeights; }
            s.feelMaster = _feel != null ? _feel.masterScale : -1f;
            s.vmMotionScale = _vmMotion != null ? _vmMotion.masterScale : -1f;
            if (_oneBit != null)
            {
                s.keyDirView = _oneBit.keyDirView; s.keyIntensity = _oneBit.keyIntensity; s.keyFloor = _oneBit.keyFloor;
                s.obLevels = _oneBit.levels; s.obInBlack = _oneBit.inBlack; s.obInWhite = _oneBit.inWhite;
                s.obDither = _oneBit.dither; s.obWrap = _oneBit.lightWrap;
            }
            if (_spider != null) { s.gazeAmount = _spider.gazeAmount; s.gazeMaxDeg = _spider.gazeMaxDeg; }
            if (_legs != null)   { s.anchorRadius = _legs.anchorRadius; s.anchorSpread = _legs.anchorSpread; }
            if (_vmCam != null)  { s.nearClip = _vmCam.nearClip; s.vrNearClip = _vmCam.vrNearClip; }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonUtility.ToJson(s, true), System.Text.Encoding.UTF8);
                Debug.Log("[F6] 저장 → " + FilePath);
                return true;
            }
            catch (System.Exception e) { Debug.LogWarning("[F6] 저장 실패: " + e.Message); return false; }
        }

        public bool Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return false;
                var s = JsonUtility.FromJson<Saved>(File.ReadAllText(FilePath, System.Text.Encoding.UTF8));
                if (s == null || !s.captured) return false;

                if (_mantle != null)
                {
                    _mantle.idleLocalR = s.idleLocalR; _mantle.idleLocalL = s.idleLocalL;
                    _mantle.idleEulerR = s.idleEulerR; _mantle.idleEulerL = s.idleEulerL;
                    _mantle.idleWeight = s.idleWeight;
                    _mantle.idleGripR = s.idleGripR;   _mantle.idleGripL = s.idleGripL;
                    // 옛 저장 파일엔 손가락별 값이 없다 — null이면 코드 기본값을 유지한다.
                    if (s.idleFingerR != null) _mantle.idleFingerR = s.idleFingerR;
                    if (s.idleFingerL != null) _mantle.idleFingerL = s.idleFingerL;
                    // 팔꿈치·등반 값도 옛 파일엔 없다. 0이면 저장된 적이 없는 것이므로 기본값을 지킨다.
                    if (s.idleElbowR != Vector3.zero) _mantle.idleElbowR = s.idleElbowR;
                    if (s.idleElbowL != Vector3.zero) _mantle.idleElbowL = s.idleElbowL;
                    if (s.gripAmount > 0f)
                    {
                        _mantle.gripAmount = s.gripAmount; _mantle.thumbCurlScale = s.thumbCurlScale;
                        _mantle.fingerSpreadOpen = s.fingerSpreadOpen;
                        _mantle.climbWristFlexDeg = s.climbWristFlexDeg; _mantle.climbWristRollDeg = s.climbWristRollDeg;
                        _mantle.climbDrivesFingers = s.climbDrivesFingers;
                    }
                    if (s.fJitterHz > 0.01f)
                    {
                        _mantle.fingerJitterAmp = s.fJitterAmp; _mantle.fingerJitterHz = s.fJitterHz;
                        _mantle.fingerBreathAmp = s.fBreathAmp; _mantle.fingerBreathHz = s.fBreathHz;
                        _mantle.fingerForwardCurl = s.fFwdCurl; _mantle.fingerForwardSmooth = s.fFwdSmooth;
                    }
                    if (s.handMotionScale >= 0f) _mantle.handMotionScale = s.handMotionScale;
                    if (s.swayHz > 0.01f)
                    {
                        _mantle.walkSwayX = s.swayX; _mantle.walkSwayY = s.swayY; _mantle.walkSwayHz = s.swayHz;
                        _mantle.walkSwayRollDeg = s.swayRoll; _mantle.walkSwaySmooth = s.swaySmooth;
                    }
                    _mantle.handEulerR = s.handEulerR; _mantle.handEulerL = s.handEulerL;
                }
                if (_fingerR != null && s.maxCurlDeg > 0f)
                {
                    _fingerR.maxCurlDeg = s.maxCurlDeg; _fingerR.jointWeights = s.jointWeights;
                    if (_fingerL != null) { _fingerL.maxCurlDeg = s.maxCurlDeg; _fingerL.jointWeights = s.jointWeights; }
                }
                // ★ 거미가 꺼져 있는 채로 저장하면 0이 들어간다. 가드 없이 되돌리면 응시가 죽는다.
                if (_spider != null && s.gazeMaxDeg > 0f) { _spider.gazeAmount = s.gazeAmount; _spider.gazeMaxDeg = s.gazeMaxDeg; }
                if (_feel != null && s.feelMaster >= 0f) _feel.masterScale = s.feelMaster;
                if (_vmMotion != null && s.vmMotionScale >= 0f) _vmMotion.masterScale = s.vmMotionScale;
                if (_oneBit != null && s.obLevels >= 2f)
                {
                    _oneBit.keyDirView = s.keyDirView; _oneBit.keyIntensity = s.keyIntensity; _oneBit.keyFloor = s.keyFloor;
                    _oneBit.levels = s.obLevels; _oneBit.inBlack = s.obInBlack; _oneBit.inWhite = s.obInWhite;
                    _oneBit.dither = s.obDither; _oneBit.lightWrap = s.obWrap;
                }
                if (_legs != null && s.anchorRadius > 0f)
                {
                    _legs.anchorRadius = s.anchorRadius; _legs.anchorSpread = s.anchorSpread;
                    _legs.CreateAnchors();
                }
                if (_vmCam != null && s.nearClip > 0f) { _vmCam.nearClip = s.nearClip; _vmCam.vrNearClip = s.vrNearClip; }
                Debug.Log("[F6] 불러옴");
                return true;
            }
            catch { return false; }
        }

        // ── GUI 헬퍼 ────────────────────────────────────────────────────

        static GUIStyle _rich;
        static GUIStyle Rich()
        {
            if (_rich == null) _rich = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
            return _rich;
        }

        /// <summary>
        /// 손가락별 <b>추가</b> 말림. 공통 말림에 더해지므로, 가장 펴진 손가락은 0으로 두고
        /// 나머지를 올린다. 다섯이 같으면 집게처럼 보인다.
        /// </summary>
        static void Fingers(string prefix, MantleRig.IdleFingerPose p)
        {
            if (p == null) return;
            p.index  = F(prefix + " 검지", p.index,  0f, 1f);
            p.middle = F(prefix + " 중지", p.middle, 0f, 1f);
            p.ring   = F(prefix + " 약지", p.ring,   0f, 1f);
            p.pinky  = F(prefix + " 소지", p.pinky,  0f, 1f);
            p.thumb  = F(prefix + " 엄지", p.thumb,  0f, 1f);
            p.spread = F(prefix + " 벌림", p.spread, -1f, 1f);
        }

        static bool Section(string title, ref bool open)
        {
            GUILayout.Space(4f);
            open = GUILayout.Toggle(open, (open ? "▼ " : "▶ ") + title, GUI.skin.button);
            return open;
        }

        static void Info(string s) => GUILayout.Label($"<size=11>{s}</size>", Rich());

        static float F(string label, float v, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, Rich(), GUILayout.Width(160f));
            GUILayout.Label(v.ToString("0.###"), Rich(), GUILayout.Width(52f));
            float r = GUILayout.HorizontalSlider(v, min, max);
            GUILayout.EndHorizontal();
            return r;
        }

        static Vector3 V3(string label, Vector3 v, float min, float max)
        {
            GUILayout.Label(label, Rich());
            return new Vector3(
                F("    X", v.x, min, max),
                F("    Y", v.y, min, max),
                F("    Z", v.z, min, max));
        }
    }

    /// <summary>Play 시작 시 자동 부착 — 씬에 오브젝트를 놓지 않아도 F6이 동작한다.</summary>
    public static class ViewmodelTuningPanelBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<ViewmodelTuningPanel>() == null)
                new GameObject("[ViewmodelTuningPanel]").AddComponent<ViewmodelTuningPanel>();
        }
    }
}
