using System.Collections.Generic;
using System.Linq;
using LiteFramework.UI;
using LiteGame.Editor.UITools.Page;
using NUnit.Framework;
using LiteGame.Modules.UI;
using UnityEditor;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// M3-3 页面单制品：生成目录必须与运行时两源（`#uiform` 表行 / 页面 prefab）逐行一致；
    /// 运行时只读 PageDefinition，不再读表/清单。
    /// </summary>
    public sealed class UIPageDefinitionEditModeTests
    {
        [Test]
        public void 生成目录_与表与prefab逐行一致()
        {
            var port = new UIPageTableHelperPort();
            Assert.IsTrue(port.TryListPages(out List<UIPageTableRow> rows, out string error), error);

            Assert.AreEqual(rows.Count, LiteGame.Modules.UI.UIPageCatalog.All.Length, "页面定义数 = tbuiform 行数（同一 prefab 多行各一条）");
            Assert.AreEqual(rows.Count, rows.Select(r => r.id).Distinct().Count(), "表主键唯一");

            foreach (UIPageTableRow row in rows)
            {
                PageDefinition page = LiteGame.Modules.UI.UIPageCatalog.Instance.Get(row.id);
                Assert.AreEqual(row.PrefabPath, page.Location, "prefab 路径单源");
                Assert.AreEqual(row.layer, page.Layer, "layer 单源");
                Assert.AreEqual(row.fullScreen, page.FullScreen, "fullScreen 单源");

                Assert.AreEqual(row.PageName, page.Name, "页面名＝prefab 文件名派生");
                StringAssert.AreEqualIgnoringCase(row.cache, page.CacheStrategy.ToString(), "缓存策略来自表列");

                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<GameObject>(row.PrefabPath),
                    "prefab 必须存在：" + row.PrefabPath);
            }
        }

        [Test]
        public void 生成目录_按Id升序且未知Id抛()
        {
            for (int i = 1; i < LiteGame.Modules.UI.UIPageCatalog.All.Length; i++)
                Assert.Less(LiteGame.Modules.UI.UIPageCatalog.All[i - 1].Id, LiteGame.Modules.UI.UIPageCatalog.All[i].Id, "All 必须按 Id 升序");

            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => LiteGame.Modules.UI.UIPageCatalog.Instance.Get(1));
        }
    }
}
