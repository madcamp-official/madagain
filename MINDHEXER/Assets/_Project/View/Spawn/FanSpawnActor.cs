using UnityEngine;

namespace Game.View
{
    /// <summary>
    /// Fan 소환 연출(설계 §4-②). 순수 View — sim은 안 건드린다. WaveRunner 상태를 관찰해 재생.
    ///
    /// 순서 (회전·또잉 없음, 하강만):
    ///   담당 웨이브 시작 → 2.5초 하강(기계식) → 바닥에서 대기 → (소환 진행) →
    ///   소환 전부 끝 → 0.5초 대기 → 2.5초 수납(기계식).
    ///
    /// 이동함수는 선형이 아니라 <b>기계식</b> — 부드럽게 가속·감속하다가 목표를 살짝 지나쳐 철컥 안착.
    ///
    /// 스폰이 하강 완료 뒤 시작되게 하려면 배관 startDelay를 (하강 2.5 + 바닥대기 0.5)=약 3초로 둔다.
    /// 스폰 위치는 고정 링크(_SpawnLinks_fixed)가 정하므로 Fan이 움직여도 안 흔들린다.
    /// </summary>
    [DisallowMultipleComponent]
    public class FanSpawnActor : MonoBehaviour
    {
        [Header("하강/수납")]
        [Tooltip("내려오는 양(월드 down, m). 크게 내려와도 됨.")]
        public float descendY = 8f;
        [Tooltip("내려오는 시간(초).")]
        public float descendTime = 2.5f;
        [Tooltip("수납(올라가는) 시간(초).")]
        public float retractTime = 2.5f;
        [Tooltip("소환이 전부 끝난 뒤 수납 시작까지 대기(초).")]
        public float holdBeforeRetract = 0.5f;
        [Tooltip("기계식 철컥 — 목표를 지나치는 정도(0=없음).")]
        public float overshoot = 1.6f;

        [Header("소환 펀치 (몹 나올 때마다 나왔다 들어갔다)")]
        [Tooltip("소환마다 추가로 더 내려갔다 오는 양(m).")]
        public float punchDepth = 0.4f;
        [Tooltip("펀치 한 번(나갔다 들어옴) 시간(초). 짧게 = 빠르게.")]
        public float punchTime = 0.28f;

        enum Phase { Up, Descending, Bottom, HoldAfter, Retracting }

        WaveRunner runner;
        Vector3 restPos, deployPos;
        Phase phase = Phase.Up;
        float phaseT;
        float punchU = 2f;   // >=1 = 비활성
        bool ready;

        /// <summary>몹 한 마리 소환 시 기계식으로 한 번 '나왔다 들어감'. WaveRunner가 부른다.</summary>
        public void Punch() => punchU = 0f;

        /// <summary>새 게임 시작 시 진행 중이던 팬 연출을 대기 위치로 즉시 복구한다.</summary>
        public void ResetToStartState()
        {
            phase = Phase.Up;
            phaseT = 0f;
            punchU = 2f;
            if (ready) transform.position = restPos;
        }

        void Start()
        {
            runner = Object.FindFirstObjectByType<WaveRunner>();
            restPos = transform.position;
            deployPos = restPos + Vector3.down * descendY;
            ready = true;
        }

        void LateUpdate()
        {
            if (!ready) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            bool resp = Responsible();
            phaseT += dt;
            float pos01 = 0f;   // 0=수납(위) · 1=완전 하강

            switch (phase)
            {
                case Phase.Up:
                    pos01 = 0f;
                    // [2026-07-22] 대기 상태에서 하강 시작 = 소환 직전 팬 하강 → 기계음.
                    if (resp) { CombatAudio.FanMove(transform.position); Go(Phase.Descending); }
                    break;

                case Phase.Descending:
                    pos01 = Mech(phaseT / Mathf.Max(0.05f, descendTime));
                    if (!resp) Go(Phase.HoldAfter);                 // 도중에 끝나면 바로 마무리 대기로
                    else if (phaseT >= descendTime) Go(Phase.Bottom);
                    break;

                case Phase.Bottom:                                  // 바닥에서 소환 대기·진행
                    pos01 = 1f;
                    if (!resp) Go(Phase.HoldAfter);                 // 소환 전부 끝 → 대기
                    break;

                case Phase.HoldAfter:                               // 0.5초 대기
                    pos01 = 1f;
                    if (resp) Go(Phase.Descending);                 // 다시 담당되면 재하강
                    else if (phaseT >= holdBeforeRetract) Go(Phase.Retracting);
                    break;

                case Phase.Retracting:
                    pos01 = 1f - Mech(phaseT / Mathf.Max(0.05f, retractTime));
                    if (resp) Go(Phase.Descending);
                    else if (phaseT >= retractTime) Go(Phase.Up);
                    break;
            }

            // 소환 펀치 — 기계식으로 한 번 더 내려갔다(나옴) 돌아옴(들어감). 짧고 빠르게.
            float punch = 0f;
            if (punchU < 1f)
            {
                punchU = Mathf.Min(1f, punchU + dt / Mathf.Max(0.05f, punchTime));
                // 0→1→0: 앞 절반 = 나감(기계식), 뒤 절반 = 들어옴(기계식)
                float half = punchU < 0.5f ? Mech(punchU * 2f) : Mech((1f - punchU) * 2f);
                punch = punchDepth * half;
            }

            transform.position = Vector3.LerpUnclamped(restPos, deployPos, pos01) + Vector3.down * punch;
        }

        void Go(Phase p) { phase = p; phaseT = 0f; }

        /// <summary>기계식 이동곡선: 부드러운 가속·감속(스무더스텝) + 끝에 살짝 오버슈트 후 안착(철컥).</summary>
        float Mech(float x)
        {
            x = Mathf.Clamp01(x);
            float s = x * x * x * (x * (x * 6f - 15f) + 10f);       // smootherstep
            float k = Mathf.Clamp01((x - 0.6f) / 0.4f);            // 끝 40% 구간
            float bump = overshoot * 0.08f * Mathf.Sin(k * Mathf.PI); // 0→피크→0 (x=1에서 0)
            return s + bump;
        }

        bool Responsible()
        {
            if (runner == null || runner.config == null) return false;
            var st = runner.CurrentState;
            if (st != WaveRunner.State.WaitStart && st != WaveRunner.State.Spawning) return false;
            int w = runner.CurrentWave;
            var waves = runner.config.waves;
            if (w < 0 || waves == null || w >= waves.Length) return false;
            var pipes = waves[w].pipes;
            if (pipes == null) return false;
            foreach (var p in pipes)
                if (p != null && p.marker == transform) return true;
            return false;
        }
    }
}
