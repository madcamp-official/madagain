using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 레일 세트가 움직이는 동안 반복재생하고, 멈추면 즉시 끊지 않고 <b>페이드아웃</b>한다.
    ///
    /// <para><see cref="RailSet.AtRest"/>를 매 프레임 봐서 이동 시작/정지를 잡는다 —
    /// 홀드 크립이든 플릭이든 <c>AtRest</c> 하나로 판정되므로 이동 방식과 무관하게 동작한다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RailSet))]
    public class RailMoveSound : MonoBehaviour
    {
        [Tooltip("이동 중 반복재생할 소리.")]
        public AudioClip moveLoop;

        [Range(0f, 1f)] public float volume = 0.7f;

        [Tooltip("멈춘 뒤 볼륨이 0까지 잦아드는 시간(초).")]
        public float fadeOutTime = 0.4f;

        [Tooltip("움직이기 시작할 때 볼륨이 올라오는 시간(초). 너무 느리면 출발이 무디게 들린다.")]
        public float fadeInTime = 0.05f;

        RailSet _rail;
        AudioSource _src;
        bool _wasMoving;

        void Awake()
        {
            _rail = GetComponent<RailSet>();
            _src = GetComponent<AudioSource>();
            if (_src == null) _src = gameObject.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.loop = true;
            _src.spatialBlend = 1f;   // 3D — 레일이 실제로 있는 위치에서 들려야 한다
            _src.volume = 0f;
        }

        void Update()
        {
            bool moving = !_rail.AtRest;

            if (moving && !_wasMoving && moveLoop != null)
            {
                _src.clip = moveLoop;
                _src.Play();
            }
            _wasMoving = moving;

            float target = moving ? volume : 0f;
            float time = moving ? fadeInTime : fadeOutTime;
            float rate = (time > 1e-4f ? volume / time : 999f) * Time.deltaTime;
            _src.volume = Mathf.MoveTowards(_src.volume, target, rate);

            if (!moving && _src.volume <= 0.0001f && _src.isPlaying) _src.Stop();
        }
    }
}
