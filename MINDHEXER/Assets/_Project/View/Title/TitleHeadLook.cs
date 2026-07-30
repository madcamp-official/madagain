using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 모노스코프 헤드트래킹(매직 윈도우). 디바이스 자이로가 있으면 폰 자세로 카메라를 회전하고,
    /// 에디터/PC에서는 마우스로 살짝 둘러본다. (spec 4: 타이틀을 VR 공간처럼 — 시야가 돈다)
    ///
    /// 로봇은 월드에 고정되어 있으므로, 시야를 돌리면 로봇이 프레임 밖으로 나간다.
    /// UI(로고·버튼)는 ScreenSpace 캔버스라 항상 시야에 남아 "시야를 따라다니는" 지침을 만족한다.
    ///
    /// 시작 순간의 폰 자세를 기준(base)으로 잡아, 어떤 방향으로 폰을 들고 있어도
    /// 처음엔 로봇 정면을 보게 한다.
    /// </summary>
    public sealed class TitleHeadLook : MonoBehaviour
    {
        [Tooltip("디바이스 자이로 사용. 없으면 마우스로 폴백.")]
        public bool useGyro = true;

        [Header("마우스 폴백(에디터/PC)")]
        [Tooltip("마우스 감도.")]
        public float mouseSensitivity = 1.5f;
        [Tooltip("좌우/상하 회전 제한(도). 0이면 무제한.")]
        public float yawClamp = 70f;
        public float pitchClamp = 45f;
        [Tooltip("입력이 없을 때의 은은한 시야 드리프트(도). 타이틀이 평면이 아니라 3D 공간임을 보여준다. 0이면 정지.")]
        public float idleSwayDeg = 3f;

        bool _gyroActive;
        bool _gyroBased;
        Quaternion _initial;
        Quaternion _gyroBase;
        float _yaw, _pitch;

        void Start()
        {
            _initial = transform.localRotation;

            if (useGyro && SystemInfo.supportsGyroscope)
            {
                Input.gyro.enabled = true;
                _gyroActive = true;
            }
        }

        void Update()
        {
            if (_gyroActive)
            {
                Quaternion att = GyroToUnity(Input.gyro.attitude);
                if (!_gyroBased)
                {
                    // 시작 자세를 기준으로: 이후 회전은 시작점 대비 상대값 → 처음엔 로봇 정면.
                    _gyroBase = _initial * Quaternion.Inverse(att);
                    _gyroBased = true;
                }
                transform.localRotation = _gyroBase * att;
                return;
            }

            // 마우스 폴백 + 은은한 아이들 드리프트(3D 공간 가시화).
            _yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
            _pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
            if (yawClamp > 0f) _yaw = Mathf.Clamp(_yaw, -yawClamp, yawClamp);
            if (pitchClamp > 0f) _pitch = Mathf.Clamp(_pitch, -pitchClamp, pitchClamp);

            float swayY = Mathf.Sin(Time.time * 0.5f) * idleSwayDeg;
            float swayX = Mathf.Sin(Time.time * 0.37f) * idleSwayDeg * 0.5f;
            transform.localRotation = _initial * Quaternion.Euler(_pitch + swayX, _yaw + swayY, 0f);
        }

        // 디바이스 자이로(우수좌표·화면기준) → 유니티(좌수) 변환. (표준 매직윈도우 보정)
        static Quaternion GyroToUnity(Quaternion q)
        {
            return new Quaternion(q.x, q.y, -q.z, -q.w);
        }
    }
}
