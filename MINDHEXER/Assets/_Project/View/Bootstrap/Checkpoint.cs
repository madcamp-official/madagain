using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 시작·부활 지점. (메인씬_통합과_클리어_설계 §2.1)
    ///
    /// <para><b>이 트랜스폼의 위치가 곧 발 위치</b>이고, <b>forward가 바라볼 방향</b>이다.
    /// 눈높이는 부활시키는 쪽이 더한다 — <see cref="GameBoot"/>가 <c>startPosition + up * eyeHeight</c>로
    /// 몸을 놓는 것과 같은 규약이라, 씬에서 마커를 바닥에 놓으면 그게 곧 설 자리다.</para>
    ///
    /// <para>스테이지 안에 두면 그 스테이지 범위와 함께 리셋된다(<see cref="StageScope"/>).
    /// 게임 시작·보스 추격 직전처럼 스테이지에 속하지 않는 지점은 씬 루트에 둔다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class Checkpoint : MonoBehaviour
    {
        [Tooltip("순서. 작을수록 앞이다. 게임 시작 0, 1~3스테이지 1~3, 보스 추격 직전 4.")]
        public int order;

        [Tooltip("표시용 이름. 로그·기즈모에 쓴다.")]
        public string label = "";

        [Tooltip("플레이어가 이 안에 들어오면 자동으로 활성화된다. 비우면 수동으로만 활성화된다.")]
        public Collider activateTrigger;

        [Tooltip("한 번 지나간 체크포인트로는 되돌아가지 않는다. 끄면 지날 때마다 갱신된다.")]
        public bool forwardOnly = true;

        /// <summary>이 지점의 발 위치.</summary>
        public Vector3 Feet => transform.position;

        /// <summary>이 지점에서 바라볼 방향(도).</summary>
        public float Yaw => transform.eulerAngles.y;

        /// <summary>이 체크포인트가 속한 스테이지 범위. 없으면 null(= 범위 리셋 없음).</summary>
        public StageScope Scope => GetComponentInParent<StageScope>();

        void OnTriggerEnter(Collider other)
        {
            if (activateTrigger == null) return;
            if (other.GetComponentInParent<FirstPersonPlayer>() == null) return;
            RunCheckpoints.Activate(this);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 1f, 0.5f, 0.9f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.9f, 0.45f);
            Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 1.8f);

            // 바라볼 방향
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.9f);
            Vector3 f = transform.forward; f.y = 0f;
            if (f.sqrMagnitude > 1e-4f)
                Gizmos.DrawRay(transform.position + Vector3.up * 0.9f, f.normalized * 2.5f);
        }
    }
}
