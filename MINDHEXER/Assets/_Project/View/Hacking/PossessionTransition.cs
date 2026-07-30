using UnityEngine;
using UnityEngine.UI;

namespace Game.View
{
    /// <summary>
    /// 빙의 진입·해제 연출. (기초_설계안 §6.3)
    ///
    /// <para><b>진입</b> — FOV가 넓어지며 카메라가 대상 쪽으로 짧게 당겨지고(현기증 나는 흡입),
    /// 그 <b>도중에 딱</b> 암전. 0.5초 뒤 풀리면서 흰 테두리가 화면 가장자리에서 안쪽으로
    /// <b>스프링하게</b> 등장하고, 검은 가로선 노이즈가 아주 강하게 시작해 가라앉는다.</para>
    ///
    /// <para><b>해제</b> — 정확히 역순. FOV가 좁아지며 동시에 가로선 노이즈를 <b>급격히 올리는
    /// 도중에</b> 암전. 0.5초 뒤 본체 시점으로 돌아오면 노이즈가 강한 상태에서 빠르게 사라지고
    /// 흰 테두리가 스프링하게 화면 밖으로 튀어나가며 소멸한다.</para>
    ///
    /// <para><b>★ 암전이 기능이다.</b> 실제 전환(리그 순간이동)을 <b>암전 중에</b> 실행하므로
    /// 순간이동이 화면에 보이지 않는다. 연출을 얹은 게 아니라, 연출이 순간이동을 가려 주는 구조다.
    /// 그래서 전환 콜백(<c>onSwap</c>)의 호출 시점이 이 클래스의 핵심 계약이다.</para>
    ///
    /// <para><b>암전 동안 입력을 잠근다.</b> 안 잠그면 눈을 감은 채로 0.5초 걸어간다.
    /// 잠금·해제는 호출자(<see cref="HackDriver"/>)가 <c>onSwap</c>·<c>onDone</c>에서 처리한다 —
    /// 얼림은 컨텍스트와 묶여 있어 여기서 손대면 소유가 흐려진다.</para>
    ///
    /// <para><b>FOV·카메라 위치는 직접 쓰지 않는다.</b> <see cref="MotionFeel"/>의
    /// <see cref="MotionFeel.ExternalFovOffset"/>·<see cref="MotionFeel.ExternalPosOffset"/>에
    /// 값만 밀어넣는다 — 카메라의 실제 작성자는 끝까지 하나여야 한다.</para>
    ///
    /// <para><b>VR</b>: FOV·돌리를 건너뛴다(XR이 FOV를 소유하고, 인위적 FOV 변화는 멀미 1순위).
    /// 스크린 스페이스 오버레이도 양안에서 렌더되지 않으므로 <b>PC 전용</b>으로 두고,
    /// VR 경로는 실기에서 월드 패널(<see cref="VrHudSpace"/>)로 옮겨야 한다 — 미검증.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class PossessionTransition : MonoBehaviour
    {
        [Header("흡입 (진입) / 축소 (해제)")]
        [Tooltip("암전까지 걸리는 시간(초). 흡입은 이 시간을 <b>완주하지 않고</b> 도중에 끊긴다.")]
        public float suckTime = 0.18f;

        [Tooltip("FOV 가산량(도). 양수 = 시야가 넓어짐 + 전진 돌리 = 현기증 나는 흡입감(돌리 줌).\n" +
                 "★ 반대로 느껴지면 부호만 뒤집으면 된다. 해제는 자동으로 반대 방향이다.")]
        public float zoomFovDelta = 14f;

        [Tooltip("카메라가 대상 쪽으로 당겨지는 거리(m). 너무 크면 벽을 뚫고 들어간다.")]
        public float dollyDistance = 0.35f;

        [Header("암전")]
        [Tooltip("검정 유지 시간(초). 이 구간에 실제 전환이 일어난다.")]
        public float blackoutTime = 0.5f;

        [Header("테두리")]
        [Tooltip("빙의 중 테두리가 자리 잡는 위치 — 화면 가장자리에서 안쪽으로 들어간 거리(px).")]
        public float frameInset = 22f;

        [Tooltip("흰 선 두께(px).")]
        public float frameThickness = 6f;

        [Tooltip("테두리 등장·소멸 스프링. damping을 1 미만으로 둬야 살짝 넘어갔다 정착한다.")]
        public PdApproach frameSpring = new PdApproach { frequency = 11f, damping = 0.55f };

        [Header("가로선 노이즈")]
        [Tooltip("빙의 중 상시 유지되는 세기(아주 약하게).")]
        [Range(0f, 1f)] public float noiseIdle = 0.08f;

        [Tooltip("등장·소멸 순간의 세기(아주 강하게).")]
        [Range(0f, 1f)] public float noiseBurst = 0.85f;

        [Tooltip("등장 후 상시 세기로 가라앉는 시간(초).")]
        public float noiseSettleTime = 0.5f;

        [Tooltip("해제 시 노이즈가 폭증하는 시간(초). suckTime과 같이 두면 암전과 딱 맞는다.")]
        public float noiseRiseTime = 0.18f;

        [Tooltip("복귀 후 노이즈가 사라지는 시간(초).")]
        public float noiseFadeTime = 0.3f;

        /// <summary>
        /// <b>전환 연출이 진행 중</b>인가. 진행 중엔 새 빙의·해제를 받지 않는다.
        ///
        /// <para>★ <see cref="Phase.Held"/>(빙의 중 테두리 유지)는 <b>포함하지 않는다.</b> 그건 연출이
        /// 끝난 정착 상태이지 진행 중이 아니다 — 포함시키면 빙의하는 순간 Busy가 영구히 true가 되어
        /// <see cref="BeginExit"/>가 계속 거부되고 <b>영영 복귀할 수 없다</b>.</para>
        /// </summary>
        public bool Busy => _phase != Phase.Idle && _phase != Phase.Held;

        /// <summary>빙의 중 테두리가 떠 있는 상태인가(연출 완료 후 유지 구간 포함).</summary>
        public bool FrameVisible { get; private set; }

        enum Phase { Idle, SuckIn, Black, FrameIn, Held, ZoomOut, BlackExit, FrameOut }

        Phase _phase = Phase.Idle;
        float _t;
        bool _entering;
        System.Action _onSwap;

        Camera _cam;
        MotionFeel _feel;
        Vector3 _dollyDir;

        // UI
        Canvas _canvas;
        Image _black;
        Image _frame;
        Material _frameMat;

        static readonly int IdInset = Shader.PropertyToID("_Inset");
        static readonly int IdThick = Shader.PropertyToID("_Thickness");
        static readonly int IdNoise = Shader.PropertyToID("_Noise");
        static readonly int IdOpacity = Shader.PropertyToID("_Opacity");

        /// <summary>
        /// 진입 연출 시작. <paramref name="onSwap"/>은 <b>암전이 켜진 뒤</b> 한 번 호출된다 —
        /// 거기서 실제 <see cref="ViewEntryController.Enter"/>를 하면 순간이동이 안 보인다.
        /// </summary>
        public void BeginEnter(Camera cam, Vector3 targetPoint, System.Action onSwap)
        {
            if (Busy) return;
            Setup(cam, onSwap, true);
            _dollyDir = SafeDir(targetPoint - cam.transform.position);
            _phase = Phase.SuckIn;
            _t = 0f;
        }

        /// <summary>해제 연출 시작. 규약은 <see cref="BeginEnter"/>와 같다.</summary>
        public void BeginExit(Camera cam, System.Action onSwap)
        {
            if (Busy) return;
            Setup(cam, onSwap, false);
            _dollyDir = -SafeDir(cam.transform.forward);   // 뒤로 물러난다 = 축소
            _phase = Phase.ZoomOut;
            _t = 0f;
        }

        void Setup(Camera cam, System.Action onSwap, bool entering)
        {
            _cam = cam != null ? cam : Camera.main;
            _feel = _cam != null ? _cam.GetComponent<MotionFeel>() : null;
            _onSwap = onSwap;
            _entering = entering;
            EnsureUi();
        }

        static Vector3 SafeDir(Vector3 v)
            => v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;

        void LateUpdate()
        {
            if (_phase == Phase.Idle || _phase == Phase.Held) return;

            // 연출은 timeScale에 안 묶인다(정지 연출 중에도 돌아야 함).
            // ★ dt를 잘라야 한다 — 컴파일·로딩으로 한 프레임이 통째로 길어지면 그 한 프레임에
            //   흡입과 암전 0.5초가 동시에 소비돼 <b>암전이 아예 안 보이고</b> 순간이동이 노출된다.
            //   프레임이 튀어도 연출은 최소 프레임 수를 확보해야 한다.
            _t += Mathf.Min(Time.unscaledDeltaTime, 0.05f);

            switch (_phase)
            {
                case Phase.SuckIn:  TickSuck(1f);  break;
                case Phase.ZoomOut: TickSuck(-1f); break;
                case Phase.Black:
                case Phase.BlackExit: TickBlack(); break;
                case Phase.FrameIn:   TickFrameIn();  break;
                case Phase.FrameOut:  TickFrameOut(); break;
                case Phase.Held: break;
            }
        }

        /// <summary>흡입/축소 — 끝까지 가지 않고 <see cref="suckTime"/>에서 암전으로 끊긴다.</summary>
        void TickSuck(float sign)
        {
            float u = suckTime > 1e-4f ? Mathf.Clamp01(_t / suckTime) : 1f;
            float e = u * u;   // 가속(ease-in) — 뒤로 갈수록 빨라져 '빨려드는' 느낌이 난다

            if (!VrMode.Enabled && _feel != null)
            {
                _feel.ExternalFovOffset = zoomFovDelta * sign * e;
                _feel.ExternalPosOffset = _dollyDir * (dollyDistance * e);
            }

            // 해제는 노이즈를 동시에 급격히 올린다.
            if (sign < 0f)
            {
                float rise = noiseRiseTime > 1e-4f ? Mathf.Clamp01(_t / noiseRiseTime) : 1f;
                SetFrame(frameInset, Mathf.Lerp(noiseIdle, noiseBurst, rise), 1f);
            }

            if (u < 1f) return;

            // ── 딱 암전. 실제 전환은 여기서. ──
            ClearCameraOffsets();
            SetBlack(true);
            var swap = _onSwap; _onSwap = null;
            swap?.Invoke();

            _phase = _entering ? Phase.Black : Phase.BlackExit;
            _t = 0f;
        }

        void TickBlack()
        {
            if (_t < blackoutTime) return;

            SetBlack(false);
            _t = 0f;

            if (_entering)
            {
                // 테두리는 화면 가장자리(0)에서 시작해 안쪽으로 스프링한다.
                frameSpring.SnapTo(0f);
                frameSpring.Target = frameInset;
                FrameVisible = true;
                _phase = Phase.FrameIn;
            }
            else
            {
                // 복귀 직후엔 노이즈가 강한 상태로 시작한다.
                frameSpring.SnapTo(frameInset);
                frameSpring.Target = -frameThickness * 2f;   // 화면 밖으로 튀어나간다
                _phase = Phase.FrameOut;
            }
        }

        void TickFrameIn()
        {
            frameSpring.Step(Mathf.Min(Time.unscaledDeltaTime, 0.05f));   // dt 상한 — _t와 같은 이유
            float settle = noiseSettleTime > 1e-4f ? Mathf.Clamp01(_t / noiseSettleTime) : 1f;
            SetFrame(frameSpring.Value, Mathf.Lerp(noiseBurst, noiseIdle, settle), 1f);

            // 스프링이 정착하고 노이즈도 가라앉으면 유지 구간으로.
            if (settle >= 1f && Mathf.Abs(frameSpring.Value - frameInset) < 0.5f)
            {
                SetFrame(frameInset, noiseIdle, 1f);
                _phase = Phase.Held;
            }
        }

        void TickFrameOut()
        {
            frameSpring.Step(Mathf.Min(Time.unscaledDeltaTime, 0.05f));   // dt 상한 — _t와 같은 이유
            float fade = noiseFadeTime > 1e-4f ? Mathf.Clamp01(_t / noiseFadeTime) : 1f;
            SetFrame(frameSpring.Value, Mathf.Lerp(noiseBurst, 0f, fade), 1f - fade);

            if (fade >= 1f)
            {
                FrameVisible = false;
                if (_frame != null) _frame.enabled = false;
                _phase = Phase.Idle;
            }
        }

        void ClearCameraOffsets()
        {
            if (_feel == null) return;
            _feel.ExternalFovOffset = 0f;
            _feel.ExternalPosOffset = Vector3.zero;
        }

        /// <summary>연출이 도중에 끊겨도 카메라·화면이 이상한 상태로 남지 않게 한다.</summary>
        public void Abort()
        {
            ClearCameraOffsets();
            SetBlack(false);
            FrameVisible = false;
            if (_frame != null) _frame.enabled = false;
            _onSwap = null;
            _phase = Phase.Idle;
            _t = 0f;
        }

        void OnDisable() => Abort();

        // ── UI ────────────────────────────────────────────────────────────

        void SetBlack(bool on)
        {
            if (_black != null) _black.enabled = on;
        }

        void SetFrame(float inset, float noise, float opacity)
        {
            if (_frame == null || _frameMat == null) return;
            _frame.enabled = opacity > 0.001f;
            _frameMat.SetFloat(IdInset, inset);
            _frameMat.SetFloat(IdThick, frameThickness);
            _frameMat.SetFloat(IdNoise, Mathf.Clamp01(noise));
            _frameMat.SetFloat(IdOpacity, Mathf.Clamp01(opacity));
        }

        void EnsureUi()
        {
            if (_canvas != null) return;

            // PatternUI와 같은 방식 — 런타임에 자기 캔버스를 만든다. 씬에 배선할 게 없어야
            // 아무 씬에 GameBoot만 두면 동작한다는 규약이 유지된다.
            var go = new GameObject("[PossessionUI]");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 900;   // 패턴 UI보다 위, 개발 콘솔보다는 아래를 의도

            _black = MakeFullScreen("Blackout", null);
            _black.color = Color.black;
            _black.enabled = false;

            var sh = Shader.Find("MINDHEXER/PossessionFrame");
            if (sh == null)
            {
                Debug.LogWarning("[빙의] 셰이더 'MINDHEXER/PossessionFrame'를 찾지 못해 테두리를 그릴 수 없습니다.", this);
                return;
            }
            _frameMat = new Material(sh) { name = "[PossessionFrame]" };
            _frame = MakeFullScreen("Frame", _frameMat);
            _frame.enabled = false;
        }

        Image MakeFullScreen(string name, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_canvas.transform, false);

            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            if (mat != null) img.material = mat;

            var rt = img.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return img;
        }
    }
}
