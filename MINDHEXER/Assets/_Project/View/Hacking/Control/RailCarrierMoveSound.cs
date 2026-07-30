using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 레일 캐리어가 움직이는 동안의 릴(reel) 사운드 — 파일 하나를 세 구간으로 나눠 쓴다.
    ///
    /// <code>
    /// 이동 시작 순간     → 클립 처음 headTime초를 재생
    /// 계속 움직이는 동안 → headTime ~ (길이-tailTime) 구간을 반복재생
    /// 이동이 멎는 순간   → 반복을 끊고 (길이-tailTime) ~ 끝까지 재생한 뒤 정지
    /// </code>
    ///
    /// <para>세 조각을 별도 파일로 자르는 대신 <see cref="AudioSource.time"/>으로 재생 위치를
    /// 직접 옮겨 붙인다. "멎었다"는 <see cref="RailCarrier"/>의 내부 상태를 빌리지 않고
    /// <c>transform.position</c>이 지난 프레임과 같은지로 직접 본다(<see cref="PressSound"/>가
    /// 액추에이터 값을 보는 것과 같은 방식) — 홀드 크립이든 플릭이든, 손을 뗐든 이동 한계에
    /// 닿았든 상관없이 실제로 멎은 순간을 잡는다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RailCarrier))]
    public class RailCarrierMoveSound : MonoBehaviour
    {
        [Tooltip("이동 중 재생할 릴 사운드. 앞 headTime초=시작, 뒤 tailTime초=정지, 그 사이=반복 구간.")]
        public AudioClip reelClip;

        [Range(0f, 1f)] public float volume = 0.8f;

        [Tooltip("클립 앞부분에서 '시작' 구간으로 쓸 길이(초).")]
        public float headTime = 1f;

        [Tooltip("클립 뒷부분에서 '정지' 구간으로 쓸 길이(초).")]
        public float tailTime = 1f;

        AudioSource _src;
        Vector3 _lastPos;
        bool _hasLastPos;
        bool _wasMoving;
        float _loopStart, _loopEnd;

        void Awake()
        {
            _src = gameObject.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.loop = false;
            _src.spatialBlend = 1f;   // 3D — 레일이 실제로 있는 위치에서 들려야 한다
            _src.volume = volume;
            _src.clip = reelClip;

            if (reelClip != null)
            {
                _loopStart = Mathf.Min(headTime, reelClip.length);
                _loopEnd = Mathf.Max(_loopStart, reelClip.length - tailTime);
            }
        }

        void Update()
        {
            if (reelClip == null) return;

            Vector3 pos = transform.position;
            bool moving = _hasLastPos && (pos - _lastPos).sqrMagnitude > 1e-10f;
            _lastPos = pos;
            _hasLastPos = true;

            if (moving && !_wasMoving)
            {
                _src.time = 0f;   // 이동 시작 — 처음(시작 1초 포함)부터 재생
                _src.Play();
            }
            else if (!moving && _wasMoving && _src.isPlaying && _src.time < _loopEnd)
            {
                _src.time = _loopEnd;   // 이동 정지 — 반복 구간을 끊고 곧장 정지 구간(마지막 1초)으로
            }
            _wasMoving = moving;

            if (moving && _src.isPlaying && _src.time >= _loopEnd)
                _src.time = _loopStart;   // 계속 이동 중 — 반복 구간을 순환

            if (!moving && _src.isPlaying && _src.time >= reelClip.length - 0.02f)
                _src.Stop();
        }
    }
}
