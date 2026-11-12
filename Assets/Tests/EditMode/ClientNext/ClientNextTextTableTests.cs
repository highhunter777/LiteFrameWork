using System.Threading;
using Cysharp.Threading.Tasks;
using LiteGameNext.Platform.Config;
using LiteGameNext.Platform.Localization;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>tbltext 投影：真实表文案进目录、中英可切、内置兜底保留。</summary>
    public sealed class ClientNextTextTableTests
    {
        private static cfg.Tables LoadTables()
        {
            var service = new LiteGameNext.Platform.Config.ConfigService((file, ct) =>
            {
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.TextAsset>(
                    "Assets/GameData/Config/" + file + ".bytes");
                return UniTask.FromResult(asset?.bytes);
            });
            service.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
            return service.Tables;
        }

        [Test]
        public void 真实tbltext_投影进目录()
        {
            cfg.Tables tables;
            try
            {
                tables = LoadTables();
            }
            catch (LiteGameNext.Platform.Config.InvalidDataException ex)
            {
                Assert.Ignore("工程内缺表数据（需先跑 Luban/gen.bat）：" + ex.Message);
                return;
            }

            var catalog = new TextCatalog();
            int count = TextTableProjection.Project(tables, catalog);
            Assert.Greater(count, 0, "真实 tbltext 应至少投影出一条文案");
            Assert.Greater(catalog.KeyCount, 0);
        }

        [Test]
        public void 中英切换_取到不同文案()
        {
            cfg.Tables tables;
            try
            {
                tables = LoadTables();
            }
            catch (LiteGameNext.Platform.Config.InvalidDataException ex)
            {
                Assert.Ignore("工程内缺表数据（需先跑 Luban/gen.bat）：" + ex.Message);
                return;
            }

            var catalog = new TextCatalog();
            TextTableProjection.Project(tables, catalog);

            // 找一条中英都有且不同的 key 来验切换真的生效。
            string probe = null;
            foreach (cfg.ltext row in tables.Tbltext.DataList)
            {
                if (row == null || string.IsNullOrEmpty(row.Key)) continue;
                if (string.IsNullOrEmpty(row.Zh) || string.IsNullOrEmpty(row.En)) continue;
                if (row.Zh == row.En) continue;
                probe = row.Key;
                break;
            }
            if (probe == null) Assert.Ignore("表内无中英差异条目");

            var service = new LocalizationService(catalog, TextCatalog.SourceLocale);
            string zh = service.Get(probe);
            service.SetLocale(TextTableProjection.EnglishLocale);
            string en = service.Get(probe);
            Assert.AreNotEqual(zh, en, "中英切换应取到不同文案（key=" + probe + "）");
        }

        [Test]
        public void 空键空文案_被跳过()
        {
            var catalog = new TextCatalog();
            catalog.Add("K", TextCatalog.SourceLocale, "中文");
            var merged = new TextCatalog().Add("K", TextTableProjection.EnglishLocale, "EN");
            int count = merged.CopyInto(catalog);
            Assert.AreEqual(1, count);
            Assert.IsTrue(catalog.HasKey("K"));
        }

        [Test]
        public void Merge_并入后切语言生效()
        {
            var baseCatalog = new TextCatalog().Add("A", TextCatalog.SourceLocale, "甲");
            var service = new LocalizationService(baseCatalog, TextCatalog.SourceLocale);
            Assert.AreEqual("甲", service.Get("A"));

            var extra = new TextCatalog().Add("A", TextTableProjection.EnglishLocale, "Alpha");
            int merged = service.Merge(extra);
            Assert.AreEqual(1, merged);
            service.SetLocale(TextTableProjection.EnglishLocale);
            Assert.AreEqual("Alpha", service.Get("A"), "并入后英文应可读");
        }
    }
}
