using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// 共享包版本单源守卫：服务端宿主的 <c>LiteGame.*</c> PackageReference 必须写**精确版本字面量**
    /// （L0 依赖扫描禁浮动/属性版本），且必须等于 <c>SharedPackages.props</c> 的
    /// <c>&lt;LiteGameSharedVersion&gt;</c>——否则本地 feed 里的包与引用方版本号会悄悄分叉。
    /// </summary>
    public sealed class SharedPackageVersionPinTests
    {
        private static readonly Regex Pin = new Regex(
            "<PackageReference\\s+Include=\"(LiteGame\\.[^\"]+)\"\\s+Version=\"([^\"]+)\"",
            RegexOptions.Compiled);

        [Fact]
        public void 宿主共享包版本必须精确且等于单源()
        {
            string root = RepoRoot();
            string props = File.ReadAllText(Path.Combine(root, "SharedPackages.props"));
            Match single = Regex.Match(props, "<LiteGameSharedVersion>([^<]+)</LiteGameSharedVersion>");
            Assert.True(single.Success, "SharedPackages.props 缺 <LiteGameSharedVersion>");
            string expected = single.Groups[1].Value.Trim();

            string csproj = File.ReadAllText(Path.Combine(root, "Server", "RoomServer", "RoomServer.csproj"));
            var pins = Pin.Matches(csproj).Cast<Match>().ToList();
            Assert.True(pins.Count >= 5, "宿主少了 LiteGame.* 包引用（当前 " + pins.Count + " 条）");

            foreach (Match pin in pins)
            {
                string id = pin.Groups[1].Value;
                string version = pin.Groups[2].Value;
                Assert.False(version.Contains("$("),
                    id + " 用了 MSBuild 属性版本（" + version + "）：L0 依赖扫描要求精确字面量");
                Assert.Equal(expected, version);
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
