using System;
using System.IO;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// M3-6 / M3-5c 接线守卫：RoomClient 拆分目标（&lt;350 行、协议分发不在门面类）与恢复动作/账号重入
    /// 的生产接线不得静默断开（“注册了但没人调”= 红）。文本守卫——只钉结构与调用点，不重复行为用例。
    /// </summary>
    public sealed class M3WiringGuardTests
    {
        [Fact]
        public void RoomClient_拆分后小于350行_协议分发已外提()
        {
            string root = RepoRoot();
            string path = Path.Combine(root, "Assets/LiteNet/Client/RoomClient.cs");
            string text = File.ReadAllText(path);
            int lines = File.ReadAllLines(path).Length;

            Assert.True(lines < 350, $"RoomClient 行数 {lines} 超路线图 §4.6 目标（<350）");
            Assert.Contains("_inbound.Handle(", text);                  // 入站走分发件
            Assert.Contains("_outbound.", text);                        // 出站走打包件
            Assert.DoesNotContain("switch (type)", text);               // 协议分发表不得回涨到门面类
            Assert.True(File.Exists(Path.Combine(root, "Assets/LiteNet/Client/RoomInbound.cs")));
            Assert.True(File.Exists(Path.Combine(root, "Assets/LiteNet/Client/RoomOutbound.cs")));
        }

        [Fact]
        public void 内容缓存清理_先关运行时_再删产物_最后回退记录()
        {
            string text = File.ReadAllText(Path.Combine(RepoRoot(),
                "Assets/LiteClient/Runtime/Resource/ContentCacheMaintenance.cs"));

            int shutdown = text.IndexOf("ShutdownAsync(", StringComparison.Ordinal);
            int delete = text.IndexOf("DeleteDirectory(ReleaseLayout.CandidateRootRelative", StringComparison.Ordinal);
            int rollback = text.IndexOf("RollbackToBuiltin(", StringComparison.Ordinal);

            Assert.True(shutdown > 0 && delete > shutdown, "必须先释放运行时句柄再删候选产物");
            Assert.True(rollback > delete, "必须先删产物再回退激活记录（记录与磁盘一致）");
        }

        [Fact]
        public void 恢复动作_清缓存已接执行器与生产动作()
        {
            string root = RepoRoot();
            string service = File.ReadAllText(Path.Combine(root,
                "Assets/LiteFramework/Scripts/Async/Recovery/ErrorRecoveryService.cs"));
            string actions = File.ReadAllText(Path.Combine(root,
                "Assets/LiteGame/App/Recovery/AppRecoveryActions.cs"));
            string container = File.ReadAllText(Path.Combine(root,
                "Assets/LiteGame/App/Bootstrap/ContainerModule.cs"));

            Assert.Contains("ClearContentCacheAsync", service);          // 执行器 Recoverable 升级路径
            Assert.Contains("ClearContentCacheAsync", actions);          // 生产动作实现
            Assert.Contains("new ContentCacheMaintenance(", container);  // 装配点构造
            Assert.Contains("AppRecoveryActions(cache: contentCache)", container);
        }

        [Fact]
        public void 账号重入_401到回登录页单点接线()
        {
            string root = RepoRoot();
            string bridge = File.ReadAllText(Path.Combine(root, "Assets/LiteGame/App/Meta/MetaBridge.cs"));
            string container = File.ReadAllText(Path.Combine(root, "Assets/LiteGame/App/Bootstrap/ContainerModule.cs"));
            string pageFlow = File.ReadAllText(Path.Combine(root, "Assets/LiteGame/App/Ui/PageFlow.cs"));
            string meta = File.ReadAllText(Path.Combine(root, "Assets/LiteGame/App/Meta/MetaEntryService.cs"));

            Assert.Contains("IsAuthLost", bridge);                       // 401/凭证失效判据
            Assert.Contains("ReturnToLoginAsync", bridge);               // 唯一重入调用点
            Assert.Contains("new AccountReentry(", container);
            Assert.Contains("RegisterInstance<IAccountReentry>", container);
            Assert.Contains("MetaBridge.Bind(metaEntry, identity, () => config.Tables, reentry)", container);
            Assert.Contains(": ILoginSurface", pageFlow);
            Assert.Contains("ShowLogin(", pageFlow);
            Assert.Contains(": IAccountSessionControl", meta);
            Assert.Contains("public void Logout(", meta);
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Tests", "Tests.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir.FullName;
        }
    }
}
