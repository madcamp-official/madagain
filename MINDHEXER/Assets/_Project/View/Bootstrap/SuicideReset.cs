using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.View
{
    /// <summary>
    /// 좌클릭+우클릭(PC) 또는 화면 좌/우 절반 동시 터치(VR 컨트롤러)를 <see cref="holdTime"/>초
    /// 누르고 있으면 자동사망 → 현재 체크포인트로 부활.
    ///
    /// <para>막혔을 때 쓰는 탈출 단축키다 — 정식 게임플레이 메커니즘이 아니다. 실제 사망·부활은
    /// <see cref="DeathSequence.Play"/> 하나로 통일된 연출을 그대로 타므로, 보스 추격 중이면
    /// 체크포인트가 이미 "보스 조우 직전"(§체크포인트 순서 4)을 가리키고 있어 자동으로 거기로
    /// 돌아간다 — 여기서 따로 분기할 필요가 없다.</para>
    ///
    /// <para><b>두 입력 소스를 OR로 본다</b>(PC 겸용 배선과 같은 방식, <see cref="ControllerDriver"/>
    /// 참조) — 어느 쪽으로 테스트하든 같은 코드 경로를 탄다.</para>
    /// </summary>
    public class SuicideReset : MonoBehaviour
    {
        [Tooltip("두 입력을 동시에 누르고 있어야 하는 시간(초).")]
        public float holdTime = 2f;

        [Tooltip("발동 후 다시 발동 가능해지기까지 최소 간격(초). 사망 연출 자체가 한동안 입력을 " +
                 "얼리지만, 그 사이 남은 홀드가 이어져 재발동하지 않게 별도로 막는다.")]
        public float cooldown = 1f;

        float _holdT;
        bool _consumed;
        float _lastTrigger = -999f;

        void Update()
        {
            if (DevConsole.Open) { _holdT = 0f; _consumed = false; return; }

            bool bothHeld = MouseBothHeld() || ControllerBothHeld();
            if (!bothHeld) { _holdT = 0f; _consumed = false; return; }

            _holdT += Time.unscaledDeltaTime;
            if (_consumed) return;
            if (_holdT < holdTime) return;
            if (Time.unscaledTime - _lastTrigger < cooldown) return;

            _consumed = true;
            _lastTrigger = Time.unscaledTime;

            var over = FindAnyObjectByType<GameOverManager>();
            if (over != null && over.IsOver) return;                 // 진짜 게임오버 화면에선 무시
            if (DeathSequence.Instance.Playing) return;               // 이미 사망 연출 중이면 무시

            Debug.Log("[SuicideReset] 좌우 동시 홀드 — 자동사망 → 체크포인트 부활");
            DeathSequence.Instance.Play();
        }

        static bool MouseBothHeld()
        {
            var mouse = Mouse.current;
            return mouse != null && mouse.leftButton.isPressed && mouse.rightButton.isPressed;
        }

        /// <summary>화면 좌/우 절반에 각각 활성 터치가 있는가. 컨트롤러가 끊기면 터치가 자동으로
        /// 풀리므로(<see cref="ControllerLink.Tick"/>) 여기서 따로 Connected를 볼 필요가 없다.</summary>
        static bool ControllerBothHeld()
        {
            var link = ControllerLink.Active;
            if (link == null) return false;
            return link.SlotOnHalf(left: true) >= 0 && link.SlotOnHalf(left: false) >= 0;
        }
    }

    /// <summary>씬에 안 심어도 되게 자동 부착 — ViewmodelCameraBoot·UiRigAutoBoot와 같은 패턴.</summary>
    public static class SuicideResetBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<SuicideReset>() == null)
                new GameObject("[SuicideReset]").AddComponent<SuicideReset>();
        }
    }
}
