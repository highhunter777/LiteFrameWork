using System;
using System.Linq;
using Xunit;

namespace LiteFramework.Tests
{
    [Collection("CoreStatic")]   // ReferencePool 池字典是全局静态；各类用独立嵌套类型避免桶互踩
    public sealed class ReferencePoolTests
    {
        private sealed class RefA : IReference { public void Clear() { } }
        private sealed class RefB : IReference { public void Clear() { } }
        private sealed class RefC : IReference { public void Clear() { } }
        private sealed class RefD : IReference { public void Clear() { } }
        private sealed class RefE : IReference { public void Clear() { } }

        [Fact]
        public void ReferencePool_重复归还_抛异常()
        {
            var a = ReferencePool.Acquire<RefA>();
            ReferencePool.Release(a);
            var ex = Assert.Throws<InvalidOperationException>(() => ReferencePool.Release(a));
            Assert.Contains("重复归还", ex.Message);
        }

        [Fact]
        public void ReferencePool_未Acquire过的实例归还_抛异常()
        {
            var foreign = new RefA();   // 不是 Acquire 所得
            Assert.Throws<InvalidOperationException>(() => ReferencePool.Release(foreign));
        }

        [Fact]
        public void ReferencePool_往返_Release再Acquire是同一实例()
        {
            var a = ReferencePool.Acquire<RefB>();
            ReferencePool.Release(a);
            Assert.Same(a, ReferencePool.Acquire<RefB>());
        }

        [Fact]
        public void ReferencePool_超上限_丢弃并保留上限数量()
        {
            var taken = new System.Collections.Generic.List<RefC>();
            ReferencePool.SetMaxSize<RefC>(2);
            for (int i = 0; i < 5; i++) taken.Add(ReferencePool.Acquire<RefC>());   // Release 只收 Acquire 所得
            foreach (var o in taken) ReferencePool.Release(o);
            var info = ReferencePool.GetAllInfos().First(i => i.Type == typeof(RefC));
            Assert.Equal(2, info.Unused);        // 池内只留上限个
            Assert.Equal(3, info.DroppedCount);  // 其余丢弃
        }

        [Fact]
        public void ReferencePool_预热超上限_自动上调且全部入池()
        {
            ReferencePool.Add<RefD>(100);
            var info = ReferencePool.GetAllInfos().First(i => i.Type == typeof(RefD));
            Assert.True(info.MaxSize >= 100);
            Assert.Equal(100, info.Unused);
        }

        [Fact]
        public void ReferencePool_裁剪_池内减到指定数量()
        {
            ReferencePool.Add<RefE>(10);
            ReferencePool.Remove<RefE>(4);
            var info = ReferencePool.GetAllInfos().First(i => i.Type == typeof(RefE));
            Assert.Equal(6, info.Unused);   // 独立类型，无跨用例共享
        }

        // ---- 批4（《对象池专项设计》§7）：per-type 淘汰 / Clear / 统计无条件化 ----

        private sealed class RefF : IReference { public void Clear() { } }
        private sealed class RefG : IReference { public void Clear() { } }
        private sealed class RefH : IReference { public void Clear() { } }
        private sealed class RefI : IReference { public void Clear() { } }
        private sealed class RefJ : IReference { public void Clear() { } }
        private sealed class RefK : IReference { public void Clear() { } }

        private sealed class RefL : IReference
        {
            public bool Broken;
            public void Clear() { if (Broken) throw new InvalidOperationException("Clear 故障注入"); }
        }

        [Fact]
        public void ReferencePool_EvictOldest_满池淘汰队首最旧_新件入池()
        {
            ReferencePool.SetMaxSize<RefF>(2);
            ReferencePool.SetEviction<RefF>(PoolEviction.EvictOldest);
            var a = ReferencePool.Acquire<RefF>();
            var b = ReferencePool.Acquire<RefF>();
            var c = ReferencePool.Acquire<RefF>();
            ReferencePool.Release(a);                              // 队序：a, b
            ReferencePool.Release(b);
            ReferencePool.Release(c);                              // 满：淘汰 a，c 入池

            var info = ReferencePool.GetAllInfos().First(i => i.Type == typeof(RefF));
            Assert.Equal(2, info.Unused);
            Assert.Equal(1, info.EvictedCount);                    // EvictOldest 路径计 Evicted
            Assert.Equal(0, info.DroppedCount);                    // 两计数不混用（§7.2 同内核 §3.1 口径）
            var next = ReferencePool.Acquire<RefF>();             // 队首 = b（a 已被淘汰）
            Assert.Same(b, next);
        }

        [Fact]
        public void ReferencePool_淘汰策略per_type独立_未配置类型保持DropNewest()
        {
            ReferencePool.SetMaxSize<RefG>(1);
            // RefG 未配置 SetEviction：默认 DropNewest（现状口径）
            var a = ReferencePool.Acquire<RefG>();
            var b = ReferencePool.Acquire<RefG>();
            ReferencePool.Release(a);
            ReferencePool.Release(b);                              // 满：DropNewest 丢弃 b

            var info = ReferencePool.GetAllInfos().First(i => i.Type == typeof(RefG));
            Assert.Equal(1, info.Unused);
            Assert.Equal(1, info.DroppedCount);                    // 未配置类型走默认丢弃（不跨类型串配置）
            Assert.Equal(0, info.EvictedCount);
        }

        [Fact]
        public void ReferencePool_Clear_清空指定类型_在途件不受影响()
        {
            ReferencePool.Add<RefH>(5);
            var inFlight = ReferencePool.Acquire<RefH>();          // 在途件（Acquire 所得）
            ReferencePool.Clear<RefH>();

            Assert.Equal(0, ReferencePool.GetAllInfos().First(i => i.Type == typeof(RefH)).Unused);   // 空闲全弃（不走第二次 Clear）
            ReferencePool.Release(inFlight);                       // 在途件按契约归还——不抛（非重复归还）
            Assert.Equal(1, ReferencePool.GetAllInfos().First(i => i.Type == typeof(RefH)).Unused);   // 归还正常入池
            ReferencePool.Clear<RefH>();                           // 幂等复清
            Assert.Equal(0, ReferencePool.GetAllInfos().First(i => i.Type == typeof(RefH)).Unused);
        }

        [Fact]
        public void ReferencePool_ClearAll_全部类型空闲件清空()
        {
            ReferencePool.Add<RefI>(3);
            ReferencePool.Add<RefK>(2);                             // 同用例自建第二类型——不依赖其他用例是否已建桶
            ReferencePool.ClearAll();

            Assert.Equal(0, ReferencePool.GetAllInfos().First(i => i.Type == typeof(RefI)).Unused);
            Assert.Equal(0, ReferencePool.GetAllInfos().First(i => i.Type == typeof(RefK)).Unused);
        }

        [Fact]
        public void ReferencePool_统计_命中与创建口径()
        {
            var a = ReferencePool.Acquire<RefJ>();                 // Miss（空池转 new）
            ReferencePool.Release(a);
            var b = ReferencePool.Acquire<RefJ>();                 // Hit（池中直取）

            var info = ReferencePool.GetAllInfos().First(i => i.Type == typeof(RefJ));
            Assert.Equal(2, info.AcquireCount);
            Assert.Equal(1, info.CreatedCount);                    // 一次空池转 new
            Assert.Equal(1, info.PoolHits);                        // 一次命中；Misses = Acquired − PoolHits = 1
            Assert.Same(a, b);                                     // 归还即移交——重新取到同一实例
        }

        [Fact]
        public void ReferencePool_Clear抛异常_丢弃不入池()
        {
            var a = ReferencePool.Acquire<RefL>();
            a.Broken = true;
            ReferencePool.Release(a);                              // Clear 是对象作者代码：炸了丢弃，不入池污染

            Assert.Equal(0, ReferencePool.GetAllInfos().First(i => i.Type == typeof(RefL)).Unused);
        }

        // ---- §7.4 空闲过期收缩（设计续命条目：单调系统钟入池时刻，调用方驱动）----

        private sealed class RefM : IReference { public void Clear() { } }
        private sealed class RefN : IReference { public void Clear() { } }

        [Fact]
        public void ReferencePool_TrimExpired_指定类型_超龄件裁剪_年轻件保留()
        {
            var a = ReferencePool.Acquire<RefM>();
            ReferencePool.Release(a);
            Assert.Equal(0, ReferencePool.TrimExpired<RefM>(60_000));    // 刚入池：远未超龄，不裁
            Assert.Equal(1, ReferencePool.GetAllInfos().First(i => i.Type == typeof(RefM)).Unused);

            var b = ReferencePool.Acquire<RefM>();
            ReferencePool.Release(b);
            Assert.Equal(1, ReferencePool.TrimExpired<RefM>(-1));       // 负阈值 = 任意空闲时长都超龄（严格大于 -1 恒真，同毫秒确定性）
            Assert.Equal(0, ReferencePool.GetAllInfos().First(i => i.Type == typeof(RefM)).Unused);
        }

        [Fact]
        public void ReferencePool_TrimExpired_跨全部桶_裁剪数汇总_未注册类型零()
        {
            ReferencePool.ClearAll();                                // 清场：隔离此前用例在各桶的遗留空闲件（静态全局态）
            var m = ReferencePool.Acquire<RefM>();
            ReferencePool.Release(m);
            var n = ReferencePool.Acquire<RefN>();
            ReferencePool.Release(n);

            Assert.Equal(2, ReferencePool.TrimExpired(-1));          // 跨全部桶：两个类型各裁一件（负阈值同毫秒确定性）
            Assert.Equal(0, ReferencePool.GetAllInfos().First(i => i.Type == typeof(RefM)).Unused);
            Assert.Equal(0, ReferencePool.GetAllInfos().First(i => i.Type == typeof(RefN)).Unused);
            Assert.Equal(0, ReferencePool.TrimExpired<RefL>(-1));    // 未注册类型零裁剪（不建桶）
        }
    }
}
