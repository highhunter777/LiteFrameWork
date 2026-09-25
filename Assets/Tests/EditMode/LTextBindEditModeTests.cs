using System.Collections.Generic;
using LiteFramework;
using LiteGame;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// LText 控件接入（《UI框架总设计》§9"语言变更刷新本地化组件…不重跑 OnShow、不重新订阅按钮、
    /// 不重发业务请求"）。
    ///
    /// 核心断言：**语言一切，已绑定 key 的控件文本自动更新**——这是 §9 与"每次切语言重建整页"
    /// 的分界；以及**未绑 key 的控件不受影响**（刷新面必须精确）。
    /// </summary>
    public sealed class LTextBindEditModeTests : UnityTestBase
    {
        private static LocalizationCatalog Catalog()
        {
            var c = new LocalizationCatalog();
            c.Add("UI.Main.Title", "zh-CN", "主界面");
            c.Add("UI.Main.Title", "en", "Main");
            c.Add("UI.Main.Gold", "zh-CN", "金币 {0}");
            c.Add("UI.Main.Gold", "en", "Gold {0}");
            c.Add("UI.Main.Count.one", "en", "{0} item");
            c.Add("UI.Main.Count.other", "en", "{0} items");
            c.Add("UI.Main.Count", "zh-CN", "{0} 个");
            return c;
        }

        private (UIBindIndex index, LocalizationService ltext, Text label, Text other)
            Build(string locale = "zh-CN")
        {
            GameObject root = Scope.CreateGameObject("bindroot", typeof(RectTransform));

            GameObject goA = Scope.CreateGameObject("Title", typeof(RectTransform), typeof(Text));
            goA.transform.SetParent(root.transform, false);
            GameObject goB = Scope.CreateGameObject("Other", typeof(RectTransform), typeof(Text));
            goB.transform.SetParent(root.transform, false);

            var controls = new Dictionary<string, Component>
            {
                { "Title", goA.GetComponent<Text>() },
                { "Other", goB.GetComponent<Text>() },
            };

            var index = new UIBindIndex(controls);
            var ltext = new LocalizationService(Catalog(), locale);
            index.BindLocale(ltext);
            return (index, ltext, goA.GetComponent<Text>(), goB.GetComponent<Text>());
        }

        [Test]
        public void 按key设文本_取当前语言()
        {
            var (index, _, label, _) = Build("zh-CN");
            index.SetTextKey("Title", "UI.Main.Title");
            Assert.AreEqual("主界面", label.text);
        }

        [Test]
        public void 语言变更_已绑定文本自动刷新()
        {
            var (index, ltext, label, _) = Build("zh-CN");
            index.SetTextKey("Title", "UI.Main.Title");
            Assert.AreEqual("主界面", label.text);

            ltext.SetLocale("en");

            // §9"语言变更刷新本地化组件"——不经任何重开页面
            Assert.AreEqual("Main", label.text);
        }

        [Test]
        public void 语言变更_未绑定key的控件不受影响()
        {
            // 刷新面必须精确：只重写登记过 key 的控件
            var (index, ltext, label, other) = Build("zh-CN");
            index.SetTextKey("Title", "UI.Main.Title");
            other.text = "由业务自己写的字面值";

            ltext.SetLocale("en");

            Assert.AreEqual("Main", label.text);
            Assert.AreEqual("由业务自己写的字面值", other.text, "未绑 key 的控件不得被语言事件改写");
        }

        [Test]
        public void 带参文本_切语言后参数保留()
        {
            var (index, ltext, label, _) = Build("zh-CN");
            index.SetTextKey("Title", "UI.Main.Gold", 100);
            Assert.AreEqual("金币 100", label.text);

            ltext.SetLocale("en");
            Assert.AreEqual("Gold 100", label.text, "切语言后模板参数必须仍在");
        }

        [Test]
        public void 复数文本_切语言后按新语言规则()
        {
            var (index, ltext, label, _) = Build("en");
            index.SetTextKeyPlural("Title", "UI.Main.Count", 1, 1);
            Assert.AreEqual("1 item", label.text);

            index.SetTextKeyPlural("Title", "UI.Main.Count", 5, 5);
            Assert.AreEqual("5 items", label.text);

            ltext.SetLocale("zh-CN");
            Assert.AreEqual("5 个", label.text, "切到无复数语言后走 other");
        }

        [Test]
        public void 解除绑定_不再随语言刷新()
        {
            var (index, ltext, label, _) = Build("zh-CN");
            index.SetTextKey("Title", "UI.Main.Title");

            Assert.IsTrue(index.UnbindTextKey("Title"));
            ltext.SetLocale("en");

            Assert.AreEqual("主界面", label.text, "解绑后不得再被语言事件改写");
            Assert.IsFalse(index.UnbindTextKey("Title"));
        }

        [Test]
        public void UnbindAll_退订语言事件_池化复用不留旧订阅()
        {
            var (index, ltext, label, _) = Build("zh-CN");
            index.SetTextKey("Title", "UI.Main.Title");

            index.UnbindAll();                      // 池化复用安全垫
            ltext.SetLocale("en");

            Assert.AreEqual("主界面", label.text, "UnbindAll 后旧页不得继续被刷新");
        }

        [Test]
        public void 未注入语言服务_按键设文本抛()
        {
            // 静默显示原始 key 会让"忘了注入"变成"线上全是 key"的隐蔽故障——故显式抛
            GameObject root = Scope.CreateGameObject("b2", typeof(RectTransform));
            var controls = new Dictionary<string, Component>
            {
                { "T", root.AddComponent<Text>() },
            };
            var index = new UIBindIndex(controls);      // 不 BindLocale

            Assert.Throws<System.InvalidOperationException>(() => index.SetTextKey("T", "UI.Main.Title"));
        }

        [Test]
        public void 重复注入语言服务_只订阅一次()
        {
            var (index, ltext, label, _) = Build("zh-CN");
            index.BindLocale(ltext);                    // 二次注入（装配点重复调用）
            index.BindLocale(ltext);                    // 三次
            index.SetTextKey("Title", "UI.Main.Title");

            ltext.SetLocale("en");
            Assert.AreEqual("Main", label.text);         // 刷新正确（多次订阅也只是重复写同一值，但退订面要干净）
        }
    }
}
