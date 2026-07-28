using System;
using System.Collections.Generic;
using UnityEngine;
using MindHexer.Shared.Protocol;

namespace MindHexer.Shared.Net
{
    /// <summary>
    /// 6DoF 입력 스트림용 지터 버퍼(Unity 비의존). (SPEC 2.1 / 5)
    /// 수신 패킷을 송신 타임스탬프 기준으로 버퍼링하고, **재생 지연(playout delay)** 만큼 뒤처진
    /// 재생 시점에서 두 샘플을 시간 보간(위치 Lerp / 회전 Slerp)해 부드러운 포즈를 낸다.
    /// 지연은 관측된 패킷 간격·지터(RFC3550식)에 맞춰 적응 조정한다.
    ///
    /// 클럭: 재생 시점 _playbackTs는 송신자 타임스탬프 축에서 움직이며, 매 프레임 (newest - delay)를
    /// 향해 부드럽게 수렴한다(패킷이 끊기면 자연히 최신값을 유지). 두 폰 시계 차이는 상대 간격만 쓰므로 무관.
    ///
    /// UnityEngine.Mathf/Quaternion.Slerp에 의존하지 않도록 보간을 자체 구현(콘솔/pc-receiver 호환).
    /// </summary>
    public sealed class JitterBuffer
    {
        // ---- 튜닝 파라미터 ----
        /// <summary>기본 재생 지연(ms). 실효 지연의 하한.</summary>
        public float TargetDelayMs = 60f;
        /// <summary>실효 지연 하한/상한(ms).</summary>
        public float MinDelayMs = 30f;
        public float MaxDelayMs = 250f;
        /// <summary>적응 지연: 실효 지연 = max(Target, 간격×BufferPackets) + K×지터.</summary>
        public bool Adaptive = true;
        public float BufferPackets = 2.0f;
        public float JitterMarginK = 2.5f;
        /// <summary>재생 커서가 목표(newest-delay)로 수렴하는 속도(1/초). 클수록 반응 빠름.</summary>
        public float CatchupRate = 8.0f;
        /// <summary>버퍼 최대 샘플 수 / 보관 창(ms).</summary>
        public int Capacity = 64;
        public float HistoryWindowMs = 600f;

        // ---- 지연 보정(예측 외삽) ----
        /// <summary>
        /// 송신 타임스탬프로 패킷 나이를 추정해 최신 샘플을 속도로 외삽(예측)한다 → 전송 지연 우회.
        /// off면 기존 지연 재생(TrySample)만.
        /// </summary>
        public bool LatencyCompensation = true;
        /// <summary>전송 지연 하한(편도)만큼 추가로 앞서 예측하는 양(ms). 대략 RTT/2. 0이면 시계 지터만 상쇄.</summary>
        public float PredictAheadMs = 40f;
        /// <summary>최신 샘플 이후로 외삽할 수 있는 최대 시간(ms). 오버슛/노이즈 증폭 방지 상한.</summary>
        public float MaxExtrapolationMs = 120f;
        /// <summary>외삽 속도 추정 창(ms). 이 구간 평균 속도로 예측(단일 프레임 노이즈 완화).</summary>
        public float VelocityWindowMs = 40f;

        private struct Sample { public long ts; public InputPacket p; }
        private readonly List<Sample> _buf = new List<Sample>(64);

        private double _playbackTs;
        private bool _init;

        // 통계(EMA)
        private double _intervalEma = -1;   // 송신 간격(ms)
        private double _jitterEma;          // RFC3550식 지터(ms)
        private long _lastTs = long.MinValue;
        private long _lastArrival = long.MinValue;
        // 두 폰 시계 오프셋 추정: min(arrival - sendTs) ≈ (시계차 + 최소 편도지연). 느린 상향 누수로 드리프트 추종.
        private double _clockOffset = double.NaN;

        public int Count => _buf.Count;
        public bool HasData => _init && _buf.Count > 0;
        public double PlaybackTs => _playbackTs;
        public double IntervalMs => _intervalEma < 0 ? 0 : _intervalEma;
        public double JitterMs => _jitterEma;
        /// <summary>시계 오프셋 추정치 확보 여부(패킷 1개 이상 수신).</summary>
        public bool HasClock => !double.IsNaN(_clockOffset);
        /// <summary>추정 시계 오프셋(ms) = min(수신 - 송신).</summary>
        public double ClockOffsetMs => double.IsNaN(_clockOffset) ? 0 : _clockOffset;
        /// <summary>직전 SampleCompensated의 예측 리드(ms, 최신 샘플 대비). 실제로 얼마나 앞서 예측 중인지.</summary>
        public double LastLeadMs { get; private set; }

        /// <summary>실효 재생 지연(ms).</summary>
        public float CurrentDelayMs
        {
            get
            {
                float d = TargetDelayMs;
                if (Adaptive && _intervalEma > 0)
                {
                    float byRate = (float)(_intervalEma * BufferPackets) + JitterMarginK * (float)_jitterEma;
                    if (byRate > d) d = byRate;
                }
                if (d < MinDelayMs) d = MinDelayMs;
                if (d > MaxDelayMs) d = MaxDelayMs;
                return d;
            }
        }

        public void Reset()
        {
            _buf.Clear();
            _init = false;
            _intervalEma = -1; _jitterEma = 0;
            _lastTs = long.MinValue; _lastArrival = long.MinValue;
            _clockOffset = double.NaN; LastLeadMs = 0;
            _playbackTs = 0;
        }

        /// <summary>수신 패킷을 버퍼에 넣는다. arrivalLocalMs는 수신측 단조 시계(ms) — 지터 추정용.</summary>
        public void Push(in InputPacket p, long arrivalLocalMs)
        {
            long ts = p.TimestampMs;

            // 간격/지터 EMA 갱신 (RFC3550: D = |(arr_j - arr_i) - (ts_j - ts_i)|)
            if (_lastTs != long.MinValue)
            {
                long tsDelta = ts - _lastTs;
                if (tsDelta > 0)
                    _intervalEma = _intervalEma < 0 ? tsDelta : _intervalEma + (tsDelta - _intervalEma) * 0.125;
                if (_lastArrival != long.MinValue)
                {
                    double d = Math.Abs((arrivalLocalMs - _lastArrival) - tsDelta);
                    _jitterEma += (d - _jitterEma) / 16.0;
                }
            }
            _lastTs = ts;
            _lastArrival = arrivalLocalMs;

            // 시계 오프셋(누수 최소필터): 최솟값을 추종하되 느린 상향 누수로 클럭 드리프트를 따라간다.
            double off = (double)arrivalLocalMs - ts;
            if (double.IsNaN(_clockOffset) || off < _clockOffset) _clockOffset = off;
            else _clockOffset += 0.02; // ≈1–2 ms/s 상향 누수 (수신 레이트에 비례)

            InsertSorted(ts, p);
            Prune();

            if (!_init)
            {
                _playbackTs = ts - CurrentDelayMs;
                _init = true;
            }
        }

        /// <summary>재생 커서를 dt(ms)만큼 진행 — (newest - delay)로 부드럽게 수렴.</summary>
        public void Advance(double dtMs)
        {
            if (!_init || _buf.Count == 0) return;
            long newest = _buf[_buf.Count - 1].ts;
            long oldest = _buf[0].ts;

            double target = newest - CurrentDelayMs;
            double a = CatchupRate * (dtMs / 1000.0);
            if (a < 0) a = 0; if (a > 1) a = 1;
            _playbackTs += (target - _playbackTs) * a;

            if (_playbackTs < oldest) _playbackTs = oldest;
            if (_playbackTs > newest) _playbackTs = newest;
        }

        /// <summary>현재 재생 시점에서 보간 샘플을 얻는다.</summary>
        public bool TrySample(out InputPacket result) => SampleAt(_playbackTs, out result);

        /// <summary>임의 시점 ts(송신 타임스탬프 축)에서 두 샘플을 시간 보간. (순수 — 테스트 용이)</summary>
        public bool SampleAt(double ts, out InputPacket result)
        {
            int n = _buf.Count;
            if (n == 0) { result = default; return false; }
            if (n == 1 || ts <= _buf[0].ts) { result = _buf[0].p; return true; }
            if (ts >= _buf[n - 1].ts) { result = _buf[n - 1].p; return true; }

            int i = 0;
            while (i < n - 1 && _buf[i + 1].ts <= ts) i++;
            Sample a = _buf[i];
            Sample b = _buf[i + 1];
            double span = b.ts - a.ts;
            float t = span <= 0 ? 0f : (float)((ts - a.ts) / span);

            result = Interpolate(in a.p, in b.p, t, (long)ts);
            return true;
        }

        /// <summary>
        /// **지연 보정 샘플**. 헤드셋 단조시계 now(ms)를 송신 타임스탬프 축으로 변환하고
        /// PredictAheadMs만큼 더 앞선 시점을 목표로 최신 샘플을 속도 외삽해 전송 지연을 상쇄한다.
        /// 시계 미확보/보정 off면 기존 지연 재생(TrySample)으로 폴백.
        /// (컨트롤러가 보낸 송신 시각 TimestampMs를 실제로 활용하는 지점.)
        /// </summary>
        public bool SampleCompensated(long headsetNowMs, out InputPacket result)
        {
            int n = _buf.Count;
            if (n == 0) { result = default; LastLeadMs = 0; return false; }
            if (!LatencyCompensation || double.IsNaN(_clockOffset))
            {
                LastLeadMs = 0;
                return TrySample(out result);
            }
            long newest = _buf[n - 1].ts;
            double target = (headsetNowMs - _clockOffset) + PredictAheadMs; // 송신축 예측 목표
            double cap = newest + MaxExtrapolationMs;
            if (target > cap) target = cap;   // 오버슛 상한
            LastLeadMs = target - newest;
            return SampleExtrapolated(target, out result);
        }

        /// <summary>ts가 최신 샘플보다 미래면 최근 속도로 외삽, 아니면 기존 보간/홀드.</summary>
        public bool SampleExtrapolated(double ts, out InputPacket result)
        {
            int n = _buf.Count;
            if (n == 0) { result = default; return false; }
            long newest = _buf[n - 1].ts;
            if (ts <= newest || n < 2) return SampleAt(ts, out result);

            // 속도 추정 기준: 최신에서 VelocityWindowMs 이전 샘플(없으면 가장 오래된 것)
            int j = n - 1;
            while (j > 0 && (newest - _buf[j].ts) < VelocityWindowMs) j--;
            Sample vb = _buf[j];
            Sample nb = _buf[n - 1];
            double vdt = nb.ts - vb.ts;
            if (vdt <= 0) { result = nb.p; result.TimestampMs = (long)ts; return true; }

            float f = (float)((ts - nb.ts) / vdt); // 창 대비 외삽 배율(속도×시간과 동치)
            result = Extrapolate(in vb.p, in nb.p, f, (long)ts);
            return true;
        }

        // ---- 내부 ----

        private void InsertSorted(long ts, in InputPacket p)
        {
            int n = _buf.Count;
            if (n == 0 || ts > _buf[n - 1].ts) { _buf.Add(new Sample { ts = ts, p = p }); return; }
            if (ts == _buf[n - 1].ts) { _buf[n - 1] = new Sample { ts = ts, p = p }; return; } // 중복 타임스탬프 → 최신으로 교체
            // 역순 도착(드묾): 정렬 삽입
            int idx = n;
            while (idx > 0 && _buf[idx - 1].ts > ts) idx--;
            if (idx > 0 && _buf[idx - 1].ts == ts) { _buf[idx - 1] = new Sample { ts = ts, p = p }; return; }
            _buf.Insert(idx, new Sample { ts = ts, p = p });
        }

        private void Prune()
        {
            long newest = _buf[_buf.Count - 1].ts;
            while (_buf.Count > 0 && (newest - _buf[0].ts) > HistoryWindowMs) _buf.RemoveAt(0);
            while (_buf.Count > Capacity) _buf.RemoveAt(0);
        }

        private static InputPacket Interpolate(in InputPacket a, in InputPacket b, float t, long ts)
        {
            var r = b; // 이산 필드(Phase/TouchId/Sequence 등)는 더 새로운 샘플 것을 사용
            r.TimestampMs = ts;
            r.NormalizedPos = LerpV2(a.NormalizedPos, b.NormalizedPos, t);
            r.Position = LerpV3(a.Position, b.Position, t);
            r.Acceleration = LerpV3(a.Acceleration, b.Acceleration, t);
            r.MoveAxis = LerpV2(a.MoveAxis, b.MoveAxis, t);
            r.Rotation = Slerp(a.Rotation, b.Rotation, t);
            return r;
        }

        // 기준 두 샘플(vb=창 시작, nb=최신)의 속도로 위치 선형 외삽 + 회전 slerp 연장.
        // 제어/이산 필드(터치·이동축·가속도·Phase 등)는 예측이 무의미·위험하므로 최신값 유지.
        private static InputPacket Extrapolate(in InputPacket vb, in InputPacket nb, float f, long ts)
        {
            var r = nb; // 이산/제어 필드는 최신 샘플 것 그대로
            r.TimestampMs = ts;
            r.Position = new Vector3(
                nb.Position.x + (nb.Position.x - vb.Position.x) * f,
                nb.Position.y + (nb.Position.y - vb.Position.y) * f,
                nb.Position.z + (nb.Position.z - vb.Position.z) * f);
            r.Rotation = Slerp(vb.Rotation, nb.Rotation, 1f + f); // t>1 → 최신 너머로 연장(예측)
            return r;
        }

        private static Vector2 LerpV2(Vector2 a, Vector2 b, float t)
            => new Vector2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);

        private static Vector3 LerpV3(Vector3 a, Vector3 b, float t)
            => new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);

        // 최단경로 구면선형보간(Slerp). Mathf/Quaternion.Slerp 비의존.
        private static Quaternion Slerp(Quaternion a, Quaternion b, float t)
        {
            float ax = a.x, ay = a.y, az = a.z, aw = a.w;
            float bx = b.x, by = b.y, bz = b.z, bw = b.w;

            float dot = ax * bx + ay * by + az * bz + aw * bw;
            if (dot < 0f) { bx = -bx; by = -by; bz = -bz; bw = -bw; dot = -dot; }

            float w1, w2;
            if (dot > 0.9995f)
            {
                // 거의 동일 → 정규화 선형보간(nlerp)
                w1 = 1f - t; w2 = t;
            }
            else
            {
                double theta = Math.Acos(dot);
                double sin = Math.Sin(theta);
                w1 = (float)(Math.Sin((1.0 - t) * theta) / sin);
                w2 = (float)(Math.Sin(t * theta) / sin);
            }

            float rx = ax * w1 + bx * w2;
            float ry = ay * w1 + by * w2;
            float rz = az * w1 + bz * w2;
            float rw = aw * w1 + bw * w2;

            float len = (float)Math.Sqrt(rx * rx + ry * ry + rz * rz + rw * rw);
            if (len < 1e-8f) return new Quaternion(0f, 0f, 0f, 1f);
            float inv = 1f / len;
            return new Quaternion(rx * inv, ry * inv, rz * inv, rw * inv);
        }
    }
}
