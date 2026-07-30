using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 게임오버 — 최소 버전. <b>플레이어 본체</b>가 죽으면(예: 압사) 여기로 온다.
    /// 빙의 중인 경비병이 죽는 것과는 다르다 — 그건 본체로 돌려보낼 뿐 게임오버가 아니다
    /// (<see cref="ViewEntryController.KillPossessedTarget"/> 참조).
    ///
    /// <para>재시작 화면·연출 등은 없다(일단 트리거와 최소 표시만) — 플레이어 입력을 얼리고
    /// 화면에 GAME OVER를 띄운다. Canvas·프리팹 없이 <c>OnGUI</c>로 즉시 동작하게 했다.</para>
    /// </summary>
    public class GameOverManager : MonoBehaviour
    {
        static GameOverManager _instance;

        public bool IsOver { get; private set; }
        string _reason = "";

        /// <summary>어디서든 이거 하나만 부르면 된다. 없으면 스스로 만든다.</summary>
        public static void Trigger(string reason)
        {
            if (_instance == null)
            {
                _instance = Object.FindFirstObjectByType<GameOverManager>();
                if (_instance == null) _instance = new GameObject("[GameOverManager]").AddComponent<GameOverManager>();
            }
            _instance.DoTrigger(reason);
        }

        void DoTrigger(string reason)
        {
            if (IsOver) return;   // 중복 트리거(같은 프레임에 여러 번 겹침 등) 무시
            IsOver = true;
            _reason = reason;

            var fpp = FirstPersonPlayer.Instance;
            if (fpp != null)
            {
                fpp.LookFrozen = true;
                fpp.ExternalMotion = true;   // 이동·중력도 넘겨받아 그 자리에 세운다
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            Debug.Log($"[게임오버] {reason}");
        }

        void OnGUI()
        {
            if (!IsOver) return;

            GUI.depth = -1000;
            var prevColor = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 48,
                alignment = TextAnchor.MiddleCenter,
            };
            titleStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(0, Screen.height * 0.38f, Screen.width, 80), "GAME OVER", titleStyle);

            var subStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleCenter,
            };
            subStyle.normal.textColor = Color.gray;
            GUI.Label(new Rect(0, Screen.height * 0.38f + 90, Screen.width, 40), _reason, subStyle);

            var btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 22 };
            if (GUI.Button(new Rect(Screen.width * 0.5f - 100, Screen.height * 0.38f + 150, 200, 50), "다시 시작", btnStyle))
                UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);

            GUI.color = prevColor;
        }
    }
}
