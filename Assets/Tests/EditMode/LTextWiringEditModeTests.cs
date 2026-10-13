using System;
using System.Collections.Generic;
using System.IO;
using LiteFramework;
using LiteGame;
using LiteTesting.Unity;
using Luban;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using LiteClient;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 批C-② LText 生产装配链（《UI框架总设计》§9 文本链 + 《上云测试页面层级设计》§10"BindLocale 注入待办"收口）：
    /// ① LTextCatalog 真表投射（tbltext 行 → zh-CN/en 双 locale 目录）；② 表内 key 命名全过 LTextKey（表源守卫）；
    /// ③ LocalizationService 真目录行为（Get/Format/切语言/缺 key 开发期显 [key]）；
    /// ④ UIBindBase（路径 B）经 ILocaleBound 接受注入——OnInit 建索引后 BindLocale 即 SetTextKey 可用、
    /// OnInit 前注入为 no-op（Empty 守卫）；⑤ LuaBehaviourAdapter（路径 A）实现 ILocaleBound（页面级
    /// 全链验收走 PlayMode/夜间——索引面行为由 LTextBindEditModeTests 既有 11 例钉住）。
    /// </summary>
    public sealed class LTextWiringEditModeTests : UnityTestBase
    {
        /// <summary>表源现役键数（增删文案键须同批核对本值——《上云测试页面层级设计》§8 总表 + 批C 增补）。</summary>
        private const int ExpectedKeyCount = 41;

        private static cfg.Tables LoadRealTables()
        {
            // 与 ConfigService 同款装载（CombatNumbersEditModeTests 同款直读磁盘——YooAsset 异步拉不起，清单单源 TableLayout）
            string dir = Path.Combine(ProjectRoot(), ConfigService.DataDir);
            var cache = new Dictionary<string, byte[]>(ConfigService.TableDataFiles.Length);
            foreach (string file in ConfigService.TableDataFiles)
                cache[TableLayout.TableName(file)] = File.ReadAllBytes(Path.Combine(dir, file + ".bytes"));
            return new cfg.Tables(file => new ByteBuf(cache[file]));
        }

        private static string ProjectRoot()
        {
            var dir = new DirectoryInfo(Path.GetDirectoryName(Application.dataPath) ?? Application.dataPath);
            for (var cur = dir; cur != null; cur = cur.Parent)
                if (Directory.Exists(Path.Combine(cur.FullName, "Assets"))
                    && File.Exists(Path.Combine(cur.FullName, "Tests", "Tests.slnx")))
                    return cur.FullName;
            throw new DirectoryNotFoundException("找不到项目根");
        }

        [Test]
        public void 表投射_真表行数与双locale()
        {
            var catalog = LTextCatalog.Build(LoadRealTables());

            Assert.AreEqual(ExpectedKeyCount, catalog.KeyCount, "键数与表源不符——增删键须同批核对本用例");
            Assert.AreEqual(2, catalog.LocaleCount, "现役 zh/en 两语言列");

            Assert.IsTrue(catalog.TryGet("UI.Login.Title", "zh-CN", out string zh, out bool fb1));
            Assert.AreEqual("联机对战测试", zh);
            Assert.IsFalse(fb1, "源语言命中不属回退");
            Assert.IsTrue(catalog.TryGet("UI.Login.Title", "en", out string en, out _));
            Assert.AreEqual("Online Playtest", en);

            // 回退语义：语言列缺席的 locale 走源语言（LocalizationService 口径——目录层只报事实）
            Assert.IsTrue(catalog.TryGet("UI.Login.Title", "fr", out string fell, out bool fb2));
            Assert.AreEqual("联机对战测试", fell);
            Assert.IsTrue(fb2, "未支持语言回退源语言");
        }

        [Test]
        public void 表源守卫_key命名全过LTextKey()
        {
            var tables = LoadRealTables();
            foreach (cfg.ltext row in tables.Tbltext.DataList)
            {
                string problem = LTextKey.Validate(row.Key);
                Assert.IsNull(problem, $"表源 key 不合规：{row.Key} → {problem}（key 跑了会让覆盖率统计与批量替换失效）");
            }
        }

        [Test]
        public void 服务行为_真目录取文案_切语言_缺key占位()
        {
            var service = new LocalizationService(LTextCatalog.Build(LoadRealTables()),
                LocalizationCatalog.SourceLocale, developmentMode: true);

            Assert.AreEqual("房间 Room-A", service.Format("UI.Room.Info", "Room-A"), "zh 带参文案");
            Assert.AreEqual("游客一键进入", service.Get("UI.Login.GuestEnter"));

            service.SetLocale("en");
            Assert.AreEqual("Enter as Guest", service.Get("UI.Login.GuestEnter"), "切语言后查询口径随动");
            Assert.AreEqual("Room Room-A", service.Format("UI.Room.Info", "Room-A"));

            Assert.AreEqual("[No.Such.Key]", service.Get("No.Such.Key"), "开发期缺 key 显 [key]（发布期占位——服务口径）");
        }

        [Test]
        public void 路径B_ILocaleBound注入_OnInit后可用_OnInit前no_op()
        {
            var tables = LoadRealTables();
            var service = new LocalizationService(LTextCatalog.Build(tables),
                LocalizationCatalog.SourceLocale, developmentMode: true);

            GameObject root = Scope.CreateGameObject("ltw_root", typeof(Canvas), typeof(CanvasGroup));
            var logic = root.AddComponent<TextBindProbeLogic>();
            GameObject label = Scope.CreateGameObject("Label", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(root.transform, false);
            logic.Label = label.GetComponent<Text>();

            // OnInit 前注入 = no-op（Empty 全局件守卫——不得抛、不得串订阅）
            ((ILocaleBound)logic).BindLocale(service);

            var form = new UIForm(new UIFormInfo { Id = 901, LuaPath = "", Location = "", Layer = 0, FullScreen = true }, root);
            ((IUIFormLogic)logic).OnInit(form, null);              // 路径 B：登记面选型（RegisterControl）
            ((ILocaleBound)logic).BindLocale(service);              // UIForm 同款注入（ApplyLocale 的等价调用）

            logic.WriteTextKey("Label", "UI.Login.Title");
            Assert.AreEqual("联机对战测试", logic.Label.text, "注入后 SetTextKey 生产可用（此前必抛）");

            service.SetLocale("en");
            Assert.AreEqual("Online Playtest", logic.Label.text, "语言切换经索引自动重写——不重跑 OnShow");

            Assert.IsTrue(logic.UnbindKey("Label"), "解除绑定");
            service.SetLocale("zh-CN");
            Assert.AreEqual("Online Playtest", logic.Label.text, "解绑后不再随语言刷新");
        }

        [Test]
        public void 路径A_Lua适配器实现ILocaleBound()
        {
            // 编译期契约钉（适配器把 BindLocale 转发给索引——索引面行为由 LTextBindEditModeTests 钉住；
            // 页面级 Lua→adapter→索引全链随 PlayMode/夜间验收）
            Assert.IsTrue(typeof(ILocaleBound).IsAssignableFrom(typeof(LuaBehaviourAdapter)),
                "LuaBehaviourAdapter 必须实现 ILocaleBound（UIForm 注入缝的存在前提）");
            Assert.IsTrue(typeof(ILocaleBound).IsAssignableFrom(typeof(UIBindBase)),
                "UIBindBase 必须实现 ILocaleBound（路径 B 与路径 A 同面）");
        }

        /// <summary>路径 B 测试探针：登记面（RegisterControl）+ LText 写入口暴露。</summary>
        private sealed class TextBindProbeLogic : UIBindBase
        {
            public Text Label;

            protected override void CollectControls()
            {
                if (Label != null) RegisterControl("Label", Label);
            }

            public void WriteTextKey(string name, string key) => SetTextKey(name, key);
            public bool UnbindKey(string name) => UnbindTextKey(name);
        }
    }
}
