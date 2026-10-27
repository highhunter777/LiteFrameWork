using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// M1 收尾债守卫：模块资源必须登记进 <c>ModuleContext.Scope</c>（路线图 §4.3）。
    ///
    /// 这些模块持有自己创建的可释放资源（设备源/命令注册/主题租约/表现服务/Lua 宿主）——
    /// 未登记 scope 时，初始化中途失败会漏放（Host 只对成功模块调用 ShutdownAsync）。
    /// 守卫按文件检查"装配期有 Scope.Register"：删除登记或新增同类模块漏登记即红。
    /// </summary>
    public sealed class ModuleScopeAdoptionTests
    {
        /// <summary>必须把资源登记进模块作用域的模块（文件名 → 持有的资源）。</summary>
        private static readonly Dictionary<string, string> Required = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "InputModule.cs", "设备源/输入服务" },
            { "ContainerModule.cs", "命令注册/根容器/场景租约" },
            { "UiServiceModule.cs", "主题资产租约/红点登记" },
            { "PresentationModule.cs", "租约缓存/实体/音频/VFX/相机" },
            { "LuaHostModule.cs", "Lua 宿主 env" },
        };

        [Fact]
        public void 持有资源的模块必须登记模块作用域()
        {
            string root = RepoRoot();
            string dir = Path.Combine(root, "Assets", "LiteGame", "App", "Bootstrap");
            Assert.True(Directory.Exists(dir), "模块目录不存在：" + dir);

            var offenders = new List<string>();
            foreach (KeyValuePair<string, string> pair in Required)
            {
                string file = Path.Combine(dir, pair.Key);
                if (!File.Exists(file)) { offenders.Add(pair.Key + "(缺失)"); continue; }
                if (!File.ReadAllText(file).Contains("context.Scope.Register"))
                    offenders.Add(pair.Key + "(" + pair.Value + ")");
            }

            Assert.True(offenders.Count == 0,
                "模块资源必须登记进 ModuleContext.Scope（初始化失败才可回收）：" + string.Join(" | ", offenders));
        }

        /// <summary>向上找含 Tests/Tests.slnx 的仓库根（与 DisciplineScannerTests 同款定位）。</summary>
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
