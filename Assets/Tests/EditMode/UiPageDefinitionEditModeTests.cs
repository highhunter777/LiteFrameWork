using System.Collections.Generic;
using System.Linq;
using LiteFramework.UI;
using LiteGame.Editor.UiTools.Contracts;
using LiteGame.Editor.UiTools.Page;
using NUnit.Framework;
using LiteGame.Modules.Ui;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// M3-3 页面单制品：生成目录必须与运行时三源（#uiform / UiPageManifest / 页面契约）
    /// 逐行一致；运行时只读 PageDefinition，不再读表/清单/契约。
    /// </summary>
    public sealed class UiPageDefinitionEditModeTests
    {
        [Test]
        public void 生成目录_与表清单契约逐行一致()
        {
            var port = new UiPageTableHelperPort();
            Assert.IsTrue(port.TryListPages(out List<UiPageTableRow> rows, out string error), error);

            UiPageManifest manifest = UiPageManifestStore.Load();
            Assert.AreEqual(rows.Count, LiteGame.Modules.Ui.UiPageCatalog.All.Length, "页面定义数 = tbuiform 行数（同一 prefab 多行各一条）");
            Assert.AreEqual(rows.Count, rows.Select(r => r.id).Distinct().Count(), "表主键唯一");

            foreach (UiPageTableRow row in rows)
            {
                PageDefinition page = LiteGame.Modules.Ui.UiPageCatalog.Instance.Get(row.id);
                Assert.AreEqual(row.PrefabPath, page.Location, "prefab 路径单源");
                Assert.AreEqual(row.lua ?? string.Empty, page.LuaPath, "lua_path 单源");
                Assert.AreEqual(row.layer, page.Layer, "layer 单源");
                Assert.AreEqual(row.fullScreen, page.FullScreen, "fullScreen 单源");

                UiPageManifestEntry entry = manifest.Pages
                    .Single(p => p.Forms.Any(f => f.FormId == row.id));
                Assert.AreEqual(entry.Name, page.Name, "页面名来自清单");
                StringAssert.AreEqualIgnoringCase(entry.CacheStrategy, page.CacheStrategy.ToString(),
                    "缓存策略来自清单");

                if (entry.ContractPresent)
                {
                    UiPageContract contract = UiContractStore.Load(entry.ContractPath);
                    Assert.IsNotNull(contract, entry.ContractPath);
                    Assert.AreEqual(contract.Controls.Count, page.Controls.Count, "绑定表进入页面定义");
                    Assert.AreEqual(contract.Text.Count, page.TextKeys.Count, "文案 key 进入页面定义");
                    Assert.AreEqual(contract.Styles.Count, page.StyleRefs.Count, "样式引用进入页面定义");
                }
            }
        }

        [Test]
        public void 生成目录_按Id升序且未知Id抛()
        {
            for (int i = 1; i < LiteGame.Modules.Ui.UiPageCatalog.All.Length; i++)
                Assert.Less(LiteGame.Modules.Ui.UiPageCatalog.All[i - 1].Id, LiteGame.Modules.Ui.UiPageCatalog.All[i].Id, "All 必须按 Id 升序");

            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => LiteGame.Modules.Ui.UiPageCatalog.Instance.Get(1));
        }
    }
}
