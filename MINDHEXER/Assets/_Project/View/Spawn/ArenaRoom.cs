using System;
using UnityEngine;
using UnityEngine.Events;

namespace Game.View
{
    /// <summary>
    /// 아레나 방 진행 관리 — 플레이어가 방 존에 들어오면 그 방 <see cref="WaveRunner"/>를 시작하고,
    /// 모든 웨이브를 클리어하면 신호를 던진다.
    ///
    /// <b>게이트 실물·여닫는 연출은 만들지 않는다.</b> onLock/onUnlock(및 IsLocked/IsCleared/IsActive)만
    /// 노출하니, 게이트 스크립트가 이걸 구독해 직접 여닫는다. 관리 훅만 제공하는 컴포넌트다.
    ///
    /// 레벨 흐름(복도-아레나-복도-…):
    ///   복도에서 스폰 → 아레나 진입 시 <b>onLock</b>(뒤쪽 게이트 닫기 = 가둠)
    ///   → 웨이브 전부 클리어 시 <b>onUnlock</b>(앞쪽 게이트 열기 = 다음 복도로).
    /// 아레나 루트에 하나씩 붙인다. 복도엔 안 붙인다(통과만 하므로).
    ///
    /// ★ 플레이어는 sim이 움직여 물리 콜라이더가 없을 수 있으므로, OnTriggerEnter 대신
    ///   PlayerAnchor 위치가 존(zone) 안에 들어왔는지 매 프레임 검사한다.
    /// ★ 프리팹화해서 마스터 씬에 여러 개 이어붙여도, 각 인스턴스가 자기 방만 독립 관리한다
    ///   (runner·zone·player를 자기 기준으로 찾으므로).
    /// </summary>
    [DisallowMultipleComponent]
    public class ArenaRoom : MonoBehaviour, IRunResettable
    {
        /// <summary>재시작 시 방을 미진입·미클리어 상태로 되돌린다(재진입하면 다시 잠기고 시작).</summary>
        public void ResetForRestart() => ReArm();

        [Header("참조 (비우면 자동 탐색)")]
        [Tooltip("이 방의 웨이브. 비우면 자기/자식에서 찾는다.")]
        public WaveRunner runner;
        [Tooltip("방 진입 판정 존(볼록 콜라이더, Box 권장 · isTrigger 권장). 비우면 자기 Collider를 쓴다.")]
        public Collider zone;
        [Tooltip("플레이어 트랜스폼. 비우면 런타임의 'PlayerAnchor'를 자동으로 찾는다.")]
        public Transform player;

        [Header("설정")]
        [Tooltip("한 번 클리어하면 다시 시작하지 않는다. 끄면 재진입 시 다시 잠그고 시작(반복 방).")]
        public bool startOnce = true;
        [Tooltip("시작할 웨이브 번호(0부터). 보통 0.")]
        public int startWaveIndex = 0;
        [Tooltip("startWaveIndex 이후 웨이브까지 순차 진행.")]
        public bool runAll = true;

        [Header("이벤트 — 여기에 게이트를 연결한다")]
        [Tooltip("플레이어 진입·웨이브 시작 시. → 뒤쪽 게이트 '닫기'를 연결.")]
        public UnityEvent onLock;
        [Tooltip("모든 웨이브 클리어 시. → 앞쪽 게이트 '열기'를 연결.")]
        public UnityEvent onUnlock;

        /// <summary>코드에서 구독하고 싶을 때(인스펙터 이벤트 대신/함께).</summary>
        public event Action OnLock;
        public event Action OnUnlock;

        // ── 상태 (게이트가 읽어도 된다) ──
        /// <summary>진입해 웨이브 진행 중(시작~클리어 전).</summary>
        public bool IsActive  { get; private set; }
        /// <summary>가둠 상태(시작~클리어 전). 뒤쪽 게이트가 닫혀 있어야 하는 구간.</summary>
        public bool IsLocked  { get; private set; }
        /// <summary>이 방을 클리어했다.</summary>
        public bool IsCleared { get; private set; }

        bool started;
        bool warnedNoRunner;

        void Reset()   // 에디터에서 컴포넌트 붙일 때 편의 자동 배선
        {
            runner = GetComponentInChildren<WaveRunner>(true);
            zone   = GetComponent<Collider>();
        }

        void Awake()
        {
            if (runner == null) runner = GetComponentInChildren<WaveRunner>(true);
            if (zone == null)   zone = GetComponent<Collider>();
        }

        void Update()
        {
            if (player == null)
            {
                var go = GameObject.Find("PlayerAnchor");
                if (go != null) player = go.transform;
            }

            if (!started)
            {
                if (runner == null)
                {
                    if (!warnedNoRunner)
                    {
                        Debug.LogWarning($"[ArenaRoom] '{name}'에 WaveRunner가 없어 웨이브를 시작할 수 없습니다. " +
                                         "runner를 지정하거나 자식에 WaveRunner를 두십시오.", this);
                        warnedNoRunner = true;
                    }
                    return;
                }
                if (IsPlayerInside()) Begin();
                return;
            }

            // 시작했고 아직 클리어 전인데 웨이브가 모두 끝났으면 클리어 처리
            if (!IsCleared && runner != null && runner.CurrentState == WaveRunner.State.Done)
                Clear();
        }

        bool IsPlayerInside()
        {
            if (player == null || zone == null) return false;
            // 볼록 콜라이더 기준: 가장 가까운 점이 곧 플레이어 위치면 안에 있다(회전·스케일 무관).
            Vector3 p = player.position;
            return (zone.ClosestPoint(p) - p).sqrMagnitude < 1e-4f;
        }

        void Begin()
        {
            started  = true;
            IsActive = true;
            IsLocked = true;
            runner.StartFrom(startWaveIndex, runAll);
            onLock?.Invoke();
            OnLock?.Invoke();
        }

        void Clear()
        {
            IsCleared = true;
            IsLocked  = false;
            IsActive  = false;
            onUnlock?.Invoke();
            OnUnlock?.Invoke();

            // 반복 방: 재진입 시 다시 잠그고 시작할 수 있게 되돌린다.
            if (!startOnce) { started = false; IsCleared = false; }
        }

        /// <summary>수동 재무장(외부에서 방을 다시 쓰고 싶을 때).</summary>
        public void ReArm()
        {
            started = false; IsActive = false; IsLocked = false; IsCleared = false;
        }

        /// <summary>사망 후 재시작 시 방과 웨이브를 첫 진입 전 상태로 되돌린다.</summary>
        public void ResetProgress()
        {
            if (runner != null) runner.ResetProgress();
            ReArm();
        }

        void OnDrawGizmosSelected()
        {
            var c = zone != null ? zone : GetComponent<Collider>();
            if (c == null) return;
            Gizmos.color = new Color(1f, 0.45f, 0.2f, 0.25f);
            var b = c.bounds;
            Gizmos.DrawCube(b.center, b.size);
            Gizmos.color = new Color(1f, 0.45f, 0.2f, 0.9f);
            Gizmos.DrawWireCube(b.center, b.size);
        }
    }
}
