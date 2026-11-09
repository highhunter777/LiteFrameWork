using System;
using LiteFramework;
using LiteGame;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;
using LiteFramework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// YooAssetContentService 用例。
    /// </summary>
    public sealed class YooAssetContentServiceEditModeTests : UnityTestBase
    {
        [Test]
        public void 未初始化获取_显性失败含location()
        {
            var content = new YooAssetContentService(new FileSystem(new UnityPathProvider(), new NewtonsoftJsonSerializer()));
            var ex = Assert.Throws<InvalidOperationException>(() =>
                content.AcquireAsync<GameObject>("Assets/LiteGame/Modules/UI/Content/Widgets/any.prefab").GetAwaiter().GetResult());
            StringAssert.Contains("Assets/LiteGame/Modules/UI/Content/Widgets/any.prefab", ex.Message);   // 契约：失败含 location
        }

        [Test]
        public void 空location_拒绝()
        {
            var content = new YooAssetContentService(new FileSystem(new UnityPathProvider(), new NewtonsoftJsonSerializer()));
            Assert.Throws<ArgumentNullException>(() =>
                content.AcquireAsync<GameObject>(null).GetAwaiter().GetResult());
        }

        [Test]
        public void 关闭_未初始化调用幂等不抛()
        {
            var content = new YooAssetContentService(new FileSystem(new UnityPathProvider(), new NewtonsoftJsonSerializer()));
            Assert.DoesNotThrow(() => content.ShutdownAsync().GetAwaiter().GetResult());
            Assert.DoesNotThrow(() => content.ShutdownAsync().GetAwaiter().GetResult());   // 幂等
            Assert.AreEqual(0, content.LiveEntries);
        }
    }
}
