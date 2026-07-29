using UnityEngine;
using MindHexer.Shared.Protocol;

namespace Game.View
{
    /// <summary>
    /// <see cref="ControllerLink"/>가 받은 원시 입력을 게임 조작으로 해석한다.
    /// 컨트롤러는 눈먼 터치패드라 <b>판정이 전부 여기</b>에 있다 — 감도·데드존 같은 값이 폰 APK에
    /// 박히면 조정할 때마다 재빌드해야 하기 때문이다.
    ///
    /// <para>A2 범위: <b>이동(왼쪽 절반 플로팅 조이스틱)</b> + <b>시점(기기 자세)</b>.
    /// 해킹·부품 조종(A3)은 아직 붙이지 않는다.</para>
    ///
    /// <para><b>시점을 자이로에 붙이는 건 에디터 한정 대역</b>이다. 실기에선 헤드셋이 시점을
    /// 소유하므로 <see cref="useGyroLook"/>을 꺼야 한다 — 안 그러면 머리와 폰이 시점을 두고 싸운다.</para>
    ///
    /// <para><b>자세→시점은 절대각이 아니라 델타</b>로 간다. 기기 자세 쿼터니언의 절대 프레임은
    /// 화면 방향·기기마다 달라 맞히기 어렵지만, 델타는 "폰을 왼쪽으로 돌렸다"가 일관되게 나온다.
    /// 축이 뒤집히거나 바뀌는 경우는 <see cref="invertYaw"/>·<see cref="invertPitch"/>·
    /// <see cref="swapAxes"/>로 실기에서 잡는다 — 프레임 규약을 책상에서 추측하지 않는다.</para>
    /// </summary>
    [DefaultExecutionOrder(-150)]   // ControllerLink(-200) 다음, HackDriver·FirstPersonPlayer 이전
    public sealed class ControllerDriver : MonoBehaviour
    {
        [Header("연결")]
        [Tooltip("비우면 씬에서 찾는다.")]
        public FirstPersonPlayer player;

        [Tooltip("비우면 씬에서 찾는다. 여기에 네트워크 입력 소스를 꽂는다.")]
        public HackDriver hackDriver;

        [Tooltip("끄면 컨트롤러가 해킹·조종 입력을 내지 않는다(이동·시점만). 문제 분리용.")]
        public bool driveHackInput = true;

        [Header("이동 — 왼쪽 절반 플로팅 조이스틱")]
        [Tooltip("최대 입력이 나오는 반경(mm). 픽셀이 아니라 물리 거리로 잡아야 기기가 바뀌어도 같은 느낌이다.")]
        public float joystickRadiusMm = 14f;

        [Tooltip("데드존(반경 대비 비율). 엄지를 얹어두기만 해도 걷는 것을 막는다.")]
        [Range(0f, 0.5f)] public float joystickDeadZone = 0.12f;

        [Tooltip("엄지가 반경 밖으로 나가면 중심이 따라온다. 화면이 안 보이므로 켜두는 게 안전하다 " +
                 "— 끄면 최대 입력에 붙은 채 안 돌아온다.")]
        public bool followOnOverflow = true;

        [Header("시점 — 기기 자세 (에디터 전용)")]
        [Tooltip("실기(헤드셋)에선 반드시 끌 것. 머리와 폰이 시점을 두고 싸운다.")]
        public bool useGyroLook = true;

        [Tooltip("자세 변화 1도당 마우스 픽셀 상당. FirstPersonPlayer.lookSens가 다시 곱해진다.")]
        public float gyroLookGain = 12f;

        [Tooltip("한 프레임 최대 시점 변화(도). 추적이 튈 때 화면이 홱 돌아가는 것을 막는다.")]
        public float gyroMaxStepDeg = 25f;

        [Tooltip("자세 평활 시정수(초). 컨트롤러는 30Hz인데 에디터는 60fps 이상이라, 원본을 그대로 쓰면 " +
                 "패킷이 없는 프레임엔 델타가 0이었다가 다음 프레임에 몰려 계단처럼 끊긴다. " +
                 "이 값만큼 지연이 생기는 대신 매 프레임 균등한 델타가 나온다. 0이면 평활 없음.")]
        public float lookSmoothTime = 0.05f;

        public bool invertYaw;
        [Tooltip("실기 확인 결과 상하가 반대여서 기본 true.")]
        public bool invertPitch = true;
        public bool swapAxes;

        [Header("패턴 — 적응형 격자")]
        [Tooltip("여유 공간 대비 안전계수. 1에 가까울수록 격자가 커지지만 가장자리에서 손이 모자란다.")]
        [Range(0.3f, 1f)] public float patternSafety = 0.6f;

        [Tooltip("격자 한 칸의 최소 물리 크기(mm). 히트 반경이 0.16유닛이라 이 값 × 0.16이 엄지 " +
                 "위치 재현 정밀도(약 2mm)보다 커야 원리적으로 맞출 수 있다. 커밋 시 커서 스냅이 " +
                 "획마다 오차를 초기화해주므로 예전보다 낮춰 잡을 수 있다.")]
        public float patternMinScaleMm = 10f;

        [Tooltip("격자 한 칸의 최대 물리 크기(mm). 너무 크면 한 획에 손이 모자란다.")]
        public float patternMaxScaleMm = 20f;

        [Tooltip("패턴 커서 이동 평활 시정수(초). 컨트롤러 30Hz를 60fps 화면에 그대로 먹이면 " +
                 "커서가 격프레임으로 튄다. 총 이동량은 보존한 채 프레임에 고르게 나눈다. 0이면 평활 없음.")]
        public float patternSmoothTime = 0.04f;

        [Tooltip("DPI를 못 받았을 때 쓰는 값(mm/유닛).")]
        public float patternFallbackScaleMm = 16f;

        // ── 진단(튜닝 패널이 읽는다) ──────────────────────────────────────
        public Vector2 JoystickValue { get; private set; }   // -1..1 디스크
        public bool JoystickActive { get; private set; }
        public Vector2 LookDeltaDeg { get; private set; }
        public float ScreenMmX { get; private set; }
        public float ScreenMmY { get; private set; }

        Vector2 _joyCenterNorm;      // 플로팅 중심(정규화)
        int _joySlot = -1;
        bool _hasPrevRot;
        Quaternion _prevRot = Quaternion.identity;

        float _nextPlayerScan;

        /// <summary>
        /// <see cref="FirstPersonPlayer"/>는 <see cref="GameBoot"/>이 런타임에 카메라에 붙인다 —
        /// Awake에서 찾으면 생성 순서에 걸려 null이 잡힌다. 잡힐 때까지 주기적으로 다시 찾는다.
        /// </summary>
        void ResolvePlayer()
        {
            if (player != null) return;
            if (Time.unscaledTime < _nextPlayerScan) return;
            _nextPlayerScan = Time.unscaledTime + 0.5f;
            player = FindFirstObjectByType<FirstPersonPlayer>();
            if (player != null) Debug.Log("[ControllerDriver] FirstPersonPlayer 연결됨: " + player.name);
        }

        NetworkHexInputSource _netSource;
        float _nextHackScan;

        /// <summary>
        /// <see cref="HackDriver"/>에 네트워크 입력 소스를 꽂는다. <see cref="GameBoot"/>은 VR 모드에서만
        /// 이 배선을 하므로 에디터(PC 경로)에선 아무도 안 꽂아준다 — 그래서 여기서 한다.
        ///
        /// <para>기존 PC 소스를 <b>버리지 않고 합친다</b>. 폰으로 조작하면서 키보드·마우스가 계속
        /// 살아 있어야 이상할 때 원인을 가를 수 있다(<see cref="CompositeHexInputSource"/>).</para>
        /// </summary>
        void ResolveHackDriver()
        {
            if (!driveHackInput) return;
            if (_netSource != null) return;
            if (Time.unscaledTime < _nextHackScan) return;
            _nextHackScan = Time.unscaledTime + 0.5f;

            if (hackDriver == null) hackDriver = FindFirstObjectByType<HackDriver>();
            if (hackDriver == null) return;

            // 리센터는 FreezeControlMapping과 <b>같은 순간</b>이어야 한다 — 다른 순간에 원점을
            // 잡으면 슬롯↔축 배정이 고정된 기준과 어긋나 손 방향과 부품 축이 안 맞는다.
            hackDriver.OnControlledChanged += OnControlledChanged;

            _netSource = new NetworkHexInputSource { Map = MapInput };

            // 이미 합성돼 있으면(재진입) 중복으로 감싸지 않는다.
            if (hackDriver.Source is CompositeHexInputSource comp) comp.B = _netSource;
            else hackDriver.Source = new CompositeHexInputSource(hackDriver.Source, _netSource);

            Debug.Log("[ControllerDriver] HackDriver에 네트워크 입력 소스 연결");
        }

        [Tooltip("VR 리그를 직접 옮길 때의 이동 속도(m/s). FirstPersonPlayer가 없을 때만 쓴다.")]
        public float rigMoveSpeed = 3.5f;

        Transform _rig;

        /// <summary>
        /// VR 모드엔 <see cref="FirstPersonPlayer"/>가 없다 — <see cref="GameBoot.SetupVrRig"/>가
        /// <c>[XR Rig]</c>에 <see cref="FreeLookController"/>만 붙인다. 그쪽은 키보드를 직접 읽고
        /// 주입 지점이 없어(기기엔 키보드가 없으니 아무 일도 안 한다) 리그를 우리가 직접 옮긴다.
        /// </summary>
        void ResolveRigFallback()
        {
            if (player != null || _rig != null) return;
            var fl = FindFirstObjectByType<FreeLookController>();
            if (fl != null) { _rig = fl.transform; Debug.Log("[ControllerDriver] XR 리그 연결: " + _rig.name); return; }
            if (Camera.main != null && Camera.main.transform.parent != null) _rig = Camera.main.transform.parent;
        }

        void Update()
        {
            ResolvePlayer();
            ResolveRigFallback();
            ResolveHackDriver();

            var link = ControllerLink.Active;
            // ⚠️ 예전엔 player가 null이면 여기서 return했다. VR 리그엔 FirstPersonPlayer가 없어서
            // 기기에서 조이스틱·서보가 통째로 죽었다. 이제 이동 대상이 없어도 나머지는 계속 돈다.
            if (link == null) return;

            UpdateScreenMetrics(link.Latest);
            TickJoystick(link);
            TickGyroLook(link);

            // 손 보정은 Map보다 먼저 돌아야 이번 프레임 지령이 반영된다.
            // 추적이 6DoF일 때만 위치를 믿는다 — GyroOnly면 위치가 항상 0이라 원점으로 오해한다.
            hand.Step(link.Latest.Position,
                      link.Latest.Tracking == TrackingStateCode.Tracking6Dof && link.Connected,
                      Time.deltaTime);
        }

        /// <summary>
        /// 정규화 좌표 → mm 환산 계수. 정규화만으론 물리 거리를 알 수 없어 조작 반경을 물리적으로
        /// 맞출 수 없다(엄지 도달 범위는 픽셀이 아니라 mm 단위다).
        /// </summary>
        void UpdateScreenMetrics(in InputPacket p)
        {
            if (p.Dpi <= 1f || p.ScreenWidth <= 0) return;
            const float MmPerInch = 25.4f;
            ScreenMmX = p.ScreenWidth / p.Dpi * MmPerInch;
            ScreenMmY = p.ScreenHeight / p.Dpi * MmPerInch;
        }

        void TickJoystick(ControllerLink link)
        {
            int slot = link.SlotOnHalf(left: true);

            if (slot < 0)
            {
                _joySlot = -1;
                JoystickActive = false;
                JoystickValue = Vector2.zero;
                if (player != null) player.ExternalWish = Vector2.zero;   // 연속값이라 0도 명시적으로 써야 한다
                return;
            }

            var t = link.Touches[slot];
            if (_joySlot != slot)
            {
                _joySlot = slot;
                _joyCenterNorm = t.startNorm;   // 엄지를 내려놓은 자리가 중심(플로팅)
            }

            // 정규화 차이를 mm로 바꿔 원형으로 다룬다. 가로/세로 mm 비율이 달라서
            // 정규화 좌표로 원을 그리면 타원이 된다.
            Vector2 dNorm = t.norm - _joyCenterNorm;
            Vector2 dMm = new Vector2(dNorm.x * ScreenMmX, dNorm.y * ScreenMmY);

            float r = Mathf.Max(1f, joystickRadiusMm);
            float mag = dMm.magnitude;

            if (followOnOverflow && mag > r)
            {
                // 중심을 끌고 온다 — 엄지가 밀려도 최대 입력에 붙어 있지 않게.
                Vector2 excessMm = dMm.normalized * (mag - r);
                _joyCenterNorm += new Vector2(excessMm.x / Mathf.Max(1e-3f, ScreenMmX),
                                              excessMm.y / Mathf.Max(1e-3f, ScreenMmY));
                dMm = dMm.normalized * r;
                mag = r;
            }

            float unit = Mathf.Clamp01(mag / r);
            float dz = joystickDeadZone;
            float shaped = unit <= dz ? 0f : (unit - dz) / (1f - dz);

            Vector2 dir = mag > 1e-5f ? dMm / mag : Vector2.zero;
            JoystickValue = dir * shaped;
            JoystickActive = true;

            // 화면 x=오른쪽, y=위 → 이동 x=오른쪽, y=앞. 그대로 대응한다.
            if (player != null) { player.ExternalWish = JoystickValue; return; }

            // VR 리그 경로 — 머리가 보는 방향 기준으로 옮긴다. 조이스틱을 앞으로 밀면
            // "지금 보고 있는 쪽"으로 가는 게 VR에서 자연스럽다.
            if (_rig == null) return;
            Camera cam = Camera.main;
            if (cam == null) return;

            Vector3 fwd = cam.transform.forward; fwd.y = 0f;
            Vector3 rgt = cam.transform.right; rgt.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f || rgt.sqrMagnitude < 1e-6f) return;

            Vector3 worldDir = rgt.normalized * JoystickValue.x + fwd.normalized * JoystickValue.y;
            _rig.position += worldDir * (rigMoveSpeed * Time.deltaTime);
        }

        /// <summary>
        /// 원시 터치 → <see cref="HexInput"/>. <see cref="NetworkHexInputSource.Poll"/>이 매 프레임 부른다.
        ///
        /// <para><b>오른쪽 절반 = Space</b>다. 탭/홀드 판정은 <see cref="HackDriver"/>가 소유하므로
        /// 여기선 raw 3필드(held/pressed/released)만 채운다 — PC와 같은 판정을 그대로 얻는다.</para>
        ///
        /// <para><b>왼쪽 절반은 분기하지 않는다.</b> 이동(조이스틱)과 사격(<see cref="HexInput.primary"/>)에
        /// 동시에 먹이는데, 고정 시점 빙의(터렛·CCTV)에선 <c>HackDriver</c>가 본체를 얼려
        /// (<c>ExternalMotion</c>) 이동이 무시되고, 경비병 빙의엔 총이 없어 사격이 무시된다.
        /// 상황이 알아서 갈라주므로 손 입장에서 왼쪽의 의미가 흔들리지 않는다.</para>
        /// </summary>
        HexInput MapInput(ControllerSample sample, ControlContext ctx)
        {
            HexInput cmd = HexInput.Empty;
            cmd.context = ctx;

            var link = ControllerLink.Active;
            if (link == null || !driveHackInput) return cmd;

            int right = link.SlotOnHalf(left: false);
            int rightUp = link.ReleasedSlotOnHalf(left: false);

            if (ctx == ControlContext.Hacking)
            {
                // 해킹 중엔 hackHeld를 <b>채우지 않는다</b>. HackDriver의 홀드-복귀 조건이
                // "Player가 아님"이라 Hacking에서도 발동하는데, 패턴을 그리려고 손가락을 대고 있는 것이
                // 복귀로 새면 그릴 수가 없다. hackHeld가 없으면 홀드 조건이 성립할 수 없다.
                //
                // 취소는 살아 있다 — 움직이지 않은 터치(툭 침)를 뗄 때만 탭을 합성한다.
                if (rightUp >= 0 && !link.Touches[rightUp].moved)
                {
                    cmd.hackPressed = true;
                    cmd.hackReleased = true;
                }

                if (right >= 0)
                {
                    var t = link.Touches[right];

                    // 배율은 이번 패턴의 <b>첫 터치</b>에서 한 번만 정한다. 획 도중에 바뀌면 손이
                    // 못 따라가고, 클러치(뗐다 다시 잡기)마다 바뀌어도 감각이 흔들린다.
                    if (_patternScaleMm <= 0f)
                        _patternScaleMm = ComputePatternScaleMm(link.Latest, t.startNorm);

                    Vector2 dMm = new Vector2(t.deltaThisFrame.x * ScreenMmX,
                                              t.deltaThisFrame.y * ScreenMmY);

                    // PatternInput은 "마우스 px × sensitivity"로 커서를 옮긴다. 게임 코드를 안 건드리려고
                    // 여기서 픽셀 상당으로 환산해 넘긴다 — 폰에서 격자 한 칸(_patternScaleMm)을 움직이면
                    // 정확히 1유닛이 나가도록.
                    _strokePending += dMm / Mathf.Max(1e-3f, _patternScaleMm) * PxPerGridUnit;
                    PatternScaleMm = _patternScaleMm;
                }

                // 밀린 이동량을 프레임에 고르게 풀어놓는다. 손을 뗀 뒤에도 남은 분은 마저 나가야
                // 총 이동량이 보존된다 — 안 그러면 획 끝이 잘려 점에 못 닿는다.
                cmd.strokeDir = ReleasePendingStroke();

                // 손을 떼면 커서를 마지막 점으로 되돌린다. 뗀 지점의 오차를 안고 다시 시작하지
                // 않게 해, 클러치로 이어 그려도 항상 정확한 지점에서 출발한다.
                // (남은 이동량도 함께 버린다 — 되돌린 뒤에 흘러들면 다시 어긋난다.)
                if (rightUp >= 0)
                {
                    _strokePending = Vector2.zero;
                    var mg = hackDriver != null ? hackDriver.minigame : null;
                    if (mg != null && mg.Input != null) mg.Input.SnapCursorToCurrent();
                }
                return cmd;
            }

            // 패턴을 벗어나면 다음 패턴에서 다시 계산한다.
            _patternScaleMm = -1f;
            _strokePending = Vector2.zero;

            // Player / ViewEntry
            if (right >= 0)
            {
                cmd.hackHeld = true;
                if (link.Touches[right].pressedThisFrame) cmd.hackPressed = true;
            }
            if (rightUp >= 0) cmd.hackReleased = true;

            int left = link.SlotOnHalf(left: true);
            if (left >= 0)
            {
                cmd.primaryHeld = true;
                if (link.Touches[left].pressedThisFrame) cmd.primary = true;
            }

            ApplyServo(ref cmd);
            return cmd;
        }

        [Header("부품 조종 — 위치 제어 서보")]
        [Tooltip("손 변위를 부품 위치로 바꾸는 보정 파이프라인.")]
        public HandCalibration hand = new HandCalibration();

        [Tooltip("서보 이득. 목표와 현재 위치의 오차(정규화)에 이 값을 곱해 속도 지령을 만든다. " +
                 "크면 빠르지만 끝에서 출렁이고, 작으면 끝까지 못 간다.")]
        public float servoGain = 5f;

        /// <summary>서보가 낸 지령(진단용).</summary>
        public Vector2 ServoAnalog { get; private set; }
        /// <summary>부품의 현재 정규화 위치(진단용).</summary>
        public Vector2 PartNormalized { get; private set; }

        void OnDisable()
        {
            if (hackDriver != null) hackDriver.OnControlledChanged -= OnControlledChanged;
        }

        /// <summary>
        /// 조종 대상이 잡히거나 풀린 순간. 잡히면 손 원점을 지금 자리로 리센터한다 —
        /// 조종 구간이 항상 이 이산 이벤트로 시작하므로, 두 기기 좌표계를 이때 한 번만 맞추면 된다.
        /// </summary>
        void OnControlledChanged(Hackable controlled)
        {
            if (controlled == null) { hand.Clear(); ServoAnalog = Vector2.zero; return; }

            var link = ControllerLink.Active;
            if (link == null) return;

            float ctrlYaw = link.Latest.Rotation.eulerAngles.y;
            float headYaw = hackDriver != null && hackDriver.cam != null
                ? hackDriver.cam.transform.eulerAngles.y : 0f;

            hand.Recenter(link.Latest.Position, ctrlYaw, headYaw);
            Debug.Log($"[ControllerDriver] 리센터 — 대상={controlled.kind} 손yaw={ctrlYaw:F0} 머리yaw={headYaw:F0}");
        }

        /// <summary>
        /// 위치 제어를 속도 인터페이스 위에 얹는다. <c>Drive</c>의 analog는 등속 크립(속도 지령)이라
        /// 위치를 직접 지정할 입구가 없다. 대신 <b>서보</b>로 오차를 속도로 바꾼다 —
        /// 부품의 자체 가감속 램프가 액추에이터 역할을 하고 범위 클램프도 그대로 산다.
        /// <b>부품 코드는 손댈 필요가 없다.</b>
        /// </summary>
        void ApplyServo(ref HexInput cmd)
        {
            ServoAnalog = Vector2.zero;
            PartNormalized = Vector2.zero;

            Hackable target = hackDriver != null ? hackDriver.Controlled : null;
            if (target == null || !hand.Ready) return;

            var ctrl = target.GetComponent<IExternalControl>();
            if (ctrl == null) return;

            float curH = ctrl.AxisCount > 0 ? ctrl.GetNormalized(0) : 0f;
            float curV = ctrl.AxisCount > 1 ? ctrl.GetNormalized(1) : 0f;
            PartNormalized = new Vector2(curH, curV);

            float aH = Mathf.Clamp((hand.CommandH - curH) * servoGain, -1f, 1f);
            float aV = Mathf.Clamp((hand.CommandV - curV) * servoGain, -1f, 1f);

            ServoAnalog = new Vector2(aH, aV);
            cmd.axisH = aH;
            cmd.axisV = aV;
        }

        float _patternScaleMm = -1f;
        Vector2 _strokePending;

        /// <summary>이번 패턴의 격자 한 칸 크기(mm). 진단용.</summary>
        public float PatternScaleMm { get; private set; }

        /// <summary>
        /// 밀린 커서 이동량을 시정수에 맞춰 조금씩 내보낸다. 총량은 보존되므로 "빠르게 그으면
        /// 덜 간다" 같은 왜곡이 없고, 30Hz 입력이 프레임마다 균등하게 풀린다.
        /// </summary>
        Vector2 ReleasePendingStroke()
        {
            if (_strokePending.sqrMagnitude < 1e-8f) { _strokePending = Vector2.zero; return Vector2.zero; }

            float k = patternSmoothTime > 1e-4f
                ? 1f - Mathf.Exp(-Time.deltaTime / patternSmoothTime)
                : 1f;

            Vector2 emit = _strokePending * k;
            _strokePending -= emit;
            return emit;
        }

        /// <summary>격자 1유닛에 해당하는 "마우스 픽셀". <c>PatternInput.sensitivity</c>의 역수다.</summary>
        float PxPerGridUnit
        {
            get
            {
                float s = hackDriver != null && hackDriver.minigame != null
                    ? hackDriver.minigame.sensitivity : 1f / 300f;
                return 1f / Mathf.Max(1e-6f, s);
            }
        }

        float PatternHitRadius =>
            hackDriver != null && hackDriver.minigame != null ? hackDriver.minigame.hitRadius : 0.16f;

        /// <summary>
        /// 터치가 내려앉은 지점에서 <b>남은 물리 공간</b>으로 격자 배율을 유도한다.
        /// 고정 상수로 박으면 화면 가장자리에서 격자가 밖으로 나가 패턴을 완성할 수 없다.
        ///
        /// <para>커서는 항상 좌상단 점(<c>PatternGraph.StartDot</c>)에서 시작하므로, 필요한 여유는
        /// 오른쪽·아래로 <c>1 + hitRadius</c>, 왼쪽·위로 <c>hitRadius</c>다. 네 방향 제약 중 가장
        /// 빡빡한 것이 배율을 정한다.</para>
        ///
        /// <para><b>등방</b>으로 잡는다 — 방향마다 다르면 대각선 획이 45도가 아니게 되어, VR에 보이는
        /// 격자와 손의 느낌이 어긋난다.</para>
        /// </summary>
        float ComputePatternScaleMm(in InputPacket p, Vector2 startNorm)
        {
            if (p.Dpi <= 1f || ScreenMmX <= 0f) return patternFallbackScaleMm;

            const float MmPerInch = 25.4f;
            float mmPerPx = MmPerInch / p.Dpi;

            // safeArea는 픽셀·좌하단 원점. 터치 정규화 좌표도 좌하단 원점이라 그대로 대응한다.
            float sx0 = p.SafeArea.x * mmPerPx;
            float sy0 = p.SafeArea.y * mmPerPx;
            float sx1 = (p.SafeArea.x + p.SafeArea.width) * mmPerPx;
            float sy1 = (p.SafeArea.y + p.SafeArea.height) * mmPerPx;

            float tx = startNorm.x * ScreenMmX;
            float ty = startNorm.y * ScreenMmY;

            float spaceRight = Mathf.Max(0f, sx1 - tx);
            float spaceLeft = Mathf.Max(0f, tx - sx0);
            float spaceUp = Mathf.Max(0f, sy1 - ty);
            float spaceDown = Mathf.Max(0f, ty - sy0);

            float needFar = 1f + PatternHitRadius;              // 반대쪽 점까지
            float needNear = Mathf.Max(0.01f, PatternHitRadius); // 시작점 주변 여유

            float s = Mathf.Min(
                Mathf.Min(spaceRight / needFar, spaceDown / needFar),
                Mathf.Min(spaceLeft / needNear, spaceUp / needNear));

            s *= patternSafety;
            return Mathf.Clamp(s, patternMinScaleMm, patternMaxScaleMm);
        }

        /// <summary>
        /// 기기 자세 델타 → 시점 변화. 절대 프레임을 해석하지 않으므로 화면 방향·기기 차이에 둔감하다.
        /// </summary>
        void TickGyroLook(ControllerLink link)
        {
            // VR에선 머리가 시점을 소유한다. 컨트롤러 자이로까지 시점을 밀면 둘이 싸워서
            // 화면이 제멋대로 돈다 — 수동 플래그에 맡기지 않고 여기서 강제로 막는다.
            if (VrMode.Enabled || !useGyroLook || player == null) { LookDeltaDeg = Vector2.zero; return; }

            Quaternion q = link.Latest.Rotation;
            if (q.x == 0f && q.y == 0f && q.z == 0f && q.w == 0f) return;   // 아직 값 없음

            if (!_hasPrevRot) { _prevRot = q; _hasPrevRot = true; return; }

            // 원본 자세를 향해 지수 평활한 값을 따라간다. 컨트롤러(30Hz)와 에디터(60fps+)의
            // 속도 차이 때문에 원본을 그대로 미분하면 패킷 없는 프레임의 델타가 0이 되어 끊긴다.
            // 평활값의 프레임간 차이를 쓰면 같은 총 회전량이 매 프레임에 고르게 나뉜다.
            float k = lookSmoothTime > 1e-4f
                ? 1f - Mathf.Exp(-Time.deltaTime / lookSmoothTime)
                : 1f;
            Quaternion next = Quaternion.Slerp(_prevRot, q, k);

            // 델타를 <b>이전 자세 기준(로컬)</b>으로 본다 — 그래야 "폰을 어느 쪽으로 비틀었나"가
            // 절대 방향과 무관하게 나온다.
            Quaternion delta = Quaternion.Inverse(_prevRot) * next;
            _prevRot = next;

            delta.ToAngleAxis(out float angleDeg, out Vector3 axis);
            if (float.IsNaN(axis.x) || angleDeg < 1e-4f) return;
            if (angleDeg > 180f) angleDeg -= 360f;

            Vector3 rot = axis.normalized * angleDeg;   // 회전 벡터(도)

            // 기기 축 → 화면 축 대응은 기기·방향마다 달라 실기에서 잡는다.
            float yaw = rot.y;
            float pitch = rot.x;
            if (swapAxes) { float tmp = yaw; yaw = pitch; pitch = tmp; }
            if (invertYaw) yaw = -yaw;
            if (invertPitch) pitch = -pitch;

            // 추적이 튀는 순간 화면이 홱 도는 것을 막는다.
            yaw = Mathf.Clamp(yaw, -gyroMaxStepDeg, gyroMaxStepDeg);
            pitch = Mathf.Clamp(pitch, -gyroMaxStepDeg, gyroMaxStepDeg);

            LookDeltaDeg = new Vector2(yaw, pitch);

            // FirstPersonPlayer는 "마우스 델타 × lookSens"를 기대하므로 픽셀 상당으로 환산해 넘긴다.
            player.ExternalLook += new Vector2(yaw, pitch) * gyroLookGain;
        }
    }
}
