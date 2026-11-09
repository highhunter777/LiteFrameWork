using System;
using System.Collections.Generic;
using Xunit;

namespace LiteFramework.Tests
{
    [Collection("CoreStatic")]   // 异常路径写 Log（全局静态）
    public sealed class ObjectPoolTests
    {
        private sealed class Pooled
        {
            public int Id;
        }

        private static ObjectPool<Pooled> NewPool(int maxIdle = 64, string name = null,
            Action<Pooled> onGet = null, Action<Pooled> onRelease = null, Action<Pooled> onDestroy = null,
            Func<Pooled, bool> validateOnAcquire = null)
            => new ObjectPool<Pooled>(() => new Pooled(), onGet, onRelease, onDestroy, maxIdle, name,
                validateOnAcquire, diagnoseDoubleRelease: true);

        [Fact]
        public void ObjectPool_空池Acquire_走create()
        {
            var pool = NewPool();
            var a = pool.Acquire();
            Assert.NotNull(a);
            Assert.Equal(0, pool.UnusedCount);
        }

        [Fact]
        public void ObjectPool_归还后Acquire_复用同实例()
        {
            var pool = NewPool();
            var a = pool.Acquire();
            pool.Release(a);
            Assert.Equal(1, pool.UnusedCount);
            var b = pool.Acquire();
            Assert.Same(a, b);
            Assert.Equal(0, pool.UnusedCount);
        }

        [Fact]
        public void ObjectPool_回调顺序_create_get_release_destroy()
        {
            var log = new List<string>();
            var pool = new ObjectPool<Pooled>(
                () => { log.Add("create"); return new Pooled(); },
                onGet: _ => log.Add("get"),
                onRelease: _ => log.Add("release"),
                onDestroy: _ => log.Add("destroy"));

            var a = pool.Acquire();
            pool.Release(a);
            pool.Clear();

            Assert.Equal(new[] { "create", "get", "release", "destroy" }, log);
        }

        [Fact]
        public void ObjectPool_池满_归还即销毁_不保留峰值()
        {
            int destroyed = 0;
            var pool = new ObjectPool<Pooled>(() => new Pooled(), maxIdle: 1, onDestroy: _ => destroyed++);
            var a = pool.Acquire();
            var b = pool.Acquire();

            pool.Release(a);
            pool.Release(b);                              // 满池：销毁新归还件

            Assert.Equal(1, destroyed);
            Assert.Equal(1, pool.UnusedCount);
            Assert.Same(a, pool.Acquire());
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
        public void ObjectPool_onRelease抛异常_销毁不入池污染并Error可见()
        {
            int destroyed = 0;
            var pool = new ObjectPool<Pooled>(() => new Pooled(),
                onRelease: _ => throw new InvalidOperationException("回调故障注入"),
                onDestroy: _ => destroyed++);
            var a = pool.Acquire();

            pool.Release(a);                              // 异常被隔离，对象销毁

            Assert.Equal(1, destroyed);
            Assert.Equal(0, pool.UnusedCount);
            var last = Log.Recent[Log.Recent.Count - 1];
            Assert.Equal(LogLevel.Error, last.Level);
        }

        [Fact]
        public void ObjectPool_onGet抛异常_直接传播()
        {
            var pool = new ObjectPool<Pooled>(() => new Pooled(),
                onGet: _ => throw new InvalidOperationException("取件回调故障注入"));

            Assert.Throws<InvalidOperationException>(() => pool.Acquire());
        }

        [Fact]
        public void ObjectPool_借出验证_失效件销毁_继续取下一件()
        {
            int seq = 0;
            int destroyed = 0;
            var pool = new ObjectPool<Pooled>(
                () => new Pooled { Id = ++seq },
                maxIdle: 4,
                onDestroy: _ => destroyed++,
                validateOnAcquire: p => p.Id != 1);

            var a = pool.Acquire();                       // Id=1
            pool.Release(a);
            var b = pool.Acquire();                       // 验证失效 → 销毁 Id=1 → 新建 Id=2

            Assert.Equal(2, b.Id);
            Assert.Equal(1, destroyed);
        }

        [Fact]
        public void ObjectPool_借出验证回调抛异常_销毁后继续()
        {
            int seq = 0;
            int destroyed = 0;
            var pool = new ObjectPool<Pooled>(
                () => new Pooled { Id = ++seq },
                maxIdle: 4,
                onDestroy: _ => destroyed++,
                validateOnAcquire: _ => throw new InvalidOperationException("验证故障注入"));

            var a = pool.Acquire();
            pool.Release(a);
            var b = pool.Acquire();                       // 验证抛 → 销毁旧件 → 新建

            Assert.Equal(2, b.Id);
            Assert.Equal(1, destroyed);
            Assert.Equal(LogLevel.Error, Log.Recent[Log.Recent.Count - 1].Level);
        }

        [Fact]
        public void ObjectPool_工厂拒绝_Acquire返回null()
        {
            var pool = new ObjectPool<Pooled>(() => null);
            Assert.Null(pool.Acquire());
            Assert.Equal(0, pool.UnusedCount);
        }

        [Fact]
        public void ObjectPool_Clear_只销毁空闲件_在用不受影响()
        {
            int destroyed = 0;
            var pool = new ObjectPool<Pooled>(() => new Pooled(), maxIdle: 4, onDestroy: _ => destroyed++);
            var a = pool.Acquire();
            var b = pool.Acquire();
            pool.Release(a);

            pool.Clear();

            Assert.Equal(1, destroyed);
            Assert.Equal(0, pool.UnusedCount);
            pool.Release(b);                              // 在用对象归还仍正常
            Assert.Equal(1, pool.UnusedCount);
        }

        [Fact]
        public void ObjectPool_非法构造参数_抛()
        {
            Assert.Throws<ArgumentNullException>(() => new ObjectPool<Pooled>(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ObjectPool<Pooled>(() => new Pooled(), maxIdle: -1));
        }
    }
}
