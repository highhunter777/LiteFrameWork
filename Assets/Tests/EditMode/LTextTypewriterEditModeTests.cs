using System.Collections.Generic;
using LiteFramework;
using LiteGame.UI;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// LTextLabel 打字机（U2-⑤d；《UI框架总设计》§9"打字机为表现行为，关闭/语言变化取消旧任务；
    /// 不改变业务数据"）。
    ///
    /// 全部用外部 Tick 驱动（AutoTick=false——单驱动者纪律），步进确定性无帧依赖；
    /// 富文本用标签配平断言（中间态不得出现半截标签）。
    /// </summary>
    public sealed class LTextTypewriterEditModeTests : UnityTestBase
    {
        private (GameObject go, Text text, LTextLabel label) Build(bool withTarget = true)
        {
            var go = Scope.CreateGameObject("label", typeof(RectTransform));
            Text text = null;
            if (withTarget) text = go.AddComponent<Text>();
            var label = go.AddComponent<LTextLabel>();
            label.AutoTick = false;                       // 测试外部驱动（双驱动违例在 Debug 下显性报错）
            return (go, text, label);
        }

        private static int TagCount(string s, char c)
        {
            int n = 0;
            foreach (char ch in s) if (ch == c) n++;
            return n;
        }

        // ---- 节拍与完成 ----

        [Test]
        [Category(TestCategory.Contract)]
        public void 节拍_按速率推进_完成即全量且事件恰一次()
        {
            var (_, text, label) = Build();
            label.CharsPerSecond = 10f;                    // 每 0.1s 一个字符
            int completed = 0;
            label.OnCompleted += () => completed++;

            label.Reveal("甲乙丙丁");
            Assert.IsTrue(label.IsPlaying);
            Assert.AreEqual("甲", text.text, "起播即显示首个可见字符");

            label.Tick(0.1f);
            Assert.AreEqual("甲乙", text.text);
            label.Tick(0.1f);
            Assert.AreEqual("甲乙丙", text.text);
            label.Tick(0.1f);                             // 推进到尾 → 完成
            Assert.AreEqual("甲乙丙丁", text.text, "完成态逐字符等于全量（数据不变）");
            Assert.IsFalse(label.IsPlaying);
            Assert.AreEqual(1, completed, "完成事件恰好一次");

            label.Tick(0.1f);                             // 完成后再步进无效
            Assert.AreEqual(1, completed);
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 富文本_截点不落标签内_中间态无半截标签()
        {
            var (_, text, label) = Build();
            label.CharsPerSecond = 10f;
            const string full = "<color=red>红</color>了";
            label.Reveal(full);

            Assert.AreEqual(2, TagCount(text.text, '<'), "起播可见段：开标签与闭标签成对（不截半截）");
            Assert.AreEqual(2, TagCount(text.text, '>'), "起播可见段：开/闭标签配平");

            while (label.IsPlaying) label.Tick(0.5f);     // 快进走完全部中间态
            Assert.AreEqual(full, text.text);
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 富文本_每一步中间态标签配平()
        {
            var (_, text, label) = Build();
            label.CharsPerSecond = 10f;
            label.Reveal("<color=red>红</color>了");

            int steps = 0;
            while (label.IsPlaying && steps++ < 20)
            {
                label.Tick(0.1f);
                Assert.AreEqual(TagCount(text.text, '<'), TagCount(text.text, '>'),
                    $"中间态[{steps}]不得出现半截标签: {text.text}");
            }
            Assert.AreEqual("<color=red>红</color>了", text.text);
        }

        // ---- 取消语义 ----

        [Test]
        [Category(TestCategory.Contract)]
        public void 新Reveal取消旧任务_旧任务不触发完成()
        {
            var (_, text, label) = Build();
            label.CharsPerSecond = 10f;
            int completed = 0;
            label.OnCompleted += () => completed++;

            label.Reveal("旧文本内容");
            label.Tick(0.1f);                             // 旧任务推进中
            Assert.IsTrue(label.IsPlaying);

            label.Reveal("新文");                          // 语言变更重入即此路径
            Assert.AreEqual("新", text.text, "新任务起播（首可见字符），旧进度不残留");
            Assert.AreEqual(0, completed, "旧任务被取消——未触发完成");

            while (label.IsPlaying) label.Tick(0.1f);
            Assert.AreEqual("新文", text.text);
            Assert.AreEqual(1, completed, "只有新任务完成一次");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 隐藏即取消_文本落全量_重开不残留半截()
        {
            var (go, text, label) = Build();
            label.CharsPerSecond = 10f;
            label.Reveal("半截内容甲乙");
            label.Tick(0.1f);
            Assert.IsTrue(label.IsPlaying);

            go.SetActive(false);                           // 页面关闭/隐藏 → 取消
            Assert.IsFalse(label.IsPlaying);
            Assert.AreEqual("半截内容甲乙", text.text, "取消落全量（数据不变——重开不残留半截文本）");

            go.SetActive(true);
            Assert.IsFalse(label.IsPlaying, "取消 ≠ 待播：重开不自播（重新 Reveal 才有打字）");
            Assert.AreEqual("半截内容甲乙", text.text);
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 速率非正_直接全量_完成语义()
        {
            var (_, text, label) = Build();
            label.CharsPerSecond = 0f;
            int completed = 0;
            label.OnCompleted += () => completed++;

            label.Reveal("直接全量文本");
            Assert.AreEqual("直接全量文本", text.text);
            Assert.IsFalse(label.IsPlaying);
            Assert.AreEqual(1, completed);
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 隐藏时Reveal登记待播_激活起播()
        {
            var (go, text, label) = Build();
            label.CharsPerSecond = 10f;
            go.SetActive(false);

            label.Reveal("待播内容甲乙");                   // 暗处不跑完
            Assert.IsFalse(label.IsPlaying);
            Assert.AreEqual(string.Empty, text.text, "未激活不写目标（待播不剧透）");

            go.SetActive(true);
            Assert.IsTrue(label.IsPlaying, "OnEnable 起播");
            Assert.AreEqual("待", text.text, "起播首可见字符");
        }

        // ---- 绑定路由（命令式经打字机 / 绑定路径瞬时 / 语言变更重入取消）----

        private static LocalizationCatalog Catalog()
        {
            var c = new LocalizationCatalog();
            c.Add("UI.Dialog.Hello", "zh-CN", "问好中文内容");
            c.Add("UI.Dialog.Hello", "en", "Hello there");
            return c;
        }

        private (UIBindIndex index, Text text, LTextLabel label) BuildBound()
        {
            var (go, text, label) = Build();
            label.CharsPerSecond = 10f;
            var controls = new Dictionary<string, Component> { { "msg", text } };
            var index = new UIBindIndex(controls);
            index.BindLocale(new LocalizationService(Catalog(), "zh-CN"));
            return (index, text, label);
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 命令式SetText_经打字机_首字符起播()
        {
            var (index, text, label) = BuildBound();

            index.SetText("msg", "路由文本内容");
            Assert.IsTrue(label.IsPlaying, "节点挂 LTextLabel 时命令式写文本进打字机");
            Assert.AreEqual("路", text.text);

            while (label.IsPlaying) label.Tick(0.1f);
            Assert.AreEqual("路由文本内容", text.text);
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 绑定路径BindText_恒瞬时_不走打字机()
        {
            var (index, text, label) = BuildBound();
            var binder = index.BindText<int>("msg", v => $"金币 {v}");

            binder.Set(42);
            Assert.IsFalse(label.IsPlaying, "数值绑定不走打字机（表现行为只属于叙事文本）");
            Assert.AreEqual("金币 42", text.text, "绑定值全量直写");
            binder.Dispose();
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 语言变更_SetTextKey重入路由_旧任务取消()
        {
            var (_, text, label) = Build();
            label.CharsPerSecond = 10f;
            var svc = new LocalizationService(Catalog(), "zh-CN");
            var index = new UIBindIndex(new Dictionary<string, Component> { { "msg", text } });
            index.BindLocale(svc);

            index.SetTextKey("msg", "UI.Dialog.Hello");   // zh-CN："问好中文内容"
            Assert.AreEqual("问", text.text);
            Assert.IsTrue(label.IsPlaying);

            svc.SetLocale("en");                            // 语言事件 → 绑定重写文本 → Reveal 重入（旧任务取消）
            Assert.AreEqual("H", text.text, "英文文本起播——旧中文任务被替换（取消语义）");
            Assert.IsTrue(label.IsPlaying, "同一组件在新任务下继续播");

            while (label.IsPlaying) label.Tick(0.1f);
            Assert.AreEqual("Hello there", text.text);
        }
    }
}
