using System.Threading;
using LiteGameNext.Features.Battle.Hud;
using LiteGameNext.Platform.Ui;
using NUnit.Framework;
using TMPro;

namespace LiteGame.Tests.EditMode
{
    /// <summary>S3 战斗 HUD：模型值域 + 真实 prefab 绑定/刷新；不依赖战斗会话。</summary>
    public sealed class ClientNextBattleHudTests
    {
        [Test]
        public void 模型_生命与弹药钳制()
        {
            var model = new BattleHudModel();
            model.SetVitals(-5, 100, -2);
            Assert.AreEqual(0, model.Health);
            Assert.AreEqual(0, model.Ammo);

            model.SetVitals(120, 100, 30);
            Assert.AreEqual(100, model.Health);
            Assert.AreEqual(30, model.Ammo);
        }

        [Test]
        public void HUD页面_绑定模型后刷新文本()
        {
            var ui = new UiRuntime(EditorPageFactory.CreateAsync);
            ui.Initialize();
            var model = new BattleHudModel();
            model.SetVitals(75, 100, 12);
            model.SetScore(3, 1);
            try
            {
                ui.OpenAsync<BattleHudView>(BattleHudView.Spec, page => page.Bind(model), CancellationToken.None)
                    .GetAwaiter().GetResult();
                var page = (BattleHudView)ui.Top.Page;
                Assert.AreEqual("75 / 100", Find(page, "Txt_Health").text);
                Assert.AreEqual("AMMO 12", Find(page, "Txt_Ammo").text);
                Assert.AreEqual("K 3  D 1", Find(page, "Txt_Score").text);
            }
            finally
            {
                ui.Shutdown();
            }
        }

        private static TMP_Text Find(PageBase page, string bindName)
        {
            TMP_Text[] labels = page.GetComponentsInChildren<TMP_Text>(true);
            for (int index = 0; index < labels.Length; index++)
            {
                BindName bind = labels[index].GetComponent<BindName>();
                if (bind != null && bind.Name == bindName) return labels[index];
            }

            throw new System.InvalidOperationException("找不到文本：" + bindName);
        }
    }
}
