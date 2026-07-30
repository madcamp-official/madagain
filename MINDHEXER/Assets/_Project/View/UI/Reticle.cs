using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 화면 중앙 조준 표시. <b>해킹 시도 중과 조종 중에는 사라진다.</b>
    ///
    /// <para><b>왜 사라져야 하는가</b> — 레티클은 "여기를 조준하고 있다"는 표시다. 패턴을 푸는 중이나
    /// 이미 조종 중일 때는 조준이 아무 의미가 없다(시선과 무관하게 대상이 정해져 있다).
    /// 그때 계속 떠 있으면 <b>아직 무언가를 겨눌 수 있다는 거짓 신호</b>가 된다.</para>
    ///
    /// <para><b>크기를 스프링으로 움직인다</b> — 감쇠 진동이라 살짝 지나쳤다가 제자리로 온다.
    /// 선형 보간이면 기계적으로 커지고, 스프링이면 튕기듯 나타난다.</para>
    ///
    /// <para>★ 트랜스폼을 직접 건드리지 않고 <see cref="VrUiAnchor.angularSize"/>를 움직인다.
    /// 위치·스케일의 소유자는 <see cref="VrUiSpace"/>다 — 둘이 같은 트랜스폼에 쓰면 서로 지운다.
    /// 각크기를 줄이면 결과적으로 스케일이 줄어들므로 원하는 그림은 같다.</para>
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VrUiAnchor))]
    public class Reticle : MonoBehaviour
    {
        [Tooltip("평상시 각크기(도). 이 값을 스프링 배율로 곱해 쓴다.")]
        public float baseAngularSize = 1.5f;

        [Tooltip("스프링 강성. 클수록 빨리 도달한다.")]
        [Range(10f, 400f)] public float stiffness = 140f;

        [Tooltip("스프링 감쇠. 낮으면 더 많이 튕긴다. 강성의 제곱근 × 2 가 '딱 안 튕기는' 값이다.")]
        [Range(1f, 60f)] public float damping = 15f;

        [Tooltip("이 배율 아래로 작아지면 렌더러를 끈다 — 0 크기 삼각형을 그리지 않도록.")]
        public float hideBelow = 0.02f;

        VrUiAnchor _anchor;
        Renderer _renderer;
        float _value = 1f, _vel;
        float _lastTime;

        HackDriver _driver;
        PatternMinigame _minigame;
        float _nextFind;

        void OnEnable()
        {
            _anchor = GetComponent<VrUiAnchor>();
            _renderer = GetComponent<Renderer>();
            _value = 1f; _vel = 0f;
            _lastTime = Time.realtimeSinceStartup;
            Push();
        }

        void Update()
        {
            float now = Time.realtimeSinceStartup;
            float dt = Mathf.Clamp(now - _lastTime, 0f, 0.1f);
            _lastTime = now;

            // 에디터에서는 해킹 상태가 없다 — 항상 평상시 크기로 두고 스프링을 돌리지 않는다.
            if (!Application.isPlaying) { _value = 1f; _vel = 0f; Push(); return; }

            float target = ShouldHide() ? 0f : 1f;

            // 감쇠 진동: a = (목표 − 현재)·k − v·c
            float accel = (target - _value) * stiffness - _vel * damping;
            _vel += accel * dt;
            _value += _vel * dt;
            if (_value < 0f) { _value = 0f; if (_vel < 0f) _vel = 0f; }

            Push();
        }

        /// <summary>패턴을 푸는 중이거나 무언가를 조종하는 중인가.</summary>
        bool ShouldHide()
        {
            if (Time.unscaledTime >= _nextFind)
            {
                _nextFind = Time.unscaledTime + 0.5f;
                if (_driver == null) _driver = FindFirstObjectByType<HackDriver>();
                if (_minigame == null) _minigame = FindFirstObjectByType<PatternMinigame>();
            }

            if (_minigame != null && _minigame.State == PatternState.InProgress) return true;
            if (_driver != null && _driver.Controlled != null) return true;
            return false;
        }

        void Push()
        {
            if (_anchor == null) return;
            _anchor.angularSize = Mathf.Max(0f, baseAngularSize * _value);
            if (_renderer != null) _renderer.enabled = _value > hideBelow;
        }
    }
}
