using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// 原生 Unity 协程纪律：业务 Unity 层只能使用可取消 UniTask。
    /// 第三方 UniTask/xLua 源码、Lua coroutine 以及 Core 的集合枚举不在扫描范围内。
    /// 规则匹配前先剔除注释——注释里提到禁用 API 不算违规。
    /// </summary>
    public sealed class NativeCoroutineDisciplineTests
    {
        private static readonly Regex Forbidden = new Regex(
            @"\bIEnumerator\b|\b(?:StartCoroutine|StopCoroutine|StopAllCoroutines)\b|\byield\s+return\b",
            RegexOptions.Compiled);

        [Fact]
        public void BusinessUnityCodeMustNotUseNativeUnityCoroutines()
        {
            string projectRoot = FindProjectRoot();
            string[] sourceRoots =
            {
                Path.Combine(projectRoot, "Packages", "com.litegame.framework", "Runtime", "Adapters", "Unity"),
                Path.Combine(projectRoot, "Assets", "LiteGame", "Scripts", "Runtime"),
            };

            var violations = new List<string>();
            foreach (string sourceRoot in sourceRoots)
            {
                // 框架线（main）检出里没有 Assets/LiteGame——该根不存在 = "无业务 Unity 代码可查"，
                // 跳过而不是失败（纪律只约束**存在**的代码；同一份测试要能同时跑在两条线上）。
                if (!Directory.Exists(sourceRoot)) continue;
                foreach (string file in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories))
                {
                    string[] lines = File.ReadAllLines(file);
                    bool inBlockComment = false;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string code = StripComments(lines[i], ref inBlockComment);
                        if (Forbidden.IsMatch(code))
                            violations.Add($"{Path.GetRelativePath(projectRoot, file)}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }

            Assert.True(
                violations.Count == 0,
                "检测到被禁止的 C# 原生 Unity 协程写法。请改用可取消 UniTask，并在宿主禁用/销毁时取消：\n"
                + string.Join(Environment.NewLine, violations));
        }

        /// <summary>去掉行注释与块注释（字符串字面量感知）——注释里提到禁用 API 不算违规。</summary>
        private static string StripComments(string line, ref bool inBlock)
        {
            var sb = new StringBuilder(line.Length);
            bool inString = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (inBlock)
                {
                    if (c == '*' && i + 1 < line.Length && line[i + 1] == '/') { inBlock = false; i++; }
                    continue;
                }

                if (inString)
                {
                    sb.Append(c);
                    if (c == '\\' && i + 1 < line.Length) { sb.Append(line[i + 1]); i++; continue; }
                    if (c == '"') inString = false;
                    continue;
                }

                if (c == '"') { inString = true; sb.Append(c); continue; }
                if (c == '/' && i + 1 < line.Length)
                {
                    if (line[i + 1] == '/') break; // 行注释 → 丢弃余下
                    if (line[i + 1] == '*') { inBlock = true; i++; continue; }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static string FindProjectRoot()
        {
            var candidates = new[]
            {
                new DirectoryInfo(Directory.GetCurrentDirectory()),
                new DirectoryInfo(AppContext.BaseDirectory),
            };

            foreach (DirectoryInfo candidate in candidates)
            {
                for (DirectoryInfo current = candidate; current != null; current = current.Parent)
                {
                    // 标记必须是**被 git 跟踪**的路径：Assets + Tests/Tests.slnx。
                    // 不能用 ProjectSettings/——它不在版本库里，干净检出（git clone / worktree / CI）都没有。
                    if (Directory.Exists(Path.Combine(current.FullName, "Assets"))
                        && File.Exists(Path.Combine(current.FullName, "Tests", "Tests.slnx")))
                        return current.FullName;
                }
            }

            throw new DirectoryNotFoundException("无法定位项目根目录（需要同时存在 Assets 与 Tests/Tests.slnx）。");
        }
    }
}
