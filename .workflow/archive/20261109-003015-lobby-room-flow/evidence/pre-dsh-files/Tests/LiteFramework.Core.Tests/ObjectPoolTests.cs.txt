using System;
using System.Collections.Generic;
using Xunit;

namespace LiteFramework.Tests
{
    [Collection("CoreStatic")]   // onRelease 异常路径写 Log（全局静态）
    public sealed class ObjectPoolTests
    {
        private sealed class Pooled
        {
        }

        private static ObjectPool<Pooled> NewPool(int maxIdle = 64, string name = null,
            Action<Pooled> onGet = null, Action<Pooled> onRelease = null, Action<Pooled> onDestroy = null)
            => new ObjectPool<Pooled>(() => new Pooled(), onGet, onRelease, onDestroy, maxIdle, name,
                diagnoseDoubleRelease: true);   // 测试面 = 开发形态（发布形态由装配点按 DevBuildFacts 关闭）

        [Fact]
        public void ObjectPool_空池Acquire_走create并计数()
        {
            var pool = NewPool();
            var a = pool.Acquire();
            Assert.NotNull(a);
            Assert.Equal(1, pool.CreatedCount);
            Assert.Equal(1, pool.AcquiredCount);
            Assert.Equal(1, pool.UsingCount);
            Assert.Equal(0, pool.UnusedCount);
        }

        [Fact]
        public void ObjectPool_归还后Acquire_复用同实例不新建()
        {
            var pool = NewPool();
            var a = pool.Acquire();
            pool.Release(a);
            Assert.Equal(0, pool.UsingCount);            // 归还瞬间在用为 0
            var b = pool.Acquire();
            Assert.Same(a, b);
            Assert.Equal(1, pool.CreatedCount);          // 没有第二次 new
            Assert.Equal(1, pool.UsingCount);
        }

        [Fact]
        public void ObjectPool_三回调按契约触发()
        {
            int gets = 0, releases = 0, destroys = 0;
            var pool = NewPool(onGet: _ => gets++, onRelease: _ => releases++, onDestroy: _ => destroys++);

            var a = pool.Acquire();                       // onGet ×1（create 路径也触发）
            pool.Release(a);                              // onRelease ×1，空闲 1
            pool.Trim(0);                                 // onDestroy ×1，空闲 0
            var b = pool.Acquire();                       // onGet ×2；空闲已空 → 新建

            Assert.Equal(2, gets);
            Assert.Equal(1, releases);
            Assert.Equal(1, destroys);
            Assert.NotSame(a, b);                         // 被裁剪销毁的对象不会复活
            Assert.Equal(2, pool.CreatedCount);
        }

        [Fact]
        public void ObjectPool_池满_归还即销毁_DroppedCount增加()
        {
            var destroyed = new List<Pooled>();
            var pool = NewPool(maxIdle: 1, onDestroy: o => destroyed.Add(o));

            var a = pool.Acquire();
            var b = pool.Acquire();                       // 空闲为 0，第二个必须新建
            pool.Release(a);                              // 入池（1/1）
            pool.Release(b);                              // 已满 → 销毁，不入池

            Assert.Single(destroyed);
            Assert.Same(b, destroyed[0]);
            Assert.Equal(1, pool.DroppedCount);
            Assert.Equal(1, pool.UnusedCount);
        }

        [Fact]
        public void ObjectPool_重复归还_Debug下抛()
        {
            var pool = NewPool();
            var a = pool.Acquire();
            pool.Release(a);
            Assert.Throws<InvalidOperationException>(() => pool.Release(a));
        }

        [Fact]
        public void ObjectPool_未Acquire直接归还_Debug下抛()
        {
            var pool = NewPool();
            Assert.Throws<InvalidOperationException>(() => pool.Release(new Pooled()));
        }

        [Fact]
        public void ObjectPool_Release_null_抛()
        {
            var pool = NewPool();
            Assert.Throws<ArgumentNullException>(() => pool.Release(null));
        }

        [Fact]
        public void ObjectPool_归还在用后_再Release_unknown_抛()
        {
            var pool = NewPool();
            var a = pool.Acquire();
            pool.Release(a);
            Assert.Throws<InvalidOperationException>(() => pool.Release(a));   // 已不在 Using 集
        }

        [Fact]
        public void ObjectPool_Preload_预支且超上限自动上调()
        {
            var pool = NewPool(maxIdle: 2);
            pool.Preload(5);                              // 预热即显式声明容量
            Assert.Equal(5, pool.UnusedCount);
            Assert.Equal(5, pool.MaxIdle);
            Assert.Equal(5, pool.CreatedCount);
            Assert.Equal(5, pool.PeakUnused);

            var a = pool.Acquire();                       // 预热后取用不新建
            Assert.Equal(5, pool.CreatedCount);
            Assert.Equal(4, pool.UnusedCount);
            pool.Release(a);
        }

        [Fact]
        public void ObjectPool_Trim_裁剪走onDestroy_在用不受影响()
        {
            var destroyed = new List<Pooled>();
            var pool = NewPool(onDestroy: o => destroyed.Add(o));

            var inUse = pool.Acquire();
            var i1 = pool.Acquire();
            var i2 = pool.Acquire();
            pool.Release(i1);
            pool.Release(i2);                             // 空闲 2
            pool.Trim(1);

            Assert.Single(destroyed);
            Assert.Equal(1, pool.UnusedCount);
            Assert.NotNull(inUse);                        // 在用对象不受影响
        }

        [Fact]
        public void ObjectPool_Clear_清空空闲_在用对象按契约归还()
        {
            var pool = NewPool(maxIdle: 4);
            var inUse = pool.Acquire();
            var idle = pool.Acquire();
            pool.Release(idle);

            pool.Clear();
            Assert.Equal(0, pool.UnusedCount);

            pool.Release(inUse);                          // Clear 后归还正常入池
            Assert.Equal(1, pool.UnusedCount);
        }

        [Fact]
        public void ObjectPool_PeakUnused_记录历史峰值()
        {
            var pool = NewPool(maxIdle: 8);
            var a = pool.Acquire();
            var b = pool.Acquire();
            var c = pool.Acquire();
            pool.Release(a);
            pool.Release(b);
            pool.Release(c);                              // 峰值 3
            var d = pool.Acquire();                       // 空闲回落 2——峰值不随取用下降

            Assert.Equal(3, pool.PeakUnused);
            Assert.Equal(2, pool.UnusedCount);
        }

        [Fact]
        public void ObjectPool_onRelease抛异常_销毁不入池污染并Error可见()
        {
            var destroyed = new List<Pooled>();
            var pool = NewPool(onRelease: _ => throw new InvalidOperationException("回调炸了"),
                               onDestroy: o => destroyed.Add(o));

            var a = pool.Acquire();
            pool.Release(a);                              // onRelease 抛 → 销毁，不入池

            Assert.Single(destroyed);
            Assert.Equal(0, pool.UnusedCount);
            Assert.Equal(0, pool.DroppedCount);           // 口径同 ReferencePool：Dropped 只记超限丢弃，异常销毁不计数

            var last = Log.Recent[Log.Recent.Count - 1];
            Assert.Equal(LogLevel.Error, last.Level);
        }

        [Fact]
        public void ObjectPool_onGet抛异常_直接传播()
        {
            var pool = NewPool(onGet: _ => throw new InvalidOperationException("激活失败"));
            Assert.Throws<InvalidOperationException>(() => pool.Acquire());
        }

        [Fact]
        public void ObjectPool_IModuleStats_Snapshot输出口径字段()
        {
            var pool = NewPool(maxIdle: 3);
            var a = pool.Acquire();
            pool.Release(a);

            var into = new Dictionary<string, string>();
            ((IModuleStats)pool).Snapshot(into);

            Assert.Equal("ObjectPool.Pooled", ((IModuleStats)pool).StatsName);
            Assert.Equal("1", into["Created"]);
            Assert.Equal("1", into["Acquired"]);
            Assert.Equal("1", into["Released"]);
            Assert.Equal("1", into["Unused"]);
            Assert.Equal("0", into["Using"]);
            Assert.Equal("1", into["PeakUnused"]);
            Assert.Equal("0", into["Dropped"]);
            Assert.Equal("3", into["MaxIdle"]);
        }

        [Fact]
        public void ObjectPool_非法构造参数_抛()
        {
            Assert.Throws<ArgumentNullException>(() => new ObjectPool<Pooled>(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => NewPool(maxIdle: -1));
        }

        // ---- IPoolable（可选自生命周期接口，与回调共存）----

        private sealed class SelfAware : IPoolable
        {
            public int Spawned;
            public int Despawned;
            public Action OnSpawnBody;
            public Action OnDespawnBody;
            public void OnSpawn() { Spawned++; OnSpawnBody?.Invoke(); }
            public void OnDespawn() { Despawned++; OnDespawnBody?.Invoke(); }
        }

        [Fact]
        public void ObjectPool_IPoolable_与回调按顺序触发()
        {
            var order = new List<string>();
            var pool = new ObjectPool<SelfAware>(
                () => new SelfAware(),
                onGet: _ => order.Add("onGet"),
                onRelease: _ => order.Add("onRelease"));

            var a = pool.Acquire();
            Assert.Equal(1, a.Spawned);
            pool.Release(a);
            Assert.Equal(1, a.Despawned);

            Assert.Equal(new[] { "onGet", "onRelease" }, order);   // 池策略在两侧各触发一次
            Assert.Equal(1, pool.UnusedCount);
        }

        [Fact]
        public void ObjectPool_IPoolable_OnDespawn先于onRelease_对象先清自己池再挪动()
        {
            var order = new List<string>();
            var pool = new ObjectPool<SelfAware>(
                () => new SelfAware(),
                onRelease: _ => order.Add("onRelease"));
            var a = pool.Acquire();
            a.OnDespawnBody = () => order.Add("OnDespawn");

            pool.Release(a);
            Assert.Equal(new[] { "OnDespawn", "onRelease" }, order);
        }

        [Fact]
        public void ObjectPool_IPoolable_OnDespawn抛异常_销毁不入池()
        {
            var pool = new ObjectPool<SelfAware>(() => new SelfAware());
            var a = pool.Acquire();
            a.OnDespawnBody = () => throw new InvalidOperationException("自清理炸了");

            pool.Release(a);

            Assert.Equal(0, pool.UnusedCount);
            Assert.Equal(0, pool.DroppedCount);           // 口径：异常销毁不记超限丢弃
            var last = Log.Recent[Log.Recent.Count - 1];
            Assert.Equal(LogLevel.Error, last.Level);
        }

        [Fact]
        public void ObjectPool_IPoolable_OnSpawn抛异常_传播且回滚账目()
        {
            int spawns = 0;
            var pool = new ObjectPool<SelfAware>(() =>
            {
                var o = new SelfAware();
                o.OnSpawnBody = () => { spawns++; if (spawns == 1) throw new InvalidOperationException("自初始化炸了"); };
                return o;
            });

            Assert.Throws<InvalidOperationException>(() => pool.Acquire());   // 第一次：炸、销毁、账目回滚
            Assert.Equal(0, pool.UsingCount);                                 // 不留幽灵占用
            Assert.Equal(0, pool.UnusedCount);

            var retry = pool.Acquire();                   // 池仍可用（新实例，第二次 OnSpawn 正常）
            Assert.Equal(2, pool.CreatedCount);
            Assert.Equal(1, retry.Spawned);
            Assert.NotSame(retry, null);
        }
    }
}
