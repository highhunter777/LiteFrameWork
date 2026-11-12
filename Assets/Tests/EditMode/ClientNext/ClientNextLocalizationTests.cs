using LiteGameNext.Platform.Localization;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>本地化：回退链/缺文案诊断/切语言事件/填参与单复数。</summary>
    public sealed class ClientNextLocalizationTests
    {
        private static LocalizationService Service(bool dev = true)
        {
            var catalog = new TextCatalog()
                .Add("UI.Lobby.Title", "zh-CN", "大厅")
                .Add("UI.Lobby.Title", "en", "Lobby")
                .Add("Common.Players", "en.one", "1 player")
                .Add("Common.Players", "en.other", "{0} players")
                .Add("Common.Players", "zh-CN", "{0} 名玩家");
            return new LocalizationService(catalog, "zh-CN", dev);
        }

        [Test]
        public void 命中目标语言_不回退()
        {
            LocalizationService svc = Service();
            svc.SetLocale("en");
            Assert.AreEqual("Lobby", svc.Get("UI.Lobby.Title"));
            Assert.AreEqual(0, svc.MissingKeyCount);
        }

        [Test]
        public void 目标语言缺失_回退源语言()
        {
            LocalizationService svc = Service();
            svc.SetLocale("en");
            // Common.Players 的 en 只有 .one/.other 后缀键，主键缺失 → 回退 zh-CN
            Assert.AreEqual("{0} 名玩家", svc.Raw("Common.Players"));
        }

        [Test]
        public void 缺文案_开发期显示占位并计数()
        {
            LocalizationService svc = Service(dev: true);
            Assert.AreEqual("[UI.No.Such]", svc.Raw("UI.No.Such"));
            Assert.AreEqual(1, svc.MissingKeyCount);
        }

        [Test]
        public void 缺文案_发布期不暴露内部key()
        {
            LocalizationService svc = Service(dev: false);
            Assert.AreEqual("…", svc.Raw("UI.No.Such"));
        }

        [Test]
        public void 切语言_触发事件且同值不重复触发()
        {
            LocalizationService svc = Service();
            int fired = 0;
            string seen = null;
            svc.LocaleChanged += locale => { fired++; seen = locale; };
            svc.SetLocale("en");
            Assert.AreEqual(1, fired);
            Assert.AreEqual("en", seen);
            svc.SetLocale("en");
            Assert.AreEqual(1, fired, "同值切语言不应重复触发");
        }

        [Test]
        public void 填参_正常替换()
        {
            LocalizationService svc = Service();
            Assert.AreEqual("3 名玩家", svc.Format("Common.Players", 3));
        }

        [Test]
        public void 英文单复数_按数量选后缀()
        {
            LocalizationService svc = Service();
            svc.SetLocale("en");
            Assert.AreEqual("1 player", svc.FormatPlural("Common.Players", 1));
            Assert.AreEqual("5 players", svc.FormatPlural("Common.Players", 5));
        }

        [Test]
        public void 中文无复数_一律走主键()
        {
            LocalizationService svc = Service();
            Assert.AreEqual("7 名玩家", svc.FormatPlural("Common.Players", 7));
        }
    }
}
