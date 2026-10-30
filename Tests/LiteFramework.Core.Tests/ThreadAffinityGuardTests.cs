using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// 线程亲和守卫（《客户端架构重构演进路线图》§4.10）：Unity 服务/UI/表现/Sim 跑主线程；
    /// 允许后台的只有纯 BCL 的内容 IO/序列化（<c>FileSystem</c>），且不得在后台触达引擎对象。
    /// 新增后台卸载点 = 先论证"纯 BCL + 结果回主线程"，再把文件登记进白名单。
    /// </summary>
    public sealed class ThreadAffinityGuardTests
    {
        private static readonly string[] ProductRoots =
        {
            "Assets/LiteFramework/Runtime/Modules", "Assets/LiteGame", "Assets/LiteFramework", "Assets/LiteNet",
        };

        private static readonly string[] BackgroundSymbols =
        {
            "Task.Run", "RunOnThreadPool", "ConfigureAwait(false)", "ThreadPool.",
        };

        /// <summary>唯一允许的后台卸载点：FileSystem 的 JSON 序列化（纯 BCL，不触达 UnityEngine）。</summary>
        private const string AllowedFile = "Assets/LiteFramework/Runtime/Core/IO/FileSystem.cs";

        [Fact]
        public void 后台卸载仅限纯BCL的FileSystem()
        {
            string root = RepoRoot();
            var offenders = new List<string>();

            foreach (string relativeRoot in ProductRoots)
            {
                string directory = Path.Combine(root, relativeRoot.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(directory)) continue;
                foreach (string file in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                    if (rel == AllowedFile) continue;
                    if (rel.Contains("/Editor/", StringComparison.Ordinal)) continue;   // 编辑器工具不在 Player 线程模型内
                    string[] lines = File.ReadAllLines(file);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i];
                        if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                        foreach (string symbol in BackgroundSymbols)
                            if (line.Contains(symbol))
                                offenders.Add(rel + ":" + (i + 1) + " " + symbol);
                    }
                }
            }

            Assert.True(offenders.Count == 0,
                "后台卸载点未登记（仅 FileSystem 允许；其余须论证纯 BCL 且结果回主线程）:" + string.Join(" | ", offenders));
        }

        [Fact]
        public void FileSystem_后台段不触达UnityEngine()
        {
            string path = Path.Combine(RepoRoot(), AllowedFile);
            string text = File.ReadAllText(path);
            Assert.DoesNotContain("UnityEngine", text);
            Assert.Contains("Task.Run", text);
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
