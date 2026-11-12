using LiteGameNext.Platform.Content;
using LiteGameNext.Platform.Ui;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>内容装载门面：地址归一化与编辑器按文件名定位真实 prefab。</summary>
    public sealed class ClientNextContentTests
    {
        [TestCase("Assets/LiteGame/Next/Runtime/Features/Account/Content/LoginPage.prefab", "LoginPage")]
        [TestCase("LoginPage.prefab", "LoginPage")]
        [TestCase("LoginPage", "LoginPage")]
        [TestCase(@"Assets\LiteGame\Next\Runtime\Platform\Ui\Content\ErrorPage.prefab", "ErrorPage")]
        public void 地址归一化_只留不含扩展名的文件名(string address, string expected)
        {
            Assert.AreEqual(expected, NextContentService.AddressKey(address));
        }

        [Test]
        public void 编辑器按文件名装载_真实登录页prefab()
        {
            var content = new NextContentService();
            GameObject go = content.InstantiateSync("LoginPage");
            try
            {
                Assert.IsNotNull(go, "LoginPage.prefab 应能被内容服务装载");
                Assert.IsNotNull(go.GetComponent<PageBase>(), "prefab 根应挂 PageBase");
            }
            finally
            {
                content.Release(go);
            }
        }

        [Test]
        public void 缺失地址_显性失败而非造替身()
        {
            var content = new NextContentService();
            Assert.Throws<System.InvalidOperationException>(() => content.InstantiateSync("NoSuchPage"));
        }
    }
}
