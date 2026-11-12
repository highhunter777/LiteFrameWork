using LiteGameNext.Features.Battle.Hud;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>伤害数字飘字池：容器缺失静默跳过、池容量上限、回收后复用。</summary>
    public sealed class ClientNextDamageNumberPoolTests
    {
        private static RectTransform NewContainer()
        {
            var go = new GameObject("Cot_DamageNumbers", typeof(RectTransform));
            return go.GetComponent<RectTransform>();
        }

        [Test]
        public void 容器缺失_全部noop不抛()
        {
            var pool = new DamageNumberPool(null);
            Assert.DoesNotThrow(() => pool.Push("25", Vector3.zero, Color.white));
            Assert.DoesNotThrow(() => pool.Tick());
            Assert.AreEqual(0, pool.PoolCount);
            Assert.AreEqual(0, pool.LiveCount);
        }

        [Test]
        public void 压入后_存活计数增加()
        {
            RectTransform container = NewContainer();
            try
            {
                var pool = new DamageNumberPool(container);
                pool.Push("25", Vector3.one, Color.white);
                Assert.AreEqual(1, pool.LiveCount);
                Assert.AreEqual(1, pool.PoolCount);
            }
            finally
            {
                Object.DestroyImmediate(container.gameObject);
            }
        }

        [Test]
        public void 空文本_被跳过()
        {
            RectTransform container = NewContainer();
            try
            {
                var pool = new DamageNumberPool(container);
                pool.Push(string.Empty, Vector3.one, Color.white);
                Assert.AreEqual(0, pool.LiveCount, "空文本不应创建飘字");
            }
            finally
            {
                Object.DestroyImmediate(container.gameObject);
            }
        }

        [Test]
        public void 池容量_不超过上限()
        {
            RectTransform container = NewContainer();
            try
            {
                var pool = new DamageNumberPool(container);
                for (int i = 0; i < DamageNumberPool.MaxLive + 8; i++)
                    pool.Push(i.ToString(), Vector3.one, Color.white);
                Assert.LessOrEqual(pool.PoolCount, DamageNumberPool.MaxLive, "实例池不应超过上限");
            }
            finally
            {
                Object.DestroyImmediate(container.gameObject);
            }
        }

        [Test]
        public void 常量_与设计一致()
        {
            Assert.AreEqual(16, DamageNumberPool.MaxLive);
            Assert.AreEqual(0.8f, DamageNumberPool.LifetimeSeconds, 0.001f);
            Assert.AreEqual(60f, DamageNumberPool.RisePixels, 0.001f);
        }
    }
}
