using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.View
{
    /// <summary>
    /// 빙의 시점 구동 — 대상의 눈 자리에 <b>별도 카메라</b>를 띄우고 마우스로 pan/tilt 한다. (§2.5)
    ///
    /// <para>플레이어 본체는 제자리에 그대로 둔다(옮기지 않는다) — CharacterController를 직접 옮기면
    /// 복귀 위치가 깨지고 물리가 꼬이기 때문. 본체 카메라만 잠시 끈다.</para>
    ///
    /// <para>대상별 차이(좌우/상하 범위, 잠금, 이동 허용)는 전부 <see cref="ViewEntryTarget"/> 값에서
    /// 읽는다 — 터렛·경비병을 붙일 때 이 클래스는 손대지 않는다.</para>
    /// </summary>
    public class ViewEntryController : MonoBehaviour
    {
        [Tooltip("빙의 시점 마우스 감도.")]
        public float lookSens = 0.1f;

        ViewEntryTarget _target;
        Camera _cam;
        Camera _playerCam;
        ViewConeMask _mask;
        float _pan, _pitch;   // _pitch는 FirstPersonPlayer와 같은 규약 — 음수가 위

        public bool Active => _target != null;

        /// <summary>지금 시야를 제공하는 카메라(빙의 중이면 빙의 카메라, 아니면 null).</summary>
        public Camera Cam => Active ? _cam : null;

        /// <summary>빙의 중 본체 이동이 허용되는지(경비병만 true).</summary>
        public bool AllowsMove => Active && _target.allowsMove;

        public void Enter(ViewEntryTarget target, Camera playerCam)
        {
            if (target == null) return;
            Exit(playerCam);

            _target = target;
            _playerCam = playerCam;
            _pan = _pitch = 0f;

            EnsureCam(playerCam);
            _cam.transform.SetParent(target.Eye, false);
            _cam.transform.localPosition = Vector3.zero;
            _cam.transform.localRotation = Quaternion.identity;
            _cam.gameObject.SetActive(true);

            if (_playerCam != null) _playerCam.enabled = false;

            // 눈이 대상 모델 안에 있어 자기 껍질이 보인다 → 빙의 중엔 대상 메시를 숨긴다.
            target.SetOwnMeshVisible(false);

            if (target.useBlocker)
                _mask.Begin(target.Eye, target.panRange, target.tiltRange, target.blockerColor);
            else
                _mask.End();
        }

        public void Exit(Camera playerCam)
        {
            if (_target == null) return;

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

        /// <summary>매 프레임(빙의 중). 마우스로 pan/tilt.</summary>
        public void Tick()
        {
            if (!Active) return;
            var mouse = Mouse.current;
            if (mouse == null || Cursor.lockState != CursorLockMode.Locked) return;

            // 본체(FirstPersonPlayer)와 같은 규약 — 마우스를 올리면 _pitch가 음수가 되고 시점이 올라간다.
            Vector2 d = mouse.delta.ReadValue();
            _pan += d.x * lookSens;
            _pitch = Mathf.Clamp(_pitch - d.y * lookSens, -89f, 89f);

            // 자유 회전이 기본 — 범위 밖은 차폐가 가린다. 하드 클램프는 터렛 상하처럼 물리적으로
            // 못 도는 축에만 쓴다.
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
