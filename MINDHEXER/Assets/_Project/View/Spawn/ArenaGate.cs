using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 아레나 게이트 — 문을 슬라이드로 여닫는다.
    /// <see cref="ArenaRoom"/>의 onLock에 <see cref="Close"/>, onUnlock에 <see cref="Open"/>을 연결해 쓴다.
    ///
    /// 위치 지정: 컴포넌트 우클릭 메뉴로 잡는다 —
    ///   ① 문을 막는 자리에 놓고 "① 닫힘으로 기록"
    ///   ② 문을 열린 자리로 옮기고 "② 열림으로 기록" (오프셋 자동 계산)
    /// 아직 기록 안 한 게이트는 임시로 1000m 극단 개방(확실히 비움)으로 동작한다.
    /// 시작 상태가 Open이면 시작 시 연출 없이 열림 위치로 스냅한다(입구 게이트용).
    /// 콜라이더는 문에 붙어 같이 움직이므로 닫히면 물리적으로 막는다
    /// (sim 이동·레이캐스트가 Unity 콜라이더를 조회하므로 sim에서도 막힌다).
    /// </summary>
    [DisallowMultipleComponent]
    public class ArenaGate : MonoBehaviour, IRunResettable
    {
        public enum StartState { Open, Closed }

        /// <summary>재시작 시 문을 시작 위치로 스냅한다.</summary>
        public void ResetForRestart() => ResetToStart();

        [Tooltip("움직일 문. 비우면 자기 자신을 움직인다.")]
        public Transform door;
        [Tooltip("닫힘 로컬 위치. 우클릭 '① 닫힘으로 기록'으로 채운다. 미기록이면 배치된 위치를 닫힘으로 쓴다.")]
        public Vector3 closedLocalPos;
        [Tooltip("닫힘→열림 로컬 오프셋. 우클릭 '② 열림으로 기록'으로 자동 계산된다.")]
        public Vector3 openOffset = new Vector3(0f, -4f, 0f);
        [Tooltip("위치를 기록했는가. false면 닫힘=배치 위치, 열림=임시 극단(1000m) 개방으로 동작.")]
        public bool positionsRecorded;
        [Tooltip("개폐에 걸리는 시간(초). 팬 내려오듯 기계식으로 천천히 — 2.5초.")]
        public float moveSeconds = 2.5f;
        [Tooltip("시작 상태. 보통 입구(뒤) 게이트=Open, 출구(앞) 게이트=Closed.")]
        public StartState startState = StartState.Open;
        [Tooltip("열리거나 닫힐 때 재생할 소리. 비우면 Resources의 'GateOpen'을 자동 로드.")]
        public AudioClip openSound;

        float t;             // 0=닫힘, 1=열림 (진행도)
        float target;
        bool initialized;

        public bool IsOpen => target > 0.5f;

        void Awake()
        {
            EnsureInitialized();
            t = target = startState == StartState.Open ? 1f : 0f;
            Apply();
        }

        /// <summary>시작 상태로 되돌린다(사망 재시작용). 연출 없이 스냅.</summary>
        public void ResetToStart()
        {
            if (door == null) door = transform;
            t = target = startState == StartState.Open ? 1f : 0f;
            Apply();
        }

        /// <summary>초기화 1회 — door 확보 + 미기록이면 현재 위치를 닫힘으로. (Awake·팀원 재시작 훅이 호출)</summary>
        void EnsureInitialized()
        {
            if (initialized) return;
            if (door == null) door = transform;
            if (!positionsRecorded) closedLocalPos = door.localPosition;
            initialized = true;
        }

        /// <summary>문 열기 — ArenaRoom.onUnlock에 연결. 닫힘→열림 전환 시 소리 재생.</summary>
        public void Open()
        {
            if (target < 0.5f) PlayGateSound();   // 이미 열려 있으면 다시 안 울림
            target = 1f;
        }
        /// <summary>문 닫기 — ArenaRoom.onLock에 연결. 열림→닫힘 전환 시 소리 재생.</summary>
        public void Close()
        {
            if (target > 0.5f) PlayGateSound();   // 이미 닫혀 있으면 다시 안 울림
            target = 0f;
        }
        public void Toggle()
        {
            PlayGateSound();
            target = IsOpen ? 0f : 1f;
        }

        static AudioClip sharedGateSound;
        static bool triedLoadSound;
        static AudioSource sfx2D;

        // Domain Reload가 꺼져 있어 정적 상태가 플레이 사이에 리셋되지 않는다 → 매 플레이 초기화.
        // (안 하면 이전 플레이의 null 캐시나 파괴된 AudioSource 참조가 남아 무음이 된다.)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ResetStatics()
        {
            sharedGateSound = null;
            triedLoadSound  = false;
            sfx2D = null;
        }

        void PlayGateSound()
        {
            AudioClip clip = openSound;
            if (clip == null)
            {
                if (!triedLoadSound) { sharedGateSound = Resources.Load<AudioClip>("GateOpen"); triedLoadSound = true; }
                clip = sharedGateSound;
            }
            if (clip == null) return;

            // 1인칭이라 위치 무관하게 항상 들리도록 2D로 재생(다른 SFX와 동일 관례).
            // PlayClipAtPoint는 3D라, 게이트가 존 어디서든 트리거되어 문과 멀면 거리 감쇠로 안 들린다.
            if (sfx2D == null)
            {
                if (Object.FindFirstObjectByType<AudioListener>() == null)
                {
                    var cam = Camera.main;
                    if (cam != null && cam.GetComponent<AudioListener>() == null)
                        cam.gameObject.AddComponent<AudioListener>();
                }
                var go = new GameObject("[GateSfx]");
                Object.DontDestroyOnLoad(go);
                sfx2D = go.AddComponent<AudioSource>();
                sfx2D.playOnAwake = false;
                sfx2D.spatialBlend = 0f;   // 2D
            }
            sfx2D.PlayOneShot(clip, 2.5f);   // 게이트 소리만 2.5배
        }

        /// <summary>새 게임의 Inspector 시작 상태로 즉시 복구한다.</summary>
        public void ResetToStartState()
        {
            EnsureInitialized();
            t = target = startState == StartState.Open ? 1f : 0f;
            Apply();
        }

        void Update()
        {
            if (Mathf.Approximately(t, target)) return;
            float speed = moveSeconds > 1e-3f ? 1f / moveSeconds : 1000f;
            t = Mathf.MoveTowards(t, target, speed * Time.deltaTime);
            Apply();
        }

        void Apply()
        {
            float s = Mathf.SmoothStep(0f, 1f, t);   // 가감속
            // 기록된 게이트는 실제 오프셋 사용. 미기록 게이트는 임시로 1000m 극단 개방(확실히 비움).
            Vector3 off = positionsRecorded
                ? openOffset
                : (openOffset.sqrMagnitude > 1e-6f ? openOffset.normalized * 1000f : new Vector3(0f, 1000f, 0f));
            door.localPosition = closedLocalPos + off * s;
        }

        // ── 위치 기록 (에디터 우클릭) ──────────────────────────────────────────
        // 워크플로: 문을 막는 자리에 놓고 ① → 문을 열린 자리로 옮기고 ②.
        [ContextMenu("① 현재 문 위치 = 닫힘으로 기록")]
        void RecordClosed()
        {
            var d = door != null ? door : transform;
            closedLocalPos = d.localPosition;
            positionsRecorded = true;
            MarkDirty();
            Debug.Log($"[ArenaGate] {name}: 닫힘 위치 기록 = {closedLocalPos:F2}", this);
        }

        [ContextMenu("② 현재 문 위치 = 열림으로 기록")]
        void RecordOpen()
        {
            var d = door != null ? door : transform;
            openOffset = d.localPosition - closedLocalPos;   // 열림 − 닫힘
            positionsRecorded = true;
            MarkDirty();
            Debug.Log($"[ArenaGate] {name}: 열림 오프셋 기록 = {openOffset:F2}", this);
        }

        [ContextMenu("문 → 닫힘 위치로 이동(미리보기)")]
        void PreviewClosed()
        {
            var d = door != null ? door : transform;
            RecordMove(d);
            d.localPosition = closedLocalPos;
            MarkDirty();
        }

        [ContextMenu("문 → 열림 위치로 이동(미리보기)")]
        void PreviewOpen()
        {
            var d = door != null ? door : transform;
            RecordMove(d);
            d.localPosition = closedLocalPos + openOffset;
            MarkDirty();
        }

        void MarkDirty()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorUtility.SetDirty(this);
                if (door != null) UnityEditor.EditorUtility.SetDirty(door);
            }
#endif
        }

        void RecordMove(Transform d)
        {
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(d, "게이트 문 이동");
#endif
        }

        void OnDrawGizmosSelected()
        {
            var d = door != null ? door : transform;
            // 에디터(배치 상태 = 닫힘)에서 열림 위치를 미리 보여준다.
            Vector3 worldOff = d.parent != null ? d.parent.TransformVector(openOffset) : openOffset;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(d.position, d.position + worldOff);
            Gizmos.DrawWireSphere(d.position + worldOff, 0.3f);
        }
    }
}
