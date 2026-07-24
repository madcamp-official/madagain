namespace MindHexer.Shared.Protocol
{
    /// <summary>
    /// 시퀀스 번호 기준으로 오래된/중복 패킷을 폐기한다. (SPEC 5.2)
    /// 수신측(S24+)에서 InputPacket 스트림당 하나씩 인스턴스를 유지.
    /// 스레드 안전하지 않음 — 단일 수신 스레드에서만 호출할 것.
    /// </summary>
    public sealed class SequenceValidator
    {
        private uint _highest;
        private bool _hasAny;

        /// <summary>지금까지 수용한 최대 시퀀스.</summary>
        public uint Highest => _highest;

        /// <summary>
        /// 이 시퀀스를 수용해야 하면 true, 오래되었거나 중복이면 false.
        /// wrap-around(uint 오버플로)를 절반-범위 비교로 처리한다.
        /// </summary>
        public bool Accept(uint sequence)
        {
            if (!_hasAny)
            {
                _hasAny = true;
                _highest = sequence;
                return true;
            }

            // (sequence - _highest)가 양의 절반 범위 안이면 "더 새로운" 패킷.
            uint delta = sequence - _highest;
            if (delta != 0 && delta < 0x80000000u)
            {
                _highest = sequence;
                return true;
            }

            return false; // 같거나(중복) 역전된(오래된) 패킷 → 폐기.
        }

        /// <summary>재연결/재페어링 시 상태 초기화.</summary>
        public void Reset()
        {
            _hasAny = false;
            _highest = 0;
        }
    }
}
