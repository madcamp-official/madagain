using Game.Sim;

namespace Game.Prediction
{
    /// <summary>
    /// 고정 크기 SimWorld 버퍼 풀. Beam Search는 후보마다 `new SimWorld`/`new EnemySim[]`를
    /// 만들지 않고 이 풀에서 빌려 쓴다(OPTIMIZATION.md 10장). enemies[] 배열은 생성 시점에
    /// 한 번만 할당된다. 풀 자체를 여러 예지 발동에 걸쳐 재사용하는 것은 다음 단계 최적화.
    /// </summary>
    public sealed class WorldBufferPool
    {
        readonly SimWorld[] buffers;
        readonly bool[] rented;

        public WorldBufferPool(int capacity)
        {
            buffers = new SimWorld[capacity];
            rented = new bool[capacity];
            for (int i = 0; i < capacity; i++)
                buffers[i] = SimWorld.Create();
        }

        public int Capacity => buffers.Length;

        /// <summary>빈 슬롯 인덱스를 빌린다. 풀이 고갈되면 -1(호출자가 용량을 넉넉히 잡아야 함).</summary>
        public int Rent()
        {
            for (int i = 0; i < rented.Length; i++)
            {
                if (rented[i]) continue;
                rented[i] = true;
                return i;
            }
            return -1;
        }

        public void Return(int index)
        {
            if (index < 0) return;
            rented[index] = false;
        }

        public ref SimWorld Get(int index) => ref buffers[index];

        public void CopyInto(int index, in SimWorld source) => Snapshot.CopyTo(in source, ref buffers[index]);
    }
}
