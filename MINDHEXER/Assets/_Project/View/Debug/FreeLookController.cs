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

        float _yaw, _pitch;

        void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Vector3 e = transform.eulerAngles;
            _yaw = e.y;
            _pitch = e.x;
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null) return;

            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
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

            if (kb.escapeKey.wasPressedThisFrame)
                Cursor.lockState = Cursor.lockState == CursorLockMode.Locked
                    ? CursorLockMode.None : CursorLockMode.Locked;
        }
    }
}
