using System;
using System.Collections.Generic;
using LiteFramework;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// 集合池家族直测（《对象池专项设计》§4）：组合内核（onRelease = Clear）与三静态门面的
    /// 取还复用/内容自清/容量上限/重复归还检测。静态门面全局状态——[Collection("CoreStatic")] 串行防互扰。
    /// </summary>
    [Collection("CoreStatic")]
    public sealed class CollectionPoolTests
    {
        // ---- 内核（CollectionPool） ----

        [Fact]
        public void 取还复用_同实例回取_内容自清()
        {
            var pool = new CollectionPool<List<int>, int>(maxIdle: 4);
            var list = pool.Acquire();
            list.AddRange(new[] { 1, 2, 3 });
            pool.Release(list);

            var again = pool.Acquire();
            Assert.True(ReferenceEquals(list, again), "池中直取同一实例");
            Assert.Empty(again);                               // 归还即 Clear：取件状态未定义契约
            Assert.Equal(1L, pool.PoolHits);
        }

        [Fact]
        public void 内容自清_三种集合Clear皆生效()
        {
            var listPool = new CollectionPool<List<int>, int>();
            var dictPool = new CollectionPool<Dictionary<string, int>, KeyValuePair<string, int>>();
            var setPool = new CollectionPool<HashSet<object>, object>();

            var list = listPool.Acquire(); list.Add(1);
            var dict = dictPool.Acquire(); dict["a"] = 1;
            var set = setPool.Acquire(); set.Add(new object());

            listPool.Release(list);
            dictPool.Release(dict);
            setPool.Release(set);

            Assert.Empty(listPool.Acquire());
            Assert.Empty(dictPool.Acquire());
            Assert.Empty(setPool.Acquire());
        }

        [Fact]
        public void 容量上限_超限丢弃()
        {
            var pool = new CollectionPool<List<int>, int>(maxIdle: 2);
            var a = pool.Acquire();
            var b = pool.Acquire();
            var c = pool.Acquire();
            pool.Release(a);
            pool.Release(b);
            pool.Release(c);                                   // 满（DropNewest）：c 丢弃

            Assert.Equal(2, pool.UnusedCount);
            var first = pool.Acquire();
            var second = pool.Acquire();
            Assert.True(ReferenceEquals(a, first), "剩余按队序可取（a 先还先回）");
            Assert.True(ReferenceEquals(b, second));
            Assert.Equal(0, pool.UnusedCount);                 // 取空后下一件是 create 新件
        }

        [Fact]
        public void 重复归还_内核口径抛()
        {
            var pool = new CollectionPool<List<int>, int>();
            var list = pool.Acquire();
            pool.Release(list);
            Assert.Throws<InvalidOperationException>(() => pool.Release(list));
        }

        // ---- 静态门面 ----

        [Fact]
        public void 静态门面_ListPool按类型分桶_跨类型不串桶()
        {
            var ints = ListPool<int>.Get();
            ints.Add(42);
            ListPool<int>.Release(ints);                       // ListPool<int> 桶：1 件

            var strs = ListPool<string>.Get();                 // ListPool<string> 桶：独立分桶
            Assert.Empty(strs);                                // 跨类型不串桶（string 桶是全新实例）
            Assert.Equal(1, ListPool<int>.UnusedCount);        // int 桶不受 string 桶取件影响

            var taken = ListPool<int>.Get();
            Assert.True(ReferenceEquals(ints, taken), "同桶取件");
            Assert.Empty(taken);                               // 同桶取件已自清
        }

        [Fact]
        public void 静态门面_三家族取还闭环()
        {
            var list = ListPool<long>.Get(); list.Add(1L); ListPool<long>.Release(list);
            var dict = DictionaryPool<int, string>.Get(); dict[1] = "a"; DictionaryPool<int, string>.Release(dict);
            var set = HashSetPool<byte>.Get(); set.Add(3); HashSetPool<byte>.Release(set);

            Assert.Empty(ListPool<long>.Get());
            Assert.Empty(DictionaryPool<int, string>.Get());
            Assert.Empty(HashSetPool<byte>.Get());
        }

        [Fact]
        public void 静态门面_maxIdle默认8_超限丢弃()
        {
            var lists = new List<List<short>>();
            for (int i = 0; i < 10; i++) lists.Add(ListPool<short>.Get());
            foreach (var l in lists) ListPool<short>.Release(l);

            Assert.Equal(8, ListPool<short>.UnusedCount);      // 默认容量 8：超出丢弃不驻留
        }
    }
}
