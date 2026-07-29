using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 실기 성능 표시. 헤드셋을 쓴 채로 볼 수 있어야 하고, 나중에 추이를 분석할 수 있어야 한다.
    ///
    /// <para><b>왜 F1 패널로는 안 되는가</b> — <see cref="MoveTuningPanel"/>은 키보드로 여는데
    /// 안드로이드엔 키보드가 없다. 기기에서는 열 방법이 없다.</para>
    ///
    /// <para><b>왜 IMGUI가 아니라 월드 공간 텍스트인가</b> — 스테레오 렌더링에서 화면 오버레이는
    /// 양안에 한 번만 그려져 어긋나 보인다. 카메라 앞에 놓인 실제 물체여야 두 눈에 제대로 맺힌다.</para>
    ///
    /// <para>동시에 <b>logcat에도 주기적으로</b> 남긴다. 발열로 인한 스로틀링은 순간값이 아니라
    /// 몇 분에 걸친 추이로 드러나므로, 시계열이 남아야 판단할 수 있다.</para>
    /// </summary>
    public sealed class VrStatsHud : MonoBehaviour
    {
        [Header("표시")]
        [Tooltip("헤드셋 안에 띄울지. 끄면 logcat 기록만 한다.")]
        public bool showInView = true;

        [Tooltip("카메라 앞 거리(m). 너무 가까우면 눈이 아프고 멀면 안 읽힌다.")]
        public float distance = 1.4f;

        [Tooltip("시야 중심에서 아래로 내린 각도(도). 정면을 가리지 않게.")]
        public float pitchOffsetDeg = 22f;

        [Tooltip("텍스트 크기 배율.")]
        public float scale = 0.0022f;

        [Header("기록")]
        [Tooltip("logcat에 남기는 주기(초). 0이면 안 남긴다.")]
        public float logIntervalSec = 5f;

        [Tooltip("평균을 내는 창(초).")]
        public float windowSec = 1f;

        Camera _cam;
        TextMesh _text;
        Transform _anchor;

        float _accum, _accumMax;
        int _frames;
        float _windowStart;
        float _nextLog;
        float _worstMs;      // 시작 이후 최악 프레임
        float _startTime;

        void Start()
        {
            _startTime = Time.realtimeSinceStartup;
            _windowStart = _startTime;
            _nextLog = _startTime + logIntervalSec;
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            float ms = dt * 1000f;

            _accum += ms;
            if (ms > _accumMax) _accumMax = ms;
            if (ms > _worstMs) _worstMs = ms;
            _frames++;

            float now = Time.realtimeSinceStartup;
            if (now - _windowStart >= Mathf.Max(0.1f, windowSec))
            {
                float avgMs = _frames > 0 ? _accum / _frames : 0f;
                float fps = avgMs > 1e-3f ? 1000f / avgMs : 0f;
                float spikeMs = _accumMax;
                float elapsed = now - _startTime;

                if (showInView) Render(fps, avgMs, spikeMs, elapsed);

                if (logIntervalSec > 0f && now >= _nextLog)
                {
                    _nextLog = now + logIntervalSec;
                    // 경과 시간을 함께 남긴다 — 스로틀링은 시간에 따른 하락으로만 보인다.
                    Debug.Log($"[VrStats] t={elapsed:F0}s fps={fps:F1} avg={avgMs:F1}ms " +
                              $"spike={spikeMs:F1}ms worst={_worstMs:F1}ms");
                }

                _accum = 0f; _accumMax = 0f; _frames = 0; _windowStart = now;
            }
        }

        void Render(float fps, float avgMs, float spikeMs, float elapsed)
        {
            if (!EnsureText()) return;

            _text.text = $"{fps:F0} fps   {avgMs:F1} ms\nspike {spikeMs:F1}   worst {_worstMs:F1}   t {elapsed:F0}s\n{Diagnostics()}";

            // 매 프레임 카메라 앞에 다시 놓는다. 부모로 붙이면 XR 카메라를 GameBoot이 갈아끼울 때
            // 같이 사라질 수 있어, 위치만 따라가게 한다.
            Transform c = _cam.transform;
            Quaternion look = c.rotation * Quaternion.Euler(pitchOffsetDeg, 0f, 0f);
            _anchor.SetPositionAndRotation(c.position + look * Vector3.forward * distance, look);
            _anchor.localScale = Vector3.one * (scale * distance);
        }

        /// <summary>
        /// 기기에서 "왜 안 되는가"를 가르는 최소 정보. 헤드셋을 쓰면 콘솔을 못 보므로
        /// 판단에 필요한 값은 화면에 있어야 한다.
        /// </summary>
        string Diagnostics()
        {
            var link = ControllerLink.Active;
            string net = link == null
                ? "link 없음"
                : $"{link.PacketRate:F0}/s {link.Latest.Tracking} touch{link.Latest.TouchCount}";

            string head = _cam != null ? _cam.transform.eulerAngles.y.ToString("F0") : "-";
            return $"VR:{(VrMode.Enabled ? "on" : "off")}  head:{head}\n{net}";
        }

        bool EnsureText()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return false;

            if (_text != null) return true;

            var go = new GameObject("[VrStats]");
            _anchor = go.transform;

            _text = go.AddComponent<TextMesh>();
            _text.anchor = TextAnchor.MiddleCenter;
            _text.alignment = TextAlignment.Center;
            _text.fontSize = 64;
            _text.characterSize = 1f;
            _text.color = new Color(0.4f, 1f, 0.5f, 0.9f);

            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return true;
        }

        void OnDestroy()
        {
            if (_anchor != null) Destroy(_anchor.gameObject);
        }
    }
}
