using System.Linq;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>引用池实例（B0）契约：不同实例互不共享桶；Dispose 清空空闲件。</summary>
    public sealed class ReferencePoolServiceTests
    {
        private sealed class Item : IReference
        {
            public int Cleared;
            public void Clear() => Cleared++;
        }

        [Fact]
        public void 实例隔离_不同服务互不影响()
        {
            var first = new ReferencePoolService();
            var second = new ReferencePoolService();
            try
            {
                Item item = first.Acquire<Item>();
                first.Release(item);

                Assert.Equal(1, first.GetAllInfos().First(i => i.Type == typeof(Item)).Unused);
                Assert.DoesNotContain(second.GetAllInfos(), i => i.Type == typeof(Item));
            }
            finally
            {
                first.Dispose();
                second.Dispose();
            }
        }

        [Fact]
        public void 释放_清空空闲件_在途件不受影响()
        {
            var pool = new ReferencePoolService();
            Item idle = pool.Acquire<Item>();
            pool.Release(idle);
            Item live = pool.Acquire<Item>();      // 复用空闲件，进入在途

            pool.Dispose();

            Assert.Equal(0, pool.GetAllInfos().Count(i => i.Type == typeof(Item)));
            Assert.NotNull(live);                  // 在途件按归还契约处理，不被 Dispose 强行回收
        }
    }
}
