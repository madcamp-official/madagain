using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.View
{
    /// <summary>
    /// 타이틀 화면의 PLAY / QUIT 동작. (Title 씬 전용)
    ///
    /// PLAY → 인트로 씬으로 이동한다. 인트로 씬은 아직 이 브랜치에 없으므로,
    /// Build Settings에 없으면 게임을 깨지 않고 경고 로그만 남긴다(존재하면 자동 이동).
    /// QUIT → 게임 종료(에디터에서는 플레이 정지).
    /// </summary>
    public class TitleMenu : MonoBehaviour
    {
        [Tooltip("PLAY 시 로드할 인트로 씬 이름. 아직 없으면 안전하게 로그만 남긴다.")]
        public string introSceneName = "Intro";

        [Tooltip("PLAY 직전 암전(페이드아웃) 연출용 TitleIntro (선택). 없으면 즉시 로드.")]
        public TitleIntro intro;

        bool _loading;

        /// <summary>PLAY 버튼.</summary>
        public void Play()
        {
            if (_loading) return;

            if (!Application.CanStreamedLevelBeLoaded(introSceneName))
            {
                Debug.LogWarning($"[TitleMenu] 인트로 씬 '{introSceneName}'이(가) Build Settings에 없습니다. " +
                                 "인트로 씬이 추가되면 PLAY가 자동으로 이동합니다.");
                return;
            }

            _loading = true;
            // PLAY 순간: 얼굴 섬광 → 암전 → 인트로 씬 로드.
            if (intro != null) intro.PlayTransition(() => SceneManager.LoadScene(introSceneName));
            else SceneManager.LoadScene(introSceneName);
        }

        /// <summary>QUIT 버튼.</summary>
        public void Quit()
        {
            Debug.Log("[TitleMenu] Quit");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
