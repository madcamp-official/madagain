namespace Game.View
{
    /// <summary>
    /// 지연 가리기 층(latency-masking) — 기둥 C.
    ///
    /// <para><b>보간 버퍼는 제거됐다.</b> 예전엔 렌더 시점을 50ms 과거에 두고 샘플 사이를 Lerp해서
    /// 매끄럽게 만들었는데, 그건 <b>지연을 주고 부드러움을 사는</b> 거래였다. 우리 게임엔 손해다:
    /// ① 이동 입력은 <see cref="MoveIntegrator"/>(가속 적분)가 이미 저역통과라 부드럽게 할 게 없고,
    /// ② 이산 이벤트(터치 down/up)는 늦추면 순수한 손해이며,
    /// ③ 지터는 <see cref="LatencyEstimator"/>가 패킷별 age로 잡으므로 버퍼로 흡수할 이유가 없다.
    /// 그래서 렌더 시점 = <b>현재</b>이고, 최신 샘플을 age만큼 앞으로 외삽해 쓴다.</para>
    ///
    /// <para><b>이산/연속 분리</b>: <see cref="TrySample"/>은 연속값(위치·회전·터치좌표) 전용이다.
    /// touchPhase 같은 이산값은 절대 보간·지연시키지 말고 <see cref="TryLatest"/>로 도착 즉시 읽는다.</para>
    ///
    /// <para><b>시계 동기화 불필요</b> — 전부 <b>로컬 도착 시각</b>만 쓴다. 두 폰 절대시각 비교
    /// 금지 원칙(SYB NETWORK_PROTOCOL)과 일치. age도 <see cref="LatencyEstimator"/>가 상대 방식(A′)으로
    /// 구하므로 이 원칙은 그대로 유지된다.</para>
    ///
    /// <para><b>순수 로직</b>(Unity 시간·타입 호출은 값 전달로만) → 합성 입력으로 단위 검증 가능.
    /// 게임/네트워크에 독립. 나중에 SYB 네트워크가 <see cref="Push"/>로 샘플을 채우면 그대로 붙는다.</para>
    /// </summary>
    public sealed class InputSmoother
    {
        struct Stamped { public double t; public ControllerSample s; }

        readonly Stamped[] _buf;
        int _count;
        int _start;   // 가장 오래된 원소의 인덱스(ring)

        /// <summary>외삽 허용 최대 시간(초). 넘으면 최신값 유지(폭주 방지).</summary>
        public double MaxExtrapolation = 0.15;

        public InputSmoother(int capacity = 32)
        {
            int cap = capacity < 4 ? 4 : capacity;
            _buf = new Stamped[cap];
        }

        public int Count => _count;

        Stamped At(int i) => _buf[(_start + i) % _buf.Length];
        Stamped Newest() => At(_count - 1);
        Stamped Oldest() => At(0);

        /// <summary>샘플 적재. <paramref name="arrivalTime"/>=로컬 도착 시각(초). 단조 증가 가정.</summary>
        public void Push(double arrivalTime, in ControllerSample sample)
        {
            if (_count > 0 && arrivalTime <= Newest().t) return;   // 역전/중복 도착 무시
            int idx = (_start + _count) % _buf.Length;
            _buf[idx] = new Stamped { t = arrivalTime, s = sample };
            if (_count < _buf.Length) _count++;
            else _start = (_start + 1) % _buf.Length;              // 가득 차면 가장 오래된 것 덮음
        }

        /// <summary>비운다(재연결·리셋 시).</summary>
        public void Clear() { _count = 0; _start = 0; }

        /// <summary>
        /// <b>이산값 전용</b> — 최신 샘플을 가공 없이 그대로 낸다. 버튼/터치 down·up은 절대
        /// 보간하거나 지연시키지 않는다(늦추면 순수한 손해). 샘플이 없으면 false.
        /// </summary>
        public bool TryLatest(out ControllerSample outState)
        {
            outState = ControllerSample.Identity;
            if (_count == 0) return false;
            outState = Newest().s;
            return true;
        }

        /// <summary>
        /// <b>연속값 전용</b>(위치·회전·터치좌표). 최신 샘플을 <paramref name="age"/>만큼 앞으로
        /// 외삽해 <b>현재</b> 상태를 낸다 — 과거를 그리지 않는다(보간 버퍼 없음).
        ///
        /// <para><paramref name="age"/>는 <see cref="LatencyEstimator"/>가 낸 "최신 샘플이 몇 초 묵었는지".
        /// 0이면 외삽 없이 최신값 그대로 — PC 경로가 이 경우다.</para>
        /// </summary>
        public bool TrySample(double age, out ControllerSample outState)
        {
            outState = ControllerSample.Identity;
            if (_count == 0) return false;

            Stamped n1 = Newest();
            if (_count == 1 || age <= 0 || age > MaxExtrapolation) { outState = n1.s; return true; }

            // 최근 두 샘플의 추세를 age만큼 연장(dead-reckoning).
            Stamped n0 = At(_count - 2);
            double baseSpan = n1.t - n0.t;
            float k = baseSpan > 1e-9 ? (float)(age / baseSpan) : 0f;
            outState = ControllerSample.Extrapolate(n0.s, n1.s, k);
            return true;
        }
    }
}
