using System;
using System.IO;
using System.IO.Compression;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using LiteGame.Modules.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 部署烙入读取件（《安卓平台适配》T4）：两条平台读取通道（桌面文件 / APK zip 条目）、
    /// 进程内 memo（引导期三处消费点只读一次）、日志语义拆分（missing＝正常、read failed＝一次性 Warning）。
    ///
    /// Android 侧的真实读取发生在模拟器上（T8 首启判据），这里用**同一条 zip 读取实现** +
    /// 临时 zip 文件覆盖其行为；平台分发本身只有一行 `#if`（本件在 R15 白名单内）。
    /// </summary>
    public sealed class DeployBakeEditModeTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "deploybake-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, true);
            }
            catch (IOException)
            {
                // 临时目录清理失败不影响结论
            }
        }

        [Test]
        public void 桌面通道_直读文件_缺文件返回null()
        {
            string path = Path.Combine(_root, DeployBake.FileName);
            File.WriteAllText(path, "-meta.url=http://127.0.0.1:5000");

            Assert.AreEqual("-meta.url=http://127.0.0.1:5000", DeployBake.ReadDesktopFile(path));
            Assert.IsNull(DeployBake.ReadDesktopFile(Path.Combine(_root, "absent.args")));
        }

        [Test]
        public void 安卓通道_按zip条目读取_缺条目返回null()
        {
            string apk = CreateZip("assets/" + DeployBake.FileName, "-meta.url=http://10.8.0.1:5000");
            Assert.AreEqual("-meta.url=http://10.8.0.1:5000",
                DeployBake.ReadZipEntry(apk, DeployBake.AndroidAssetsEntry));
            Assert.IsNull(DeployBake.ReadZipEntry(apk, "assets/other.args"));
            Assert.IsNull(DeployBake.ReadZipEntry(Path.Combine(_root, "absent.apk"), DeployBake.AndroidAssetsEntry));
        }

        [Test]
        public void memo_三处消费点只读一次()
        {
            int reads = 0;
            var bake = new DeployBake(_ =>
            {
                reads++;
                return "-a=1";
            });

            Assert.AreEqual("-a=1", bake.ReadArgs());
            Assert.AreEqual("-a=1", bake.ReadArgs());
            Assert.AreEqual("-a=1", bake.ReadArgs());
            Assert.AreEqual(1, reads, "memo 失效——引导期多处消费点会重复读包内文件");
        }

        [Test]
        public void 缺烙入_missing日志一次且返回null()
        {
            var bake = new DeployBake(_ => null);
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex("DeployBake.*missing"));

            Assert.IsNull(bake.ReadArgs());
            Assert.IsNull(bake.ReadArgs());
            // 只有一条 missing 日志（memo + 一次性标记双保险）——多余日志会被 LogAssert 判失败
        }

        [Test]
        public void 读失败_退化为无烙入并留一次性警告()
        {
            var bake = new DeployBake(_ => throw new IOException("jar read exploded"));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("DeployBake.*read failed.*jar read exploded"));

            Assert.IsNull(bake.ReadArgs());
            Assert.IsNull(bake.ReadArgs());
        }

        [Test]
        public void 读成功后不再读取也不打日志()
        {
            int reads = 0;
            var bake = new DeployBake(_ =>
            {
                reads++;
                return "-b=2";
            });
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex("DeployBake.*read 4 chars"));

            Assert.AreEqual("-b=2", bake.ReadArgs());
            Assert.AreEqual(1, reads);
            Assert.AreEqual("-b=2", bake.ReadArgs());
            Assert.AreEqual(1, reads);
        }

        [Test]
        public void 新实例不复用旧实例的失效结果()
        {
            var first = new DeployBake(_ => null);
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex("DeployBake.*missing"));
            Assert.IsNull(first.ReadArgs());

            // 新实例 = 全新 memo（不再有"全局复位"这种可变静态状态）
            var second = new DeployBake(_ => "-c=3");
            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex("DeployBake.*read 4 chars"));
            Assert.AreEqual("-c=3", second.ReadArgs());
        }

        private string CreateZip(string entryName, string content)
        {
            string path = Path.Combine(_root, "fake.apk");
            if (File.Exists(path)) File.Delete(path);
            using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                ZipArchiveEntry entry = archive.CreateEntry(entryName);
                using (var writer = new StreamWriter(entry.Open()))
                    writer.Write(content);
            }

            return path;
        }
    }
}
