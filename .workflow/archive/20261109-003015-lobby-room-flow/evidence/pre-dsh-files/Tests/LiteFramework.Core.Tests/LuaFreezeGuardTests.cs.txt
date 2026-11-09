using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// Lua 退出迁移期棘轮（《Lua 退出专项设计》§4.7）：存量只减不增。
    /// P3 清完构建面后允许面已为空；P5 改为全仓零命中。
    /// </summary>
    public sealed class LuaFreezeGuardTests
    {
        /// <summary>P0 冻结时在册的脚本（相对 <c>Assets/LiteGame/Lua</c>；删掉的条目留在这里无害，出现新条目即红）。</summary>
        private static readonly HashSet<string> FrozenScripts = new HashSet<string>(StringComparer.Ordinal)
        {
            "main.lua",
            "Core/class.lua",
            "Tests/UITestPage.lua",
            "UI/Page_ButtonDemo.lua",
            "UI/Page_Login.lua",
            "UI/Page_Lobby.lua",
            "UI/Page_Records.lua",
            "UI/Page_Result.lua",
            "UI/Page_Room.lua",
            "UI/Page_Settings.lua",
        };

        /// <summary>允许持有 XLua 类型的目录（P3 已全部删除，允许面为空）。</summary>
        private static readonly string[] AdapterRoots = Array.Empty<string>();

        /// <summary>适配层外的存量例外（P3 已清空；不得再新增）。</summary>
        private static readonly HashSet<string> AdapterExceptions = new HashSet<string>(StringComparer.Ordinal);

        private static readonly Regex TypeUsage = new Regex(@"\b(?:LuaTable|LuaEnv|LuaFunction|XLua)\b", RegexOptions.Compiled);

        [Fact]
        public void Lua冻结_不得新增脚本()
        {
            string root = RepoRoot();
            string luaRoot = Path.Combine(root, "Assets", "LiteGame", "Lua");
            var extras = new List<string>();
            if (Directory.Exists(luaRoot))
            {
                foreach (string file in Directory.GetFiles(luaRoot, "*.lua", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(luaRoot, file).Replace('\\', '/');
                    if (!FrozenScripts.Contains(rel)) extras.Add(rel);
                }
            }

            Assert.True(extras.Count == 0,
                "Lua 已进入退出流程：不得新增 .lua 脚本（只减不增）：" + string.Join(" | ", extras));
        }

        [Fact]
        public void Lua冻结_适配层外不得新增XLua类型引用()
        {
            string root = RepoRoot();
            var offenders = new List<string>();
            foreach (string scanRoot in new[] { "Assets/LiteGame", "Packages/com.litegame.framework" })
            {
                string dir = Path.Combine(root, scanRoot.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(dir)) continue;
                foreach (string file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                    if (AdapterRoots.Any(prefix => rel.StartsWith(prefix, StringComparison.Ordinal))) continue;
                    if (AdapterExceptions.Contains(rel)) continue;

                    int lineNumber = 0;
                    foreach (string line in File.ReadAllLines(file))
                    {
                        lineNumber++;
                        if (HasXluaTypeUsage(line)) offenders.Add(rel + ":" + lineNumber);
                    }
                }
            }

            Assert.True(offenders.Count == 0,
                "Lua 已进入退出流程：适配层外不得新增 XLua 类型引用（只减不增）：" + string.Join(" | ", offenders));
        }

        [Fact]
        public void Lua冻结_匹配器自检()
        {
            Assert.True(HasXluaTypeUsage("using XLua;"), "using XLua 应命中");
            Assert.True(HasXluaTypeUsage("var t = env.NewTable(); LuaTable x;"), "LuaTable 类型应命中");
            Assert.True(HasXluaTypeUsage("private LuaFunction _fn;"), "LuaFunction 类型应命中");
            Assert.False(HasXluaTypeUsage("        // LuaTable 注释不该命中"), "整行注释不应命中");
            Assert.False(HasXluaTypeUsage("var s = \"LuaEnv 需存活\";"), "字符串字面量不应命中");
            Assert.False(HasXluaTypeUsage("public const string LuaDir = \"Assets/LiteGame/Lua/\";"), "LuaDir 名字不应命中");
            Assert.False(HasXluaTypeUsage("if (page.LuaPath == null) return;"), "LuaPath 名字不应命中");
        }

        /// <summary>行内是否出现 XLua 类型用法（先剥字符串、再剥行注释，避免注释与文案误报）。</summary>
        internal static bool HasXluaTypeUsage(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            string code = Regex.Replace(line, "\"[^\"]*\"", "\"\"");
            int comment = code.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) code = code.Substring(0, comment);
            return TypeUsage.IsMatch(code);
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
