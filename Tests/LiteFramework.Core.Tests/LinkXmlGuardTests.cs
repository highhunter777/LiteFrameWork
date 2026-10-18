using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// link.xml（IL2CPP 防裁表）守卫：每个 <c>assembly/@fullname</c> 必须命中真实存在的
    /// 程序集名（Assets 与 Packages 两处的 asmdef name 字段）。程序集拆分/改名后旧名残留
    /// 表内＝静默失锚（裁剪档位提升或 DI 按类型注册启用时才显影）——本用例把"表内名字真实"
    /// 钉成可执行判据，防整类陈旧。
    /// </summary>
    public sealed class LinkXmlGuardTests
    {
        /// <summary>asmdef name 字段提取（asmdef 是 JSON，非 XML；正则取字段免严格解析边界）。</summary>
        private static readonly Regex AsmdefNameRegex =
            new Regex("\"name\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);

        [Fact]
        public void 防裁表_每个fullname都是真实程序集名()
        {
            string root = RepoRoot();
            string linkPath = Path.Combine(root, "Assets", "LiteGame", "link.xml");
            Assert.True(File.Exists(linkPath), "link.xml 不存在：" + linkPath);

            var doc = XDocument.Load(linkPath);
            var fullnames = doc.Root
                .Elements("assembly")
                .Select(a => (string)a.Attribute("fullname") ?? string.Empty)
                .Where(n => n.Length > 0)
                .ToList();
            Assert.True(fullnames.Count > 0, "link.xml 无任何 assembly 条目——防裁面为空");

            var asmdefNames = CollectAsmdefNames(root);
            Assert.True(asmdefNames.Count > 0, "未收集到任何 asmdef 程序集名——扫描根失效");

            var stale = fullnames.Where(n => !asmdefNames.Contains(n)).ToList();
            Assert.True(stale.Count == 0,
                "link.xml 引用了不存在的程序集名（陈旧＝静默失锚）：" + string.Join(", ", stale));
        }

        /// <summary>
        /// 全部候选程序集名＝Assets 与 Packages 下的 asmdef name 字段并集。
        /// （程序集可住工程任一处：Assets 模块、GameData 生成物、Packages 本地包。）
        /// </summary>
        private static HashSet<string> CollectAsmdefNames(string root)
        {
            var names = new HashSet<string>();
            foreach (string scanRoot in new[] { "Assets", "Packages" })
            {
                string dir = Path.Combine(root, scanRoot);
                if (!Directory.Exists(dir)) continue;
                foreach (string file in Directory.EnumerateFiles(dir, "*.asmdef", SearchOption.AllDirectories))
                {
                    string text = File.ReadAllText(file);
                    var m = AsmdefNameRegex.Match(text);
                    if (m.Success) names.Add(m.Groups[1].Value);
                }
            }
            return names;
        }

        /// <summary>向上找含 Tests/Tests.slnx 的仓库根（与内容夹具同款定位）。</summary>
        private static string RepoRoot()
        {
            var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "Tests", "Tests.slnx")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return dir.FullName;
        }
    }
}
