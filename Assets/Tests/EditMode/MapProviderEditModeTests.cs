using System;
using LiteClient;
using LiteGame;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>地图提供者（D9/批4）装配面守卫：构造缺配置服务、未装载即取图——都要抛（fail-fast）。
    /// 取值点（进房装配/本地服建房）恒在 Preload 装载之后；未装载 = 装配序错误，不得静默兜底。</summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class MapProviderEditModeTests
    {
        [Test]
        public void 构造_缺配置服务_抛()
        {
            Assert.Throws<ArgumentNullException>(() => new MapProvider(null));
        }

        [Test]
        public void 未装载即取图_抛()
        {
            var config = new ConfigService((loc, ct) => throw new InvalidOperationException("不该取字节：未装载即取图路径"));
            var provider = new MapProvider(config);
            Assert.Throws<InvalidOperationException>(() => provider.GetMap());
        }
    }
}
