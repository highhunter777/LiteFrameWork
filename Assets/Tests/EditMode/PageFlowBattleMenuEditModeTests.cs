using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using LiteClient;
using LiteFramework;
using LiteGame;
using LiteClient.UI;
using LiteTesting.Unity;
using Luban;
using NUnit.Framework;
using UnityEngine;
using XLua;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// HUD 批C 退场链：PageFlow 对局菜单接缝——对局中"ESC＝菜单"Back 策略（swap/restore/幂等）与
    /// <see cref="PageFlow.OpenBattleSettings"/> 导航数据（battleExit 只经对局中入口产生——设置页
    /// 退场钮门控依据）。真 PageFlow + 真导航/UIService（替身目录：设置页＝合成 form、非空
    /// LuaPath 走探针逻辑记录导航数据）+ 真 LuaComponent（仅用 Env 建数据表）+ 真表反查
    /// （Bridge.Data.ResolveUIFormId——生成页面目录单源）。对局视图/真 prefab 装配归 PlayMode/夜间。
    /// </summary>
    public sealed class PageFlowBattleMenuEditModeTests : UnityTestBase
    {
        private const int SettingsFormId = 15;

        private UIService _ui;
        private UINavigationController _nav;
        private PageFlow _flow;
        private ProbeLogic _probe;
        private Func<UniTask<bool>> _baselineBack;

        /// <summary>探针逻辑：记录 OnShow 收到的导航数据（空 LuaPath 会被 ResolveLogic 短路，故用非空路径走 resolver）。</summary>
        private sealed class ProbeLogic : IUIFormLogic
        {
            public IUIData LastData;
            public int Shows;

            public void OnInit(UIForm form, IUIData data) { }
            public void OnShow(IUIData data) { Shows++; LastData = data; }
            public void OnUpdate(float deltaTime) { }
            public void OnPause() { }
            public void OnCover() { }
            public void OnReveal() { }
            public void OnHide() { }
            public void Release() { }
        }

        private sealed class StaticCatalog : IPageCatalog
        {
            private readonly PageDefinition[] _rows;
            public StaticCatalog(PageDefinition[] rows) => _rows = rows;
            public PageDefinition Get(int id)
            {
                foreach (var row in _rows) if (row.Id == id) return row;
                return null;
            }
        }

        [SetUp]
        public void SetUp()
        {
            _probe = new ProbeLogic();
            _ui = new UIService(new StaticCatalog(new[]
                {
                    new PageDefinition { Id = SettingsFormId, Layer = 1, FullScreen = true, LuaPath = "probe.settings", Location = "probe" },
                }),
                logicResolver: info => _probe,
                loadPrefab: (loc, ct) => UniTask.FromResult<IUIPrefabLease>(UIPrefabLeases.Unowned(FakePrefab())),
                transitionStrategy: new InstantTransition());
            _nav = new UINavigationController(_ui);
            _baselineBack = () => _nav.BackAsync();               // UiServiceModule 同款基线装订
            _ui.Focus.PlatformBack = _baselineBack;

            var lua = Scope.CreateGameObject("lua-host").AddComponent<LuaComponent>();
            lua.Init(new LuaPreloader((path, ct) => UniTask.FromResult<byte[]>(null), () => Array.Empty<string>()),
                new EventBus(new ReferencePoolService()));
            Bridge.Bind(LoadRealTables);                            // 页面寻址反查（真表——与生产同源）

            _flow = new PageFlow(_nav, _ui, lua);
        }

        [TearDown]
        public void TearDown()
        {
            if (_ui != null)
            {
                var shutdown = _ui.ShutdownAsync();
                for (int i = 0; i < 200 && shutdown.Status == UniTaskStatus.Pending; i++) _ui.Tick(0.05f);
            }
            _ui = null;
            _nav = null;
            _flow = null;
        }

        private GameObject FakePrefab()
            => Scope.CreateGameObject("probe_settings", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));

        private T Await<T>(UniTask<T> task, int maxTicks = 200)
        {
            for (int i = 0; i < maxTicks && task.Status == UniTaskStatus.Pending; i++) _ui.Tick(0.05f);
            Assert.AreNotEqual(UniTaskStatus.Pending, task.Status, "任务未完成——用例时序有误");
            return task.GetAwaiter().GetResult();
        }

        private void Pump(int maxTicks = 200)
        {
            for (int i = 0; i < maxTicks; i++) _ui.Tick(0.05f);
        }

        private static cfg.Tables LoadRealTables()
        {
            // 与 ConfigService 同款装载（LTextWiringEditModeTests 同款直读磁盘——清单单源 TableLayout）
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
        public void 对局Back_空栈_开菜单页并带battleExit数据()
        {
            _flow.EnableBattleMenuBack();
            Assert.AreNotEqual(_baselineBack, _ui.Focus.PlatformBack, "对局中平台返回换成菜单策略");

            bool consumed = Await(_ui.Focus.PlatformBack());
            Assert.IsTrue(consumed, "空栈 Back 也被消费（开菜单）");
            Assert.IsTrue(_ui.IsOpen(SettingsFormId), "空栈 Back → 设置页（对局菜单）");

            var luaData = _probe.LastData as LuaUIData;
            Assert.IsNotNull(luaData, "导航数据经 LuaUIData 包装（Lua 侧拿到原生表）");
            Assert.AreEqual(true, luaData.Table.Get<string, bool>("battleExit"),
                "对局中入口带 battleExit——设置页退场钮门控依据");
        }

        [Test]
        public void 对局Back_菜单开着_先关菜单不重开()
        {
            _flow.EnableBattleMenuBack();
            _flow.OpenBattleSettings();
            Pump();
            Assert.IsTrue(_ui.IsOpen(SettingsFormId), "菜单页已开（前置）");

            bool consumed = Await(_ui.Focus.PlatformBack());
            Assert.IsTrue(consumed, "有可返回目标时走原 Back 链（同样消费）");
            Assert.IsFalse(_ui.IsOpen(SettingsFormId), "Back 先关菜单页——原 Back 链语义保持，不重开");
        }

        [Test]
        public void 菜单Back策略_启用幂等_停用还原基线()
        {
            _flow.EnableBattleMenuBack();
            var wrapped = _ui.Focus.PlatformBack;
            _flow.EnableBattleMenuBack();
            Assert.AreEqual(wrapped, _ui.Focus.PlatformBack, "重复启用幂等（不叠加包装）");

            _flow.DisableBattleMenuBack();
            Assert.AreEqual(_baselineBack, _ui.Focus.PlatformBack, "停用还原基线装订（对局外 ESC 恢复原 Back）");

            // 停用后空栈 Back 不得再开菜单页
            Await(_baselineBack());
            Assert.IsFalse(_ui.IsOpen(SettingsFormId), "停用后空栈 Back 不开设置页");
        }

        [Test]
        public void OnMatchEnded_菜单开着_随对局结束收口()
        {
            _flow.OpenBattleSettings();
            Pump();
            Assert.IsTrue(_ui.IsOpen(SettingsFormId), "菜单页已开（前置）");

            _flow.OnMatchEnded(completed: false, result: null, localPlayerId: 1);
            Pump();
            Assert.IsFalse(_ui.IsOpen(SettingsFormId), "对局结束关菜单页——结算/大厅不被设置页压着");
        }
    }
}
