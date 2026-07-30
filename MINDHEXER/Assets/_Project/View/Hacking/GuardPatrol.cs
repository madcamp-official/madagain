using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// 경비병 순찰 — 여러 선분으로 이어진 경로를 왕복한다.
    ///
    /// <para>중간 지점에 도착하면 <see cref="midPause"/>초 정지하며 다음 구간 방향으로 회전한 뒤
    /// 다시 걷는다. 경로의 양 끝(첫/마지막 지점)에 도착하면 <see cref="endPause"/>초 정지하며
    /// 반대 방향으로 회전한 뒤 왔던 길을 되돌아간다(왕복).</para>
    ///
    /// <para><b>순서(중요)</b>: 도착 → <b>Walk→Idle 크로스페이드</b>(순간전환 아님, <see cref="blendTime"/>초) →
    /// 블렌드가 끝날 때까지 대기(<c>Settling</c>) → 그제서야 <see cref="GuardTurnStep"/>에 회전을 맡김
    /// → 회전 끝 → <b>Idle→Walk 크로스페이드</b> → 걷기 재개. 애니메이션 전환과 절차적 회전이
    /// 같은 프레임에 겹치면 팝(pop)과 회전이 동시에 일어나 뭉개진다 — 그래서 반드시 순서대로 간다.</para>
    ///
    /// <para><b>정지 자세를 번갈아 쓴다</b>: <c>Idle_1</c>은 완전 정지 자세라 크로스페이드해도 도착한
    /// 뒤엔 죽어 보인다. 대신 고개를 돌리는 <c>Idle_2</c>(왼쪽 보기)·<c>Idle_3</c>(오른쪽 보기)를
    /// 정지할 때마다 번갈아 재생해 매번 다른 정지 자세가 되고 살아있는 것처럼 보인다.</para>
    ///
    /// <para>이동은 물리가 아니라 <c>transform.position</c> 직접 이동이다 — 경비병에는
    /// CharacterController가 없고, 평평한 바닥 순찰이라 그걸로 충분하다.</para>
    ///
    /// <para><b>웨이포인트는 씬 배치용</b>이라 프리팹엔 빈 배열로 두고, 레벨에 배치한 경비병
    /// 인스턴스마다 씬에서 직접 채운다(빈 오브젝트를 순서대로 드래그, 또는 커스텀 인스펙터의
    /// "웨이포인트 N개 추가" 버튼).</para>
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class GuardPatrol : MonoBehaviour
    {
        enum State { Walking, Settling, Turning, Resuming }

        [Tooltip("경로 지점(씬의 빈 오브젝트). 최소 2개. 순서대로 왕복한다.")]
        public Transform[] waypoints;

        [Header("이동")]
        public float moveSpeed = 1.4f;

        [Tooltip("이 거리(m) 안으로 들어오면 도착으로 본다.")]
        public float arriveDistance = 0.05f;

        [Header("정지·회전")]
        [Tooltip("중간 지점 정지 시간(초) — 다음 구간으로 회전. 블렌드 시간도 이 안에서 쓴다.")]
        public float midPause = 0.65f;

        [Tooltip("끝점 정지 시간(초) — 반대 방향으로 회전(왕복).")]
        public float endPause = 1.3f;

        [Header("애니메이션 (GuardManual 컨트롤러) — 절차적 크로스페이드로 연결")]
        public string walkClip = "Walk_IP";

        [Tooltip("정지 자세 A — 정지할 때마다 B와 번갈아 쓴다.")]
        public string idleClipA = "Idle_2";

        [Tooltip("정지 자세 B.")]
        public string idleClipB = "Idle_3";

        [Tooltip("Walk↔Idle 전환 블렌드 시간(초). 이 시간이 지나야(Settling) 회전을 시작/끝낸다.")]
        public float blendTime = 0.2f;

        [Header("디버그")]
        public bool drawGizmos = true;

        Animator _anim;
        GuardTurnStep _turnStep;
        int _index;     // 지금 걸어가고 있는(또는 막 도착한) 목표 웨이포인트 인덱스
        int _dir = 1;   // +1 = 배열 순방향, -1 = 역방향
        State _state = State.Walking;
        float _pauseT, _pauseDur, _blendT;
        Quaternion _turnFrom, _turnTo;
        bool _usingStepTurn;   // 이번 회전을 GuardTurnStep이 맡았으면 true — 폴백 슬러프를 건너뛴다
        bool _nextIsA = true;  // 다음 정지 때 idleClipA/B 중 어느 걸 쓸지 — 정지할 때마다 뒤집는다
        bool _standStill;      // 웨이포인트 1개 — 도착하면 회전 없이 그대로 서서 자기(Update) 끔

        /// <summary>지금 실제로 걷는 중인가(회전·정지 중엔 false). 절차적 숨쉬기 같은 걸 걷기 중엔 끄고 싶을 때 참고.</summary>
        public bool IsWalking => _state == State.Walking;

        void Awake()
        {
            _anim = GetComponent<Animator>();
            _turnStep = GetComponent<GuardTurnStep>();
        }

        void Start()
        {
            // 웨이포인트 0개 = 애초에 제자리 경비병. 1개 = 그 지점까지만 걷고 정지(방향은
            // 웨이포인트의 회전값을 바라본다 — 빈 오브젝트를 배치해 둔 각도 그대로).
            // 두 경우 다 Update를 계속 돌 이유가 없으니 자세 하나 잡고 스스로 꺼진다.
            if (waypoints == null || waypoints.Length == 0)
            {
                CrossFadeIdle();
                enabled = false;
                return;
            }

            if (waypoints.Length == 1)
            {
                _index = 0;
                _dir = 1;
                _state = State.Walking;
                _standStill = true;
                PlayWalk();
                return;
            }

            // 시작 위치는 waypoints[0] 근처라고 가정(씬에서 그렇게 배치) — 첫 목표는 [1].
            _index = 1;
            FaceTowards(waypoints[_index].position);
            PlayWalk();
        }

        void Update()
        {
            if (waypoints == null || waypoints.Length == 0) return;
            if (waypoints.Length < 2 && !_standStill) return;   // Start()가 이미 정지 모드로 안 잡았으면 배선 안 된 것

            switch (_state)
            {
                case State.Walking: TickWalking(); break;
                case State.Settling: TickSettling(); break;
                case State.Turning: TickTurning(); break;
                case State.Resuming: TickResuming(); break;
            }
        }

        void TickWalking()
        {
            Vector3 to = waypoints[_index].position - transform.position;
            to.y = 0f;
            float dist = to.magnitude;

            if (dist <= arriveDistance) { BeginSettle(); return; }

            float step = Mathf.Min(moveSpeed * Time.deltaTime, dist);
            transform.position += (to / dist) * step;
        }

        /// <summary>도착 — Walk→Idle 크로스페이드를 시작하고, 다 끝날 때까지는 회전을 걸지 않는다.</summary>
        void BeginSettle()
        {
            // 웨이포인트 1개 — 다음 구간이 없으니 방향은 그 지점의 회전값을 그대로 따른다(왕복 없음).
            // 회전만 한 번 걸고 도착하면 영구 정지 — Update를 계속 돌 이유가 없어 여기서 끈다.
            if (_standStill)
            {
                _pauseDur = endPause;
                _turnFrom = transform.rotation;
                _turnTo = waypoints[_index].rotation;
                CrossFadeIdle();
                _blendT = 0f;
                _state = State.Settling;
                return;
            }

            bool atEnd = (_dir > 0 && _index == waypoints.Length - 1)
                      || (_dir < 0 && _index == 0);

            _pauseDur = atEnd ? endPause : midPause;
            _turnFrom = transform.rotation;

            if (atEnd) _dir = -_dir;   // 왕복 — 끝점에서만 반전

            Vector3 to = waypoints[_index + _dir].position - waypoints[_index].position;
            to.y = 0f;
            _turnTo = to.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(to.normalized, Vector3.up)
                : _turnFrom;

            CrossFadeIdle();
            _blendT = 0f;
            _state = State.Settling;
        }

        void TickSettling()
        {
            _blendT += Time.deltaTime;
            if (_blendT < blendTime) return;   // 블렌드가 완전히 끝날 때까지는 회전 시작 안 함

            _usingStepTurn = _turnStep != null;
            _pauseT = 0f;
            _state = State.Turning;
            if (_usingStepTurn) _turnStep.BeginTurn(_turnFrom, _turnTo, _pauseDur, OnTurnFinished);
        }

        void OnTurnFinished()
        {
            // 웨이포인트 1개 — 방향을 잡았으니 여기서 영구 정지. 걷기로 돌아갈 다음 구간이 없다.
            if (_standStill) { enabled = false; return; }

            CrossFadeWalk();
            _blendT = 0f;
            _state = State.Resuming;
        }

        void TickResuming()
        {
            _blendT += Time.deltaTime;
            if (_blendT < blendTime) return;   // Idle→Walk 블렌드도 끝까지 기다린 뒤에야 실제로 걷기 시작

            _index += _dir;
            _state = State.Walking;
        }

        /// <summary>GuardTurnStep이 없을 때만 쓰는 폴백 — 몸 전체를 한 번에 슬러프.</summary>
        void TickTurning()
        {
            if (_usingStepTurn) return;   // GuardTurnStep이 스스로 LateUpdate에서 진행하고 끝나면 콜백으로 알려온다

            _pauseT += Time.deltaTime;
            float remain = Mathf.Max(0.01f, _pauseDur - blendTime * 2f);   // 양쪽 블렌드 시간만큼 뺀 나머지가 순수 회전 몫
            float u = Mathf.Clamp01(_pauseT / remain);
            float eased = u * u * (3f - 2f * u);   // smoothstep — 시작·끝이 자연스럽게
            transform.rotation = Quaternion.Slerp(_turnFrom, _turnTo, eased);

            if (u >= 1f) OnTurnFinished();
        }

        void FaceTowards(Vector3 worldPos)
        {
            Vector3 to = worldPos - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
        }

        void PlayWalk() { if (_anim != null && !string.IsNullOrEmpty(walkClip)) _anim.Play(walkClip, 0, 0f); }

        void CrossFadeWalk()
        {
            if (_anim != null && !string.IsNullOrEmpty(walkClip)) _anim.CrossFade(walkClip, blendTime, 0, 0f);
        }

        /// <summary>정지 자세 크로스페이드 — 매번 idleClipA/B를 번갈아 써서 같은 정지가 반복되지 않게 한다.</summary>
        void CrossFadeIdle()
        {
            string clip = _nextIsA ? idleClipA : idleClipB;
            _nextIsA = !_nextIsA;
            if (_anim != null && !string.IsNullOrEmpty(clip)) _anim.CrossFade(clip, blendTime, 0, 0f);
        }

        void OnDrawGizmos()
        {
            if (!drawGizmos || waypoints == null) return;
            Gizmos.color = Color.yellow;
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] == null) continue;
                Gizmos.DrawSphere(waypoints[i].position, 0.1f);
                if (i > 0 && waypoints[i - 1] != null)
                    Gizmos.DrawLine(waypoints[i - 1].position, waypoints[i].position);
            }
        }
    }
}
