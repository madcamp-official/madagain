using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.View
{
    /// <summary>
    /// 마우스 입력 → 포즈 시퀀스 재생(작업용 미리보기).
    ///   좌클릭 = slash1 / slash2 번갈아
    ///   우클릭 = thrust1
    /// 서로 다른 애니메이션 사이는 항상 순간이동(각 클릭이 새 시퀀스를 처음부터 재생).
    /// 콘솔 `bind` 로 끄고 켤 수 있다.
    /// </summary>
    public class PoseInputBinder : MonoBehaviour
    {
        public static PoseInputBinder Instance { get; private set; }

        // 실제 전투 연동(PoseCombatDriver)이 켜져 있으면 마우스를 직접 읽지 않는다.
        // 그쪽은 sim의 공격 상태를 보므로 쿨다운·선입력 같은 게임 규칙을 그대로 따른다.
        [Tooltip("마우스를 직접 읽어 재생(전투 연동이 꺼져 있을 때만 쓰는 예비 경로)")]
        public bool active;
        [Tooltip("포즈 하나 넘어가는 시간(초)")]
        public float clickSegTime = 0.15f;

        [Tooltip("좌클릭에서 번갈아 쓸 접두어")]
        public string[] leftPrefixes  = { "slash1_", "slash2_" };
        [Tooltip("우클릭 접두어")]
        public string   rightPrefix   = "thrust1_";

        int nextLeft;   // 0 → slash1, 1 → slash2 …

        void Awake() { Instance = this; }

        void Update()
        {
            if (!active) return;
            var drv = PoseCombatDriver.Instance;
            if (drv != null && drv.active) return;   // 전투 연동이 담당 — 중복 재생 방지
            var m = Mouse.current;
            if (m == null) return;
            if (UiBlocking()) return;      // 콘솔·튜닝 패널 조작 중이면 클릭을 먹지 않는다

            if (m.leftButton.wasPressedThisFrame)  PlayLeft();
            if (m.rightButton.wasPressedThisFrame) PlayRight();
        }

        /// <summary>콘솔(`)이나 F2/F3 패널이 열려 있는가.</summary>
        static bool UiBlocking()
        {
            var dc = FindFirstObjectByType<DevConsole>();
            if (dc != null && dc.IsOpen) return true;
            return DevPanels.BlocksPoseDriver;   // 판단 기준을 한곳(DevPanels)으로 모은다
        }

        /// <summary>좌클릭 — slash1 / slash2 번갈아.</summary>
        public void PlayLeft()
        {
            if (leftPrefixes == null || leftPrefixes.Length == 0) return;
            string prefix = leftPrefixes[nextLeft % leftPrefixes.Length];
            nextLeft = (nextLeft + 1) % leftPrefixes.Length;
            PlayPrefix(prefix);
        }

        /// <summary>우클릭 — 찌르기.</summary>
        public void PlayRight() => PlayPrefix(rightPrefix);

        /// <summary>콤보 카운터를 처음(slash1)으로.</summary>
        public void ResetCombo() => nextLeft = 0;

        void PlayPrefix(string prefix)
        {
            var pp = PosePlayer.Instance;
            if (pp == null || string.IsNullOrEmpty(prefix)) return;
            // 각 클릭은 독립 시퀀스 — 이전 것과 블렌드하지 않고 첫 포즈로 즉시 스냅된다
            pp.springBetweenPoses = false;          // 선형
            pp.Play(prefix, clickSegTime, false);
        }
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class PoseInputBinderBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindFirstObjectByType<PoseInputBinder>() == null)
                new GameObject("[PoseInputBinder]").AddComponent<PoseInputBinder>();
        }
    }
}
