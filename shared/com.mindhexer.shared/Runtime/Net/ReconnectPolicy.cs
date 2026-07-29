using System;

namespace MindHexer.Shared.Net
{
    /// <summary>
    /// 지수 백오프 재연결 스케줄(Unity 비의존). (SPEC 5.1)
    /// 연속 실패마다 대기시간을 factor배로 늘리되 maxMs로 상한. 성공 시 <see cref="Reset"/>.
    /// 순수 로직이라 결정론적으로 테스트 가능.
    /// </summary>
    public sealed class ReconnectPolicy
    {
        private readonly double _baseMs;
        private readonly double _maxMs;
        private readonly double _factor;
        private int _attempt;

        public ReconnectPolicy(double baseMs = 500, double maxMs = 8000, double factor = 2.0)
        {
            _baseMs = baseMs;
            _maxMs = maxMs;
            _factor = factor;
        }

        /// <summary>지금까지의 연속 실패 횟수.</summary>
        public int Attempt => _attempt;

        /// <summary>다음 시도까지 대기할 시간(ms)을 반환하고 실패 카운트를 1 증가시킨다.</summary>
        public double NextDelayMs()
        {
            double d = _baseMs * Math.Pow(_factor, _attempt);
            _attempt++;
            return Math.Min(d, _maxMs);
        }

        /// <summary>연결 성공 시 카운트 초기화.</summary>
        public void Reset() => _attempt = 0;
    }
}
