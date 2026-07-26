using System.Collections.Generic;
using UnityEngine;
using Game.Sim;

namespace Game.View
{
    /// <summary>
    /// 웨이브 런타임. ArenaWaves(데이터)를 읽어 스폰 진행·진행조건 감시·다음 웨이브를 돌린다.
    /// 한 웨이브의 여러 배관은 **각자 독립적으로 동시에** 자기 몹 목록을 뱉는다.
    /// View 계층 — 스폰은 Main을 통해 sim에 주입한다(기존 스폰 경로와 동일).
    /// 웨이브 소속 추적은 Sim을 건드리지 않고 "스폰한 적 id 집합"으로 한다(결정론 영향 0).
    /// 설계 문서: docs/shared/웨이브_시스템_설계.md
    /// </summary>
    [DisallowMultipleComponent]
    public class WaveRunner : MonoBehaviour, IRunResettable
    {
        public enum State { Idle, WaitStart, Spawning, Watching, Done }

        /// <summary>재시작 시 진행 중이던 웨이브를 멈춘다(ArenaRoom이 재진입 시 다시 시작한다).</summary>
        public void ResetForRestart() => Stop();

        [Tooltip("비워두면 같은 오브젝트의 ArenaWaves를 자동으로 쓴다.")]
        public ArenaWaves config;

        [Tooltip("씬 시작(아레나 진입) 시 자동으로 웨이브 0부터 순차 시작. loop와 함께 쓰면 진입하자마자 주기 스폰.")]
        public bool autoStart = false;

        public State CurrentState { get; private set; } = State.Idle;
        public int   CurrentWave  { get; private set; } = -1;

        /// <summary>배관 하나의 진행 커서 — 배관마다 독립적으로 돈다.</summary>
        struct PipeCursor
        {
            public int index;       // 다음에 뱉을 몹 번호
            public int waitTicks;   // 남은 대기 틱
            public bool done;
        }

        // 웨이브별로 "이 웨이브가 스폰한 적 id" — 잔당이 다음 웨이브로 넘어가도 소속이 유지된다.
        readonly List<HashSet<int>> spawnedIds = new List<HashSet<int>>();
        PipeCursor[] cursors = new PipeCursor[0];
        int  waveWaitTicks;
        int  waveElapsedTicks;   // 현재 웨이브 시작 후 경과 틱(Timer 진행 기준)
        int  spawnedSoFar;
        bool sequential;   // true = 조건 충족 시 다음 웨이브로, false = 이 웨이브만

        void Awake()
        {
            if (config == null) config = GetComponent<ArenaWaves>();
        }

        void Start()
        {
            // 아레나 진입(씬 시작) 시 자동 시작. loop면 무한 순환.
            if (autoStart && config != null && config.HasWave(0))
                StartFrom(0, true);
        }

        // ── 외부(개발 콘솔) 진입점 ──

        /// <summary>index 웨이브부터 시작. runAll=true면 이후 웨이브까지 순차 진행.</summary>
        public string StartFrom(int index, bool runAll)
        {
            if (config == null) config = GetComponent<ArenaWaves>();
            if (config == null) return "ArenaWaves 컴포넌트가 없습니다";
            int count = config.waves != null ? config.waves.Length : 0;
            if (!config.HasWave(index)) return $"웨이브 {index + 1} 없음 (유효: 1~{count})";

            sequential = runAll;
            BeginWave(index);
            return $"[{name}] 웨이브 {index + 1} 시작" + (runAll ? " (이후 순차 진행)" : " (이 웨이브만)");
        }

        public void Stop()
        {
            CurrentState = State.Idle;
            CurrentWave  = -1;
        }

        /// <summary>강제 클리어 판정 — 콘솔 '클리어'용. 진행 중인 웨이브를 즉시 Done으로 만든다
        /// (ArenaRoom이 Done을 감지해 출구 게이트를 연다).</summary>
        public void ForceComplete()
        {
            CurrentState = State.Done;
        }

        /// <summary>진행 중(대기·소환·감시)인가 — 콘솔에서 '현재 아레나'를 찾을 때 쓴다.</summary>
        public bool IsRunning => CurrentState != State.Idle && CurrentState != State.Done;

        /// <summary>새 게임을 시작할 때 웨이브 진행 기록을 최초 상태로 되돌린다.</summary>
        public void ResetProgress()
        {
            CurrentState = State.Idle;
            CurrentWave = -1;
            waveWaitTicks = 0;
            spawnedSoFar = 0;
            sequential = false;
            cursors = new PipeCursor[0];
            spawnedIds.Clear();
        }

        public string Status()
        {
            if (config == null) return $"[{name}] ArenaWaves 없음";
            if (CurrentState == State.Idle)
                return $"[{name}] 대기 중 (웨이브 {(config.waves != null ? config.waves.Length : 0)}개)";
            int alive = AliveOfWave(CurrentWave);
            int total = config.SpawnCountOf(CurrentWave);
            return $"[{name}] 웨이브 {CurrentWave + 1} · {CurrentState} · 배관 {config.PipeCountOf(CurrentWave)}개 · " +
                   $"스폰 {spawnedSoFar}/{total} · 생존 {alive}";
        }

        // ── 진행 ──

        void BeginWave(int index)
        {
            CurrentWave  = index;
            spawnedSoFar = 0;
            waveElapsedTicks = 0;   // 시간 진행(Timer) 기준 리셋
            while (spawnedIds.Count <= index) spawnedIds.Add(new HashSet<int>());
            spawnedIds[index].Clear();

            Wave w = config.waves[index];
            int pipeCount = w.pipes != null ? w.pipes.Length : 0;
            cursors = new PipeCursor[pipeCount];
            for (int i = 0; i < pipeCount; i++)
            {
                PipeEmission p = w.pipes[i];
                bool empty = p == null || p.marker == null || p.MobCount == 0;
                cursors[i] = new PipeCursor
                {
                    index = 0,
                    waitTicks = empty ? 0 : Sec2Ticks(p.startDelay),   // 배관별 시작 지연
                    done = empty,
                };
            }

            waveWaitTicks = Sec2Ticks(w.startDelay);
            CurrentState  = waveWaitTicks > 0 ? State.WaitStart : State.Spawning;
        }

        // sim과 같은 주기(고정 틱)에서 돌려 스폰 타이밍을 틱에 정확히 맞춘다.
        void FixedUpdate()
        {
            if (CurrentState == State.Idle || CurrentState == State.Done) return;
            waveElapsedTicks++;

            switch (CurrentState)
            {
                case State.WaitStart:
                    if (--waveWaitTicks <= 0) CurrentState = State.Spawning;
                    break;
                case State.Spawning:
                    TickSpawning();
                    break;
                case State.Watching:
                    TickWatching();
                    break;
            }

            // 시간 기반 진행(웨이브 시작 기준). 스폰/감시 중이어도 시간이 되면 다음으로 — 주기 스폰.
            if (CurrentState != State.Done && config.HasWave(CurrentWave))
            {
                Wave w = config.waves[CurrentWave];
                if (w.advance == WaveAdvanceMode.Timer && waveElapsedTicks >= Sec2Ticks(w.advanceValue))
                    GoNext("타이머");
            }
        }

        /// <summary>모든 배관을 이번 틱에 각자 한 번씩 진행시킨다(동시 진행).</summary>
        void TickSpawning()
        {
            Wave w = config.waves[CurrentWave];
            bool allDone = true;

            for (int i = 0; i < cursors.Length; i++)
            {
                if (cursors[i].done) continue;
                allDone = false;

                if (cursors[i].waitTicks > 0) { cursors[i].waitTicks--; continue; }

                PipeEmission pipe = w.pipes[i];

                // 간격 0인 몹은 같은 틱에 이어서 뱉는다(동시 방출 묶음).
                while (cursors[i].index < pipe.MobCount)
                {
                    // 공백(대기) 엔트리는 소환하지 않고 간격만 소비한다(번갈아 리듬). Fan도 또잉하지 않음.
                    MobEmit entry = pipe.mobs[cursors[i].index];
                    if (!entry.isGap) SpawnOne(pipe, entry, cursors[i].index);
                    cursors[i].index++;
                    if (cursors[i].index >= pipe.MobCount) break;

                    MobEmit next = pipe.mobs[cursors[i].index];
                    float iv = next.intervalOverride < 0f ? pipe.interval : next.intervalOverride;
                    int t = Sec2Ticks(iv);
                    if (t > 0) { cursors[i].waitTicks = t; break; }   // 다음 마리는 나중 틱에
                }

                if (cursors[i].index >= pipe.MobCount) cursors[i].done = true;
            }

            if (allDone) CurrentState = State.Watching;
        }

        void SpawnOne(PipeEmission pipe, MobEmit emit, int spawnIndex)
        {
            Main main = Main.Instance;
            if (main == null) return;

            var (c, m, s) = MapSpawnConfig.Axes(emit.kind);

            // Fan의 SpawnDrop 링크를 순번 순환으로 하나 고른다(결정론). 스폰 위치 = 링크 시작점(팬 아래 입).
            var fan  = pipe.marker != null ? pipe.marker.GetComponent<FanSpawn>() : null;
            var link = fan != null ? fan.LinkForIndex(spawnIndex) : null;
            Vector3 at = link != null ? link.PointA
                       : fan  != null ? fan.Mouth
                       : pipe.marker != null ? pipe.marker.position : Vector3.zero;

            int id;
            if (m == MobilityType.Flying)
            {
                // 공중몹: 링크 안 탐 — 아래로 + 순번 기반 사선(결정론) 약한 펄스로 흘러나옴.
                float ang = spawnIndex * 1.7f;
                Vector3 side = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * SimConfig.SpawnFlyingSideSpeed;
                Vector3 pulse = Vector3.down * SimConfig.SpawnFlyingDownSpeed + side;
                id = main.SpawnEnemyLaunched(at, c, m, s, pulse);
            }
            else if (link != null)
            {
                // 지상·돌진몹: 펄스 없이 즉시 링크 낙하. SpawnDrop clearance는 항상 0(수직 낙하).
                float grav = link.gravity > 0f ? link.gravity : SimConfig.TraversalGravity;
                id = main.SpawnEnemyDropping(at, c, m, s, link.Low, 0f, grav);
            }
            else
            {
                id = main.SpawnEnemyAt(at, c, m, s);   // 폴백: FanSpawn/링크 없으면 그냥 스폰
            }

            if (id >= 0) { spawnedIds[CurrentWave].Add(id); spawnedSoFar++; }

            // Fan 소환 펀치 — 몹 나올 때마다 기계식으로 나왔다 들어감(공백은 SpawnOne을 안 부르므로 펀치 없음).
            if (pipe.marker != null)
            {
                var actor = pipe.marker.GetComponent<FanSpawnActor>();
                if (actor != null) actor.Punch();
            }
        }

        void TickWatching()
        {
            Wave w = config.waves[CurrentWave];
            int alive = AliveOfWave(CurrentWave);
            int total = config.SpawnCountOf(CurrentWave);

            bool advance;
            switch (w.advance)
            {
                case WaveAdvanceMode.RemainingCount:
                    advance = alive <= Mathf.Max(0, Mathf.RoundToInt(w.advanceValue));
                    break;
                case WaveAdvanceMode.RemainingPercent:
                    advance = total <= 0 || alive <= total * (w.advanceValue / 100f);
                    break;
                case WaveAdvanceMode.Timer:
                    advance = false;   // 시간 진행은 FixedUpdate 타이머가 담당 — 킬로는 안 넘긴다
                    break;
                default:   // KillAll
                    advance = alive <= 0;
                    break;
            }
            if (advance) GoNext($"생존 {alive}/{total}");
        }

        /// <summary>다음 웨이브로. 마지막이면 loop면 0으로 되돌아가고, 아니면 종료.</summary>
        void GoNext(string reason)
        {
            int count = config.waves != null ? config.waves.Length : 0;
            int next = CurrentWave + 1;
            if (next >= count)
            {
                if (config.loop && count > 0) next = 0;          // 루프: 처음으로
                else { Finish(); return; }
            }
            else if (!sequential && !config.loop) { Finish(); return; }   // '이 웨이브만' 모드
            Debug.Log($"[WaveRunner] {name} 웨이브 {CurrentWave + 1} → {next + 1} ({reason})");
            BeginWave(next);
        }

        void Finish()
        {
            CurrentState = State.Done;
            Debug.Log($"[WaveRunner] {name} 웨이브 {CurrentWave + 1} 완료 — 아레나 클리어(문 해제 훅 자리).");
            // TODO(설계 §5): 마무리 정리(시야각+LOS·거리·지속) 및 문 해제 훅.
        }

        int AliveOfWave(int index)
        {
            if (index < 0 || index >= spawnedIds.Count) return 0;
            Main main = Main.Instance;
            return main != null ? main.AliveCountAmong(spawnedIds[index]) : 0;
        }

        /// <summary>초 → 틱(60틱 = 1초). Inspector는 초로 입력, 내부는 틱으로 정확히 돈다.</summary>
        static int Sec2Ticks(float seconds)
            => seconds <= 0f ? 0 : Mathf.Max(0, Mathf.RoundToInt(seconds / SimConfig.TickDelta));
    }
}
