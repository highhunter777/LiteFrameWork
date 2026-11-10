using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// Settings 页面端口契约守卫：端口住 Settings 域、ProductSettings 直接实现、
    /// 页面只认端口且不再有 App 侧适配器（同程序集单实现端口由本用例与 ISettingsPagePort 归属钉住）。
    /// </summary>
    public sealed class SettingsPortMigrationTests
    {
        private const string AsmdefPath = "Assets/LiteGame/Modules/UI/LiteGame.Modules.UI.asmdef";
        private const string PagePath = "Assets/LiteGame/Modules/UI/Pages/Screens/Settings/Page_SettingsLogic.cs";
        private const string ServicesPath = "Assets/LiteGame/Modules/UI/Pages/PageServices.cs";
        private const string PortPath = "Assets/LiteGame/Modules/Settings/Abstractions/ISettingsPagePort.cs";
        private const string ImplPath = "Assets/LiteGame/Modules/Settings/Model/ProductSettings.cs";
        private const string LegacyContextPath = "Assets/LiteGame/Modules/UI/Abstractions/IPageContext.cs";
        private const string AdapterPath = "Assets/LiteGame/App/PageServices/ProductSettingsAdapter.cs";

        /// <summary>端口 15 个读成员（按页面实际读取面；含索引式读口）。</summary>
        private static readonly string[] ReadMembers =
        {
            "LastTab", "FrameRateOptionCount", "FrameRateOption", "DisplayMode", "ResolutionCount",
            "ResolutionLabel", "ResolutionIndex", "LookSensitivity", "AdsSensitivity", "Volume",
            "SoundEnabled", "Quality", "FrameRate", "VSync", "InvertLookY",
        };

        /// <summary>端口 13 个写/动作成员（setter 即生效，无 Apply）。</summary>
        private static readonly string[] WriteMembers =
        {
            "SetLastTab", "SetVolume", "SetSoundEnabled", "SetQuality", "SetDisplayMode",
            "SetResolutionIndex", "SetFrameRate", "SetVSync", "SetLookSensitivity",
            "SetAdsSensitivity", "SetInvertLookY", "ResetDefaults", "Save",
        };

        [Fact]
        public void UI程序集引用设置域且页面只认端口()
        {
            string root = RepoRoot();
            string asmdef = File.ReadAllText(Path.Combine(root, AsmdefPath));
            Assert.Contains("LiteGame.Modules.Settings", asmdef);

            string code = StripComments(File.ReadAllText(Path.Combine(root, PagePath)));
            Assert.DoesNotContain("ProductSettings", code);
            Assert.Contains("ISettingsPagePort", code);
        }

        [Fact]
        public void 页面装配面暴露设置端口且旧上下文已退役()
        {
            string root = RepoRoot();
            string code = StripComments(File.ReadAllText(Path.Combine(root, ServicesPath)));
            Assert.True(code.Contains("ISettingsPagePort"),
                "PageServices 未声明设置端口类型：" + ServicesPath);
            Assert.True(Regex.IsMatch(code, @"\bSettings\s*\{"),
                "PageServices 缺 Settings 属性：" + ServicesPath);
            Assert.False(File.Exists(Path.Combine(root, LegacyContextPath)),
                "IPageContext 已由 IPageServices 取代，不得回涨：" + LegacyContextPath);
        }

        [Fact]
        public void 设置端口必须声明28个原语成员()
        {
            string root = RepoRoot();
            string path = Path.Combine(root, PortPath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "缺设置页端口（应住 Settings 域）：" + PortPath);

            string code = StripComments(File.ReadAllText(path));
            var missing = new List<string>();
            foreach (string name in ReadMembers)
            {
                if (!IsDeclared(code, name)) missing.Add("读:" + name);
            }
            foreach (string name in WriteMembers)
            {
                if (!IsDeclared(code, name)) missing.Add("写/动作:" + name);
            }

            Assert.True(missing.Count == 0,
                "ISettingsPagePort 缺 " + missing.Count + "/28 个成员（15 读 + 13 写/动作，无 Apply）："
                + string.Join(", ", missing));
        }

        [Fact]
        public void 产品设置直接实现端口且无页面适配器()
        {
            string root = RepoRoot();
            string code = StripComments(File.ReadAllText(Path.Combine(root, ImplPath)));
            Assert.Matches(@"class\s+ProductSettings\s*:\s*ISettingsPagePort", code);
            Assert.False(File.Exists(Path.Combine(root, AdapterPath)),
                "ProductSettings 已直接实现端口，页面适配器不得回涨：" + AdapterPath);
        }

        /// <summary>成员声明形态：属性 Name { get; } 或方法 Name(...)（索引式读口亦然）。</summary>
        private static bool IsDeclared(string code, string name)
            => Regex.IsMatch(code, @"\b" + Regex.Escape(name) + @"\s*[\(\{]");

        private static string StripComments(string text)
        {
            text = Regex.Replace(text, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            return Regex.Replace(text, @"//[^\n]*", string.Empty);
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
