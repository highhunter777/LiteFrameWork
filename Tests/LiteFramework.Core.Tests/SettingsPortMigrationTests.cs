using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace LiteFramework.Core.Tests
{
    /// <summary>
    /// Settings 页面端口迁移守卫（T4/T5/T6 · AC2）：源码与 asmdef 扫描，不依赖 Unity。
    /// 红＝迁移缺口仍在；绿＝页面只经端口、UI 程序集不再认识 Settings 实现程序集。
    /// </summary>
    public sealed class SettingsPortMigrationTests
    {
        private const string AsmdefPath = "Assets/LiteGame/Modules/UI/LiteGame.Modules.UI.asmdef";
        private const string PagePath = "Assets/LiteGame/Modules/UI/Pages/Screens/Settings/Page_SettingsLogic.cs";
        private const string PageContextPath = "Assets/LiteGame/Modules/UI/Abstractions/IPageContext.cs";
        private const string PortPath = "Assets/LiteGame/Modules/UI/Abstractions/ISettingsPagePort.cs";
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
        public void UI程序集不得引用Settings模块()
        {
            string root = RepoRoot();
            string path = Path.Combine(root, AsmdefPath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "缺 UI asmdef：" + AsmdefPath);

            string text = File.ReadAllText(path);
            Assert.False(text.Contains("LiteGame.Modules.Settings"),
                "UI asmdef 仍引用 Settings（T6 未完成）：" + AsmdefPath
                + " 的 references 仍含 LiteGame.Modules.Settings；页面须只经 ISettingsPagePort，UI 程序集不得认识 Settings 实现程序集");
        }

        [Fact]
        public void 设置页不得直取ProductSettings静态门面()
        {
            string root = RepoRoot();
            string path = Path.Combine(root, PagePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "缺设置页逻辑：" + PagePath);

            string code = StripComments(File.ReadAllText(path));
            MatchCollection matches = Regex.Matches(code, @"\bProductSettings\s*\.\s*Current\b");
            Assert.True(matches.Count == 0,
                "设置页仍直取静态门面 ProductSettings.Current（T5 未完成）：" + PagePath
                + " 命中 " + matches.Count + " 处；应改为 Ctx.Settings，并覆盖空端口 ?. 与 OnShow 早退（基线 10 处）");
        }

        [Fact]
        public void 页面上下文必须暴露设置端口()
        {
            string root = RepoRoot();
            string path = Path.Combine(root, PageContextPath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), "缺页面上下文：" + PageContextPath);

            string code = StripComments(File.ReadAllText(path));
            Assert.True(code.Contains("ISettingsPagePort"),
                "IPageContext 缺 ISettingsPagePort 成员（T4 未完成）：" + PageContextPath
                + " 未声明设置页端口类型");
            Assert.True(Regex.IsMatch(code, @"\bSettings\s*\{"),
                "IPageContext 缺 Settings 属性（T4 未完成）：" + PageContextPath
                + " 应声明 ISettingsPagePort Settings { get; }");
        }

        [Fact]
        public void 设置端口必须声明28个原语成员()
        {
            string root = RepoRoot();
            string path = Path.Combine(root, PortPath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path),
                "缺设置页端口：" + PortPath + "（T4 未完成；应落在 Modules/UI/Abstractions，28 个原语成员，无 Apply）");

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
        public void 产品设置适配器必须存在()
        {
            string root = RepoRoot();
            string path = Path.Combine(root, AdapterPath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path),
                "缺产品设置适配器：" + AdapterPath
                + "（T4 未完成；应包 ProductSettings，创建点在 BindProductSettings 之后或惰性取）");
        }

        /// <summary>成员声明形态：属性 Name { get; } 或方法 Name(...)（索引式读口亦然）。</summary>
        private static bool IsDeclared(string code, string name)
            => Regex.IsMatch(code, @"\b" + Regex.Escape(name) + @"\s*[\(\{]");

        /// <summary>去注释保留字符串：注释里的旧门面不算残留，字符串字面量不被误伤。</summary>
        private static string StripComments(string text)
        {
            var builder = new StringBuilder(text.Length);
            int index = 0;
            while (index < text.Length)
            {
                char current = text[index];
                if (current == '/' && index + 1 < text.Length && text[index + 1] == '/')
                {
                    while (index < text.Length && text[index] != '\n') index++;
                }
                else if (current == '/' && index + 1 < text.Length && text[index + 1] == '*')
                {
                    index += 2;
                    while (index + 1 < text.Length && !(text[index] == '*' && text[index + 1] == '/')) index++;
                    index = Math.Min(index + 2, text.Length);
                }
                else if (current == '"')
                {
                    builder.Append(current);
                    index++;
                    while (index < text.Length && text[index] != '"')
                    {
                        if (text[index] == '\\' && index + 1 < text.Length)
                        {
                            builder.Append(text[index]);
                            index++;
                        }
                        builder.Append(text[index]);
                        index++;
                    }
                    if (index < text.Length)
                    {
                        builder.Append(text[index]);
                        index++;
                    }
                }
                else
                {
                    builder.Append(current);
                    index++;
                }
            }
            return builder.ToString();
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
