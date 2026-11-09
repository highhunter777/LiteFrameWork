using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;
namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// M3-6 / M3-5c 接线守卫：RoomClient 拆分形态（协议分发不在门面类）与恢复动作/账号重入
    /// 的生产接线不得静默断开（“注册了但没人调”= 红）。文本守卫——只钉结构与调用点，不重复行为用例。
    /// </summary>
    public sealed class M3WiringGuardTests
    {
        [Fact]
        public void RoomClient_协议分发已外提()
        {
            string root = RepoRoot();
            string path = Path.Combine(root, "Packages/com.litegame.sync/Client/Session/RoomClient.cs");
            string text = File.ReadAllText(path);

            Assert.Contains("_inbound.Handle(", text);                  // 入站走分发件
            Assert.Contains("_outbound.", text);                        // 出站走打包件
            Assert.DoesNotContain("switch (type)", text);               // 协议分发表不得回涨到门面类
            Assert.True(File.Exists(Path.Combine(root, "Packages/com.litegame.sync/Client/Wire/RoomInbound.cs")));
            Assert.True(File.Exists(Path.Combine(root, "Packages/com.litegame.sync/Client/Wire/RoomOutbound.cs")));
        }

        [Fact]
        public void 内容缓存清理_先关运行时_再删产物_最后回退记录()
        {
            string text = File.ReadAllText(Path.Combine(RepoRoot(),
                "Packages/com.litegame.framework/Runtime/Services/Content/Activation/ContentCacheMaintenance.cs"));

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
                "Packages/com.litegame.framework/Runtime/Hosting/Recovery/ErrorRecoveryService.cs"));
            string actions = File.ReadAllText(Path.Combine(root,
                "Assets/LiteGame/Modules/Procedure/SubModules/RecoverySubModule/AppRecoveryActions.cs"));
            string content = File.ReadAllText(Path.Combine(root,
                "Assets/LiteGame/Modules/Composition/Segments/Content/ContentSegment.cs"));
            string procedure = File.ReadAllText(Path.Combine(root,
                "Assets/LiteGame/Modules/Procedure/ProcedureSegment.cs"));

            Assert.Contains("ClearContentCacheAsync", service);          // 执行器 Recoverable 升级路径
            Assert.Contains("ClearContentCacheAsync", actions);          // 生产动作实现
            Assert.Contains("new ContentCacheMaintenance(", content);    // 内容段构造（清缓存实现面）
            Assert.Contains("AppRecoveryActions(cache: context.Require<IContentCacheMaintenance>())", procedure);
        }

        [Fact]
        public void 账号重入_401到回登录页单点接线()
        {
            string root = RepoRoot();
            string procedure = File.ReadAllText(Path.Combine(root, "Assets/LiteGame/Modules/Procedure/ProcedureSegment.cs"));
            string composition = File.ReadAllText(Path.Combine(root, "Assets/LiteGame/App/AppComposition.cs"));
            string pageFlow = File.ReadAllText(Path.Combine(root, "Assets/LiteGame/Modules/Procedure/Flow/PageFlow.cs"));
            string flow = File.ReadAllText(Path.Combine(root, "Assets/LiteGame/Modules/Procedure/Flow/ProcedureFlow.cs"));
            string meta = File.ReadAllText(Path.Combine(root, "Assets/LiteGame/Modules/Meta/Client/MetaEntryService.cs"));

            // 控制权归流程实现；Meta 退为纯票据源，不再拥有阶段切换/后绑定。
            Assert.Matches(@"class\s+ProcedureFlow\s*:[^{]*\bIRoomFlow\b", flow);
            Assert.Matches(@"class\s+ProcedureFlow\s*:[^{]*\bIAccountSessionControl\b", flow);
            Assert.Contains("public void Logout(", flow);
            Assert.DoesNotContain("IAccountSessionControl", meta);
            Assert.DoesNotContain("BindFlow", meta);
            Assert.DoesNotContain("IAppFlow", meta);

            // 同一 ProcedureFlow 实例注册到两个端口，并交给 AccountReentry（变量名不写死）。
            Match flowDecl = Regex.Match(procedure,
                @"(?:using\s+)?(?:var|ProcedureFlow)\s+(?<flow>\w+)\s*=\s*new\s+ProcedureFlow\s*\(");
            if (!flowDecl.Success)
            {
                // 合法形态：先声明（可空）后赋值，例如 ProcedureFlow flow = null; ... flow = new ProcedureFlow(...)。
                flowDecl = Regex.Match(procedure, @"(?<flow>\w+)\s*=\s*new\s+ProcedureFlow\s*\(");
            }
            Assert.True(flowDecl.Success, "ProcedureSegment 必须构造 ProcedureFlow");
            string flowVar = Regex.Escape(flowDecl.Groups["flow"].Value);
            Assert.Matches($@"context\.Put<IRoomFlow>\s*\(\s*{flowVar}\s*\)", procedure);
            Assert.Matches($@"context\.Put<IAccountSessionControl>\s*\(\s*{flowVar}\s*\)", procedure);
            Assert.Matches($@"new\s+AccountReentry\s*\(\s*(?:\w+\s*:\s*)?{flowVar}\s*,", procedure);
            Assert.Contains("Put<IAccountReentry>", procedure);
            Assert.DoesNotContain("Put<IAppFlow>", procedure);

            // Match/Battle/Flow 必须共用同一生命周期实例（共享占用/清理信号）。
            Match lifetimeDecl = Regex.Match(procedure,
                @"(?:using\s+)?(?:var|RoomFlowLifetime)\s+(?<life>\w+)\s*=\s*new\s+RoomFlowLifetime\s*\(");
            Assert.True(lifetimeDecl.Success, "ProcedureSegment 必须构造共享 RoomFlowLifetime");
            string lifetimeVar = Regex.Escape(lifetimeDecl.Groups["life"].Value);
            Assert.Matches(@"new\s+ProcedureMatch\s*\([^;]*?\b" + lifetimeVar + @"\b", procedure);
            Assert.Matches(@"new\s+ProcedureBattle\s*\([^;]*?\b" + lifetimeVar + @"\b", procedure);
            Assert.Matches(@"new\s+ProcedureFlow\s*\([^;]*?\b" + lifetimeVar + @"\b", procedure);

            // 页面流仍是登录落点单写者；组合根直接读取 IRoomFlow，不再有 BindMetaEntry 后绑定。
            Assert.Contains(": ILoginSurface", pageFlow);
            Assert.Contains("ShowLogin(", pageFlow);
            Assert.Contains("Require<IRoomFlow>()", composition);
            Assert.DoesNotContain("BindMetaEntry", composition);
            Assert.Matches(@"new\s+MetaEntrySession\s*\(\s*context\.Require<MetaEntryService>\(\)\s*,\s*context\.Require<IRoomFlow>\(\)", composition);
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
