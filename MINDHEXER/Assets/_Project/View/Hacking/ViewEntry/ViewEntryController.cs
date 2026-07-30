using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.View
{
    /// <summary>
    /// 빙의 시점 구동. 대상에 따라 <b>두 방식</b>을 쓴다. (§2.5·§6.3)
    ///
    /// <list type="bullet">
    ///  <item><b>고정 시점</b>(CCTV·터렛 — <c>allowsMove=false</c>): 대상 눈에 별도 카메라를 띄우고
    ///        마우스로 pan/tilt. 본체는 제자리에 얼린다.</item>
    ///  <item><b>몸 이동</b>(경비병 — <c>allowsMove=true</c>): <b>플레이어 리그를 통째로 대상 위치로 옮긴다.</b>
    ///        원래 자리엔 <b>본체 셸</b>을 남긴다(§6.3의 "발사 자세로 정지한 본체").</item>
    /// </list>
    ///
    /// <para><b>왜 리그를 옮기나</b> — "본체와 완전히 동일한 조작"을 코드 중복 없이 보장하는 유일한 방법이다.
    /// 이동·가속·자동 도약·등반·착지 연출이 전부 리그에 물려 있으므로, 리그가 곧 경비병이 되면
    /// 그 전부가 공짜로 따라온다. 경비병에 이동 스택을 복제하면 두 벌을 영원히 같이 관리해야 한다.</para>
    ///
    /// <para>빙의 중 경비병 본체는 <b>메시를 숨기고 콜라이더를 끈다</b> — 리그의 CharacterController가
    /// 충돌을 맡고, 눈이 모델 안에 있어 자기 껍질이 보이기 때문. 위치는 매 프레임 리그를 따라가므로
    /// <b>빙의를 풀면 데려간 자리에 그대로 남는다</b>(§6.3 "경비병을 옮겨 길 정리").</para>
    /// </summary>
    public class ViewEntryController : MonoBehaviour
    {
        [Tooltip("고정 시점(CCTV·터렛) 마우스 감도.")]
        public float lookSens = 0.1f;

        [Tooltip("본체 셸 프리팹. 비우면 임시 캡슐을 만든다(본체 모델이 생기면 여기에 꽂는다).")]
        public GameObject shellPrefab;

        ViewEntryTarget _target;
        Camera _cam;            // 고정 시점 전용 카메라
        Camera _playerCam;
        ViewConeMask _mask;
        float _pan, _pitch;     // _pitch는 FirstPersonPlayer와 같은 규약 — 음수가 위

        // ── 몸 이동(경비병) 상태 ──
        bool _bodyMode;

        /// <summary>
        /// 옮길 대상 = <b><c>CharacterController</c>가 붙은 트랜스폼</b>(<c>[PlayerBody]</c>).
        ///
        /// <para>★ 예전엔 여기에 <b>카메라</b>를 넣었다. 카메라가 곧 리그였던 구 구조의 잔재인데,
        /// 지금은 <c>[PlayerBody] &gt; [Head] &gt; Main Camera</c> 3층이라 카메라에는 CC가 없다.
        /// 그래서 빙의하면 <b>몸은 그대로 두고 카메라만</b> 경비병 자리로 날아갔다 — 충돌·이동은
        /// 원래 자리에서 계속 일어나 회전·이동이 제멋대로였고, 복귀도 카메라만 돌아왔다.</para>
        /// </summary>
        Transform _rig;

        /// <summary>
        /// 시선 트랜스폼(카메라). <b>yaw는 여기서 읽어야 한다</b> — <c>[PlayerBody]</c>는 절대 회전하지
        /// 않으므로(<see cref="FirstPersonPlayer"/>가 identity로 고정) 몸에서 yaw를 읽으면 항상 0이다.
        /// 경비병 모델의 방향·본체 셸 방향이 화면과 안 맞던 원인이 이것이다.
        /// </summary>
        Transform _view;

        Transform _shell;               // 원래 자리에 남는 본체
        float _eyeHeight = 1.6f;        // 리그 원점(눈)에서 발까지
        readonly List<Collider> _offCols = new List<Collider>();

        Transform _head;                // [PlayerBody] > [Head] — possessEyeLift를 얹을 곳
        VrTuning _vrTuning;              // VR에서 [Head] 로컬위치의 실소유자. PC엔 없다(0 고정 규약)

        public bool Active => _target != null;

        /// <summary>지금 시야를 제공하는 카메라. <b>몸 이동 모드에선 플레이어 카메라를 그대로 쓰므로 null</b>.</summary>
        public Camera Cam => Active && !_bodyMode ? _cam : null;

        /// <summary>빙의 중 본체 이동이 허용되는지(경비병만 true).</summary>
        public bool AllowsMove => Active && _target.allowsMove;

        /// <summary>남겨진 본체 셸. 조종 실(§7)의 시작점 — 빙의 중이 아니면 null.</summary>
        public Transform Shell => _shell;

        /// <summary>
        /// 현재 활성 인스턴스(플레이어는 하나뿐이라 안전한 싱글턴). 몸 이동(경비병) 빙의 여부를
        /// 다른 시스템(터렛 등 "빙의당한 경비병은 안 쏨" 판정)이 물어볼 수 있게.
        /// </summary>
        public static ViewEntryController Current { get; private set; }

        void Awake() => Current = this;
        void OnDestroy() { if (Current == this) Current = null; }

        public void Enter(ViewEntryTarget target, Camera playerCam)
        {
            if (target == null) return;
            Exit(playerCam);

            _target = target;
            _playerCam = playerCam;
            _pan = _pitch = 0f;
            _bodyMode = target.allowsMove;

            if (_bodyMode) EnterBody(target, playerCam);
            else           EnterFixed(target, playerCam);
        }

        // ── 몸 이동(경비병) ────────────────────────────────────────────────

        void EnterBody(ViewEntryTarget target, Camera playerCam)
        {
            // 옮길 것은 카메라가 아니라 몸이다. CharacterController가 붙은 트랜스폼을 찾는다.
            var fpp = playerCam != null ? playerCam.GetComponentInParent<FirstPersonPlayer>()
                                        : FirstPersonPlayer.Instance;
            _rig = fpp != null ? fpp.transform : null;
            _view = playerCam != null ? playerCam.transform : _rig;

            if (_rig == null)
            {
                Debug.LogError("[빙의] 플레이어 몸(FirstPersonPlayer)을 찾지 못해 빙의할 수 없습니다.", this);
                _target = null;
                return;
            }

            var cc = _rig.GetComponent<CharacterController>();
            if (cc != null) _eyeHeight = cc.height * 0.5f - cc.center.y;   // 원점에서 발까지

            // 원래 자리에 본체를 남긴다 — 이게 취약 표적이자 복귀 지점이다(§6.3).
            _shell = MakeShell(_rig.position, ViewYaw);

            // 눈높이는 대상의 진짜 눈높이에 맞추지 않는다(경비병은 스케일 2라 3.6m — 몸 원점/CC는
            // 그대로 플레이어 것이라 거기까지 올리면 충돌·이동 판정과 어긋난다). 대신 [Head]만
            // 살짝 들어올려 "평소보다 눈높이가 높다"는 인상만 준다. 몸 원점(=충돌박스)은 안 건드린다.
            _head = _rig.Find("[Head]");
            ApplyPossessLift(target.possessEyeLift);

            // 경비병 본체: 충돌을 끄고(리그 CC가 맡는다) 메시를 숨긴다(눈이 모델 안에 있다).
            _offCols.Clear();
            foreach (var c in target.GetComponentsInChildren<Collider>(true))
                if (c.enabled) { c.enabled = false; _offCols.Add(c); }
            target.SetOwnMeshVisible(false);

            // 리그를 경비병 자리로. CharacterController는 위치를 직접 옮기면 씹히므로 껐다 켠다.
            Vector3 feet = target.transform.position;
            Teleport(_rig, feet + Vector3.up * _eyeHeight);
        }

        void ExitBody()
        {
            if (_rig != null && _shell != null) Teleport(_rig, _shell.position);

            if (_target != null)
            {
                _target.SetOwnMeshVisible(true);
                for (int i = 0; i < _offCols.Count; i++)
                    if (_offCols[i] != null) _offCols[i].enabled = true;
            }
            _offCols.Clear();

            ApplyPossessLift(0f);   // [Head] 원복 — 몸을 원래 자리로 되돌리기 전에 할 필요는 없지만 순서 무관
            _head = null;

            if (_shell != null) Destroy(_shell.gameObject);
            _shell = null;
            _rig = null;
            _view = null;
            _bodyMode = false;
        }

        /// <summary>[Head]에 빙의 리프트를 얹는다/원복한다. VR은 [Head] 위치를 VrTuning이 소유하므로
        /// 그쪽에 값만 넘기고, PC는 [Head] 위치가 항상 0으로 고정돼 있던 자리라 여기서 직접 쓴다.</summary>
        void ApplyPossessLift(float lift)
        {
            if (_head == null) return;

            if (VrMode.Enabled)
            {
                if (_vrTuning == null) _vrTuning = FindFirstObjectByType<VrTuning>();
                if (_vrTuning != null) _vrTuning.SetPossessLift(lift);
            }
            else
            {
                _head.localPosition = Vector3.up * lift;
            }
        }

        /// <summary>시선의 수평 각도(도). 몸은 회전하지 않으므로 여기서만 읽을 수 있다.</summary>
        float ViewYaw => _view != null ? _view.eulerAngles.y : 0f;

        static void Teleport(Transform rig, Vector3 pos)
        {
            var cc = rig.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            rig.position = pos;
            if (cc != null) cc.enabled = true;
        }

        Transform MakeShell(Vector3 rigPos, float yaw)
        {
            GameObject go;
            if (shellPrefab != null)
            {
                go = Instantiate(shellPrefab);
            }
            else
            {
                // 본체 모델이 아직 없어 임시 캡슐. 조준을 가로채지 않게 콜라이더는 제거한다.
                go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
            }
            go.name = "[BodyShell]";
            go.transform.SetPositionAndRotation(rigPos + Vector3.down * (_eyeHeight - 1f), Quaternion.Euler(0f, yaw, 0f));
            return go.transform;
        }

        // ── 고정 시점(CCTV·터렛) ──────────────────────────────────────────

        void EnterFixed(ViewEntryTarget target, Camera playerCam)
        {
            EnsureCam(playerCam);
            _cam.transform.SetParent(target.Eye, false);
            _cam.transform.localPosition = Vector3.zero;
            _cam.transform.localRotation = Quaternion.identity;
            _cam.gameObject.SetActive(true);

            if (_playerCam != null) _playerCam.enabled = false;
            target.SetOwnMeshVisible(false);

            // 렌즈가 가려진 대상(터렛) — 조준(회전)은 되지만 화면엔 항상 검정만 나온다.
            // _cam은 대상이 바뀔 때마다 재사용되므로 매번 명시적으로 설정/복구한다.
            if (target.eyeBlocked)
            {
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = Color.black;
                _cam.cullingMask = 0;
            }
            else
            {
                _cam.clearFlags = CameraClearFlags.Skybox;
                _cam.cullingMask = playerCam != null ? playerCam.cullingMask : ~0;
            }

            if (target.useBlocker)
                _mask.Begin(target.Eye, target.panRange, target.tiltRange, target.blockerColor);
            else
                _mask.End();
        }

        public void Exit(Camera playerCam)
        {
            if (_target == null) return;

            if (_bodyMode)
            {
                ExitBody();
                _target = null;
                return;
            }

            if (_mask != null) _mask.End();
            _target.SetOwnMeshVisible(true);
            _target = null;

            if (_cam != null)
            {
                _cam.transform.SetParent(null, false);
                _cam.gameObject.SetActive(false);
            }
            var pc = playerCam != null ? playerCam : _playerCam;
            if (pc != null) pc.enabled = true;
        }

        /// <summary>
        /// 빙의 중인 경비병이 (압사 등으로) 죽었을 때 호출한다. 경비병을 파괴하고 플레이어는
        /// 즉시 본체로 돌아간다 — 사망이라 페이드 연출 없이 강제로 끊는다.
        ///
        /// <para><b>본체 자체가 죽는 것과는 다르다</b> — 이건 게임오버가 아니다(§경비병은
        /// 소모품, 본체가 진짜 목숨). 본체 사망은 <see cref="GameOverManager"/> 몫이다.</para>
        ///
        /// <para><b>순서가 중요하다</b>: <see cref="GuardDestruction"/> 참조를 먼저 잡아두고
        /// <see cref="Exit"/>를 <b>먼저</b> 불러 정상 복귀 절차(메시·콜라이더 원복, 리그 텔레포트)를
        /// 태운 다음 <see cref="GuardDestruction.Destruct"/>를 부른다. 반대 순서로 하면 <see cref="Exit"/>가
        /// <c>SetOwnMeshVisible(true)</c>·콜라이더 복구로 방금 죽으며 꺼둔 것들을 되살려버린다.</para>
        /// </summary>
        public void KillPossessedTarget()
        {
            if (!_bodyMode || _target == null) return;

            var d = _target.GetComponent<GuardDestruction>();
            Exit(null);
            if (d != null && !d.Destroyed) d.Destruct(Vector3.zero);
        }

        /// <summary>매 프레임(빙의 중).</summary>
        public void Tick()
        {
            if (!Active) return;

            if (_bodyMode)
            {
                // 경비병이 리그를 따라온다 — 빙의를 풀면 데려간 자리에 그대로 남는다.
                // 방향은 <b>시선</b>에서 온다 — 몸은 회전하지 않으므로 몸에서 읽으면 항상 0이다.
                // pitch·roll은 빼야 모델이 기울지 않는다.
                if (_rig != null && _target != null)
                    _target.transform.SetPositionAndRotation(
                        _rig.position + Vector3.down * _eyeHeight,
                        Quaternion.Euler(0f, ViewYaw, 0f));
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null || Cursor.lockState != CursorLockMode.Locked) return;

            Vector2 d = mouse.delta.ReadValue();
            _pan += d.x * lookSens;
            _pitch = Mathf.Clamp(_pitch - d.y * lookSens, -89f, 89f);

            if (_target.hardClampPan) _pan = Mathf.Clamp(_pan, -_target.panRange, _target.panRange);
            if (_target.hardClampTilt) _pitch = Mathf.Clamp(_pitch, -_target.tiltRange, _target.tiltRange);

            _cam.transform.localRotation = Quaternion.Euler(_pitch, _pan, 0f);
        }

        void EnsureCam(Camera src)
        {
            if (_cam != null) return;

            var go = new GameObject("[ViewEntryCam]");   // MainCamera 태그 안 붙임 — Camera.main 유지
            _cam = go.AddComponent<Camera>();
            if (src != null)
            {
                _cam.fieldOfView = src.fieldOfView;
                _cam.nearClipPlane = src.nearClipPlane;
                _cam.farClipPlane = src.farClipPlane;
                _cam.cullingMask = src.cullingMask;
            }
            _mask = go.AddComponent<ViewConeMask>();
            go.SetActive(false);
        }
    }
}
