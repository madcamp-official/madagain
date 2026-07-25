using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MindHexer.Shared.Events
{
    /// <summary>WebSocket 확정 이벤트 종류. (SPEC 2.2 / NETWORK_PROTOCOL.md)</summary>
    public enum EventType
    {
        PairRequest,
        PairAck,
        PairReject,
        PatternSubmit,   // S10e→S24+ : 완성된 스와이프 패턴(노드 시퀀스)
        PatternResult,   // S24+→S10e : 판정 결과
        BatteryWarning,
        Disconnect,
        Unknown
    }

    /// <summary>
    /// WebSocket으로 오가는 저빈도 확정 이벤트. 의존성 없는 플랫 JSON으로 (역)직렬화된다.
    /// (Unity JsonUtility 비의존 → 순수 .NET 하니스/테스트로 검증 가능)
    ///
    /// 와이어: {"type":"PatternResult","success":"true","patternId":"0"} 형태의
    /// 문자열:문자열 플랫 오브젝트. 값은 전부 문자열로 저장하고 타입 게터로 변환한다.
    /// </summary>
    public sealed class EventMessage
    {
        // 필드 키 상수
        public const string KeyProtocolVersion = "protocolVersion";
        public const string KeyDeviceName = "deviceName";
        public const string KeySuccess = "success";
        public const string KeyPatternId = "patternId";
        public const string KeyNodes = "nodes";
        public const string KeyLevel = "level";
        public const string KeyReason = "reason";

        public EventType Type;
        public readonly Dictionary<string, string> Fields = new Dictionary<string, string>();

        public EventMessage(EventType type) { Type = type; }

        // ---- 빌더 ----

        public static EventMessage PairRequest(byte protocolVersion, string deviceName)
        {
            var m = new EventMessage(EventType.PairRequest);
            m.Fields[KeyProtocolVersion] = protocolVersion.ToString(CultureInfo.InvariantCulture);
            m.Fields[KeyDeviceName] = deviceName ?? "";
            return m;
        }

        public static EventMessage PairAck(byte protocolVersion)
        {
            var m = new EventMessage(EventType.PairAck);
            m.Fields[KeyProtocolVersion] = protocolVersion.ToString(CultureInfo.InvariantCulture);
            return m;
        }

        public static EventMessage PairReject(string reason)
        {
            var m = new EventMessage(EventType.PairReject);
            m.Fields[KeyReason] = reason ?? "";
            return m;
        }

        public static EventMessage PatternSubmit(int[] nodes)
        {
            var m = new EventMessage(EventType.PatternSubmit);
            m.Fields[KeyNodes] = JoinInts(nodes);
            return m;
        }

        public static EventMessage PatternResult(bool success, int patternId)
        {
            var m = new EventMessage(EventType.PatternResult);
            m.Fields[KeySuccess] = success ? "true" : "false";
            m.Fields[KeyPatternId] = patternId.ToString(CultureInfo.InvariantCulture);
            return m;
        }

        public static EventMessage BatteryWarning(float level)
        {
            var m = new EventMessage(EventType.BatteryWarning);
            m.Fields[KeyLevel] = level.ToString("R", CultureInfo.InvariantCulture);
            return m;
        }

        public static EventMessage Disconnect(string reason)
        {
            var m = new EventMessage(EventType.Disconnect);
            m.Fields[KeyReason] = reason ?? "";
            return m;
        }

        // ---- 타입 게터 ----

        public string GetString(string key, string fallback = "")
            => Fields.TryGetValue(key, out var v) ? v : fallback;

        public int GetInt(string key, int fallback = 0)
            => Fields.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : fallback;

        public byte GetByte(string key, byte fallback = 0)
            => Fields.TryGetValue(key, out var v) && byte.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : fallback;

        public float GetFloat(string key, float fallback = 0f)
            => Fields.TryGetValue(key, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : fallback;

        public bool GetBool(string key, bool fallback = false)
            => Fields.TryGetValue(key, out var v) ? v == "true" || v == "1" : fallback;

        /// <summary>콤마로 이어진 정수 배열 필드를 파싱(예: "0,1,3,2"). 없으면 빈 배열.</summary>
        public int[] GetIntArray(string key)
        {
            if (!Fields.TryGetValue(key, out var v) || string.IsNullOrEmpty(v)) return new int[0];
            var parts = v.Split(',');
            var list = new List<int>(parts.Length);
            foreach (var s in parts)
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) list.Add(n);
            return list.ToArray();
        }

        private static string JoinInts(int[] a)
        {
            if (a == null || a.Length == 0) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < a.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(a[i].ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        // ---- 코덱 (의존성 없는 플랫 JSON) ----

        public string Encode()
        {
            var sb = new StringBuilder();
            sb.Append('{');
            AppendPair(sb, "type", Type.ToString(), first: true);
            foreach (var kv in Fields)
                AppendPair(sb, kv.Key, kv.Value, first: false);
            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendPair(StringBuilder sb, string key, string value, bool first)
        {
            if (!first) sb.Append(',');
            sb.Append('"').Append(Escape(key)).Append("\":\"").Append(Escape(value)).Append('"');
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>플랫 JSON 문자열을 EventMessage로 파싱. 실패 시 false.</summary>
        public static bool TryDecode(string json, out EventMessage msg)
        {
            msg = null;
            if (string.IsNullOrEmpty(json)) return false;
            if (!TryParseFlatObject(json, out var map)) return false;
            if (!map.TryGetValue("type", out var typeStr)) return false;

            var type = ParseType(typeStr);
            var m = new EventMessage(type);
            foreach (var kv in map)
                if (kv.Key != "type") m.Fields[kv.Key] = kv.Value;
            msg = m;
            return true;
        }

        private static EventType ParseType(string s)
        {
            switch (s)
            {
                case "PairRequest": return EventType.PairRequest;
                case "PairAck": return EventType.PairAck;
                case "PairReject": return EventType.PairReject;
                case "PatternSubmit": return EventType.PatternSubmit;
                case "PatternResult": return EventType.PatternResult;
                case "BatteryWarning": return EventType.BatteryWarning;
                case "Disconnect": return EventType.Disconnect;
                default: return EventType.Unknown;
            }
        }

        // 플랫 {"key":"value",...} 파서 (문자열:문자열만 지원). 성공 시 true.
        private static bool TryParseFlatObject(string s, out Dictionary<string, string> map)
        {
            map = new Dictionary<string, string>();
            int i = 0, n = s.Length;
            SkipWs(s, ref i);
            if (i >= n || s[i] != '{') return false;
            i++;
            SkipWs(s, ref i);
            if (i < n && s[i] == '}') return true; // 빈 오브젝트

            while (i < n)
            {
                SkipWs(s, ref i);
                if (!TryReadString(s, ref i, out string key)) return false;
                SkipWs(s, ref i);
                if (i >= n || s[i] != ':') return false;
                i++;
                SkipWs(s, ref i);
                if (!TryReadString(s, ref i, out string val)) return false;
                map[key] = val;
                SkipWs(s, ref i);
                if (i >= n) return false;
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') return true;
                return false;
            }
            return false;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }

        private static bool TryReadString(string s, ref int i, out string result)
        {
            result = null;
            if (i >= s.Length || s[i] != '"') return false;
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') { result = sb.ToString(); return true; }
                if (c == '\\')
                {
                    if (i >= s.Length) return false;
                    char e = s[i++];
                    switch (e)
                    {
                        case '\\': sb.Append('\\'); break;
                        case '"': sb.Append('"'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case '/': sb.Append('/'); break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            return false; // 닫는 따옴표 없음
        }
    }
}
