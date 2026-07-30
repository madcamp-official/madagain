using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 배경 음악 — 평상시 한 곡, 보스 추격 중엔 다른 곡으로 교체했다가 끝나면 되돌아온다.
    ///
    /// <code>
    /// 게임 시작                        → defaultClip 반복재생
    /// 추격 시작(BossChaseState.Begin) → chaseClip으로 크로스페이드, 반복재생
    /// 추격 종료(BossChaseState.End)   → defaultClip으로 다시 크로스페이드
    /// </code>
    /// </summary>
    [DisallowMultipleComponent]
    public class BossMusicController : MonoBehaviour
    {
        [Tooltip("평상시(보스 추격 중이 아닐 때) 반복재생.")]
        public AudioClip defaultClip;

        [Tooltip("추격이 시작되면 이 곡으로 교체해 반복재생.")]
        public AudioClip chaseClip;

        [Range(0f, 1f)] public float volume = 0.8f;

        [Tooltip("곡 교체 시 크로스페이드 시간(초). 0이면 즉시 전환.")]
        public float crossfadeTime = 1.5f;

        AudioSource _src;
        float _fadeT = -1f;
        float _fadeFrom, _fadeTo;

        void Awake()
        {
            _src = GetComponent<AudioSource>();
            if (_src == null) _src = gameObject.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.spatialBlend = 0f;   // 2D — 배경음악
            _src.loop = true;
        }

        void Start() => PlayClip(defaultClip);

        void OnEnable() => BossChaseState.OnActiveChanged += HandleChaseChanged;
        void OnDisable() => BossChaseState.OnActiveChanged -= HandleChaseChanged;

        void Update()
        {
            if (_fadeT < 0f) return;
            _fadeT += Time.deltaTime;
            float u = crossfadeTime > 1e-3f ? Mathf.Clamp01(_fadeT / crossfadeTime) : 1f;
            _src.volume = Mathf.Lerp(_fadeFrom, _fadeTo, u);
            if (u >= 1f) _fadeT = -1f;
        }

        void HandleChaseChanged(bool active) => PlayClip(active ? chaseClip : defaultClip);

        void PlayClip(AudioClip clip)
        {
            if (clip == null) return;
            if (_src.clip == clip && _src.isPlaying) return;   // 이미 그 곡이면 처음부터 다시 트지 않는다

            _src.clip = clip;
            _src.volume = crossfadeTime > 1e-3f ? 0f : volume;
            _src.Play();
            if (crossfadeTime > 1e-3f) { _fadeFrom = 0f; _fadeTo = volume; _fadeT = 0f; }
        }
    }
}
