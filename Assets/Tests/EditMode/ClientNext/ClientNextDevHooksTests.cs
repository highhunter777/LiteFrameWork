using LiteGameNext.Platform.Dev;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>开发面钩子：NullDev 发布兜底 + GM 命令字典（Dev 程序集装载时）。</summary>
    public sealed class ClientNextDevHooksTests
    {
        [Test]
        public void NullDev_发布兜底全为noop()
        {
            IDevHooks hooks = NullDevHooks.Instance;
            Assert.IsFalse(hooks.Enabled, "发布构建开发面必须禁用");
            Assert.DoesNotThrow(() => hooks.Tick(0.016f));
            Assert.DoesNotThrow(() => hooks.Shutdown());
            Assert.IsTrue(hooks.ExecuteCommand("help").Contains("未启用"));
        }

        [Test]
        public void NullDev_启动本地房间返回null()
        {
            IDevHooks hooks = NullDevHooks.Instance;
            var task = hooks.StartLocalRoomAsync("x", 2, System.Threading.CancellationToken.None);
            Assert.IsNull(task.GetAwaiter().GetResult(), "发布构建不应起本地服");
        }

        [Test]
        public void DevFeature_可反射装载且命令字典非空()
        {
            System.Type type = System.Type.GetType("LiteGameNext.Dev.DevFeature, LiteGameNext.Dev", false);
            if (type == null) Assert.Ignore("Dev 程序集未参与编译（发布构建）");
            var hooks = (IDevHooks)System.Activator.CreateInstance(type);
            Assert.IsTrue(hooks.Enabled);
            Assert.IsTrue(hooks.ExecuteCommand("help").Contains("local"), "help 应列出 local 命令");
            hooks.Shutdown();
        }

        [Test]
        public void DevFeature_未知命令返回提示不抛()
        {
            System.Type type = System.Type.GetType("LiteGameNext.Dev.DevFeature, LiteGameNext.Dev", false);
            if (type == null) Assert.Ignore("Dev 程序集未参与编译（发布构建）");
            var hooks = (IDevHooks)System.Activator.CreateInstance(type);
            try
            {
                string result = hooks.ExecuteCommand("no-such-command");
                StringAssert.Contains("未知命令", result);
            }
            finally
            {
                hooks.Shutdown();
            }
        }
    }
}
