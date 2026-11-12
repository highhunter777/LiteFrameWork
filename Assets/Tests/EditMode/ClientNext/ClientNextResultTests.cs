using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteGameNext.Features.Battle;
using LiteGameNext.Features.Result;
using LiteGameNext.Platform.Ui;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>结算页：只消费 BattleResult；显示终态/明细；页面关闭后 ShowAsync 返回。</summary>
    public sealed class ClientNextResultTests
    {
        [Test]
        public void 完成结果_页面可装载且显示明细()
        {
            var ui = new UiRuntime(EditorPageFactory.CreateAsync);
            ui.Initialize();
            var feature = new ResultFeature(ui);
            var result = BattleResult.Completed("p1", new List<BattleSettlementRow>
            {
                new BattleSettlementRow("p1", 3, 1, 100),
            });
            try
            {
                UniTask run = feature.ShowAsync(result, CancellationToken.None);
                var page = (ResultPage)ui.Top.Page;
                Assert.IsNotNull(page);
                Assert.AreEqual(result, feature.LastResult);
                Assert.IsTrue(FindText(page, "Txt_Detail").Contains("p1  K 3 / D 1"),
                    "结算页未显示结算行");

                ui.CloseAsync(page.Handle).GetAwaiter().GetResult();
                Assert.AreEqual(UniTaskStatus.Succeeded, run.Status);
                run.GetAwaiter().GetResult();
            }
            finally
            {
                feature.Dispose();
                ui.Shutdown();
            }
        }

        private static string FindText(PageBase page, string bindName)
        {
            TMPro.TMP_Text[] labels = page.GetComponentsInChildren<TMPro.TMP_Text>(true);
            for (int index = 0; index < labels.Length; index++)
            {
                BindName bind = labels[index].GetComponent<BindName>();
                if (bind != null && bind.Name == bindName) return labels[index].text;
            }

            throw new System.InvalidOperationException("找不到文本：" + bindName);
        }

        [Test]
        public void 失败结果_可展示原因()
        {
            var ui = new UiRuntime(EditorPageFactory.CreateAsync);
            ui.Initialize();
            var feature = new ResultFeature(ui);
            try
            {
                UniTask run = feature.ShowAsync(BattleResult.Failed("connect refused"), CancellationToken.None);
                var page = (ResultPage)ui.Top.Page;
                Assert.IsNotNull(page);
                ui.CloseAsync(page.Handle).GetAwaiter().GetResult();
                run.GetAwaiter().GetResult();
                Assert.AreEqual(BattleOutcome.SessionFailed, feature.LastResult.Outcome);
            }
            finally
            {
                feature.Dispose();
                ui.Shutdown();
            }
        }
    }
}
