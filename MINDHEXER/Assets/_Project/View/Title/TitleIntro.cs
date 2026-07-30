using System;
using System.Collections;
using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 타이틀 연출. (spec 3 재해석)
    /// <para><b>시작(씬 로드)</b>: 검정에서 부드럽게 페이드 인만 한다. 얼굴은 어둠에 잠긴 채 후광(rim)만
    /// 윤곽을 드러낸다. — 얼굴 섬광은 여기서 일어나지 않는다.</para>
    /// <para><b>PLAY(게임 시작)</b>: 얼굴 필 라이트가 <b>번쩍</b>이며 로봇의 얼굴을 아주 잠깐 드러낸 뒤,
    /// 화면이 완전한 암흑으로 페이드아웃되고 인트로 씬을 로드한다.</para>
    /// </summary>
    public sealed class TitleIntro : MonoBehaviour
    {
        [Header("연결")]
        [Tooltip("전체화면 검은 오버레이의 CanvasGroup.")]
        public CanvasGroup fadeOverlay;
        [Tooltip("로봇 얼굴 정면 필 라이트. 평소 0, PLAY 섬광 때만 켜진다.")]
        public Light faceFill;

        [Header("시작 페이드 인(초)")]
        public float startFadeIn = 0.8f;

        [Header("PLAY 전환(섬광 → 암전, 초)")]
        public float flashUp = 0.07f;    // 얼굴 드러나는 섬광 상승
        public float flashHold = 0.06f;  // 섬광 유지
        public float fadeOut = 0.5f;     // 완전한 암흑으로

        [Header("세기(라이트 유닛에 맞춰 튜닝)")]
        public float flashFillIntensity = 12f;
        public float restFillIntensity = 0f;

        bool _transitioning;

        void Start()
        {
            if (faceFill != null) faceFill.intensity = restFillIntensity;   // 얼굴은 어둠
            if (fadeOverlay != null) { fadeOverlay.alpha = 1f; fadeOverlay.blocksRaycasts = false; }
            StartCoroutine(FadeIn());
        }

        IEnumerator FadeIn()
        {
            yield return Ramp(startFadeIn, a =>
            {
                if (fadeOverlay != null) fadeOverlay.alpha = Mathf.Lerp(1f, 0f, a);
            });
            if (fadeOverlay != null) fadeOverlay.alpha = 0f;
        }

        /// <summary>PLAY: 얼굴 섬광 → 암전 → onDone(씬 로드). (spec 3의 '게임이 시작되는 순간')</summary>
        public void PlayTransition(Action onDone)
        {
            if (_transitioning) { return; }
            _transitioning = true;
            StartCoroutine(Transition(onDone));
        }

        IEnumerator Transition(Action onDone)
        {
            if (fadeOverlay != null) fadeOverlay.blocksRaycasts = true;

            // 섬광: 얼굴 필 라이트가 확 켜져 얼굴을 잠깐 드러낸다.
            yield return Ramp(flashUp, a =>
            {
                if (faceFill != null) faceFill.intensity = Mathf.Lerp(restFillIntensity, flashFillIntensity, a);
            });
            yield return new WaitForSeconds(flashHold);

            // 완전한 암흑으로 페이드아웃(동시에 필 라이트도 서서히 소멸).
            yield return Ramp(fadeOut, a =>
            {
                if (fadeOverlay != null) fadeOverlay.alpha = Mathf.Lerp(0f, 1f, a);
                if (faceFill != null) faceFill.intensity = Mathf.Lerp(flashFillIntensity, 0f, a);
            });

            onDone?.Invoke();
        }

        static IEnumerator Ramp(float dur, Action<float> step)
        {
            if (dur <= 0f) { step(1f); yield break; }
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                step(Mathf.Clamp01(t / dur));
                yield return null;
            }
            step(1f);
        }
    }
}
