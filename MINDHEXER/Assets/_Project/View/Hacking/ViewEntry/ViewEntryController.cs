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
        Transform _rig;                 // 플레이어 리그 = 카메라 오브젝트(GameBoot 구성)
        Transform _shell;               // 원래 자리에 남는 본체
        float _eyeHeight = 1.6f;        // 리그 원점(눈)에서 발까지
        readonly List<Collider> _offCols = new List<Collider>();

        public bool Active => _target != null;

        /// <summary>지금 시야를 제공하는 카메라. <b>몸 이동 모드에선 플레이어 카메라를 그대로 쓰므로 null</b>.</summary>
        public Camera Cam => Active && !_bodyMode ? _cam : null;

        /// <summary>빙의 중 본체 이동이 허용되는지(경비병만 true).</summary>
        public bool AllowsMove => Active && _target.allowsMove;

        /// <summary>남겨진 본체 셸. 조종 실(§7)의 시작점 — 빙의 중이 아니면 null.</summary>
        public Transform Shell => _shell;

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
            _rig = playerCam != null ? playerCam.transform : null;
            if (_rig == null) { _target = null; return; }

            var cc = _rig.GetComponent<CharacterController>();
            if (cc != null) _eyeHeight = cc.height * 0.5f - cc.center.y;   // 원점에서 발까지

            // 원래 자리에 본체를 남긴다 — 이게 취약 표적이자 복귀 지점이다(§6.3).
            _shell = MakeShell(_rig.position, _rig.eulerAngles.y);

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

            if (_shell != null) Destroy(_shell.gameObject);
            _shell = null;
            _rig = null;
            _bodyMode = false;
        }

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

        /// <summary>매 프레임(빙의 중).</summary>
        public void Tick()
        {
            if (!Active) return;

            if (_bodyMode)
            {
                // 경비병이 리그를 따라온다 — 빙의를 풀면 데려간 자리에 그대로 남는다.
                if (_rig != null && _target != null)
                    _target.transform.SetPositionAndRotation(
                        _rig.position + Vector3.down * _eyeHeight,
                        Quaternion.Euler(0f, _rig.eulerAngles.y, 0f));   // pitch는 빼야 모델이 안 기운다
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
