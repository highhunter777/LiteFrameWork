using System;
using System.IO;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// M3-5 守卫：已迁移的操作面不得回退到裸 <c>CancellationTokenSource</c>。
    /// 迁移清单随批次推进（M3-5a：UI 打开/导航；后续内容/场景/会话）。
    /// </summary>
    public sealed class OperationScopeGuardTests
    {
        [Theory]
        [InlineData("Assets/LiteClient/UI/UIService.cs")]
        [InlineData("Assets/LiteClient/UI/UINavigationController.cs")]
        public void 已迁移文件_只经OperationScope持有取消(string relativePath)
        {
            string root = RepoRoot();
            string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "文件不存在：" + path);
            string text = File.ReadAllText(path);

            Assert.DoesNotContain("new CancellationTokenSource", text);
            Assert.DoesNotContain("WorkCts", text);
            Assert.Contains("OperationScope.Start(", text);
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
