using System;

namespace MindHexer.Shared.Events
{
    /// <summary>WebSocket 확정 이벤트 종류. (SPEC 2.2 / NETWORK_PROTOCOL.md)</summary>
    public enum EventType
    {
        PairRequest,
        PairAck,
        PatternResult,
        BatteryWarning,
        Disconnect
    }

    /// <summary>
    /// WebSocket으로 오가는 저빈도 확정 이벤트. JSON으로 직렬화된다.
    /// UnityEngine.JsonUtility 또는 원하는 JSON 라이브러리로 (역)직렬화.
    /// payload는 이벤트별 세부 필드를 담는 JSON 문자열.
    /// </summary>
    [Serializable]
    public class EventMessage
    {
        public string type;    // EventType.ToString()
        public string payload; // 이벤트별 JSON (예: PatternResultPayload)

        public EventMessage() { }

        public EventMessage(EventType t, string payloadJson)
        {
            type = t.ToString();
            payload = payloadJson;
        }
    }

    /// <summary>해킹 패턴 판정 결과(S24+ → S10e). PatternResult 이벤트의 payload.</summary>
    [Serializable]
    public class PatternResultPayload
    {
        public bool success;
        public int patternId;
    }

    /// <summary>배터리 경고. BatteryWarning 이벤트의 payload.</summary>
    [Serializable]
    public class BatteryWarningPayload
    {
        public float level; // 0..1
    }

    /// <summary>페어링 요청/응답. 프로토콜 버전 불일치 시 거부 판단에 사용.</summary>
    [Serializable]
    public class PairPayload
    {
        public byte protocolVersion;
        public string deviceName;
    }
}
