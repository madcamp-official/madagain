using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 유압프레스(피스톤 포함) 구동 소리 — 세 조각.
    ///
    /// <code>
    /// 처음 움직이기 시작       → startClip 한 번
    /// 계속 움직이는 동안       → loopClip 반복재생(경계마다 중간 볼륨으로 살짝 죽었다 다시 차오름 — 이음매 숨김)
    /// 움직임이 멎는 순간       → loopClip 빠르게 페이드아웃 + endClip 한 번
    /// </code>
    ///
    /// <para><b>"조종이 끝났다"는 <see cref="Hackable.captureState"/>가 아니라 실제 오프셋 변화로 본다.</b>
    /// <see cref="TelescopingActuator.Current"/>가 이번 프레임에도 지난 프레임과 같으면(홀드를 놓았든,
    /// 상한에 닿아 더 못 가든, 손을 뗐든 이유 불문) 그 순간이 곧 "끝"이다 — 대상을 계속 붙잡고 있어도
    /// 최근에 움직인 적이 없으면 소리도 멎어야 한다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class PressSound : MonoBehaviour
    {
        [Header("대상 (비우면 자동 탐색)")]
        public TelescopingActuator actuator;

        [Header("시작 (0~1초 구간)")]
        public AudioClip startClip;
        [Range(0f, 1f)] public float startVolume = 0.8f;

        [Header("반복 (1~2초 구간 — 계속 움직이는 동안)")]
        public AudioClip loopClip;
        [Range(0f, 1f)] public float loopVolume = 0.6f;
        [Tooltip("루프 경계(시작·끝)에서 이 시간(초)만큼 중간 볼륨↔최대 볼륨을 오간다 — 반복 이음매를 감춘다.")]
        public float loopSeamFade = 0.12f;
        [Range(0f, 1f)] public float loopSeamLevel = 0.5f;

        [Header("종료 (움직임이 멎은 직후, 2초 이후 구간)")]
        public AudioClip endClip;
        [Range(0f, 1f)] public float endVolume = 0.8f;

        [Tooltip("움직임이 멎는 순간 반복 소리가 잦아드는 시간(초).")]
        public float controlEndFade = 0.12f;

        AudioSource _oneShot, _loopSrc;
        float _lastCurrent = float.NaN;
        bool _wasMoving;
        float _endFadeMul = 1f;

        void Awake()
        {
            if (actuator == null) actuator = GetComponentInChildren<TelescopingActuator>(true);

            _oneShot = gameObject.AddComponent<AudioSource>();
            _oneShot.playOnAwake = false;
            _oneShot.spatialBlend = 1f;   // 3D — 실제로 그 자리에서 나야 한다

            _loopSrc = gameObject.AddComponent<AudioSource>();
            _loopSrc.playOnAwake = false;
            _loopSrc.spatialBlend = 1f;
            _loopSrc.loop = true;
        }

        void Update()
        {
            if (actuator == null) return;

            float current = actuator.Current;
            bool moving = !float.IsNaN(_lastCurrent) && !Mathf.Approximately(current, _lastCurrent);

            if (moving && !_wasMoving)
            {
                if (startClip != null) _oneShot.PlayOneShot(startClip, startVolume);
                if (loopClip != null) { _loopSrc.clip = loopClip; _loopSrc.time = 0f; _loopSrc.Play(); }
                _endFadeMul = 1f;
            }
            else if (!moving && _wasMoving && endClip != null)
            {
                // 최근 오프셋 조정이 없어진 바로 그 순간 — 이유는 안 가린다(홀드 해제·상한 도달·손 뗌 등).
                _oneShot.PlayOneShot(endClip, endVolume);
            }
            _wasMoving = moving;
            _lastCurrent = current;

            if (_loopSrc.isPlaying && loopClip != null)
            {
                // 이음매 숨김 — 재생 위치 기준 경계 구간에서 중간 볼륨으로 살짝 죽인다.
                float len = Mathf.Max(0.01f, loopClip.length);
                float t = _loopSrc.time;
                float fade = Mathf.Min(loopSeamFade, len * 0.5f);
                float env = 1f;
                if (fade > 0.0001f)
                {
                    if (t < fade) env = Mathf.Lerp(loopSeamLevel, 1f, t / fade);
                    else if (t > len - fade) env = Mathf.Lerp(1f, loopSeamLevel, (t - (len - fade)) / fade);
                }

                if (!moving)
                {
                    float rate = controlEndFade > 1e-3f ? 1f / controlEndFade : 999f;
                    _endFadeMul = Mathf.MoveTowards(_endFadeMul, 0f, rate * Time.deltaTime);
                    if (_endFadeMul <= 0.001f) _loopSrc.Stop();
                }

                _loopSrc.volume = loopVolume * env * _endFadeMul;
            }
        }
    }
}
