using System;
using System.IO;
using System.Linq;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// M3-5b 守卫：错误分类单一来源（只有 <see cref="ErrorKind"/> 一套），旧的无执行器骨架不得回流；
    /// 恢复执行器必须有生产消费者（“先建机制等消费者”是本批要删的形态）。
    /// </summary>
    public sealed class ErrorRecoveryGuardTests
    {
        private static readonly string[] Roots =
        {
            "Assets/LiteFramework", "Assets/LiteFramework/Runtime/Modules", "Assets/LiteGame", "Assets/LiteNet",
        };

        [Fact]
        public void 旧骨架已删除_无第二套错误枚举()
        {
            string root = RepoRoot();
            Assert.False(File.Exists(Path.Combine(root,
                "Assets/LiteFramework/Runtime/Core/Errors/ClientErrorRecovery.cs")));

            int errorEnums = 0;
            foreach (string file in Files(root))
            {
                string text = File.ReadAllText(file);
                Assert.DoesNotContain("enum ClientErrorKind", text);
                Assert.DoesNotContain("class ClientRecoveryException", text);
                Assert.DoesNotContain("static class ClientErrorRecovery", text);
                if (text.Contains("public enum ErrorKind")) errorEnums++;
            }

            Assert.Equal(1, errorEnums);
        }

        [Fact]
        public void 恢复执行器有生产消费者()
        {
            string root = RepoRoot();
            string patch = File.ReadAllText(Path.Combine(root, "Assets/LiteGame/App/Procedure/Stages/ProcedurePatch.cs"));
            string error = File.ReadAllText(Path.Combine(root, "Assets/LiteGame/App/Procedure/Stages/ProcedureError.cs"));

            Assert.Contains("ErrorRecoveryService", patch);
            Assert.Contains("HandleAsync", patch);
            Assert.Contains("ErrorRecoveryService.Classify", error);
            Assert.Contains("HandleAsync", error);
        }

        private static System.Collections.Generic.IEnumerable<string> Files(string root)
        {
            foreach (string relativeRoot in Roots)
            {
                string dir = Path.Combine(root, relativeRoot.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(dir)) continue;
                foreach (string file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                    yield return file;
            }
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
