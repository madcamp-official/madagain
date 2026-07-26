using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 비주얼(환경 하이라이트) 개발용 디버그 목(mock). 해킹 로직 없이 `Hackable`의 런타임 상태를
    /// 인스펙터/토글로 수동 세팅한다 → 테두리·색·글리치·격화를 로직 없이 확인·튜닝. (계획 v4 §2)
    ///
    /// 실제 게임플레이(F1~)가 붙으면 이 목은 씬에서 빼거나 비활성화한다.
    /// </summary>
    [RequireComponent(typeof(Hackable))]
    public class HackableStateMock : MonoBehaviour
    {
        [Tooltip("사거리 안 여부(=글리치 on).")]
        public bool inRange = true;
        [Tooltip("시선 조준 여부(=격화 치직).")]
        public bool isGazed;
        [Tooltip("장악 상태(=색: None/Hacking 초록, Captured 파랑).")]
        public CaptureState captureState = CaptureState.None;
        [Tooltip("플레이어와의 거리(=테두리 두께). 미사용 시 실제 거리로 대체 가능.")]
        public float distanceToPlayer = 5f;

        [Tooltip("체크 시 카메라와의 실제 거리를 매 프레임 계산해 distanceToPlayer 대신 사용.")]
        public bool useRealDistance = true;

        Hackable _h;

        void Awake() { _h = GetComponent<Hackable>(); }

        void Update()
        {
            if (_h == null) return;
            _h.InRange = inRange;
            _h.IsGazed = isGazed;
            _h.captureState = captureState;

            if (useRealDistance && Camera.main != null)
                _h.DistanceToPlayer = Vector3.Distance(Camera.main.transform.position, transform.position);
            else
                _h.DistanceToPlayer = distanceToPlayer;
        }
    }
}
