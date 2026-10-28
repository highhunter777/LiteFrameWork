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
        [InlineData("Assets/LiteClient/Runtime/Resource/PatchRunner.cs")]
        [InlineData("Assets/LiteClient/Runtime/Resource/HttpCandidateFetcher.cs")]
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

        [Fact]
        public void 场景服务_操作经作用域_生命周期CTS仅一处()
        {
            string path = Path.Combine(RepoRoot(), "Assets/LiteClient/Adapters/Content.YooAsset/Scene/SceneService.cs");
            string text = File.ReadAllText(path);

            Assert.Contains("OperationScope.StartLinked(", text);
            Assert.Contains("scene.load", text);
            Assert.Contains("scene.unload", text);
            // 仅服务生命周期一枚（_lifetime）；操作面不得再各持 CTS。
            Assert.Equal(1, CountOccurrences(text, "new CancellationTokenSource"));
        }

        private static int CountOccurrences(string text, string token)
        {
            int count = 0, index = 0;
            while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += token.Length;
            }
            return count;
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
