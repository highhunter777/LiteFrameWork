using System;
using LiteFramework;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>GameObject 池门面：桶键记在池内（不往实例补组件）、外来物入池抛、工厂拒绝按 null 透传。</summary>
    public sealed class GameObjectPoolEditModeTests : UnityTestBase
    {
        private GameObjectPool _pool;

        [SetUp]
        public void NewPool()
            => _pool = new GameObjectPool(Scope.CreateGameObject("[TestPoolRoot]").transform);

        [TearDown]
        public void DisposePool() => _pool.Dispose();

        [Test]
        public void 取用产物_不往实例补组件()
        {
            var go = _pool.Get("k", () => Scope.CreateGameObject("k"));

            Assert.AreEqual(1, go.GetComponents<Component>().Length, "池产物只有 Transform——运行期不挂组件");
            Assert.AreEqual(0, _pool.PooledCount("k"), "借出未归还：桶空");
        }

        [Test]
        public void 归还后同键复用_同一实例()
        {
            var first = _pool.Get("k", () => Scope.CreateGameObject("k"));
            _pool.Release(first);

            Assert.AreEqual(1, _pool.PooledCount("k"), "归还入桶");
            Assert.IsTrue(_pool.TryGet("k", out var again), "桶命中");
            Assert.AreSame(first, again, "复用同一实例，不重建");
        }

        [Test]
        public void 外来物归还_抛且不入池()
        {
            var stranger = Scope.CreateGameObject("stranger");

            Assert.Throws<InvalidOperationException>(() => _pool.Release(stranger));
            Assert.AreEqual(0, _pool.PooledTotal, "外来物不得进任何桶");
        }

        [Test]
        public void 工厂拒绝返回null_按null透传()
        {
            Assert.IsNull(_pool.Get("missing", () => null), "资源缺失时 Get 返回 null（ObjectPool 契约）");
            Assert.AreEqual(0, _pool.PooledCount("missing"));
        }

        [Test]
        public void 外部销毁后_释放面不抛()
        {
            var go = _pool.Get("k", () => Scope.CreateGameObject("k"));
            UnityEngine.Object.DestroyImmediate(go);

            Assert.DoesNotThrow(() => _pool.Clear(), "悬空桶键不阻断释放面");
        }
    }
}
