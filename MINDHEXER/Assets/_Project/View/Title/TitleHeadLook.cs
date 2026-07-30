using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.View
{
    /// <summary>
    /// 모노스코프 헤드트래킹(매직 윈도우). 디바이스 자이로가 있으면 폰 자세로 카메라를 회전하고,
    /// 에디터/PC에서는 마우스로 살짝 둘러본다. (spec 4: 타이틀을 VR 공간처럼 — 시야가 돈다)
    ///
    /// <b>입력은 반드시 Input System 패키지로 읽는다.</b> 이 프로젝트는 Player Settings의
    /// Active Input Handling이 "Input System Package (New)" 단독이라, 레거시 <c>UnityEngine.Input</c>을
    /// 건드리면 매 프레임 InvalidOperationException이 터지고 그 뒤 코드(회전·시차)가 통째로 실행되지 않는다.
    ///
    /// 로봇은 월드에 고정되어 있으므로, 시야를 돌리면 로봇이 프레임 밖으로 나간다.
    /// UI(로고·버튼)는 ScreenSpace 캔버스라 항상 시야에 남아 "시야를 따라다니는" 지침을 만족한다.
    ///
    /// 시작 순간의 폰 자세를 기준(base)으로 잡아, 어떤 방향으로 폰을 들고 있어도
    /// 처음엔 로봇 정면을 보게 한다.
    /// </summary>
    public sealed class TitleHeadLook : MonoBehaviour
    {
        [Header("실기(폰) — 자세 센서")]
        [Tooltip("폰의 회전값(AttitudeSensor)으로 시야를 돌린다. 센서가 없으면 자동으로 마우스 폴백.")]
        public bool useGyro = true;

        [Header("에디터·PC 테스트용 마우스")]
        [Tooltip("마우스 감도. 실기에서는 쓰이지 않는다(자세 센서가 우선). " +
                 "타이틀은 '살짝 둘러보는' 연출이라 낮게 잡는다 — 크게 올리면 화면이 홱홱 돈다.")]
        public float mouseSensitivity = 0.4f;
        [Tooltip("좌우/상하 회전 제한(도). 0이면 무제한.")]
        public float yawClamp = 70f;
        public float pitchClamp = 45f;
        [Tooltip("입력이 없을 때의 은은한 시야 드리프트(도). 타이틀이 평면이 아니라 3D 공간임을 보여준다. 0이면 정지.")]
        public float idleSwayDeg = 3f;

        [Header("시차(parallax)")]
        [Tooltip("시야를 돌릴 때 카메라를 함께 평행이동시키는 양(m). " +
                 "회전만 하면 배경이 통째로 도는 파노라마와 구분이 안 되지만, 평행이동이 들어가면 " +
                 "로봇의 앞뒤가 서로 다른 속도로 밀려 '진짜 입체 공간'이 눈으로 확인된다. 0이면 회전만.\n" +
                 "씬 스케일에 비례해야 한다 — 로봇이 크고 카메라가 멀수록 이 값도 커야 시차가 보인다. " +
                 "(TitleSceneBuilder가 로봇 키의 6%로 자동 설정한다)")]
        public float parallaxAmount = 3.5f;

        // 레거시 GetAxis("Mouse X")는 픽셀 이동량에 기본 감도 0.1을 곱한 값을 돌려줬다.
        // Input System의 Mouse.delta는 픽셀 그대로라, 기존 mouseSensitivity 값을 그대로 쓰려고 같은 계수를 곱한다.
        const float MouseDeltaToAxis = 0.1f;

        bool _gyroBased;
        bool _gyroWarned;
        float _gyroWaitTime;
        Quaternion _initial;
        Vector3 _initialPos;
        Quaternion _gyroBase;
        float _yaw, _pitch;

        void Start()
        {
            _initial = transform.localRotation;
            _initialPos = transform.localPosition;
        }

        /// <summary>
        /// 지금 쓸 수 있는 자세 센서를 돌려준다(없으면 null).
        /// <para>Start에서 한 번만 확인하면 안 된다 — 안드로이드에서 센서 디바이스가 첫 프레임 이후에
        /// 등록되는 경우가 있어, 그때 놓치면 실기에서 영영 마우스 폴백에 갇힌다(=폰을 돌려도 반응 없음).
        /// 그래서 매 프레임 확인하고, 잡히는 순간 활성화한다.</para>
        /// </summary>
        AttitudeSensor AcquireGyro()
        {
            if (!useGyro) return null;

            var sensor = AttitudeSensor.current;
            if (sensor == null)
            {
                // 실기에서 센서를 못 잡으면 조용히 마우스로 흘러가 원인을 못 찾는다 → 한 번은 알린다.
                _gyroWaitTime += Time.unscaledDeltaTime;
                if (!_gyroWarned && _gyroWaitTime > 2f)
                {
                    _gyroWarned = true;
                    Debug.LogWarning("[TitleHeadLook] AttitudeSensor를 찾지 못했습니다. " +
                                     "폰 회전으로 시야가 돌지 않습니다(마우스 폴백으로 동작). " +
                                     "기기에 회전 벡터 센서가 없거나 Input System이 아직 등록하지 않았습니다.");
                }
                return null;
            }

            if (!sensor.enabled) InputSystem.EnableDevice(sensor);   // 센서는 기본 비활성이라 명시적으로 켠다
            return sensor;
        }

        void Update()
        {
            var gyro = AcquireGyro();
            if (gyro != null && gyro.enabled)
            {
                Quaternion att = GyroToUnity(gyro.attitude.ReadValue());
                if (!_gyroBased)
                {
                    // 시작 자세를 기준으로: 이후 회전은 시작점 대비 상대값 → 처음엔 로봇 정면.
                    _gyroBase = _initial * Quaternion.Inverse(att);
                    _gyroBased = true;
                }
                transform.localRotation = _gyroBase * att;
                ApplyParallax();
                return;
            }

            // 마우스 폴백 + 은은한 아이들 드리프트(3D 공간 가시화).
            // 마우스가 없는 환경(빌드된 폰 등)에서도 드리프트는 계속 돌도록 delta만 0으로 둔다.
            Vector2 mouseDelta = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
            _yaw += mouseDelta.x * MouseDeltaToAxis * mouseSensitivity;
            _pitch -= mouseDelta.y * MouseDeltaToAxis * mouseSensitivity;
            if (yawClamp > 0f) _yaw = Mathf.Clamp(_yaw, -yawClamp, yawClamp);
            if (pitchClamp > 0f) _pitch = Mathf.Clamp(_pitch, -pitchClamp, pitchClamp);

            float swayY = Mathf.Sin(Time.time * 0.5f) * idleSwayDeg;
            float swayX = Mathf.Sin(Time.time * 0.37f) * idleSwayDeg * 0.5f;
            transform.localRotation = _initial * Quaternion.Euler(_pitch + swayX, _yaw + swayY, 0f);
            ApplyParallax();
        }

        /// <summary>
        /// 시작 자세 대비 얼마나 돌아봤는지에 비례해 카메라를 좌우/상하로 아주 조금 민다.
        /// 회전만으로는 깊이 단서가 없지만, 씬 크기에 비례한 평행이동이 들어가면 앞뒤가 서로 다르게 밀려
        /// 화면이 평면이 아니라는 게 즉시 보인다.
        /// </summary>
        void ApplyParallax()
        {
            if (parallaxAmount <= 0f) return;
            Quaternion delta = Quaternion.Inverse(_initial) * transform.localRotation;
            Vector3 f = delta * Vector3.forward;                       // 시작 시야 기준 바라보는 방향
            Vector3 offset = new Vector3(f.x, f.y, 0f) * parallaxAmount;
            transform.localPosition = _initialPos + _initial * offset;
        }

        // 디바이스 자세(우수좌표) → 유니티(좌수) 변환. (표준 매직윈도우 보정)
        //
        // Input System의 AttitudeSensor.attitude는 안드로이드 로테이션 벡터 '원본'에
        // 화면 방향 보정(CompensateRotationProcessor)만 적용한 값이다 — 좌표계 변환은 해주지 않으므로
        // 레거시 Input.gyro.attitude와 똑같이 여기서 뒤집어야 한다. (세로/가로 회전 보정은 이미 되어 있음)
        static Quaternion GyroToUnity(Quaternion q)
        {
            return new Quaternion(q.x, q.y, -q.z, -q.w);
        }
    }
}
