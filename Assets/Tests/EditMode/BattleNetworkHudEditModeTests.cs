using LiteNet;
using LiteTesting.Unity;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 网络质量 HUD 驱动的 L2 EditMode 覆盖（ping/丢包批）：质量源走**构造注入**（免真会话——确定性驱动）。
    /// 钉住：离线/无样本整组隐藏（不显示陈旧值）、千分比 → 百分比一位小数、值/连通性变化才写、Dispose 归位。
    /// 真场景观感（字号/落位）归 PlayMode/手测。
    /// </summary>
    public sealed class BattleNetworkHudEditModeTests : UnityTestBase
    {
        [Test]
        public void 网络质量_离线与无样本隐藏_有样本才显示()
        {
            GameObject root = Scope.CreateGameObject("NetStats", typeof(RectTransform));
            GameObject textGo = Scope.CreateGameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(root.transform, false);
            var text = textGo.GetComponent<TextMeshProUGUI>();

            NetworkQuality quality = NetworkQuality.None;
            var driver = new BattleNetworkHudDriver(() => quality, root, text);

            driver.Tick();
            Assert.IsFalse(root.activeSelf, "无样本（未进房）整组隐藏");

            quality = new NetworkQuality(48, 3, true);
            driver.Tick();
            Assert.IsTrue(root.activeSelf, "在线且有样本 → 显示");
            Assert.AreEqual("48ms / 0.3%", text.text, "千分比 → 百分比一位小数（3‰ = 0.3%）");

            quality = new NetworkQuality(120, 1000, true);
            driver.Tick();
            Assert.AreEqual("120ms / 100.0%", text.text, "值变才重写（千分比 1000 = 100%）");

            quality = new NetworkQuality(48, 3, false);          // 断线：同一 RTT/丢包但已离线
            driver.Tick();
            Assert.IsFalse(root.activeSelf, "离线整组隐藏（不显示陈旧值）");

            driver.Dispose();
            Assert.IsFalse(root.activeSelf, "Dispose 归位");
            Assert.DoesNotThrow(() => driver.Dispose(), "Dispose 幂等");
        }

        [Test]
        public void 网络质量_无源形态静默空转()
        {
            GameObject root = Scope.CreateGameObject("NetStats", typeof(RectTransform));
            GameObject textGo = Scope.CreateGameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(root.transform, false);

            var driver = new BattleNetworkHudDriver(null, root, textGo.GetComponent<TextMeshProUGUI>());
            Assert.DoesNotThrow(() => driver.Tick(), "无质量源（旧装配/测试形态）→ 静默空转，不抛");
            Assert.IsFalse(root.activeSelf);
            driver.Dispose();
        }
    }
}
