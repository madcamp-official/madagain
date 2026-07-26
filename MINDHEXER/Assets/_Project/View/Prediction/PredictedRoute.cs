using System.Collections.Generic;
using UnityEngine;
using Game.Sim;
using Game.Prediction;

namespace Game.View
{
    /// <summary>
    /// 연출이 소비하는 루트 데이터. 생성 방식(스텁이든 실제 전투 봇이든)과 무관한 고정 계약.
    /// 지금은 RoutePreviewStub이 채우고, 예측 봇이 준비되면 Sim/Prediction 산출물로 교체한다.
    /// </summary>
    public class PredictedRoute
    {
        public List<Vector3> path  = new List<Vector3>();   // 경로 점(월드 좌표)

        /// <summary>[예측 세션 추가, 2026-07-21] path와 같은 인덱스(틱)로 대응하는 플레이어 요(yaw,
        /// 도). Following 중 틱별 회전 속도를 재서 완급(회전=느리게/직진=빠르게) 페이싱에 쓴다.
        /// RoutePreviewStub은 못 채운다 — 그때는 비어있다.</summary>
        public List<float> yaw = new List<float>();
        public List<Vector3> kills = new List<Vector3>();   // 처치 위치 마커
        public float seconds;                               // 예상 소요 시간(초)
        public Color color = Color.white;

        // >>> [예측 세션 추가, 2026-07-18] 아래 ghostFrames/actionMarkers/controls 3개 필드와
        // 파일 맨 아래 ActionMarker 구조체는 원래 없었다(위 4개 필드가 KJH 원본). 실제 예측
        // 연동·자동실행·잔상 표시에 필요해서 추가함 — RoutePreviewStub은 이 3개를 안 채우고
        // 기본값(빈 리스트/null)으로 두므로 하위 호환된다.
        /// <summary>정지 잔상 샘플 프레임 — 고정 간격이 아니라 actionMarkers와 같은
        /// ActionEvent 틱(+ 항상 포함되는 마지막 프레임)에서 뽑는다. 순수 가독성용 표식이며,
        /// 계약 3.1절 "모든 일반 잔상이 입력 노드는 아니다"대로 리듬 판정 대상이 아니다 —
        /// 판정 대상은 아래 actionMarkers뿐.
        /// RoutePreviewStub은 못 채운다(실제 재시뮬레이션이 있어야 함) — 그때는 비어있다.</summary>
        public List<PredictedFrame> ghostFrames = new List<PredictedFrame>();

        /// <summary>계약 3.1/3.1.1절: 실제 행동이 시작된 정확한 틱마다 하나씩 — 0.5초 격자와
        /// 무관하게, 같은 0.5초 안에 행동이 여러 개 있어도(예: 런지 직후 대시) 각자의 정확한
        /// 위치에 별도로 생긴다. 리듬 판정(Perfect/Good/Miss)은 이 마커의 tick만 기준으로 한다.
        /// RoutePreviewStub은 못 채운다 — 그때는 비어있다.</summary>
        public List<ActionMarker> actionMarkers = new List<ActionMarker>();

        /// <summary>확정 시 실제 플레이어를 자동 재생하기 위한 매 틱 기록 입력. RoutePreviewStub은
        /// 못 채운다 — 그때는 null이고, PredictionController가 자동실행을 건너뛴다.</summary>
        public InputCmd[] controls;

        /// <summary>이 루트를 만든 평가 프로필 이름(안전형/기회형/공격형, CandidatePath.profileLabel
        /// 그대로). RoutePreviewStub은 못 채운다 — 그때는 빈 문자열.</summary>
        public string profileLabel = "";
    }
    // <<< [예측 세션 추가 끝]

    /// <summary>계약 3.1.1절 액션 잔상 1개 — 대시 방향·평타·런지 등 실제 행동이 시작된 정확한
    /// 틱의 위치·방향·종류. PredictedActionEvent(Prediction 쪽 계약)에 월드 위치·yaw를 얹은
    /// View 전용 표현.</summary>
    public struct ActionMarker
    {
        public int tick;
        public Vector3 position;
        public float yaw;
        public PredictedActionType type;
        public int targetId;   // Lunge 전용, 그 외 -1
    }
}
