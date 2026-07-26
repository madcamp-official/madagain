using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// MINDHEXER 게임 씬 부트스트랩. 이식된 Precog <c>Main.cs</c>(결정론 예측 시뮬레이션 기반)를
    /// 대체할 **우리 진입점**이다. 씬에 [GameBoot] 하나만 두면 1인칭 플레이어 카메라 + 해킹 시스템이
    /// 구성된다. 예측·결정론 World/Snapshot에 의존하지 않는 **실시간 게임 부트**.
    ///
    /// Precog Main과의 관계: 이 컴포넌트가 씬에 있으면 Precog <c>AutoBoot</c>가 [Main] 자동 생성을
    /// 건너뛴다(Main.cs Boot 가드) → 두 부트가 충돌하지 않는다. Precog 코어(예측/전투)는 MINDHEXER
    /// 게임 루프가 이걸로 충분히 대체된 뒤 삭제 예정. (docs/KJH/decisions/0002-precog-purge.md)
    /// </summary>
    [DisallowMultipleComponent]
    public class GameBoot : MonoBehaviour
    {
        [Header("플레이어 시작")]
        [Tooltip("플레이어(카메라) 시작 위치.")]
        public Vector3 startPosition = new Vector3(0f, 0f, -6f);
        [Tooltip("눈높이(카메라 Y 오프셋).")]
        public float eyeHeight = 1.6f;
        [Tooltip("시작 시 마우스 커서를 잠글지.")]
        public bool lockCursor = true;

        Camera _cam;

        void Awake()
        {
            EnsurePlayerRig();
            if (lockCursor) Cursor.lockState = CursorLockMode.Locked;
        }

        /// <summary>1인칭 카메라 + 이동 + 해킹 시스템을 보장한다(없으면 만든다).</summary>
        void EnsurePlayerRig()
        {
            _cam = Camera.main;
            if (_cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                _cam = go.AddComponent<Camera>();
                go.AddComponent<AudioListener>();
            }

            _cam.transform.position = startPosition + Vector3.up * eyeHeight;

            var camGo = _cam.gameObject;
            if (camGo.GetComponent<FreeLookController>() == null) camGo.AddComponent<FreeLookController>();
            // HackDriver는 [RequireComponent(HackContext)]라 HackContext도 함께 붙는다.
            if (camGo.GetComponent<HackDriver>() == null) camGo.AddComponent<HackDriver>();
        }
    }
}
