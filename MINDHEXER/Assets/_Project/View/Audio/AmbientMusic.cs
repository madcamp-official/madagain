using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 평상시 낮게 깔리는 절차적 앰비언트 BGM. 오디오 에셋 0개 — AudioClip.Create 합성.
    ///
    /// 존재 이유의 절반은 <b>늘어지기 위해서</b>다. 예지가 발동하면 PredictionAudio가
    /// Slow()를 불러 이 곡을 테이프 스톱처럼 0.42배속으로 끌어내린다 — 방금까지 듣던
    /// 바로 그 곡이 뭉개지며 늘어지는 게, 새 곡을 트는 것보다 훨씬 확실하게
    /// "시간이 늘어졌다"로 읽힌다. 그 위에 PredictionAudio의 늘어진 루프가 겹친다.
    ///
    /// A 단조 기반(A1 55Hz) — 예지 루프도 같은 조성이라 두 층이 한 곡으로 들린다.
    /// </summary>
    public class AmbientMusic : MonoBehaviour, IRunResettable
    {
        const int SR = 44100;
        const float LoopSeconds = 8f;      // 75BPM 기준 10박(0.8초 펄스 × 10)

        /// <summary>평상시 볼륨. 깔리는 층이라 존재를 의식하지 못할 정도가 맞다.</summary>
        public const float FullVolume = 0.16f;

        // 예지 중 이 곡의 상태 — 느려지고, 예지 루프에 자리를 내주느라 살짝 물러난다.
        // 0.42배속이면 0.8초 펄스가 1.9초로 늘어지고 패드는 한 옥타브 넘게 내려앉는다.
        const float SlowPitch = 0.42f;
        const float SlowVolumeMul = 0.55f;
        const float SlowSeconds = 0.55f;   // 테이프가 서는 데 걸리는 시간
        const float RestoreSeconds = 0.45f;

        static AmbientMusic inst;

        AudioSource src;
        float target = FullVolume;
        float rate = 1f;                   // 초당 볼륨 변화량
        float pitchTarget = 1f;
        float pitchRate = 1f;              // 초당 피치 변화량

        void Awake()
        {
            if (inst != null && inst != this) { Destroy(gameObject); return; }
            inst = this;
            DontDestroyOnLoad(gameObject);

            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.loop = true;
            src.clip = BuildLoop();
            src.volume = FullVolume;
            src.Play();
        }

        /// <summary>재시작 시 속도·볼륨을 평상시로 되돌린다(예지로 늘어진 채 죽었어도 원복) + 처음부터.</summary>
        public void ResetForRestart()
        {
            if (src == null) return;
            target = FullVolume;
            pitchTarget = 1f;
            src.pitch = 1f;
            src.volume = FullVolume;
            src.time = 0f;
            if (!src.isPlaying) src.Play();
        }

        void Update()
        {
            if (src == null) return;
            float dt = Time.unscaledDeltaTime;
            src.volume = Mathf.MoveTowards(src.volume, target, rate * dt);
            src.pitch = Mathf.MoveTowards(src.pitch, pitchTarget, pitchRate * dt);
        }

        /// <summary>예지 진입 — 테이프가 서듯 곡이 늘어지며 뒤로 물러난다.</summary>
        public static void Slow()
        {
            if (inst == null) return;
            inst.pitchTarget = SlowPitch;
            inst.pitchRate = (1f - SlowPitch) / SlowSeconds;
            inst.target = FullVolume * SlowVolumeMul;
            inst.rate = FullVolume / SlowSeconds;
        }

        /// <summary>예지 해제 — 곡이 제 속도를 되찾는다.</summary>
        public static void Normal()
        {
            if (inst == null) return;
            inst.pitchTarget = 1f;
            inst.pitchRate = (1f - SlowPitch) / RestoreSeconds;
            inst.target = FullVolume;
            inst.rate = FullVolume / RestoreSeconds;
        }

        /// <summary>
        /// 8초 루프. 사인 성분 주파수는 전부 0.125Hz(=1/8초)의 정수배라 그 자체로 이어지고,
        /// 노이즈만 꼬리를 앞머리에 접어 이음매를 지운다(PredictionAudio와 같은 방식).
        /// </summary>
        static AudioClip BuildLoop()
        {
            int n = (int)(LoopSeconds * SR);
            int fold = (int)(0.35f * SR);
            var buf = new float[n + fold];
            var rng = new System.Random(301);
            float lp = 0f;

            for (int i = 0; i < buf.Length; i++)
            {
                float ti = (float)i / SR;

                // 1) 저역 펄스 — 0.8초마다 한 번 치는 심장 같은 서브. 리듬의 뼈대.
                float beatT = ti % 0.8f;
                float pulse = Mathf.Sin(6.2832f * Mathf.Lerp(52f, 38f, beatT / 0.8f) * beatT)
                              * Mathf.Exp(-beatT * 9f) * 0.55f;

                // 2) 패드 — A 단조 화음(A2·E3·A3). 아주 느린 LFO로 서로 다르게 부풀었다 죽는다.
                float pad = Mathf.Sin(6.2832f * 110f * ti) * (0.55f + 0.45f * Mathf.Sin(6.2832f * 0.125f * ti))
                          + 0.6f * Mathf.Sin(6.2832f * 165f * ti) * (0.55f + 0.45f * Mathf.Sin(6.2832f * 0.25f * ti))
                          + 0.4f * Mathf.Sin(6.2832f * 220f * ti) * (0.5f + 0.5f * Mathf.Sin(6.2832f * 0.375f * ti));

                // 3) 고역 시머 — 존재감보다 공기감. 없으면 패드가 답답하게 들린다.
                float shimmer = Mathf.Sin(6.2832f * 880f * ti)
                                * (0.35f + 0.35f * Mathf.Sin(6.2832f * 0.5f * ti)) * 0.05f;

                // 4) 숨 — 로우패스로 뭉갠 노이즈 층
                float white = (float)(rng.NextDouble() * 2 - 1);
                lp += (white - lp) * 0.02f;

                buf[i] = pulse * 0.35f + pad * 0.085f + shimmer + lp * 1.6f;
            }

            var s = new float[n];
            System.Array.Copy(buf, s, n);
            for (int i = 0; i < fold; i++)
            {
                float w = (float)i / fold;
                s[i] = s[i] * w + buf[n + i] * (1f - w);
            }
            for (int i = 0; i < n; i++) s[i] = Mathf.Clamp(s[i], -1f, 1f);

            var c = AudioClip.Create("amb_loop", n, 1, SR, false);
            c.SetData(s, 0);
            return c;
        }
    }

    /// <summary>Play 시 BGM 자동 부착.</summary>
    public static class AmbientMusicBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<AmbientMusic>() == null)
                new GameObject("[AmbientMusic]").AddComponent<AmbientMusic>();
        }
    }
}
