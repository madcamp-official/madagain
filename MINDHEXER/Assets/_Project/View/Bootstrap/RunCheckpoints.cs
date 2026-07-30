using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 체크포인트 진행과 부활을 관리한다. (메인씬_통합과_클리어_설계 §2.1)
    ///
    /// <para><see cref="DeathSequence.OnResetPoint"/>(암전이 완전히 덮인 순간)를 구독해
    /// <b>죽은 스테이지만</b> 리셋하고 플레이어를 마지막 체크포인트로 옮긴다. 화면이 검을 때
    /// 하므로 순간이동도 리셋도 보이지 않는다.</para>
    ///
    /// <para>지금까지 <see cref="IRunResettable"/>은 <b>구현만 되고 아무도 호출하지 않았다</b> —
    /// 인터페이스 주석이 참조하는 <c>Main.RestartRun</c>은 존재하지 않는다. 이 컴포넌트가 그 자리를 채운다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public class RunCheckpoints : MonoBehaviour
    {
        [Tooltip("게임 시작 시 여기서 시작한다. 비우면 order가 가장 작은 체크포인트를 쓴다.")]
        public Checkpoint startAt;

        [Tooltip("시작할 때 기상 연출(인트로)을 재생할지.")]
        public bool playIntroOnStart = true;

        /// <summary>지금 활성화된 체크포인트.</summary>
        public static Checkpoint Current { get; private set; }

        static RunCheckpoints _instance;

        void Awake()
        {
            if (_instance == null) _instance = this;
        }

        void OnEnable()
        {
            var d = DeathSequence.Instance;
            if (d != null) d.OnResetPoint += HandleReset;
        }

        void OnDisable()
        {
            // Instance 프로퍼티는 없으면 새로 만든다 — 종료 중에 만들지 않도록 필드로 확인만 한다.
            var d = FindAnyObjectByType<DeathSequence>();
            if (d != null) d.OnResetPoint -= HandleReset;
        }

        void Start()
        {
            if (Current == null)
                Current = startAt != null ? startAt : Earliest();

            if (Current != null) MoveTo(Current);
            if (playIntroOnStart) DeathSequence.Instance.PlayIntro();
        }

        /// <summary>order가 가장 작은 체크포인트.</summary>
        static Checkpoint Earliest()
        {
            var all = FindObjectsByType<Checkpoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Checkpoint best = null;
            for (int i = 0; i < all.Length; i++)
                if (best == null || all[i].order < best.order) best = all[i];
            return best;
        }

        /// <summary>체크포인트를 활성화한다. 트리거가 부르거나 직접 불러도 된다.</summary>
        public static void Activate(Checkpoint cp)
        {
            if (cp == null) return;
            if (Current == cp) return;

            // 되돌아가며 지날 때 체크포인트가 뒤로 밀리면, 죽었을 때 앞 스테이지로 튕긴다.
            if (cp.forwardOnly && Current != null && cp.order < Current.order) return;

            Current = cp;
            Debug.Log($"[체크포인트] {(string.IsNullOrEmpty(cp.label) ? cp.name : cp.label)} (#{cp.order})");
        }

        /// <summary>사망 연출이 화면을 완전히 덮은 순간 — 판을 되돌리고 옮긴다.</summary>
        void HandleReset()
        {
            if (Current == null) { Debug.LogWarning("[체크포인트] 활성 체크포인트가 없어 부활 위치를 모릅니다."); return; }

            var scope = Current.Scope;
            if (scope != null) scope.ResetScope();   // 죽은 스테이지만. 앞 스테이지는 보존된다

            MoveTo(Current);
        }

        /// <summary>
        /// 플레이어를 그 지점으로. <b>발 위치 + 눈높이</b>로 몸을 놓는다 —
        /// <see cref="GameBoot"/>가 <c>startPosition + up * eyeHeight</c>로 놓는 것과 같은 규약이라
        /// 씬에서 마커를 바닥에 두면 그게 곧 설 자리가 된다.
        /// </summary>
        public static void MoveTo(Checkpoint cp)
        {
            var body = FirstPersonPlayer.Instance;
            if (body == null || cp == null) return;

            float eye = 1.6f;
            var boot = FindAnyObjectByType<GameBoot>();
            if (boot != null) eye = boot.eyeHeight;

            var t = body.transform;

            // CharacterController가 켜진 채 위치를 바꾸면 다음 프레임에 밀려난다.
            var cc = t.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            t.position = cp.Feet + Vector3.up * eye;
            if (cc != null) cc.enabled = true;

            body.VerticalVelocity = 0f;   // 죽기 직전의 낙하 속도가 남아 있으면 부활하자마자 처박힌다

            // 시점은 MouseLook이 유일 소유자다. 직접 회전을 쓰면 다음 프레임에 덮인다.
            var look = FindAnyObjectByType<MouseLook>();
            if (look != null) look.SetLook(cp.Yaw, 0f);
        }
    }
}
