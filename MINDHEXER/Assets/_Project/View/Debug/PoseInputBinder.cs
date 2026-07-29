using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.View
{
    /// <summary>
    /// 키 → 포즈 시퀀스 재생 (작업용 미리보기).
    ///
    /// <para>Precog에는 "좌클릭=베기1/베기2 번갈아, 우클릭=찌르기"로 <b>칼 콤보 전용</b> 하드코딩이었다.
    /// 우리 게임엔 대응하는 공격이 없고, 게다가 <b>마우스 좌·우 드래그가 해킹 입력</b>(§2.5)이라
    /// 마우스에 미리보기를 걸면 실제 조작과 충돌한다. 그래서 <b>키보드 바인딩</b>으로 다시 짰다.</para>
    ///
    /// <para>바인딩은 인스펙터에서 지정한다. 한 바인딩에 접두어를 여러 개 넣으면 누를 때마다
    /// 번갈아 재생된다(콤보 확인용). 각 입력은 독립 시퀀스라 이전 것과 블렌드하지 않고 첫 포즈로 스냅한다.</para>
    ///
    /// 이 컴포넌트는 <b>작업 도구</b>다. 빌드에 남아도 <see cref="active"/>가 꺼져 있으면 아무것도 하지 않는다.
    /// </summary>
    public class PoseInputBinder : MonoBehaviour
    {
        /// <summary>키 하나에 묶인 포즈 시퀀스.</summary>
        [Serializable]
        public class Binding
        {
            [Tooltip("표시용 이름(동작 없음)")]
            public string label = "";

            [Tooltip("누를 키")]
            public Key key = Key.Digit1;

            [Tooltip("재생할 포즈 접두어. 둘 이상이면 누를 때마다 번갈아 재생한다(콤보 확인용).")]
            public string[] prefixes = new string[0];

            [Tooltip("포즈 하나 넘어가는 시간(초)")]
            public float segTime = 0.15f;

            [Tooltip("포즈 사이를 스프링(탄성)으로 이을지. 끄면 선형.")]
            public bool spring;

            [NonSerialized] public int next;   // 번갈아 인덱스
        }

        public static PoseInputBinder Instance { get; private set; }

        [Tooltip("켜야 키 입력을 읽는다. 실제 플레이 중엔 꺼두십시오.")]
        public bool active;

        [Tooltip("키 → 포즈 시퀀스. 비어 있으면 아무것도 하지 않는다.")]
        public List<Binding> bindings = new List<Binding>();

        bool _warned;

        void Awake() { Instance = this; }

        void Update()
        {
            if (!active) return;

            var kb = Keyboard.current;
            if (kb == null) return;
            if (UiBlocking()) return;   // 콘솔·튜닝 패널 조작 중이면 키를 먹지 않는다

            if (bindings.Count == 0)
            {
                if (!_warned)
                {
                    _warned = true;
                    Debug.Log("[PoseInputBinder] 바인딩이 비어 있습니다. 인스펙터에서 키와 포즈 접두어를 지정하십시오.");
                }
                return;
            }

            foreach (var b in bindings)
            {
                if (b == null || b.prefixes == null || b.prefixes.Length == 0) continue;
                var ctrl = kb[b.key];
                if (ctrl == null || !ctrl.wasPressedThisFrame) continue;
                PlayBinding(b);
            }
        }

        /// <summary>콘솔(`)이나 F3/F4 패널이 열려 있는가.</summary>
        static bool UiBlocking() =>
            DevConsole.Open || PoseTunePanel.AnyOpen || PoseSeqPanel.AnyOpen;

        /// <summary>바인딩 하나를 재생. 접두어가 여럿이면 번갈아 간다.</summary>
        public void PlayBinding(Binding b)
        {
            var pp = PosePlayer.Instance;
            if (pp == null || b == null || b.prefixes == null || b.prefixes.Length == 0) return;

            string prefix = b.prefixes[b.next % b.prefixes.Length];
            b.next = (b.next + 1) % b.prefixes.Length;
            if (string.IsNullOrEmpty(prefix)) return;

            // 각 입력은 독립 시퀀스 — 이전 것과 블렌드하지 않고 첫 포즈로 즉시 스냅된다.
            pp.springBetweenPoses = b.spring;
            pp.Play(prefix, b.segTime, false);
        }

        /// <summary>이름으로 바인딩을 찾아 재생(다른 시스템에서 호출용).</summary>
        public bool PlayByLabel(string label)
        {
            foreach (var b in bindings)
                if (b != null && b.label == label) { PlayBinding(b); return true; }
            return false;
        }

        /// <summary>모든 바인딩의 번갈아 인덱스를 처음으로.</summary>
        public void ResetAll()
        {
            foreach (var b in bindings) if (b != null) b.next = 0;
        }
    }

    /// <summary>Play 시 자동 부착.</summary>
    public static class PoseInputBinderBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            // using System 때문에 Object가 모호해진다 — 명시적으로 UnityEngine 쪽을 쓴다.
            if (UnityEngine.Object.FindFirstObjectByType<PoseInputBinder>() == null)
                new GameObject("[PoseInputBinder]").AddComponent<PoseInputBinder>();
        }
    }
}
