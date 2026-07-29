using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// BossLab 전용 더미 플레이어 — 복도를 자동 전진하며 고무줄 추격을 검증한다. 게임 코드 아님.
    ///
    /// <para>P 키로 멈춤 토글: 멈추면 보스가 따라붙어 잡는 것(catch)까지 확인할 수 있다.
    /// 복도 끝에 도달하면 보스와 함께 시작 지점으로 평행이동해 무한 반복한다(상대 거리 유지).</para>
    /// </summary>
    public class BossLabDummy : MonoBehaviour
    {
        [Tooltip("달리기 속도(m/s). 플레이어 실제 이동 속도에 맞출 것.")]
        public float runSpeed = 8f;

        [Tooltip("함께 되감을 보스(상대 거리 유지용).")]
        public Transform boss;

        [Tooltip("이 z를 넘으면 시작 지점으로 되감는다.")]
        public float loopEndZ = 500f;

        [Tooltip("되감을 때 빼는 거리(m).")]
        public float loopBackDistance = 400f;

        public bool running = true;

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.pKey.wasPressedThisFrame) running = !running;

            if (running)
                transform.position += Vector3.forward * (runSpeed * Time.deltaTime);

            if (transform.position.z > loopEndZ)
            {
                Vector3 back = Vector3.forward * loopBackDistance;
                transform.position -= back;
                if (boss != null) boss.position -= back;
            }
        }
    }
}
