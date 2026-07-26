using System.Collections.Generic;
using UnityEngine;
using Game.Sim;
using Game.Prediction;

namespace Game.View
{
    /// <summary>
    /// RoutePreviewStub.cs 자신의 주석대로("실제 전투 봇(Sim/Prediction)이 준비되면 이 자리를
    /// 그 산출물로 교체하면 된다") 이 자리를 실제 Game.Prediction(BeamSearch+CandidateReplayer)
    /// 결과로 채운다. 연출(PredictionController)은 PredictedRoute 계약만 소비하므로 그쪽은
    /// 안 건드린다.
    /// </summary>
    public static class RealRoutePreview
    {
        /// <param name="seconds">내다볼 시간(초). 예지 게이지가 찬 만큼 길어진다 —
        /// <see cref="PredictionSettings.ForDuration"/>이 1~5초로 자른다.</param>
        public static List<PredictedRoute> Build(in SimWorld w, in SimServices services, Color[] colors, float seconds)
        {
            var routes = new List<PredictedRoute>();
            if (w.player.combat.hp <= 0) return routes;

            // [예지 게이지 연결, 2026-07-22] 예전엔 항상 Full(3초 고정)이라, "게이지 양에 따라
            // 1~5초를 내다본다"는 설계로 만들어 둔 ForDuration이 게임에서 한 번도 안 불렸다.
            // 이제 게이지가 곧 예측 지평이다 — 아껴서 길게 볼지, 짧게 자주 쓸지가 선택이 된다.
            // 성능상으로도 제일 비싼 5초 탐색이 "게이지를 다 채워야만" 나오는 구조가 된다.
            // 그 위에 동적 축소(Degrade)를 실제 적 수에 맞춰 얹는다 — F 입력 순간 동기적으로
            // 도는 검색이라 적이 많으면 그만큼 체감 끊김이 생기기 때문.
            // PlanByProfile은 안전형/기회형/공격형이 월드 확장을 공유하고 점수만 따로 계산한다.
            PredictionSettings settings =
                PredictionSettings.Degrade(PredictionSettings.ForDuration(seconds), w.enemyCount);
            PredictionProfiler.Begin(w.enemyCount, in settings);
            PredictionProfiler.TotalMarker.Begin();
            CandidatePath[] plans = PredictionPlanner.PlanByProfile(in w, in services, settings);

            for (int i = 0; i < plans.Length; i++)
            {
                CandidatePath plan = plans[i];
                bool ok;
                using (PredictionProfiler.FinalResimulation())
                    ok = CandidateReplayer.Replay(in w, in services, plan, settings.macroTicks);
                if (!ok || plan.predictedFrames.Length == 0) continue;

                var route = new PredictedRoute
                {
                    color = colors[i % colors.Length],
                    seconds = plan.durationTicks / (float)SimConfig.TickRate,
                    controls = plan.controls,   // 확정 시 실제 플레이어 자동 재생용(계약 11장 controls)
                    profileLabel = plan.profileLabel ?? "",
                };
                for (int f = 0; f < plan.predictedFrames.Length; f++)
                {
                    route.path.Add(plan.predictedFrames[f].playerPosition);
                    route.yaw.Add(plan.predictedFrames[f].playerYaw);
                }

                // [예측 세션 수정, 2026-07-20] 정지 잔상은 더 이상 고정 0.5초(30틱) 격자가
                // 아니라 실제 행동이 시작되는 ActionEvent 틱마다 찍는다 — PREDICTION_CONTRACT.md
                // §3.1.1의 "30틱 간격" 서술과는 달라졌다(팀 합의 전이라 문서는 그대로 둠).
                // 이동만 하는 구간은 정지 스탬프 없이 View의 이동 트레일(UpdateRevealTrails)로만
                // 표현한다. ghostFrames·actionMarkers를 같은 순회에서 함께 채운다.
                foreach (PredictedActionEvent evt in plan.actionEvents)
                {
                    if (evt.tick < 0 || evt.tick >= plan.predictedFrames.Length) continue;
                    PredictedFrame f = plan.predictedFrames[evt.tick];
                    route.ghostFrames.Add(f);
                    route.actionMarkers.Add(new ActionMarker
                    {
                        tick = evt.tick,
                        position = f.playerPosition,
                        yaw = f.playerYaw,
                        type = evt.type,
                        targetId = evt.targetId,
                    });
                }
                // 마지막 프레임(경로 종료 자세)은 행동이 없어도 항상 표시.
                PredictedFrame last = plan.predictedFrames[plan.predictedFrames.Length - 1];
                if (route.ghostFrames.Count == 0 || route.ghostFrames[route.ghostFrames.Count - 1].tick != last.tick)
                    route.ghostFrames.Add(last);

                for (int e = 0; e < plan.defeatEvents.Length; e++)
                    route.kills.Add(plan.defeatEvents[e].worldPosition);
                routes.Add(route);
            }
            PredictionProfiler.TotalMarker.End();
            Debug.Log(PredictionProfiler.Finish(routes.Count));
            return routes;
        }

    }
}
