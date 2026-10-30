using System;
using Cysharp.Threading.Tasks;
using LiteFramework.UI;
using LiteTesting;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 确认缝路由器（批 3 · A14）：UI 段产出可后绑定的 <see cref="IConfirmDialog"/>，装配根后段绑定真实现。
    /// 判据——**未绑定**只记一条告警并按取消返回（不抛、不悬挂）；**绑定后**四段文案原样转发并返回实现结果；
    /// 解绑回到取消语义（模块关闭不留指向已拆服务的委托）。
    /// </summary>
    public sealed class ConfirmDialogRouterEditModeTests
    {
        [Test]
        [Category(TestCategory.Unit)]
        public void 未绑定_按取消返回且只记一条告警()
        {
            var router = new ConfirmDialogRouter();
            Assert.IsFalse(router.IsBound, "初始未绑定");

            UniTask<bool> first = router.ShowAsync("标题", "内容", "确定", "取消");
            Assert.AreEqual(UniTaskStatus.Succeeded, first.Status, "未绑定不得悬挂（立即落定）");
            Assert.IsFalse(first.GetAwaiter().GetResult(), "未绑定 = 取消语义（false，不抛）");

            UniTask<bool> second = router.ShowAsync("标题", "内容", "确定", "取消");
            Assert.IsFalse(second.GetAwaiter().GetResult());
            Assert.AreEqual(1, router.WarningCount, "告警只记一条（不刷屏）");
        }

        [Test]
        [Category(TestCategory.Unit)]
        public void 绑定后_原样转发并返回实现结果_解绑回到取消()
        {
            var router = new ConfirmDialogRouter();
            string seen = null;
            router.Bind((title, message, okText, cancelText) =>
            {
                seen = title + "|" + message + "|" + okText + "|" + cancelText;
                return UniTask.FromResult(true);
            });

            Assert.IsTrue(router.IsBound, "绑定后 IsBound");
            Assert.IsTrue(router.ShowAsync("标题", "内容", "确定", "取消").GetAwaiter().GetResult(), "实现结果原样返回");
            Assert.AreEqual("标题|内容|确定|取消", seen, "四段文案原样转发");

            router.Unbind();
            Assert.IsFalse(router.IsBound, "解绑后 IsBound=false");
            Assert.IsFalse(router.ShowAsync("标题", "内容", null, null).GetAwaiter().GetResult(), "解绑后回到取消语义");
            Assert.AreEqual(1, router.WarningCount, "解绑后的调用记告警（每进程首条）");
            Assert.Throws<ArgumentNullException>(() => router.Bind(null), "绑定空实现显性失败");
        }
    }
}
