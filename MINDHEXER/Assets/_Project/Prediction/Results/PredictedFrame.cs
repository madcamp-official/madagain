using UnityEngine;
using Game.Sim;

namespace Game.Prediction
{
    /// <summary>
    /// 최종 후보를 최초 스냅샷에서 60Hz(매 틱)로 정밀 재실행해 얻은 프레임 1개.
    /// docs/shared/PREDICTION_CONTRACT.md 11장·3.1.1절 — View의 경로선·잔상은 이 배열에서만
    /// 샘플링한다(View가 직접 보간·생성하지 않는다).
    /// </summary>
    public struct PredictedFrame
    {
        public int tick;
        public Vector3 playerPosition;
        public float playerYaw;
        public bool playerAlive;
        public int floorId;
    }

    /// <summary>계약 3.1.1절 잔상 액션 아이콘 종류.</summary>
    public enum PredictedActionType : byte
    {
        Jump,
        DashForward,
        DashBackward,
        DashLeft,
        DashRight,
        Attack,
        Lunge
    }

    /// <summary>
    /// 매크로 행동이 아니라 실제 Sim 상태머신에서 행동이 "시작된" 정확한 틱.
    /// 리듬 판정(Perfect/Good/Miss, 계약 3.2절)은 이 이벤트의 tick만 기준으로 삼는다.
    /// </summary>
    public struct PredictedActionEvent
    {
        public int tick;
        public PredictedActionType type;

        /// <summary>Lunge 전용 대상 적 id. 그 외는 -1.</summary>
        public int targetId;
    }

    /// <summary>
    /// 최종 후보 정밀 재생 중 적의 결과가 처음 확정된 순간. 일반 사망뿐 아니라
    /// 글로리킬처럼 gloryStage가 시작되어 결과가 잠긴 경우도 포함한다.
    /// </summary>
    public struct PredictedDefeatEvent
    {
        public int tick;
        public int enemyId;
        public Vector3 worldPosition;
    }

    public enum RhythmJudgement : byte
    {
        Pending,
        Perfect,
        Good,
        Miss
    }

    /// <summary>
    /// 액션 이벤트 틱만 판정하는 결정론적 리듬 판정기. View 입력 프레임이나 VFX를 참조하지 않는다.
    /// </summary>
    public sealed class RhythmJudge
    {
        // [예측 세션 수정, 2026-07-21] 3 → 5 → 8, 판정이 너무 빡세다는 피드백으로 Perfect창을
        // 계속 완화. 60Hz 기준 ±8틱 ≈ ±0.13초.
        public const int PerfectWindowTicks = 8;
        // [예측 세션 수정, 2026-07-21] 8 → 11 → 16 → 22: 실시간 판정창을 넓혀서, 이벤트 직전
        // 슬로모를 덜 걸어도 사람이 반응 가능한 정도의 실시간 유예를 확보한다(PredictionConfig의
        // RhythmMaxTimeScale/RhythmMinTimeScale 조정과 짝을 이루는 변경 — 체감 난이도는
        // 유지하되 평균 속도를 올리기 위함). 여전히 좌클릭 판정이 빡세다는 피드백으로 22로 재조정.
        public const int GoodWindowTicks = 22;

        readonly PredictedActionEvent[] events;
        readonly RhythmJudgement[] judgements;
        int firstPending;

        public RhythmJudge(PredictedActionEvent[] events)
        {
            this.events = events ?? System.Array.Empty<PredictedActionEvent>();
            judgements = new RhythmJudgement[this.events.Length];
        }

        public int Count => events.Length;
        public int FirstPendingIndex => firstPending < events.Length ? firstPending : -1;
        public RhythmJudgement GetJudgement(int index) => judgements[index];
        public PredictedActionEvent GetEvent(int index) => events[index];

        /// <summary>
        /// 화면 리듬 마커와 같은 실제 시간축의 입력을 결정론적 판정 틱으로 변환한다.
        /// 목표보다 이른 쪽은 60Hz 틱 간격, 늦은 쪽은 View가 제공하는 대기 창 전체를
        /// GoodWindowTicks로 매핑한다. 정확히 목표 시각이면 반드시 이벤트 틱이다.
        /// </summary>
        public static int MapDisplayTimeToTick(
            float inputRealTime, float targetRealTime, int eventTick, float lateGoodSeconds)
        {
            float delta = inputRealTime - targetRealTime;
            if (delta <= 0f)
                return eventTick + UnityEngine.Mathf.RoundToInt(delta * SimConfig.TickRate);

            int lateTicks = UnityEngine.Mathf.RoundToInt(
                delta / UnityEngine.Mathf.Max(0.001f, lateGoodSeconds) * GoodWindowTicks);
            return eventTick + UnityEngine.Mathf.Clamp(lateTicks, 0, GoodWindowTicks + 1);
        }

        public RhythmJudgement Submit(PredictedActionType type, int inputTick)
        {
            int best = -1;
            int bestDistance = int.MaxValue;
            for (int i = firstPending; i < events.Length; i++)
            {
                if (judgements[i] != RhythmJudgement.Pending || events[i].type != type) continue;
                int delta = inputTick - events[i].tick;
                int distance = System.Math.Abs(delta);
                if (distance > GoodWindowTicks) continue;
                if (distance < bestDistance
                    || (distance == bestDistance && (best < 0 || events[i].tick < events[best].tick)))
                {
                    best = i;
                    bestDistance = distance;
                }
            }
            if (best < 0) return RhythmJudgement.Pending;

            RhythmJudgement result = bestDistance <= PerfectWindowTicks
                ? RhythmJudgement.Perfect
                : RhythmJudgement.Good;
            judgements[best] = result;
            AdvancePending();
            return result;
        }

        /// <summary>해당 틱 입력을 모두 제출한 뒤 호출한다. +8틱은 입력 허용 후 Miss로 닫힌다.</summary>
        public int CompleteTick(int tick)
        {
            int missed = -1;
            for (int i = firstPending; i < events.Length; i++)
            {
                if (judgements[i] != RhythmJudgement.Pending) continue;
                if (tick < events[i].tick + GoodWindowTicks) break;
                judgements[i] = RhythmJudgement.Miss;
                if (missed < 0) missed = i;
            }
            AdvancePending();
            return missed;
        }

        void AdvancePending()
        {
            while (firstPending < events.Length
                   && judgements[firstPending] != RhythmJudgement.Pending)
                firstPending++;
        }
    }
}
