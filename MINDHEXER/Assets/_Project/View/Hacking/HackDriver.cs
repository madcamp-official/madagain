using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 해킹 루프 배선. 카메라 중앙 시선 Raycast로 Hackable 조준 → controlType 색 하이라이트 →
    /// <b>Space 단발 탭</b> → 점 패턴 미니게임(§2.4) → 성공하면 그 대상을 <b>조종 대상</b>으로 잡는다.
    /// 해킹 중엔 마우스가 패턴을 그리므로 시점만 잠기고 WASD 이동은 계속된다(§2.5).
    ///
    /// <para><b>Space 하나로 전부 처리한다(Q 폐기).</b> 길이로 갈린다:
    ///  · <b>홀드</b>(<see cref="holdThreshold"/> 이상, <b>빙의 중일 때만</b>) → 본체 복귀. 임계 도달 즉시 발동하고
    ///    이어지는 릴리스는 소비된다(복귀하면서 해킹까지 걸리지 않게).
    ///  · <b>뗌</b> → 조준 중이면 해킹 시작 / 그리는 중이면 취소 / 그 외면 조종 해제.</para>
    ///
    /// <para>홀드를 <b>빙의 중에만</b> 인정하는 이유: 평소에 길게 눌렀다 떼면 아무 일도 안 하는 죽은 입력이
    /// 생긴다. 홀드가 발동하지 않았으면 릴리스는 <b>무조건</b> 탭 동작을 하므로, 길게 눌러도 해킹은 걸린다.</para>
    ///
    /// <para>홀드 판정을 리더가 아니라 여기서 하는 이유: <see cref="HexInput"/>의 raw 3필드
    /// (held/pressed/released)만 채우면 <b>VR도 같은 판정을 공짜로</b> 얻는다.</para>
    ///
    /// <para>조종 대상은 <b>한 번에 하나</b>이고 <b>시선과 무관하게</b> 계속 조종된다(도주하며 조종, §2.5).
    /// 다른 대상을 해킹하면 이전 대상은 자동으로 풀린다 — 되찾으려면 다시 해킹해야 한다.
    /// 조종 중에는 좌/우클릭(+Shift)이 그 대상의 축 입력이고, 실(<see cref="ControlTether"/>)이
    /// 플레이어와 대상을 계속 잇는다(§6.2 마리오네트).</para>
    /// (기초_설계안 §2.5·§6.2·§7)
    /// </summary>
    [RequireComponent(typeof(HackContext))]
    public class HackDriver : MonoBehaviour
    {
        [Tooltip("시선 Raycast 기준 카메라. 비우면 Camera.main.")]
        public Camera cam;

        [Tooltip("점 패턴 미니게임(§2.4). 비우면 자동 추가.")]
        public PatternMinigame minigame;

        [Tooltip("조종 실(플레이어↔대상). 비우면 자동 추가.")]
        public ControlTether tether;

        [Tooltip("빙의 시점 구동(§2.5). 비우면 자동 추가.")]
        public ViewEntryController viewEntry;

        [Tooltip("Space를 이 시간(초) 이상 유지하면 본체 복귀(빙의 중일 때만). 이하로 떼면 평소의 탭 동작.")]
        public float holdThreshold = 0.25f;

        static readonly Color ExternalColor  = new Color(0.4f, 1f, 0.3f);   // 연두 (외부 조종)
        static readonly Color ViewEntryColor = new Color(0f, 0.8f, 0.85f);  // 청록 (시점 진입)
        static readonly Color StunColor      = new Color(1f, 0.85f, 0.1f);  // 노랑 (보스 스턴)
        static readonly Color HackedColor    = new Color(0.45f, 0.78f, 1f); // 밝은 하늘색 (한 번이라도 해킹됨, 영구)

        /// <summary>입력 출처. 기본 PC(키보드/마우스). VR에선 GameBoot이 네트워크 소스로 교체.</summary>
        public IHexInputSource Source = new PcHexInputSource();
        HackContext _ctx;
        FirstPersonPlayer _fpp;         // PC 본체 — 해킹 중 정지 + 점프 요청(§2.5)
        FreeLookController _freeLook;   // VR 리그 mover fallback — 해킹 중 시점 고정
        bool _lookWasEnabled;
        Hackable _highlighted;
        Hackable _gazed;
        MaterialPropertyBlock _mpb;
        int _baseColorId;

        /// <summary>지금 조종 중인 대상(한 번에 하나). 시선과 무관하게 유지된다.</summary>
        public Hackable Controlled { get; private set; }

        // 해킹 성공 시점에 고정되는 슬롯↔축 배정·부호(§6.2). 조종 중에는 절대 안 바뀐다.
        readonly int[] _slotAxis = { -1, -1 };
        readonly float[] _slotSign = { 1f, 1f };

        // Space 홀드 판정. _holdConsumed면 그 릴리스는 탭 동작을 하지 않는다(복귀+해킹 이중 발동 방지).
        float _spaceDownTime = -1f;
        bool _holdConsumed;

        void Awake()
        {
            _ctx = GetComponent<HackContext>();
            if (cam == null) cam = Camera.main;
            if (minigame == null) minigame = GetComponent<PatternMinigame>() ?? gameObject.AddComponent<PatternMinigame>();
            if (minigame.ui == null) minigame.ui = GetComponent<PatternUI>() ?? gameObject.AddComponent<PatternUI>();
            if (tether == null) tether = GetComponent<ControlTether>() ?? gameObject.AddComponent<ControlTether>();
            if (viewEntry == null) viewEntry = GetComponent<ViewEntryController>() ?? gameObject.AddComponent<ViewEntryController>();
            _fpp = GetComponentInParent<FirstPersonPlayer>();
            _freeLook = GetComponentInParent<FreeLookController>();
            _mpb = new MaterialPropertyBlock();
            _baseColorId = Shader.PropertyToID("_BaseColor");
        }

        void Update()
        {
            // 개발 콘솔이 열려 있으면 타이핑이 해킹 입력으로 새지 않게 통째로 무시한다(` 토글).
            if (DevConsole.Open) return;

            HexInput input = Source.Poll(_ctx.Current);

            bool canAim = _ctx.Current == ControlContext.Player || _ctx.Current == ControlContext.ViewEntry;
            Hackable aimed = canAim ? Raycast() : null;
            UpdateHighlight(aimed);
            UpdateGazeFlags(aimed);

            // ── Space 홀드 = 본체 복귀 (빙의 중일 때만). 임계 도달 즉시 발동한다. ──
            if (input.hackPressed) { _spaceDownTime = Time.unscaledTime; _holdConsumed = false; }

            if (input.hackHeld && !_holdConsumed && _spaceDownTime >= 0f
                && Time.unscaledTime - _spaceDownTime >= holdThreshold
                && _ctx.Current != ControlContext.Player)
            {
                _holdConsumed = true;          // 이어지는 릴리스는 탭 동작을 하지 않는다
                viewEntry.Exit(cam);
                SetPlayerFrozen(false);
                _ctx.ReturnToBody();
                Debug.Log("[Hack] 복귀(Space 홀드) → Player");
            }

            // 릴리스가 탭 동작을 할 자격이 있는가. 홀드가 이미 소비했으면 없다.
            bool tap = input.hackReleased && !_holdConsumed;
            if (input.hackReleased) { _spaceDownTime = -1f; _holdConsumed = false; }

            switch (_ctx.Current)
            {
                case ControlContext.Player:
                case ControlContext.ViewEntry:
                {
                    // Space를 뗄 때 처리(홀드는 위에서 본체 복귀로 먼저 소비됨).
                    //  · 새 대상을 조준 중이면  → 그 대상 해킹 시작
                    //  · 아니면(허공/조종 중인 대상) → 조종 해제
                    if (tap)
                    {
                        if (aimed != null && aimed != Controlled)
                        {
                            _ctx.BeginHacking(aimed);
                            aimed.captureState = CaptureState.Hacking;

                            // 한 번이라도 해킹된 적 있으면 패턴 생략, 즉시 성공(전체 해킹 규칙).
                            if (aimed.everHacked)
                            {
                                Debug.Log($"[Hack] 재해킹(패턴 생략): {aimed.kind}");
                                OnPatternDone(true);
                            }
                            else
                            {
                                minigame.Begin(aimed);
                                FreezeLook(true);
                                Debug.Log($"[Hack] 시작: {aimed.kind} ({aimed.controlType}, {aimed.PatternLineCount}선)");
                            }
                        }
                        else ReleaseControlled("탭");
                    }

                    // 조종은 시선과 무관 — 어디를 보든 잡고 있는 대상이 움직인다.
                    if (Controlled != null) DriveExternal(Controlled, input);
                    viewEntry.Tick();   // 빙의 중이면 마우스로 pan/tilt(§2.5)

                    // 빙의 중인 대상에 총(TurretGun)이 있으면 좌클릭으로 사격.
                    // primary/primaryHeld는 HexInput에 시점진입용으로 이미 있었는데 여기서 처음 쓴다.
                    if (_ctx.Current == ControlContext.ViewEntry && _ctx.ActiveTarget != null)
                    {
                        var gun = _ctx.ActiveTarget.GetComponent<TurretGun>();
                        if (gun != null) gun.TickPlayerFire(input.primary, input.primaryHeld);
                    }
                    break;
                }

                case ControlContext.Hacking:
                {
                    // 그리는 중 Space를 다시 뗐다 = 취소.
                    PatternState st = tap ? minigame.Cancel() : minigame.Tick(input.strokeDir);
                    if (st == PatternState.Succeeded) OnPatternDone(true);
                    else if (st == PatternState.Cancelled) OnPatternDone(false);
                    break;
                }
            }

            // 실은 해킹 시도 순간(초록)부터 붙고, 성공하면 조종 대상으로 넘어가며 파랑이 된다(§7·§6.2).
            if (tether != null)
            {
                bool hacking = _ctx.Current == ControlContext.Hacking && _ctx.ActiveTarget != null;

                // 빙의 중이면 실은 <b>남겨진 본체 셸 → 빙의한 대상</b>을 잇는다(§6.3의 "손에서 이어지는 줄").
                // 리그가 대상 자리로 옮겨갔으므로 카메라를 시작점으로 쓰면 길이가 0이 된다.
                bool possessing = !hacking && viewEntry != null && viewEntry.Active;
                Transform from = possessing && viewEntry.Shell != null
                    ? viewEntry.Shell
                    : (cam != null ? cam.transform : transform);

                Hackable tetherTarget = hacking ? _ctx.ActiveTarget
                                      : (possessing ? _ctx.ActiveTarget : Controlled);

                tether.UpdateTether(from,
                                    tetherTarget != null ? tetherTarget.transform : null,
                                    captured: !hacking && tetherTarget != null);
            }
        }

        /// <summary>조종 대상 교체 — 이전 대상은 자동으로 풀린다(되찾으려면 재해킹).</summary>
        void SetControlled(Hackable next)
        {
            if (Controlled == next) return;
            if (Controlled != null) Controlled.captureState = CaptureState.None;
            Controlled = next;
            if (Controlled != null)
            {
                Controlled.captureState = CaptureState.Captured;
                FreezeControlMapping(Controlled);   // 지금 시점 기준으로 매핑 확정
            }
        }

        void ReleaseControlled(string why)
        {
            if (Controlled == null) return;
            Debug.Log($"[Hack] 조종 해제({why}): {Controlled.kind}");
            SetControlled(null);
        }

        /// <summary>
        /// 조종 대상에 입력을 먹인다(§2.5 슬롯 표 → §6.2 월드 축).
        /// 슬롯↔축 배정과 부호는 <see cref="FreezeControlMapping"/>이 <b>해킹 성공 시점에 고정</b>한 값을 쓴다.
        /// </summary>
        void DriveExternal(Hackable target, HexInput input)
        {
            var ctrl = target.GetComponent<IExternalControl>();
            if (ctrl == null) return;

            for (int slot = 0; slot < 2; slot++)
            {
                int axis = _slotAxis[slot];
                if (axis < 0 || axis >= ctrl.AxisCount) continue;

                float analog = slot == 0 ? input.axisH : input.axisV;
                int flick = FlickFor(slot, input.flick);
                if (Mathf.Approximately(analog, 0f) && flick == 0) continue;

                float s = _slotSign[slot];
                ctrl.Drive(axis, analog * s, (int)(flick * s));
            }
        }

        static int FlickFor(int slot, FlickDir f)
        {
            if (slot == 0) return f == FlickDir.Right ? 1 : f == FlickDir.Left ? -1 : 0;
            return f == FlickDir.Up ? 1 : f == FlickDir.Down ? -1 : 0;
        }

        /// <summary>
        /// 해킹 성공 순간의 시점을 기준으로 <b>슬롯↔축 배정과 부호를 고정</b>한다(§6.2).
        /// 화면에서 가장 가로로 보이는 축 → 슬롯0(좌/우클릭, 우=+), 나머지 → 슬롯1(Shift, 위=+).
        /// 한번 정해지면 조종 중 시점을 아무리 돌려도 매핑이 바뀌지 않는다 — 조종 중 방향이 흔들리면
        /// 조작이 불가능해지기 때문.
        /// </summary>
        void FreezeControlMapping(Hackable target)
        {
            _slotAxis[0] = _slotAxis[1] = -1;
            _slotSign[0] = _slotSign[1] = 1f;

            var ctrl = target != null ? target.GetComponent<IExternalControl>() : null;
            if (ctrl == null || cam == null) return;

            Vector3 right = cam.transform.right, up = cam.transform.up, fwd = cam.transform.forward;
            int n = Mathf.Min(ctrl.AxisCount, 2);
            if (n <= 0) return;

            // 화면 가로 성분이 가장 큰 축을 슬롯0(좌/우클릭)에 준다.
            int best = 0;
            float bestH = -1f;
            for (int i = 0; i < n; i++)
            {
                float h = Mathf.Abs(Vector3.Dot(ctrl.AxisWorld(i), right));
                if (h > bestH) { bestH = h; best = i; }
            }

            _slotAxis[0] = best;
            _slotSign[0] = SignAlong(ctrl.AxisWorld(best), right, fwd);

            for (int i = 0; i < n; i++)
            {
                if (i == best) continue;
                _slotAxis[1] = i;
                _slotSign[1] = SignAlong(ctrl.AxisWorld(i), up, fwd);
                break;
            }

            Debug.Log($"[Hack] 조종 매핑 고정 — 슬롯0=축{_slotAxis[0]}({_slotSign[0]:+0;-0}) 슬롯1=축{_slotAxis[1]}({_slotSign[1]:+0;-0})");
        }

        // 화면 기준 부호. 기준축 성분이 너무 작으면(화면상 깊이 방향) forward로 대체 판정한다.
        static float SignAlong(Vector3 axisWorld, Vector3 screenRef, Vector3 fwd)
        {
            float d = Vector3.Dot(axisWorld, screenRef);
            if (Mathf.Abs(d) < 0.3f) d = Vector3.Dot(axisWorld, fwd);
            return d < 0f ? -1f : 1f;
        }

        // 비주얼(환경 하이라이트)이 읽는 이음새 상태를 매 프레임 갱신한다.
        void UpdateGazeFlags(Hackable aimed)
        {
            if (_gazed != null && _gazed != aimed) _gazed.IsGazed = false;
            _gazed = aimed;
            if (_gazed != null)
            {
                _gazed.IsGazed = true;
                _gazed.DistanceToPlayer = Vector3.Distance(transform.position, _gazed.transform.position);
                _gazed.InRange = _gazed.DistanceToPlayer <= _gazed.hackRange;
            }
        }

        void OnPatternDone(bool success)
        {
            Hackable target = _ctx.ActiveTarget;
            FreezeLook(false);
            if (success)
            {
                if (target != null) target.everHacked = true;   // 영구 기록(전체 해킹 규칙)

                // 외부 조종 = 새 조종 대상으로 교체(이전 대상은 SetControlled가 자동으로 풀어준다).
                if (target != null && target.controlType == ControlType.ExternalControl) SetControlled(target);
                else if (target != null) target.captureState = CaptureState.None;

                _ctx.OnPatternSucceeded();

                // 시점 진입 = 대상의 눈으로 카메라를 옮긴다. 이동 허용 여부는 대상이 정한다(경비병만 이동).
                if (_ctx.Current == ControlContext.ViewEntry && target != null)
                {
                    var vet = target.GetComponent<ViewEntryTarget>();
                    if (vet != null)
                    {
                        viewEntry.Enter(vet, cam);
                        // 경비병(allowsMove)은 리그가 통째로 그 몸이 되므로 얼리면 안 된다 —
                        // 고정 시점(CCTV·터렛)일 때만 본체를 세운다.
                        SetPlayerFrozen(!vet.allowsMove);
                        Debug.Log($"[Hack] 빙의: {target.kind} (좌우±{vet.panRange} 상하±{vet.tiltRange})");
                    }
                    else Debug.LogWarning($"[Hack] {target.kind}에 ViewEntryTarget이 없어 빙의 시점을 만들 수 없음.");
                }
                Debug.Log($"[Hack] 성공 → 컨텍스트 = {_ctx.Current}");
            }
            else
            {
                // 취소된 대상이 조종 중이던 대상은 아니므로 그냥 초기 상태로.
                if (target != null && target != Controlled) target.captureState = CaptureState.None;
                _ctx.OnPatternCancelled();
                Debug.Log($"[Hack] 실패/취소 → 컨텍스트 = {_ctx.Current}");
            }
        }

        // 빙의 중 본체 정지 — 시점(본체 카메라는 꺼져 있음)과 이동을 함께 막는다.
        // 경비병처럼 이동이 허용되는 대상에선 호출되지 않는다.
        void SetPlayerFrozen(bool frozen)
        {
            if (_fpp == null) return;
            _fpp.LookFrozen = frozen;
            _fpp.ExternalMotion = frozen;   // 이동·중력 처리를 넘겨 제자리에 세운다
        }

        // 해킹 중 마우스가 패턴을 그리므로 시점만 멈춘다(§2.5). WASD 이동은 계속된다.
        void FreezeLook(bool freeze)
        {
            if (_fpp != null) { _fpp.LookFrozen = freeze; return; }   // PC 본체: 시점만 정지
            if (_freeLook == null) return;                        // VR 리그 mover fallback
            if (freeze) { _lookWasEnabled = _freeLook.lookEnabled; _freeLook.lookEnabled = false; }
            else _freeLook.lookEnabled = _lookWasEnabled;
        }

        /// <summary>지금 화면을 그리는 카메라. 빙의 중이면 대상의 눈 — 그래야 릴레이 해킹 조준이 맞는다.</summary>
        // 몸 이동(경비병) 빙의는 플레이어 카메라를 그대로 쓰므로 viewEntry.Cam이 null이다 → 그때는 cam으로.
        Camera ActiveCam
        {
            get
            {
                if (viewEntry != null && viewEntry.Active && viewEntry.Cam != null) return viewEntry.Cam;
                return cam;
            }
        }

        Hackable Raycast()
        {
            Camera c = ActiveCam;
            if (c == null) return null;
            var ray = new Ray(c.transform.position, c.transform.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, 100f))
            {
                var h = hit.collider.GetComponentInParent<Hackable>();
                if (h != null && hit.distance <= h.hackRange) return h;
            }
            return null;
        }

        void UpdateHighlight(Hackable target)
        {
            if (_highlighted == target) return;
            if (_highlighted != null) SetTint(_highlighted, null);
            _highlighted = target;
            if (_highlighted != null) SetTint(_highlighted, ColorFor(_highlighted));
        }

        // 한 번이라도 해킹된 대상은 종류·상태와 무관하게 항상 하늘색(전체 해킹 규칙 — 영구 표시).
        Color ColorFor(Hackable h)
        {
            if (h.everHacked) return HackedColor;
            if (h.controlType == ControlType.ViewEntry) return ViewEntryColor;
            if (h.controlType == ControlType.Stun) return StunColor;
            return ExternalColor;
        }

        void SetTint(Hackable h, Color? c)
        {
            Renderer[] rends = (h.glowRenderers != null && h.glowRenderers.Length > 0)
                ? h.glowRenderers : h.GetComponentsInChildren<Renderer>();
            foreach (var r in rends)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                if (c.HasValue) _mpb.SetColor(_baseColorId, c.Value);
                else _mpb.Clear();
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}
