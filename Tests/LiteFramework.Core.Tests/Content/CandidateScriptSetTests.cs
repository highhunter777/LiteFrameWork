using System;
using System.Collections.Generic;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// 候选脚本集合（《热更与内容发布专项设计》§10"从固定文件清单构建不可变脚本集合 →
    /// 检查依赖/语法/导出/Bridge 能力"里**可静态做**的部分；§7"同步 require 的依赖必须完整预载"）。
    ///
    /// 真实语法/执行/Bridge 能力检查需 Lua VM，不属本类范围（见施工记录 H3-d）。
    /// </summary>
    public sealed class CandidateScriptSetTests
    {
        private static ScriptEntry E(string module, string path, params string[] requires)
            => new ScriptEntry(module, path, requires);

        [Fact]
        public void 合法集合_接受且摘要稳定()
        {
            var entries = new[]
            {
                E("main", "lua/main.lua", "ui.home"),
                E("ui.home", "lua/ui/home.lua"),
            };

            Assert.True(CandidateScriptSet.TryBuild(entries, out CandidateScriptSet set).Accepted);
            Assert.Equal(2, set.Entries.Count);
            Assert.NotNull(set.Find("main"));
            Assert.Null(set.Find("nope"));
            Assert.Equal(64, set.ScriptDigest.Length);
        }

        [Fact]
        public void 摘要与输入顺序无关()
        {
            // 同一集合以不同顺序给出必须同摘要——否则"内容是否变化"的判定会被顺序噪声污染
            var a = new[] { E("m1", "lua/a.lua", "m2"), E("m2", "lua/b.lua") };
            var b = new[] { E("m2", "lua/b.lua"), E("m1", "lua/a.lua", "m2") };

            Assert.True(CandidateScriptSet.TryBuild(a, out CandidateScriptSet sa).Accepted);
            Assert.True(CandidateScriptSet.TryBuild(b, out CandidateScriptSet sb).Accepted);
            Assert.Equal(sa.ScriptDigest, sb.ScriptDigest);
        }

        [Fact]
        public void 依赖不在本批_拒绝()
        {
            // §7"同步 require 的依赖必须完整预载"——缺一个就不能整体激活
            ScriptSetVerdict v = CandidateScriptSet.TryBuild(
                new[] { E("main", "lua/main.lua", "ui.missing") }, out _);

            Assert.False(v.Accepted);
            Assert.Contains("依赖", v.Reason);
            Assert.Contains("ui.missing", v.Reason);
        }

        [Fact]
        public void 依赖成环_拒绝()
        {
            ScriptSetVerdict v = CandidateScriptSet.TryBuild(new[]
            {
                E("a", "lua/a.lua", "b"),
                E("b", "lua/b.lua", "c"),
                E("c", "lua/c.lua", "a"),
            }, out _);

            Assert.False(v.Accepted);
            Assert.Contains("成环", v.Reason);
        }

        [Fact]
        public void 自环_拒绝()
        {
            Assert.False(CandidateScriptSet.TryBuild(new[] { E("a", "lua/a.lua", "a") }, out _).Accepted);
        }

        [Fact]
        public void 菱形依赖_不成环_接受()
        {
            Assert.True(CandidateScriptSet.TryBuild(new[]
            {
                E("top", "lua/top.lua", "left", "right"),
                E("left", "lua/left.lua", "base"),
                E("right", "lua/right.lua", "base"),
                E("base", "lua/base.lua"),
            }, out _).Accepted);
        }

        [Fact]
        public void 模块名重复_拒绝()
        {
            ScriptSetVerdict v = CandidateScriptSet.TryBuild(new[]
            {
                E("dup", "lua/a.lua"),
                E("dup", "lua/b.lua"),
            }, out _);

            Assert.False(v.Accepted);
            Assert.Contains("重复", v.Reason);
        }

        [Fact]
        public void 路径重复含大小写归一_拒绝()
        {
            ScriptSetVerdict v = CandidateScriptSet.TryBuild(new[]
            {
                E("m1", "lua/A.lua"),
                E("m2", "lua/a.lua"),
            }, out _);

            Assert.False(v.Accepted);
        }

        [Theory]
        [InlineData("")]
        [InlineData(".a")]
        [InlineData("a.")]
        [InlineData("a..b")]
        [InlineData("a/b")]
        [InlineData("a\\b")]
        [InlineData("a-b")]
        [InlineData("a..")]
        public void 非法模块名_拒绝(string module)
        {
            Assert.False(CandidateScriptSet.TryBuild(new[] { E(module, "lua/x.lua") }, out _).Accepted);
        }

        [Theory]
        [InlineData("")]
        [InlineData("/lua/a.lua")]
        [InlineData("lua\\a.lua")]
        [InlineData("C:/lua/a.lua")]
        [InlineData("lua/../a.lua")]
        [InlineData("lua//a.lua")]
        public void 非法脚本路径_拒绝(string path)
        {
            Assert.False(CandidateScriptSet.TryBuild(new[] { E("m", path) }, out _).Accepted);
        }

        [Fact]
        public void 空集合_拒绝()
        {
            Assert.False(CandidateScriptSet.TryBuild(new ScriptEntry[0], out _).Accepted);
            Assert.False(CandidateScriptSet.TryBuild(null, out _).Accepted);
        }

        [Fact]
        public void 空条目_拒绝()
        {
            Assert.False(CandidateScriptSet.TryBuild(new ScriptEntry[] { null }, out _).Accepted);
        }

        [Fact]
        public void 依赖声明为空串_忽略()
        {
            // 空依赖项不构成"缺依赖"——但不能因此让空段混进摘要
            var withEmpty = new[] { E("a", "lua/a.lua", ""), E("b", "lua/b.lua", "") };
            var withoutDep = new[] { E("a", "lua/a.lua"), E("b", "lua/b.lua", "") };

            Assert.True(CandidateScriptSet.TryBuild(withEmpty, out CandidateScriptSet s1).Accepted);
            Assert.True(CandidateScriptSet.TryBuild(withoutDep, out CandidateScriptSet s2).Accepted);
        }

        [Fact]
        public void 深依赖链_不爆栈()
        {
            // 迭代式环检测：深度链不应导致栈溢出
            var entries = new List<ScriptEntry>();
            const int depth = 5000;
            for (int i = 0; i < depth; i++)
                entries.Add(E("m" + i, "lua/m" + i + ".lua", i + 1 < depth ? "m" + (i + 1) : null));

            Assert.True(CandidateScriptSet.TryBuild(entries, out _).Accepted);
        }

        [Fact]
        public void 深依赖链成环_检出()
        {
            var entries = new List<ScriptEntry>();
            const int depth = 2000;
            for (int i = 0; i < depth; i++)
                entries.Add(E("m" + i, "lua/m" + i + ".lua", "m" + ((i + 1) % depth)));

            ScriptSetVerdict v = CandidateScriptSet.TryBuild(entries, out _);
            Assert.False(v.Accepted);
            Assert.Contains("成环", v.Reason);
        }
    }
}
