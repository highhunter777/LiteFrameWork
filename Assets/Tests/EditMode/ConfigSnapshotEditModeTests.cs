using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteGame;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// ConfigService 快照化用例（C1-⑨：《商业级通用客户端框架总设计》§9 + 热更专项 §11）：
    /// 候选构建 → 校验 → 原子发布三段式——失败保留空态（Loaded=false、Version=0、Tables 访问抛）。
    /// 半发布回归卡：旧实现"先设 _tables 再 ApplyCombatNumbers"在校验炸了后 Loaded 已为 true
    /// （热更专项 §2 Current 登记的缺陷）——本用例锁死该行为不再复发。
    /// 字节通道注入假 provider（直读磁盘 .bytes，与 CombatNumbersEditModeTests 同源），
    /// 不依赖 YooAsset（EditMode 无法初始化——见 YooAssetContentServiceEditModeTests 边界注记）。
    /// </summary>
    public sealed class ConfigSnapshotEditModeTests : UnityTestBase
    {
        private static Dictionary<string, byte[]> ReadTableBytes()
        {
            string dir = Path.Combine(ProjectRoot(), "Assets", "LiteGame", "RawFile", "Config");
            var cache = new Dictionary<string, byte[]>(ConfigService.TableDataFiles.Length);
            foreach (string f in ConfigService.TableDataFiles)
            {
                string path = Path.Combine(dir, f + ".bytes");
                Assert.IsTrue(File.Exists(path), $"缺表数据：{path}（跑 Luban/gen.bat）");
                cache[f] = File.ReadAllBytes(path);
            }
            return cache;
        }

        /// <summary>假字节通道：从预读缓存应答（缺文件 = FileNotFoundException——与真实通道缺表同语义）。</summary>
        private static UniTask<byte[]> FromCache(Dictionary<string, byte[]> cache, string location, CancellationToken ct)
        {
            const string dir = ConfigService.DataDir;
            const string ext = ".bytes";
            string file = location.Substring(dir.Length, location.Length - dir.Length - ext.Length);
            return cache.TryGetValue(file, out var bytes)
                ? UniTask.FromResult(bytes)
                : UniTask.FromException<byte[]>(new FileNotFoundException(location));
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 正常链路_候选构建_原子发布_数值装载()
        {
            var cache = ReadTableBytes();
            var svc = new ConfigService((loc, ct) => FromCache(cache, loc, ct));

            svc.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.IsTrue(svc.Loaded);
            Assert.AreEqual(1UL, svc.Version, "首次成功发布版本 = 1（单调起点）");
            Assert.IsNotNull(svc.CurrentSnapshot);
            Assert.IsNotNull(svc.Tables.Tbcombatnum.Get(1));
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 坏表_建表失败_保留空态_无半发布()
        {
            var cache = ReadTableBytes();
            cache["tbcombatnum"] = new byte[] { 0x01, 0x02, 0x03 };   // 损坏字节——Luban 解析必炸

            var svc = new ConfigService((loc, ct) => FromCache(cache, loc, ct));
            Assert.Catch<Exception>(() => svc.LoadAsync(CancellationToken.None).GetAwaiter().GetResult());

            Assert.IsFalse(svc.Loaded, "建表失败必须保留空态——半发布缺陷回归卡（热更专项 §2）");
            Assert.AreEqual(0UL, svc.Version, "未发布版本不递增");
            Assert.IsNull(svc.CurrentSnapshot);
            Assert.Throws<InvalidOperationException>(() => { var _ = svc.Tables; });
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 缺表_fail_fast_报错含location_保留空态()
        {
            var cache = ReadTableBytes();
            cache.Remove("tbuiform");                                // 删一张表——缺表路径

            var svc = new ConfigService((loc, ct) => FromCache(cache, loc, ct));
            var ex = Assert.Throws<InvalidOperationException>(
                () => svc.LoadAsync(CancellationToken.None).GetAwaiter().GetResult());

            StringAssert.Contains("tbuiform", ex.Message, "报错必须带完整 location（缺哪张表可定位）");
            Assert.IsFalse(svc.Loaded);
            Assert.AreEqual(0UL, svc.Version);
        }

        private static string ProjectRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(Application.dataPath) ?? Application.dataPath);
            for (var cur = dir; cur != null; cur = cur.Parent)
                if (Directory.Exists(Path.Combine(cur.FullName, "Assets"))
                    && File.Exists(Path.Combine(cur.FullName, "Tests", "Tests.slnx")))
                    return cur.FullName;
            throw new DirectoryNotFoundException("找不到项目根");
        }
    }
}
