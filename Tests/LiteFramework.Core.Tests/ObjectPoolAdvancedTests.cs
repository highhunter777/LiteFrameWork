using System;
using System.Collections.Generic;
using LiteFramework;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// ObjectPool 高级化直测（《对象池专项设计》§3/§6）——与基线套件 ObjectPoolTests（初始提交随库，
    /// §2 继承基线 20 例）分文件并存：
    /// - §3 高级化：PoolEviction（DropNewest 默认与 EvictOldest 队首淘汰）、TrimExpired 按龄收缩
    ///   （消费方驱动零时钟）、validateOnAcquire 借出验证（先于 onGet/OnSpawn）、统计追加（PoolHits/Evicted/Invalidated）；
    /// - §3.5 兼容红线：默认参数下与基线行为逐位一致（DropNewest/不收缩/无验证）。
    /// [Collection("CoreStatic")]：异常回滚路径写全局 Log（ErrorCount/Recent），与断言日志面的用例串行。
    /// </summary>
    [Collection("CoreStatic")]
    public sealed class ObjectPoolAdvancedTests
    {
        /// <summary>可池化对象替身：记录生命周期回调次数。</summary>
        private sealed class Item : IPoolable
        {
            public static int NextId;
            public int Id;
            public int SpawnCount;
            public int DespawnCount;
            public bool Broken;              // OnSpawn/OnDespawn 故障注入（生命周期回调抛异常）
            public bool ExternallyBroken;    // 验证失效标记（借出后"被外部破坏"——不影响生命周期回调）
            public bool ValidationThrows;    // 验证回调故障注入
            public void OnSpawn()
            {
                SpawnCount++;
                if (Broken) throw new InvalidOperationException("OnSpawn 故障注入");
            }
            public void OnDespawn()
            {
                DespawnCount++;
                if (Broken) throw new InvalidOperationException("OnDespawn 故障注入");
            }
        }

        private static Item NewItem() => new Item { Id = Item.NextId++ };

        // ---- §2 基线 ----

        [Fact]
        public void 默认参数_满池归还销毁新归还件_DropNewest为默认策略()
        {
            var destroyed = new List<Item>();
            var pool = new ObjectPool<Item>(NewItem, onDestroy: destroyed.Add, maxIdle: 1);

            var a = pool.Acquire();
            var b = pool.Acquire();
            pool.Release(a);
            pool.Release(b);                                   // 满池：DropNewest（默认）销毁 b

            Assert.Equal(1, pool.UnusedCount);
            Assert.Equal(1L, pool.DroppedCount);
            Assert.Single(destroyed);
            Assert.True(ReferenceEquals(b, destroyed[0]), "默认策略销毁的是新归还件（不是池内他人）");
            Assert.Equal(0L, pool.EvictedCount);               // DropNewest 路径不计 Evicted（两计数不混用）
        }

        [Fact]
        public void 回调顺序_Acquire空池后为create_onGet_OnSpawn()
        {
            var order = new List<string>();
            var pool = new ObjectPool<OrderProbe>(create: () => new OrderProbe(order, "create"),
                onGet: p => p.Mark("onGet"));
            pool.Acquire();
            Assert.Equal(new[] { "create", "onGet", "OnSpawn" }, order);
        }

        private sealed class OrderProbe : IPoolable
        {
            private readonly List<string> _order;
            public OrderProbe(List<string> order, string tag) { _order = order; _order.Add(tag); }
            public void Mark(string tag) => _order.Add(tag);
            public void OnSpawn() => _order.Add("OnSpawn");
            public void OnDespawn() => _order.Add("OnDespawn");
        }

        [Fact]
        public void 回调顺序_Release为OnDespawn_onRelease_入池()
        {
            var order = new List<string>();
            var pool = new ObjectPool<OrderProbe>(
                create: () => new OrderProbe(order, "create"),
                onRelease: p => p.Mark("onRelease"));
            var probe = pool.Acquire();
            order.Clear();
            pool.Release(probe);

            Assert.Equal(new[] { "OnDespawn", "onRelease" }, order);
            Assert.Equal(1, pool.UnusedCount);                 // onRelease 后入池
        }

        [Fact]
        public void OnSpawn抛异常_销毁并回滚账目_异常传播()
        {
            var destroyed = new List<Item>();
            bool inject = false;
            var pool = new ObjectPool<Item>(
                create: () => { var it = NewItem(); it.Broken = inject; return it; },
                onDestroy: destroyed.Add, maxIdle: 4);
            var a = pool.Acquire();                    // 正常件在用
            long acquiredBefore = pool.AcquiredCount;
            long createdBefore = pool.CreatedCount;

            inject = true;
            Assert.Throws<InvalidOperationException>(() => pool.Acquire());
            Assert.True(pool.AcquiredCount == acquiredBefore, $"炸掉的 Acquire 回滚不计账（实际 {pool.AcquiredCount}）");
            Assert.True(pool.CreatedCount == createdBefore + 1, "故障件确实被 create 过一次");
            Assert.Single(destroyed);                          // OnSpawn 炸掉的件被销毁（不留幽灵占用）
            Assert.Equal(1, pool.UsingCount);                   // 在用件 a 不受影响
        }

        [Fact]
        public void OnDespawn抛异常_销毁不入池()
        {
            var destroyed = new List<Item>();
            var pool = new ObjectPool<Item>(NewItem, onDestroy: destroyed.Add, maxIdle: 4);
            var a = pool.Acquire();
            a.Broken = true;
            pool.Release(a);

            Assert.True(pool.UnusedCount == 0, "故障件不入池污染");
            Assert.Single(destroyed);
            Assert.Equal(1L, pool.ReleasedCount);
        }

        [Fact]
        public void 重复归还_Debug三宏下抛()
        {
            var pool = new ObjectPool<Item>(NewItem);
            var a = pool.Acquire();
            pool.Release(a);
            Assert.Throws<InvalidOperationException>(() => pool.Release(a));
        }

        [Fact]
        public void Preload_预热并自动上调上限()
        {
            var pool = new ObjectPool<Item>(NewItem, maxIdle: 4);
            pool.Preload(10);

            Assert.True(pool.MaxIdle == 10, "预热即显式声明容量");
            Assert.Equal(10, pool.UnusedCount);
            Assert.Equal(10L, pool.CreatedCount);
        }

        [Fact]
        public void Trim_只销毁空闲件_不二次OnDespawn_在用不受影响()
        {
            var pool = new ObjectPool<Item>(NewItem, maxIdle: 8);
            pool.Preload(3);
            var inUse = pool.Acquire();                       // 取走一件（该件已 OnSpawn 一次）

            pool.Trim(1);                                     // 裁掉两件空闲（从未 OnSpawn 过——Preload 不走生命周期）
            Assert.Equal(1, pool.UnusedCount);
            Assert.Equal(1, pool.UsingCount);                 // 在用对象不受裁剪影响
            Assert.Equal(0, inUse.DespawnCount);               // 裁剪不触碰在用件
            pool.Release(inUse);
            int despawnAfterRelease = inUse.DespawnCount;      // 归还侧 OnDespawn 恰一次
            pool.Trim(0);                                     // 裁掉已归还件——不得二次 OnDespawn
            Assert.Equal(0, pool.UnusedCount);
            Assert.True(inUse.DespawnCount == despawnAfterRelease, "Trim 只走 onDestroy，不二次 OnDespawn");
        }

        // ---- §3.1 淘汰策略 ----

        [Fact]
        public void EvictOldest_满池归还淘汰队首最旧_新件入池()
        {
            var destroyed = new List<Item>();
            var pool = new ObjectPool<Item>(NewItem, onDestroy: destroyed.Add, maxIdle: 2,
                eviction: PoolEviction.EvictOldest);
            var a = pool.Acquire();
            var b = pool.Acquire();
            var c = pool.Acquire();
            pool.Release(a);                                  // 队序：a, b
            pool.Release(b);
            pool.Release(c);                                  // 满：淘汰队首 a，c 入池

            Assert.Equal(2, pool.UnusedCount);
            Assert.Equal(1L, pool.EvictedCount);
            Assert.Equal(0L, pool.DroppedCount);               // EvictOldest 路径不计 Dropped
            Assert.Single(destroyed);
            Assert.True(ReferenceEquals(a, destroyed[0]), "被淘汰的是最旧空闲件（不是新归还件）");

            var next = pool.Acquire();                        // 取出队首 = b（a 已被淘汰）
            Assert.True(ReferenceEquals(b, next), "剩余空闲按原队序可取");
        }

        [Fact]
        public void EvictOldest_热点语义_刚归还件立即可复用()
        {
            var pool = new ObjectPool<Item>(NewItem, maxIdle: 1, eviction: PoolEviction.EvictOldest);
            var a = pool.Acquire();
            var b = pool.Acquire();
            pool.Release(a);                                  // idle: a
            pool.Release(b);                                   // 满：淘汰 a，b 入池
            var again = pool.Acquire();
            Assert.True(ReferenceEquals(b, again), "刚归还的热点件不被反复重建（EvictOldest 的存在理由）");
            Assert.Equal(2L, pool.CreatedCount);               // 无第三次 create
        }

        // ---- §3.2 空闲过期收缩 ----

        [Fact]
        public void TrimExpired_超龄销毁_年轻保留_按队首序()
        {
            var pool = new ObjectPool<Item>(NewItem, maxIdle: 8);
            var items = new List<Item>();
            for (int i = 0; i < 4; i++) items.Add(pool.Acquire());
            pool.Release(items[0], nowMs: 0);
            pool.Release(items[1], nowMs: 100);
            pool.Release(items[2], nowMs: 200);
            pool.Release(items[3], nowMs: 300);

            int trimmed = pool.TrimExpired(nowMs: 250, maxIdleMs: 100);

            Assert.Equal(2, trimmed);                          // 0/100 超龄，200/300 年轻
            Assert.Equal(2, pool.UnusedCount);
            var first = pool.Acquire();
            var second = pool.Acquire();
            Assert.True(ReferenceEquals(items[2], first), "留下的按队序可取（200）");
            Assert.True(ReferenceEquals(items[3], second), "留下的按队序可取（300）");
        }

        [Fact]
        public void TrimExpired_边界为严格大于_恰等于阈值不裁()
        {
            var pool = new ObjectPool<Item>(NewItem, maxIdle: 8);
            var a = pool.Acquire();
            pool.Release(a, nowMs: 100);

            Assert.Equal(0, pool.TrimExpired(nowMs: 200, maxIdleMs: 100));   // 200-100=100 不大于 100：保留
            Assert.Equal(1, pool.TrimExpired(nowMs: 201, maxIdleMs: 100));   // 201-100=101 > 100：裁剪
            Assert.Equal(0, pool.UnusedCount);
        }

        [Fact]
        public void TrimExpired_全超龄清空_返回数正确()
        {
            var pool = new ObjectPool<Item>(NewItem, maxIdle: 8);
            var items = new List<Item>();
            for (int i = 0; i < 4; i++) items.Add(pool.Acquire());   // 四件独立在用
            for (int i = 0; i < 4; i++) pool.Release(items[i], nowMs: i * 10);
            Assert.Equal(4, pool.TrimExpired(nowMs: 1000, maxIdleMs: 50));
            Assert.Equal(0, pool.UnusedCount);
        }

        [Fact]
        public void TrimExpired_预热件按nowMs参与按龄收缩()
        {
            var pool = new ObjectPool<Item>(NewItem, maxIdle: 8);
            pool.Preload(2, nowMs: 50);

            Assert.Equal(2, pool.TrimExpired(nowMs: 200, maxIdleMs: 100));   // 200-50=150 > 100：预热件同样按龄
            Assert.Equal(0, pool.UnusedCount);
        }

        [Fact]
        public void TrimExpired_不自动跑_无调用则空闲驻留()
        {
            var pool = new ObjectPool<Item>(NewItem, maxIdle: 8);
            pool.Release(pool.Acquire(), nowMs: 0);
            // 消费方不驱动 = 零时钟纪律：池不自动收缩（无后台 Tick）
            Assert.Equal(1, pool.UnusedCount);
        }

        // ---- §3.3 借出验证 ----

        [Fact]
        public void 借出验证_失效件销毁计数_继续取下一件()
        {
            var destroyed = new List<Item>();
            var pool = new ObjectPool<Item>(NewItem, onDestroy: destroyed.Add, maxIdle: 8,
                validateOnAcquire: it => !it.ExternallyBroken);
            var ok1 = pool.Acquire();
            var ok2 = pool.Acquire();
            var broken = pool.Acquire();
            broken.ExternallyBroken = true;                    // 借出后"被外部破坏"（语义上等价 Unity fake-null）
            pool.Release(ok1);
            pool.Release(broken);
            pool.Release(ok2);                                 // 队序：ok1, broken, ok2

            var first = pool.Acquire();                         // ok1 有效直取
            var second = pool.Acquire();                        // broken 失效销毁 → 取 ok2
            Assert.True(ReferenceEquals(ok1, first), "队首有效件直取");
            Assert.True(ReferenceEquals(ok2, second), "失效件跳过取下一件");
            Assert.Equal(1L, pool.InvalidatedCount);
            Assert.Single(destroyed);
            Assert.True(ReferenceEquals(broken, destroyed[0]), "被销毁的是失效件（只走 onDestroy，与 Trim 同口径）");
            Assert.Equal(2L, pool.PoolHits);                    // 失效件不记命中
        }

        [Fact]
        public void 借出验证_全失效转create()
        {
            var pool = new ObjectPool<Item>(NewItem, maxIdle: 8,
                validateOnAcquire: it => !it.ExternallyBroken);
            var a = pool.Acquire();
            a.ExternallyBroken = true;
            pool.Release(a);

            var fresh = pool.Acquire();
            Assert.False(ReferenceEquals(a, fresh), "池中唯一件失效 → 转 create");
            Assert.Equal(1L, pool.InvalidatedCount);
            Assert.Equal(2L, pool.CreatedCount);
            Assert.Equal(0L, pool.PoolHits);                    // Misses = Acquired - PoolHits = 1
        }

        [Fact]
        public void 借出验证_先于onGet与OnSpawn()
        {
            var order = new List<string>();
            var pool = new ObjectPool<ValidateProbe>(
                create: () => new ValidateProbe(order),
                onGet: _ => order.Add("onGet"),
                validateOnAcquire: p => { order.Add("validate"); return true; });
            pool.Release(pool.Acquire());
            order.Clear();

            pool.Acquire();
            Assert.Equal(new[] { "validate", "onGet", "OnSpawn" }, order);   // 验证在池策略与对象自初始化之前
        }

        private sealed class ValidateProbe : IPoolable
        {
            private readonly List<string> _order;
            public ValidateProbe(List<string> order) { _order = order; }
            public void OnSpawn() => _order.Add("OnSpawn");
            public void OnDespawn() { }
        }

        [Fact]
        public void 借出验证_回调抛异常_销毁后继续取下一件()
        {
            var pool = new ObjectPool<Item>(NewItem, maxIdle: 8,
                validateOnAcquire: it => { if (it.ValidationThrows) throw new InvalidOperationException("验证故障注入"); return true; });
            var bad = pool.Acquire();
            var good = pool.Acquire();
            bad.ValidationThrows = true;
            pool.Release(bad);
            pool.Release(good);

            var taken = pool.Acquire();
            Assert.True(ReferenceEquals(good, taken), "验证回调炸掉不阻塞池可用性：销毁该件继续取");
            Assert.Equal(1L, pool.InvalidatedCount);
        }

        // ---- §3.4 统计面 ----

        [Fact]
        public void 统计_PoolHits与Misses推导()
        {
            var pool = new ObjectPool<Item>(NewItem);
            pool.Acquire();                                    // Miss（空池 create）
            var b = pool.Acquire();
            pool.Release(b);
            pool.Acquire();                                    // Hit（池中直取）

            Assert.Equal(1L, pool.PoolHits);
            Assert.Equal(3L, pool.AcquiredCount);
            Assert.Equal(2L, pool.AcquiredCount - pool.PoolHits);   // Misses = Acquired - PoolHits
        }

        [Fact]
        public void 统计_Snapshot含新增键()
        {
            var pool = new ObjectPool<Item>(NewItem);
            var into = new Dictionary<string, string>();
            ((IModuleStats)pool).Snapshot(into);

            Assert.True(into.ContainsKey("PoolHits"));
            Assert.True(into.ContainsKey("Evicted"));
            Assert.True(into.ContainsKey("Invalidated"));
            Assert.True(into.ContainsKey("Dropped"));         // 既有键不丢（HUD 消费方兼容）
            Assert.True(into.ContainsKey("MaxIdle"));
        }
    }
}
