#if UNITY_EDITOR || DEVELOPMENT_BUILD || LITEFRAMEWORK_DEBUG
namespace LiteNet.Diagnostics
{
    /// <summary>确定性随机源：固定种子下决策序列可复现（上下行各持一实例）。</summary>
    public sealed class WeakNetRandom
    {
        private uint _state;

        public WeakNetRandom(uint seed)
        {
            Seed = seed == 0 ? 0x9E3779B9u : seed;
            _state = Seed;
        }

        public uint Seed { get; }

        public uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        public int NextRange(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return minInclusive + (int)(NextUInt() % (uint)(maxExclusive - minInclusive));
        }
    }
}
#endif
