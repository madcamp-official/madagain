using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 예지 중에 흐르는 절차적 브금. 오디오 에셋 0개 — AudioClip.Create로 합성.
    ///
    /// 파는 감각은 하나다: <b>시간이 늘어진다</b>. 그걸 두 층으로 만든다.
    ///   1. 평상시 BGM(AmbientMusic)을 0.42배속으로 끌어내린다 — 방금까지 듣던 그 곡이
    ///      테이프 서듯 뭉개지며 늘어진다. 새 곡을 트는 것보다 이쪽이 훨씬 직접적이다.
    ///   2. 그 위에 늘어진 루프를 겹친다. 같은 A 단조지만 박자는 절반(1.6초), 음은
    ///      역방향으로 서서히 차오르고(리버스 스웰), 테이프처럼 미세하게 흔들린다(워우).
    ///
    /// 루프가 16초인 이유: 경로 전개(PredictionConfig.PreviewRevealSeconds = 8초) 동안
    /// 한 바퀴도 못 돌아서 반복이 들키지 않는다. 예지는 짧게 여러 번 쓰는 기능이라
    /// "매번 같은 시작"이 오히려 신호로 읽힌다 — 진입할 때마다 처음부터 다시 재생한다.
    ///
    /// 배속 주의: AudioSource는 Time.timeScale의 영향을 받지 않는다. 예지가 timeScale을
    /// 매 프레임 덮어써도(=이 프로젝트 방식) 이 소리들은 실시간으로 흐른다 — 의도된 것.
    /// </summary>
    public class PredictionAudio : MonoBehaviour
    {
        const int SR = 44100;
        const float LoopSeconds = 16f;

        static PredictionAudio inst;

        AudioSource oneShot;   // 진입/해제 전환음
        AudioSource loopSrc;   // 늘어진 브금
        AudioClip stretchClip, releaseClip, loopClip;

        const float LoopVolume = 0.26f;
        const float FadeInSeconds = 0.7f;    // BGM이 늘어지는 동안 스며들어온다
        const float FadeOutSeconds = 0.6f;
        float target, rate;

        void Awake()
        {
            if (inst != null && inst != this) { Destroy(gameObject); return; }
            inst = this;

            oneShot = NewSource();
            loopSrc = NewSource();
            loopSrc.loop = true;
            loopSrc.volume = 0f;

            // 씬에 리스너가 없으면 카메라에 부착 (CombatAudio가 이미 붙였으면 통과)
            if (Object.FindFirstObjectByType<AudioListener>() == null)
            {
                var cam = Camera.main;
                if (cam != null && cam.GetComponent<AudioListener>() == null)
                    cam.gameObject.AddComponent<AudioListener>();
            }

            stretchClip = BuildStretch();
            releaseClip = BuildRelease();
            loopClip = BuildLoop();
            loopSrc.clip = loopClip;
        }

        AudioSource NewSource()
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;   // 2D (1인칭이라 위치 무관)
            return s;
        }

        void Update()
        {
            if (loopSrc == null) return;
            loopSrc.volume = Mathf.MoveTowards(loopSrc.volume, target, rate * Time.unscaledDeltaTime);
            if (loopSrc.volume <= 0.0001f && loopSrc.isPlaying) loopSrc.Stop();
        }

        // ── 정적 접근자 (null 안전) ──

        /// <summary>예지 진입 — 평상시 BGM이 늘어지고, 그 위로 예지 브금이 올라온다.</summary>
        public static void Enter()
        {
            AmbientMusic.Slow();
            if (inst == null) return;
            inst.oneShot.PlayOneShot(inst.stretchClip, 0.7f);
            inst.target = LoopVolume;
            inst.rate = LoopVolume / FadeInSeconds;
            inst.loopSrc.time = 0f;      // 진입할 때마다 곡의 같은 자리에서 시작
            if (!inst.loopSrc.isPlaying) inst.loopSrc.Play();
        }

        /// <summary>예지 해제 — 예지 브금이 빠지고 평상시 BGM이 제 속도를 되찾는다.</summary>
        public static void Exit()
        {
            AmbientMusic.Normal();
            if (inst == null) return;
            inst.oneShot.PlayOneShot(inst.releaseClip, 0.55f);
            inst.target = 0f;
            inst.rate = LoopVolume / FadeOutSeconds;
        }

        // ── 합성 ──

        /// <summary>
        /// 진입 전환음(0.9초). 세계가 늘어지기 시작하는 하강 도플러 — AmbientMusic의
        /// 테이프 스톱과 같은 방향(아래)으로 움직여서 둘이 한 동작으로 들린다.
        /// 임팩트가 아니라 <b>미끄러짐</b>이라 어택을 세우지 않는다.
        /// </summary>
        static AudioClip BuildStretch()
        {
            const float dur = 0.90f;
            int n = (int)(dur * SR);
            var s = new float[n];
            var rng = new System.Random(221);
            float p1 = 0f, p2 = 0f, lp = 0f;

            for (int i = 0; i < n; i++)
            {
                float ti = (float)i / SR;
                float k = Mathf.Clamp01(ti / 0.55f);

                // 하강 도플러 — 440 → 110Hz(정확히 두 옥타브). 늘어짐의 주선율.
                float f = Mathf.Lerp(440f, 110f, k * k);
                p1 += 6.2832f * f / SR;
                p2 += 6.2832f * f * 1.5f / SR;                 // 5도를 얹어 두께만 준다
                float tone = (Mathf.Sin(p1) + 0.45f * Mathf.Sin(p2)) * 0.3f;

                // 같이 끌려 내려가는 공기
                float white = (float)(rng.NextDouble() * 2 - 1);
                lp += (white - lp) * Mathf.Lerp(0.35f, 0.03f, k);
                float air = lp * 0.45f;

                // 어택을 세우지 않는 봉투 — 스르륵 들어와 길게 눕는다
                float env = Mathf.Clamp01(ti / 0.06f) * Mathf.Exp(-ti * 2.6f);
                s[i] = (tone + air) * env;
            }
            return Clip("pa_stretch", s);
        }

        /// <summary>해제 전환음(0.55초). 늘어졌던 게 도로 감기는 짧은 상승.</summary>
        static AudioClip BuildRelease()
        {
            const float dur = 0.55f;
            int n = (int)(dur * SR);
            var s = new float[n];
            var rng = new System.Random(222);
            float p = 0f, lp = 0f;

            for (int i = 0; i < n; i++)
            {
                float ti = (float)i / SR;
                float k = Mathf.Clamp01(ti / 0.30f);
                p += 6.2832f * Mathf.Lerp(110f, 440f, k * k) / SR;
                float tone = Mathf.Sin(p) * 0.3f;

                float white = (float)(rng.NextDouble() * 2 - 1);
                lp += (white - lp) * Mathf.Lerp(0.04f, 0.4f, k);
                float air = lp * 0.4f;

                float env = Mathf.Clamp01(ti / 0.03f) * Mathf.Exp(-ti * 5f);
                s[i] = (tone + air) * env;
            }
            return Clip("pa_release", s);
        }

        /// <summary>
        /// 예지 브금 16초 루프. 늘어짐을 만드는 장치 넷:
        ///   · 절반 박자(1.6초) — 게다가 치는 게 아니라 부풀어오른다(어택 0.5초).
        ///   · 리버스 스웰 패드 — A단조 화음이 서로 시차를 두고 <b>거꾸로</b> 차오른다.
        ///   · 워우(wow) — 0.1875Hz로 전체 피치가 ±0.6% 흔들린다. 테이프가 늘어난 소리.
        ///   · 길게 끌리는 벨 — 4초에 걸쳐 아래로 미끄러지며 사라진다.
        /// 사인 주파수는 전부 0.0625Hz(=1/16초) 정수배라 루프가 그 자체로 이어진다.
        /// </summary>
        static AudioClip BuildLoop()
        {
            int n = (int)(LoopSeconds * SR);
            int fold = (int)(0.5f * SR);
            var buf = new float[n + fold];
            var rng = new System.Random(223);
            float lp = 0f;

            // 리버스 스웰 패드 — A단조(A2·C3·E3·A3)가 4초 간격으로 하나씩 차오른다.
            float[] padFreq = { 110f, 130.8125f, 164.8125f, 220f };
            float[] padStart = { 0f, 4f, 8f, 12f };
            var padPhase = new float[padFreq.Length];

            float pSub = 0f, pSub2 = 0f, pBell = 0f;

            for (int i = 0; i < buf.Length; i++)
            {
                float ti = (float)i / SR;

                // 워우 — 이 배율이 모든 음정을 함께 흔든다(테이프가 늘어난 느낌의 핵심).
                float wow = 1f + 0.006f * Mathf.Sin(6.2832f * 0.1875f * ti);

                // 1) 절반 박자 펄스 — 1.6초마다, 치는 게 아니라 부풀었다 꺼진다.
                float bt = ti % 1.6f;
                float swell = Mathf.Clamp01(bt / 0.5f);                 // 느린 어택
                float pulse = Mathf.Sin(6.2832f * 41.25f * bt * wow)
                              * swell * Mathf.Exp(-Mathf.Max(0f, bt - 0.5f) * 2.6f) * 0.5f;

                // 2) 저역 드론 — 0.25Hz 맥놀이로 4초에 한 번 부풀었다 죽는다.
                pSub += 6.2832f * 55f * wow / SR;
                pSub2 += 6.2832f * 55.25f * wow / SR;
                float sub = (Mathf.Sin(pSub) + 0.8f * Mathf.Sin(pSub2)) * 0.10f;

                // 3) 리버스 스웰 패드
                float pad = 0f;
                for (int v = 0; v < padFreq.Length; v++)
                {
                    padPhase[v] += 6.2832f * padFreq[v] * wow / SR;
                    float x = Mathf.Repeat(ti - padStart[v], LoopSeconds);
                    if (x >= 6f) continue;                              // 이 목소리는 쉬는 중
                    // 거꾸로 감긴 봉투: 3.8초에 걸쳐 차올랐다가 2.2초에 걷힌다.
                    float env = x < 3.8f ? Mathf.Pow(x / 3.8f, 1.7f)
                                         : 1f - (x - 3.8f) / 2.2f;
                    pad += Mathf.Sin(padPhase[v]) * env * 0.085f;
                }

                // 4) 끌리는 벨 — 0초와 8초에 한 번씩, 4초에 걸쳐 사라진다.
                float bx = ti % 8f;
                pBell += 6.2832f * 440f * wow / SR;
                float bell = Mathf.Sin(pBell) * Mathf.Exp(-bx * 1.1f) * 0.09f;

                // 5) 숨 — 뭉갠 노이즈 층
                float white = (float)(rng.NextDouble() * 2 - 1);
                lp += (white - lp) * 0.015f;

                buf[i] = pulse * 0.5f + sub + pad + bell + lp * 1.4f;
            }

            var s = new float[n];
            System.Array.Copy(buf, s, n);
            for (int i = 0; i < fold; i++)
            {
                float w = (float)i / fold;
                s[i] = s[i] * w + buf[n + i] * (1f - w);
            }
            return Clip("pa_loop", s, fadeTail: false);
        }

        static AudioClip Clip(string name, float[] samples, bool fadeTail = true)
        {
            if (fadeTail)
            {
                // 클릭 방지: 끝 2ms 페이드아웃 (루프 클립엔 쓰면 안 된다 — 이음매가 생긴다)
                int fade = Mathf.Min(samples.Length, (int)(0.002f * SR));
                for (int i = 0; i < fade; i++)
                    samples[samples.Length - 1 - i] *= (float)i / fade;
            }
            for (int i = 0; i < samples.Length; i++)
                samples[i] = Mathf.Clamp(samples[i], -1f, 1f);

            var c = AudioClip.Create(name, samples.Length, 1, SR, false);
            c.SetData(samples, 0);
            return c;
        }
    }

    /// <summary>Play 시 예지 오디오 자동 부착.</summary>
    public static class PredictionAudioBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<PredictionAudio>() == null)
                new GameObject("[PredictionAudio]").AddComponent<PredictionAudio>();
        }
    }
}
