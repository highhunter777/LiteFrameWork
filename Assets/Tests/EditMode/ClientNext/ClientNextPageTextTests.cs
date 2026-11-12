using System.Threading;
using Cysharp.Threading.Tasks;
using LiteGameNext.Platform.Config;
using LiteGameNext.Platform.Localization;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>页面接表文案：注入本地化后 PageBase.Text 取到表值；未注入时返回 key 不崩。</summary>
    public sealed class ClientNextPageTextTests
    {
        private sealed class ProbePage : LiteGameNext.Platform.Ui.PageBase
        {
            public string Resolve(string key) => Text(key);

            public string ResolveFormat(string key, params object[] args) => Text(key, args);
        }

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
        public void 未注入本地化_Text返回key不抛()
        {
            var page = new UnityEngine.GameObject("probe").AddComponent<ProbePage>();
            try
            {
                Assert.AreEqual("UI.Login.Title", page.Resolve("UI.Login.Title"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(page.gameObject);
            }
        }

        [Test]
        public void 注入本地化_Text取到表文案()
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
            var loc = new LocalizationService(catalog, TextCatalog.SourceLocale);

            var page = new UnityEngine.GameObject("probe").AddComponent<ProbePage>();
            try
            {
                typeof(LiteGameNext.Platform.Ui.PageBase)
                    .GetMethod("AttachLocalization",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .Invoke(page, new object[] { loc });

                Assert.AreEqual("联机对战测试", page.Resolve("UI.Login.Title"));
                loc.SetLocale(TextTableProjection.EnglishLocale);
                Assert.AreEqual("Online Playtest", page.Resolve("UI.Login.Title"),
                    "切到英文后同一 key 应取英文");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(page.gameObject);
            }
        }

        [Test]
        public void 表内带占位符key_可格式化()
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
            var loc = new LocalizationService(catalog, TextCatalog.SourceLocale);
            string version = loc.Format("Common.Version", "0.1.0");
            StringAssert.Contains("0.1.0", version, "占位符应被替换（实际：" + version + "）");
        }
    }
}
