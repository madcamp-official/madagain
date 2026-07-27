namespace Game.View
{
    /// <summary>
    /// 패킷별 추가 지연(age) 추정 — <b>시계 동기화 없이</b>. (설계 A′)
    ///
    /// <para><c>D = 로컬수신시각 − 원격송신시각</c> 에는 (진짜 편도 지연) + (두 기기 시계 차이)가 섞여 있다.
    /// 시계 차이는 <b>상수</b>이므로, 최근 창에서의 <c>min(D)</c>를 기준선으로 빼면 남는 값이
    /// 그 패킷이 <b>가장 빨리 온 패킷 대비 얼마나 더 늦었는가</b> = 순수 지터가 된다.</para>
    ///
    /// <para><c>age = D − min(D)</c>. 가장 빠른 패킷의 age는 0. 두 폰 절대시각을 비교하지 않으므로
    /// SYB NETWORK_PROTOCOL의 "절대시각 비교 금지" 원칙을 그대로 지킨다.</para>
    ///
    /// <para><b>못 얻는 것</b>: 기저 편도 지연(모든 패킷이 공통으로 깔고 있는 바닥값). 시계 차이와
    /// 구분할 방법이 없다. 다만 이건 상수라 조작감에 거의 영향이 없고, 필요하면 이동 쪽
    /// baseline 튜닝값 하나로 더하면 된다. 조작감을 망치는 건 지터 쪽이고 그건 여기서 잡힌다.</para>
    ///
    /// <para><b>단조 시계 필수</b> — 벽시계(현재 시각)를 쓰면 OS의 NTP 보정이 중간에 점프시켜서
    /// 기준선이 통째로 망가진다. 안드로이드는 elapsedRealtime, 유니티는 realtimeSinceStartup.</para>
    ///
    /// <para>순수 로직(유니티 타입·시간 호출 없음) → 합성 입력으로 단위 검증 가능.</para>
    /// </summary>
    public sealed class LatencyEstimator
    {
        /// <summary>기준선(min D)을 구하는 롤링 창(초). 길면 시계 드리프트가 섞이고 짧으면 기준선이 흔들린다.</summary>
        public double WindowSeconds = 8.0;

        /// <summary>age 상한(초). 넘으면 클램프 — 나쁜 기준선 하나가 캐릭터를 날려버리는 걸 막는다.</summary>
        public double MaxAge = 0.25;

        struct Entry { public double t; public double d; }

        // 단조 증가 덱(monotonic deque): head가 항상 창 안의 최솟값이라 O(1)로 min을 얻는다.
        readonly Entry[] _dq;
        int _head, _count;

        public LatencyEstimator(int capacity = 1024)
        {
            _dq = new Entry[capacity < 8 ? 8 : capacity];
        }

        public bool HasBaseline => _count > 0;

        /// <summary>현재 기준선 min(D). 절대값 자체는 시계 차이가 섞여 무의미 — 진단용.</summary>
        public double Baseline => _count > 0 ? _dq[_head].d : 0.0;

        /// <summary>
        /// 패킷 하나를 관측하고 그 패킷의 age(초)를 낸다.
        /// 두 시각 모두 <b>각자의 단조 시계</b> 기준이면 된다(서로 맞출 필요 없음).
        /// </summary>
        public double Observe(double localRecvTime, double remoteSendTime)
        {
            double d = localRecvTime - remoteSendTime;

            // 꼬리에서 d가 새 값 이상인 항목 제거 — 앞으로 절대 최솟값이 될 수 없다.
            while (_count > 0)
            {
                int tail = (_head + _count - 1) % _dq.Length;
                if (_dq[tail].d >= d) _count--;
                else break;
            }

            if (_count == _dq.Length) { _head = (_head + 1) % _dq.Length; _count--; }   // 넘침 방어
            _dq[(_head + _count) % _dq.Length] = new Entry { t = localRecvTime, d = d };
            _count++;

            // 창 밖으로 나간 항목 만료(최소 1개는 남긴다).
            double cutoff = localRecvTime - WindowSeconds;
            while (_count > 1 && _dq[_head].t < cutoff) { _head = (_head + 1) % _dq.Length; _count--; }

            double age = d - _dq[_head].d;
            if (age < 0.0) age = 0.0;
            if (age > MaxAge) age = MaxAge;
            return age;
        }

        /// <summary>재연결·리셋 — 기준선을 버리고 다시 수렴시킨다.</summary>
        public void Reset() { _head = 0; _count = 0; }
    }
}
