using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using MindHexer.Shared.Protocol;
using Debug = UnityEngine.Debug;

namespace Game.View
{
    /// <summary>
    /// S10e 컨트롤러 UDP 수신 → 게임 입력. (프로토콜 v3, 128바이트)
    ///
    /// <para><b>실행 순서를 앞당긴다</b>(-200). <see cref="HackDriver"/>가 <c>Source.Poll()</c>로
    /// 매핑을 부르고 <see cref="FirstPersonPlayer"/>가 이동을 처리하기 <b>전에</b> 이 프레임의
    /// 패킷을 전부 소화해야 한다. 순서가 뒤집히면 입력이 한 프레임씩 밀린다.</para>
    ///
    /// <para><b>연속값과 이산값을 분리한다.</b> 포즈만 <see cref="InputSmoother"/>(외삽)를 태우고,
    /// 터치는 <c>touchId</c>별 상태기계로 직접 관리한다. 터치를 외삽하면 <b>안 눌린 손가락이 눌린 것으로
    /// 예측되는</b> 사고가 난다.</para>
    ///
    /// <para><b>큐를 쓰는 이유 — 최신 패킷만 보면 탭이 사라진다.</b> 폰은 60Hz인데 에디터는 가변
    /// 프레임이라 한 프레임에 패킷이 둘 이상 올 수 있다. 그 안에 Down과 Up이 같이 들어 있으면(빠른 탭)
    /// 프레임 시작과 끝이 모두 "안 눌림"이라 <b>변화가 없어 보인다</b>. 그래서 도착한 패킷을 하나도
    /// 빠뜨리지 않고 순서대로 상태기계에 먹여 엣지를 <b>누적</b>한다.</para>
    ///
    /// <para>페어링(WebSocket)은 쓰지 않는다 — 컨트롤러가 직결 모드로 UDP만 쏜다. 디스커버리도 끈다:
    /// 노트북이 eduroam과 핫스팟을 동시에 물고 있어 <c>LocalIPv4.Resolve()</c>가 기본 경로(eduroam) IP를
    /// 광고해 폰이 못 찾는다.</para>
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class ControllerLink : MonoBehaviour
    {
        [Header("수신")]
        [Tooltip("컨트롤러가 쏘는 포트. shared NetworkConstants.UdpInputPort와 같아야 한다.")]
        public int port = NetworkConstants.UdpInputPort;

        [Tooltip("이 시간(초) 동안 유효 패킷이 없으면 연결 끊김으로 본다.")]
        public float timeoutSeconds = 1f;

        [Tooltip("메인 스레드가 밀렸을 때 큐 상한. 넘으면 오래된 것부터 버린다(지연 누적 방지).")]
        public int queueLimit = 256;

        [Header("터치")]
        [Tooltip("터치가 이 거리(정규화) 이상 움직이면 '그린 것'으로 보고 탭으로 치지 않는다.")]
        public float tapMoveThreshold = 0.02f;

        // ── 공개 상태 (Map·튜닝 패널이 읽는다) ────────────────────────────

        /// <summary>지금 살아 있는 링크. Map과 진단 패널이 이걸로 접근한다.</summary>
        public static ControllerLink Active { get; private set; }

        /// <summary>마지막 유효 패킷 이후 경과(초). 한 번도 못 받았으면 무한대.</summary>
        public float SinceLastPacket => _lastPacketTime < 0f ? float.PositiveInfinity
                                                             : Now - _lastPacketTime;

        public bool Connected => SinceLastPacket <= timeoutSeconds;

        /// <summary>가장 최근 패킷(원본). 진단·화면기하 조회용.</summary>
        public InputPacket Latest => _latest;

        public long AcceptedCount => Interlocked.Read(ref _accepted);
        public long DiscardedCount => Interlocked.Read(ref _discarded);

        /// <summary>초당 수신 패킷 수(1초 창).</summary>
        public float PacketRate { get; private set; }

        /// <summary>시퀀스 기준 누락 추정치(도착 순서 기준 건너뛴 개수).</summary>
        public long LostCount { get; private set; }

        /// <summary>이번 프레임에 소화한 패킷 수. 0이면 이 프레임엔 새 입력이 없었다.</summary>
        public int DrainedThisFrame { get; private set; }

        /// <summary>터치 슬롯 상태. 인덱스는 <see cref="NetworkConstants.MaxTouches"/> 미만.</summary>
        public readonly TouchTrack[] Touches = new TouchTrack[NetworkConstants.MaxTouches];

        /// <summary>
        /// 터치 하나의 추적 상태. 화면 절반 소유권은 <b>Down 시점에 정해 생애 동안 안 바꾼다</b> —
        /// 도중에 반대편으로 넘어가도 그 손가락의 역할은 유지돼야 손이 안 헷갈린다.
        /// </summary>
        public struct TouchTrack
        {
            public bool active;
            public int id;
            public bool leftHalf;        // Down 시점 x < 0.5
            public Vector2 startNorm;
            public Vector2 norm;
            public Vector2 deltaThisFrame;   // 이번 프레임 누적 이동(정규화)
            public float downTime;
            public bool moved;           // 탭이 아니라 드래그로 판정됨
            public bool pressedThisFrame;
            public bool releasedThisFrame;
        }

        // ── 내부 ──────────────────────────────────────────────────────────

        struct Stamped { public double arrival; public InputPacket p; }

        UdpClient _udp;
        Thread _thread;
        volatile bool _running;

        readonly Queue<Stamped> _queue = new Queue<Stamped>();
        readonly object _queueLock = new object();

        readonly SequenceValidator _validator = new SequenceValidator();
        long _accepted, _discarded;

        InputPacket _latest;
        uint _sessionId;
        bool _hasSession;
        uint _lastSeq;
        bool _hasSeq;

        float _lastPacketTime = -1f;
        float _rateWindowStart;
        int _rateCount;

        static readonly double TicksToSeconds = 1.0 / Stopwatch.Frequency;
        static float Now => Time.realtimeSinceStartup;

        void Awake()
        {
            Active = this;
            for (int i = 0; i < Touches.Length; i++) Touches[i].id = -1;
        }

        void OnEnable()
        {
            try
            {
                _udp = new UdpClient(port);
                _running = true;
                _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "MHX-ControllerRx" };
                _thread.Start();
                Debug.Log($"[ControllerLink] UDP {port} 수신 시작");
            }
            catch (SocketException e)
            {
                // 포트 선점(다른 에디터 인스턴스·이전 실행 잔재)이 가장 흔한 원인이다.
                Debug.LogError($"[ControllerLink] UDP {port} 바인드 실패 — {e.Message}");
                enabled = false;
            }
        }

        void OnDisable()
        {
            _running = false;
            try { _udp?.Close(); } catch { /* 종료 중 */ }
            _thread?.Join(300);
            _thread = null;
            _udp = null;
            lock (_queueLock) _queue.Clear();
        }

        void OnDestroy()
        {
            if (Active == this) Active = null;
        }

        /// <summary>수신 스레드. 도착 시각을 <b>여기서</b> 찍는다 — 메인 스레드 드레인 시각을 쓰면
        /// 프레임 지터가 age 추정에 섞여 지연 보상이 오염된다.</summary>
        void ReceiveLoop()
        {
            var remote = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    byte[] data = _udp.Receive(ref remote);
                    double arrival = Stopwatch.GetTimestamp() * TicksToSeconds;

                    if (!PacketSerializer.TryDeserialize(data, data.Length, out InputPacket p))
                    {
                        Interlocked.Increment(ref _discarded);
                        continue;
                    }
                    if (!_validator.Accept(p.Sequence))
                    {
                        Interlocked.Increment(ref _discarded);   // 역전·중복
                        continue;
                    }

                    Interlocked.Increment(ref _accepted);
                    lock (_queueLock)
                    {
                        // 메인 스레드가 밀렸을 때 무한히 쌓이면 입력이 과거를 재생하게 된다.
                        while (_queue.Count >= queueLimit) _queue.Dequeue();
                        _queue.Enqueue(new Stamped { arrival = arrival, p = p });
                    }
                }
                catch (SocketException) when (!_running) { break; }
                catch (System.ObjectDisposedException) { break; }
                catch { /* 개별 패킷 오류는 무시하고 계속 */ }
            }
        }

        void Update()
        {
            ClearFrameEdges();
            DrainQueue();
            UpdateRate();
        }

        /// <summary>
        /// 엣지는 <b>프레임 시작에</b> 지운다. 이 컴포넌트가 -200으로 먼저 돌고 HackDriver가 뒤에
        /// 소비하므로, 프레임 N에 세운 엣지는 같은 프레임에 읽히고 N+1 시작에 사라진다.
        /// </summary>
        void ClearFrameEdges()
        {
            DrainedThisFrame = 0;
            for (int i = 0; i < Touches.Length; i++)
            {
                Touches[i].pressedThisFrame = false;
                Touches[i].releasedThisFrame = false;
                Touches[i].deltaThisFrame = Vector2.zero;
            }
        }

        void DrainQueue()
        {
            while (true)
            {
                Stamped s;
                lock (_queueLock)
                {
                    if (_queue.Count == 0) break;
                    s = _queue.Dequeue();
                }
                Consume(s);
                DrainedThisFrame++;
            }
        }

        void Consume(in Stamped s)
        {
            InputPacket p = s.p;

            // 앱 재시작 감지 — 타임스탬프가 0으로 돌아가 age 추정이 음수로 튀는 것을 막는
            // 유일한 방어선이다. 세션이 바뀌면 지연·평활 상태를 통째로 버린다.
            if (!_hasSession || p.SessionId != _sessionId)
            {
                _hasSession = true;
                _sessionId = p.SessionId;
                _hasSeq = false;
                LostCount = 0;
                var src0 = NetworkHexInputSource.Active;
                if (src0 != null) { src0.Smoother.Clear(); src0.Latency.Reset(); }
                ResetTouches();
                Debug.Log($"[ControllerLink] 새 세션 {p.SessionId} — 지연·평활 상태 리셋");
            }

            if (_hasSeq && p.Sequence > _lastSeq + 1) LostCount += p.Sequence - _lastSeq - 1;
            _lastSeq = p.Sequence;
            _hasSeq = true;

            _latest = p;
            _lastPacketTime = Now;
            _rateCount++;

            // 연속값(포즈)만 지연 가리기 층에 넣는다. 터치는 아래에서 직접.
            var src = NetworkHexInputSource.Active;
            if (src != null)
            {
                var sample = new ControllerSample
                {
                    position = p.Position,
                    rotation = p.Rotation,
                    touchNorm = Vector2.zero,   // 터치는 이 경로를 타지 않는다(이산값)
                    touchPhase = 0,
                };
                src.Push(s.arrival, p.TimestampMs / 1000.0, sample);
            }

            ApplyTouches(in p);
        }

        /// <summary>
        /// 패킷의 터치 배열을 슬롯 상태기계에 먹인다. 같은 <c>fingerId</c>를 추적하고,
        /// 사라진 손가락은 <see cref="TouchTrack.releasedThisFrame"/>을 세운 뒤 비운다.
        /// </summary>
        void ApplyTouches(in InputPacket p)
        {
            // 이 패킷에 살아 있는 id 목록(스택 배열 대신 고정 크기라 할당 없음).
            int a0 = -1, a1 = -1;
            for (int i = 0; i < NetworkConstants.MaxTouches; i++)
            {
                TouchSample t = p.GetTouch(i);
                if (!t.IsActive || t.Phase == TouchPhaseCode.Up) continue;
                if (a0 < 0) a0 = t.Id; else if (a1 < 0) a1 = t.Id;
            }

            // 1) 사라졌거나 Up으로 온 슬롯 해제
            for (int s = 0; s < Touches.Length; s++)
            {
                if (!Touches[s].active) continue;
                int id = Touches[s].id;
                bool stillDown = (id == a0) || (id == a1);
                if (stillDown) continue;

                Touches[s].releasedThisFrame = true;
                Touches[s].active = false;
            }

            // 2) 이번 패킷의 터치 반영
            for (int i = 0; i < NetworkConstants.MaxTouches; i++)
            {
                TouchSample t = p.GetTouch(i);
                if (!t.IsActive) continue;
                if (t.Phase == TouchPhaseCode.Up) continue;   // 위에서 해제 처리됨

                int slot = FindSlot(t.Id);
                if (slot < 0) slot = NewSlot(t.Id, t.Normalized);
                if (slot < 0) continue;   // 슬롯 없음(있을 수 없지만 방어)

                Vector2 prev = Touches[slot].norm;
                Touches[slot].norm = t.Normalized;
                Touches[slot].deltaThisFrame += t.Normalized - prev;

                if (!Touches[slot].moved &&
                    (t.Normalized - Touches[slot].startNorm).sqrMagnitude > tapMoveThreshold * tapMoveThreshold)
                    Touches[slot].moved = true;
            }
        }

        int FindSlot(int id)
        {
            for (int s = 0; s < Touches.Length; s++)
                if (Touches[s].active && Touches[s].id == id) return s;
            return -1;
        }

        int NewSlot(int id, Vector2 norm)
        {
            for (int s = 0; s < Touches.Length; s++)
            {
                if (Touches[s].active) continue;
                Touches[s] = new TouchTrack
                {
                    active = true,
                    id = id,
                    // 절반 소유권은 여기서 한 번만 정한다(가로 화면이라 x가 긴 축).
                    leftHalf = norm.x < 0.5f,
                    startNorm = norm,
                    norm = norm,
                    deltaThisFrame = Vector2.zero,
                    downTime = Now,
                    moved = false,
                    pressedThisFrame = true,
                    releasedThisFrame = false,
                };
                return s;
            }
            return -1;
        }

        void ResetTouches()
        {
            for (int s = 0; s < Touches.Length; s++)
                Touches[s] = new TouchTrack { id = -1 };
        }

        void UpdateRate()
        {
            float now = Now;
            if (now - _rateWindowStart >= 1f)
            {
                PacketRate = _rateCount / Mathf.Max(1e-3f, now - _rateWindowStart);
                _rateCount = 0;
                _rateWindowStart = now;
            }

            // 끊기면 눌린 채로 굳은 터치를 풀어준다 — 안 그러면 조작이 눌린 상태로 멈춘다.
            if (!Connected)
            {
                for (int s = 0; s < Touches.Length; s++)
                {
                    if (!Touches[s].active) continue;
                    Touches[s].active = false;
                    Touches[s].releasedThisFrame = true;
                }
            }
        }

        /// <summary>지정한 절반에서 시작한 활성 터치 슬롯. 없으면 -1.</summary>
        public int SlotOnHalf(bool left)
        {
            for (int s = 0; s < Touches.Length; s++)
                if (Touches[s].active && Touches[s].leftHalf == left) return s;
            return -1;
        }

        /// <summary>이번 프레임 그 절반에서 <b>떼어진</b> 터치. 탭 판정용(해제 후에도 1프레임 읽힌다).</summary>
        public int ReleasedSlotOnHalf(bool left)
        {
            for (int s = 0; s < Touches.Length; s++)
                if (Touches[s].releasedThisFrame && Touches[s].leftHalf == left) return s;
            return -1;
        }
    }
}
