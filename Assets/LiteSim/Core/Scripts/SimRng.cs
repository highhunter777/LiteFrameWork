using System;

namespace LiteSim
{
    /// <summary>
    /// Sim 随机数：Xorshift64*（<see cref="State"/> 为 ulong）。随机状态进快照。
    ///
    /// 只用整数位运算 + 乘法换算（无除法精度依赖），跨运行时逐位确定：同种子同序列。
    /// 使用注意：本类型是 <c>struct</c>，<see cref="NextUInt32"/> 等会就地推进 <see cref="State"/>，
    /// 必须保存在字段/数组元素中调用，勿在只读副本上调用。
    /// </summary>
    public struct SimRng
    {
        /// <summary>随机状态（进快照）。</summary>
        public ulong State;

        public SimRng(ulong seed)
        {
            // 状态为 0 时 xorshift 会锁死，替换为非零常量。
            State = seed == 0UL ? 0x9E3779B97F4A7C15UL : seed;
        }

        /// <summary>Xorshift64* 走一步，返回高 32 位。</summary>
        public uint NextUInt32()
        {
            ulong x = State;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            State = x;
            return (uint)((x * 0x2545F4914F6CDD1DUL) >> 32);
        }

        /// <summary>[0,1) 浮点：24 位尾数构造（不引入 double）。</summary>
        public float NextFloat01()
        {
            return (NextUInt32() >> 8) * (1f / 16777216f);
        }

        /// <summary>[minInclusive, maxExclusive) 整数；区间为空时返回 minInclusive。</summary>
        public int NextRange(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            uint range = (uint)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextUInt32() % range);
        }

        /// <summary>[min, max) 浮点。</summary>
        public float NextRange(float min, float max)
        {
            return min + (max - min) * NextFloat01();
        }
    }
}
