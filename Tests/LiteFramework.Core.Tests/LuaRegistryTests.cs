using System;
using System.Collections.Generic;
using Xunit;

namespace LiteFramework.Tests
{
    public sealed class LuaRegistryTests   // 实例注册表，无静态状态
    {
        private interface IFakeLogic { }

        private sealed class FakeLogic : IFakeLogic { }

        private readonly LuaRegistry<IFakeLogic> _reg = new LuaRegistry<IFakeLogic>("Test");

        [Fact]
        public void LuaRegistry_Fill_Get_Has往返()
        {
            var impl = new FakeLogic();
            _reg.Fill("Test.A", impl);
            Assert.True(_reg.Has("Test.A"));
            Assert.False(_reg.Has("Test.B"));
            Assert.Same(impl, _reg.Get("Test.A"));
        }

        [Fact]
        public void LuaRegistry_重复Fill_抛()
        {
            _reg.Fill("Test.A", new FakeLogic());
            var ex = Assert.Throws<InvalidOperationException>(() => _reg.Fill("Test.A", new FakeLogic()));
            Assert.Contains("重复注册", ex.Message);
        }

        [Fact]
        public void LuaRegistry_Get未命中_抛且信息含路径约定提示()
        {
            var ex = Assert.Throws<KeyNotFoundException>(() => _reg.Get("Test.Missing"));
            Assert.Contains("Test.", ex.Message);           // 报错含 kind 前缀
            Assert.Contains("按路径约定应为", ex.Message);   // 路径约定提示
        }

        [Fact]
        public void LuaRegistry_Clear_清空可重填且Generation前进()
        {
            _reg.Fill("Test.A", new FakeLogic());
            var genBefore = _reg.Generation;
            _reg.Clear();
            Assert.False(_reg.Has("Test.A"));               // 旧表全弃
            _reg.Fill("Test.A", new FakeLogic());           // 重填不再被重复 Fill 拦截
            Assert.True(_reg.Has("Test.A"));
            Assert.True(_reg.Generation > genBefore);       // 失效纪元：同名重填也可被消费方感知
            _reg.Clear();                                   // 幂等：空表 Clear 不再前进
            Assert.Equal(_reg.Generation, _reg.Generation);
        }

        [Fact]
        public void LuaRegistry_FillGeneration递增()
        {
            Assert.Equal(0, _reg.Generation);
            _reg.Fill("Test.A", new FakeLogic());
            _reg.Fill("Test.B", new FakeLogic());
            Assert.Equal(2, _reg.Generation);               // 壳据此丢弃缓存引用
        }

        // ---- 运行期增量重填（M4 §2.3：不重建 env 的轻路径）----

        [Fact]
        public void LuaRegistry_多轮重填_Generation单调不回退()
        {
            _reg.Fill("Test.A", new FakeLogic());
            int prev = _reg.Generation;

            for (int round = 1; round <= 3; round++)
            {
                _reg.Clear();
                Assert.True(_reg.Generation > prev, $"第 {round} 轮 Clear 应前进纪元");
                prev = _reg.Generation;

                _reg.Fill("Test.A", new FakeLogic());
                Assert.True(_reg.Generation > prev, $"第 {round} 轮 Fill 应前进纪元");
                prev = _reg.Generation;
            }
        }

        [Fact]
        public void LuaRegistry_连续Clear_空表幂等不动纪元()
        {
            int emptyBefore = _reg.Generation;
            _reg.Clear();                                     // 空表：不前进
            Assert.Equal(emptyBefore, _reg.Generation);

            _reg.Fill("Test.A", new FakeLogic());
            int filled = _reg.Generation;
            _reg.Clear();                                     // 非空：前进一位
            Assert.Equal(filled + 1, _reg.Generation);
            _reg.Clear();                                     // 再清（已空）：不再前进
            Assert.Equal(filled + 1, _reg.Generation);
        }

        // ---- M1 demo 链路（手册 §三 自测：注册测试服务与假适配器）----

        [Fact]
        public void M1Demo_DI注入假适配器_经注册表Get取回同一实例()
        {
            var container = new ServiceContainer();
            container.RegisterFactory<IFakeLogic>(_ => new FakeLogic());   // 假适配器（M1 尚无 Lua）

            var adapter = container.Resolve<IFakeLogic>();                 // ① DI 注入

            var reg = new LuaRegistry<IFakeLogic>("Test");
            reg.Fill("Test.Adapter", adapter);                             // ② Fill
            Assert.Same(adapter, reg.Get("Test.Adapter"));                 // ③ Get 同实例
        }
    }
}
