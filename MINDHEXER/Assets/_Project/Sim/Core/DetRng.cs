namespace Game.Sim
{
    /// <summary>
    /// 결정론적 난수. 상태(uint)를 월드에 넣고 굴린다 → 복제하면 시드도 복제되어
    /// 예측이 미래를 똑같이 재현한다. UnityEngine.Random(전역 상태)은 금지.
    /// xorshift32.
    /// </summary>
    public static class DetRng
    {
        /// <summary>다음 난수(uint). state를 갱신한다.</summary>
        public static uint Next(ref uint state)
        {
            uint x = state;
            if (x == 0) x = 0x9E3779B9u;   // 0 방지
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            state = x;
            return x;
        }

        /// <summary>0~1 실수.</summary>
        public static float Next01(ref uint state) => (Next(ref state) & 0xFFFFFF) / (float)0x1000000;

        /// <summary>min~max-1 정수.</summary>
        public static int Range(ref uint state, int min, int max)
            => max <= min ? min : min + (int)(Next(ref state) % (uint)(max - min));
    }
}
