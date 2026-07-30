using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.View
{
    /// <summary>
    /// 테스트 하버스: WASD 이동 + 마우스 시점 + 커서 락(Esc 토글).
    /// 실제 플레이어가 아니라 스캐폴딩(HackDriver) 확인용 임시 카메라 조작. (기초_설계안 §10 검증용)
    /// </summary>
    public class FreeLookController : MonoBehaviour
    {
        public float moveSpeed = 6f;
        public float lookSens = 0.1f;

        [Tooltip("E=위 / Q=아래로 날아다닌다. 높이를 바꿔가며 확인해야 하는 테스트(보스 허리 굽힘 등)용.")]
        public bool verticalKeys = true;

        [Tooltip("상하 이동 속도(m/s). 0 이하면 moveSpeed를 쓴다.")]
        public float verticalSpeed = 0f;

        [Tooltip("마우스 시점 회전 사용 여부. VR에선 머리 트래킹(Cardboard)이 회전을 소유하므로 false(이동만).")]
        public bool lookEnabled = true;

        float _yaw, _pitch;

        void Start()
        {
            if (lookEnabled) Cursor.lockState = CursorLockMode.Locked;
            Vector3 e = transform.eulerAngles;
            _yaw = e.y;
            _pitch = e.x;
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null) return;

            if (lookEnabled && mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 d = mouse.delta.ReadValue();
                _yaw += d.x * lookSens;
                _pitch = Mathf.Clamp(_pitch - d.y * lookSens, -85f, 85f);
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }

            Vector3 m = Vector3.zero;
            if (kb.wKey.isPressed) m.z += 1f;
            if (kb.sKey.isPressed) m.z -= 1f;
            if (kb.aKey.isPressed) m.x -= 1f;
            if (kb.dKey.isPressed) m.x += 1f;
            transform.position += transform.TransformDirection(m.normalized) * moveSpeed * Time.deltaTime;

            // 상하는 <b>월드 기준</b>이다 — 시점 기준으로 하면 아래를 볼 때 E가 앞으로 가버려서
            // 높이만 바꿔가며 보는 테스트가 안 된다.
            if (verticalKeys)
            {
                float v = 0f;
                if (kb.eKey.isPressed) v += 1f;
                if (kb.qKey.isPressed) v -= 1f;
                if (v != 0f)
                    transform.position += Vector3.up * (v * (verticalSpeed > 0f ? verticalSpeed : moveSpeed) * Time.deltaTime);
            }

            if (lookEnabled && kb.escapeKey.wasPressedThisFrame)
                Cursor.lockState = Cursor.lockState == CursorLockMode.Locked
                    ? CursorLockMode.None : CursorLockMode.Locked;
        }
    }
}
