using System.Collections;
using LiteFramework;
using LiteTesting;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LiteGame.Tests.EditMode
{
    /// <summary>发布布局约定（《热更与内容发布专项设计》§7/§8 的探针装配输入——
    /// config/ 前缀 = 配置、lua/ 前缀 = 热更脚本、模块名派生与 LuaPreloader 同规则）。
    /// 这些约定是 GameModules 装配与发布流水线的**共同契约**，派生错了探针就查错文件。</summary>
    public sealed class ReleaseLayoutEditModeTests
    {
        private static ReleaseManifest ManifestWith(params string[] paths)
        {
            var m = new ReleaseManifest { ReleaseId = "rel", Revision = 1 };
            foreach (var p in paths) m.Files.Add(new ReleaseFileEntry { Path = p });
            return m;
        }

        [Test]
        public void 布局_config前缀_挑出配置文件()
        {
            var m = ManifestWith("config/tbuiform.bytes", "lua/ui/UIMain.lua", "other/x.bin");
            var paths = ReleaseLayout.ConfigPaths(m);

            Assert.AreEqual(1, paths.Count);
            Assert.AreEqual("config/tbuiform.bytes", paths[0]);
        }

        [Test]
        public void 布局_lua前缀_派生模块名与LuaPreloader同规则()
        {
            var m = ManifestWith("lua/ui/UIMain.lua", "lua/cfg/tbuiform.lua", "lua/main.lua", "lua/x.txt", "config/a.bytes");
            var scripts = ReleaseLayout.LuaScripts(m);

            Assert.AreEqual(3, scripts.Count, "只取 lua/ 前缀的 .lua");
            Assert.AreEqual("ui/UIMain", scripts[0].Module);
            Assert.AreEqual("cfg/tbuiform", scripts[1].Module);
            Assert.AreEqual("main", scripts[2].Module);
            foreach (var s in scripts)
            {
                Assert.AreEqual(s.Path, "lua/" + s.Module + ".lua", "路径与模块名一一对应");
                Assert.AreEqual(0, s.Requires.Count, "清单不声明依赖——运行期沙箱 require 按批内模块解析");
            }
        }

        [Test]
        public void 布局_空清单_返回空集合不抛()
        {
            var m = ManifestWith();
            Assert.AreEqual(0, ReleaseLayout.ConfigPaths(m).Count);
            Assert.AreEqual(0, ReleaseLayout.LuaScripts(m).Count);
        }

        [Test]
        public void 信任锚_零锚点Apply_返回零且库保持空()
        {
            var store = new TrustedKeyStore();

            int applied = ContentTrustAnchors.ApplyTo(store);

            Assert.AreEqual(0, applied, "当前零内置锚点——fail-closed 保持（候选一律 UnknownOrRevokedKey 拒）");
            Assert.AreEqual(0, store.ProbeCount);
        }

        [Test]
        public void 信任锚_登记后Resolve_未登记keyId返回null()
        {
            var store = new TrustedKeyStore();
            store.Add("k1", new byte[128], new byte[] { 1, 0, 1 });   // 128 字节模数（RSA 导入合法）

            Assert.IsNotNull(store.Resolve("k1"), "已登记 → 验签器");
            Assert.IsNull(store.Resolve("nope"), "未登记 → null（校验器按 UnknownOrRevokedKey 拒）");
            store.Revoke("k1");
            Assert.IsNull(store.Resolve("k1"), "已撤销 → null");
        }
    }
}
