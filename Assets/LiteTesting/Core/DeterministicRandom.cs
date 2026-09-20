using System;

namespace LiteTesting
{
    /// <summary>
    /// SplitMix64-based random source with a sequence that is stable across runtimes.
    /// It is intended for test data, not cryptography.
    /// </summary>
    public sealed class DeterministicRandom
    {
        private ulong _state;

        public DeterministicRandom(ulong seed)
        {
            _state = seed;
        }

        public ulong NextUInt64()
        {
            ulong value = (_state += 0x9E3779B97F4A7C15UL);
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (minInclusive >= maxExclusive)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive must be greater than minInclusive.");
            }

            ulong range = (ulong)((long)maxExclusive - minInclusive);
            ulong limit = ulong.MaxValue - (ulong.MaxValue % range);
            ulong value;
            do
            {
                value = NextUInt64();
            }
            while (value >= limit);

            return (int)(minInclusive + (long)(value % range));
        }

        public double NextDouble()
        {
            return (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);
        }

        public void NextBytes(byte[] buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            int index = 0;
            while (index < buffer.Length)
            {
                ulong value = NextUInt64();
                for (int i = 0; i < 8 && index < buffer.Length; i++, index++)
                {
                    buffer[index] = (byte)value;
                    value >>= 8;
                }
            }
        }
    }
}
