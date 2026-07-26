using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// [2026-07-22] 배경음악(BGM). Play 시 자동 부착, 루프 재생. ★ 순수 연출·독립.
    /// SFX와 별개의 AudioSource라 서로 볼륨이 안 섞인다. 곡은 Resources/Music/bgm.
    /// </summary>
    public class BgmPlayer : MonoBehaviour, IRunResettable
    {
        // ★ AudioSource.volume은 최대 1.0(100%)까지만 먹힌다 — 1보다 크게 넣어도 소용없다.
        //   더 크게 하려면 음악 파일 자체 음량을 키워야 한다(이미 라우드하게 처리해둠).
        public static float Volume = 1.0f;

        static BgmPlayer inst;
        AudioSource src;

        void Awake()
        {
            // 씬이 다시 로드돼도 하나만 살아남아 끊김 없이 이어 재생한다.
            if (inst != null && inst != this) { Destroy(gameObject); return; }
            inst = this;
            DontDestroyOnLoad(gameObject);

            var clip = Resources.Load<AudioClip>("Music/bgm");
            if (clip == null)
            {
                Debug.LogWarning("[BgmPlayer] Resources/Music/bgm 을 못 찾음 — BGM 재생 안 함.");
                return;
            }
            src = gameObject.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;             // 곡 끝나면 자동으로 처음부터
            src.playOnAwake = false;
            src.spatialBlend = 0f;       // 2D
            src.volume = Mathf.Clamp01(Volume);
            src.Play();
        }

        void Update()
        {
            if (src == null) return;
            float v = Mathf.Clamp01(Volume);
            if (!Mathf.Approximately(src.volume, v)) src.volume = v;
            // 루프 보강 — 어떤 이유로든 멈추면(끝나거나 중단되면) 다시 재생한다.
            if (!src.isPlaying) { src.loop = true; src.Play(); }
        }

        /// <summary>게임 재시작 시 BGM을 처음부터 다시 튼다(루프라 그냥 두면 이어서 재생됨).</summary>
        public void ResetForRestart()
        {
            if (src == null) return;
            src.Stop();
            src.time = 0f;
            src.loop = true;
            src.volume = Mathf.Clamp01(Volume);
            src.Play();
        }
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class BgmPlayerBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<BgmPlayer>() == null)
                new GameObject("[BgmPlayer]").AddComponent<BgmPlayer>();
        }
    }
}
