using Cysharp.Threading.Tasks;
using LiteGame.UI;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 路径 B 接线用例（《UI框架总设计》§4.6 Designer 登记形态）：
    /// **prefab 自带 UIBindBase 派生页**（组件挂根，覆写 CollectControls 逐控件 RegisterControl）→
    /// UIService 实例化后经"自带 IUIFormLogic 组件"解析缝取用（优先于表行 Lua 逻辑）——
    /// OnInit 走路径 B 索引、OnShow 经受控 API 写回。证明"挂件即被驱动"的整条链。
    /// 全替身（fake catalog/loader/假件 prefab，同 FeedbackServiceEditModeTests 形态）。
    /// </summary>
    [Category(TestCategory.Contract)]
    public sealed class UiBindBasePathBEditModeTests : UnityTestBase
    {
        private const int FormId = 9;

        private sealed class FakeCatalog : IUIFormCatalog
        {
            public UIFormInfo Get(int id)
                => new UIFormInfo { Id = id, Layer = 1, FullScreen = false, Location = "x" + id, LuaPath = "" };
        }

        private sealed class InstantTransition : ITransitionStrategy
        {
            public UniTask PlayShow(UIForm form, System.Threading.CancellationToken ct) => UniTask.CompletedTask;
            public UniTask PlayClose(UIForm form, System.Threading.CancellationToken ct) => UniTask.CompletedTask;
        }

        /// <summary>路径 B 测试页：<see cref="UIBindBase.CollectControls"/> 登记（生成类同形）、
        /// OnInit/OnShow 经受控 API 读写。**嵌套类**——编辑器专用测试程序集拒绝 AddComponent 顶层脚本；
        /// 登记钩子由基类在 OnInit 内调用，不依赖 Unity Awake 派发（嵌套类收不到）。</summary>
        private sealed class CarriedPage : UIBindBase
        {
            public TMP_Text Title;
            public string SeenOnInit;
            public string SeenOnShow;
            public int ShowCount;

            protected override void CollectControls() => RegisterControl("Title", Title);

            protected override void OnInit(IUIData data) => SeenOnInit = GetControl<TMP_Text>("Title").text;

            protected override void OnShow(IUIData data)
            {
                ShowCount++;
                SeenOnShow = GetControl<TMP_Text>("Title").text;
                SetText("Title", "路径 B 生效");
            }
        }

        [Test]
        public void 路径B_挂件页被驱动_登记控件经受控API写回()
        {
            // 假件 prefab：根带页组件 + 页面根必备的 Canvas/CanvasGroup（UIForm 构造期 fail-fast 校验）；
            // 非激活期完成字段赋值，激活即就绪（注册件挂 prefab 根）
            var pageGo = new GameObject("CarriedPagePrefab", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            pageGo.SetActive(false);
            var labelGo = new GameObject("Title", typeof(RectTransform));
            labelGo.transform.SetParent(pageGo.transform, false);
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text = "初始标题";
            var source = pageGo.AddComponent<CarriedPage>();
            source.Title = label;
            pageGo.SetActive(true);                               // 激活即就绪：注册件挂 prefab 根

            var ui = new UIService(new FakeCatalog(),
                transitionStrategy: new InstantTransition(),
                loadPrefab: (loc, ct) => UniTask.FromResult<IUIPrefabLease>(UIPrefabLeases.Unowned(pageGo)));

            var show = ui.ShowAsync(FormId);
            Pump(ui, show);                                       // 泵到完成

            var driven = FindDriven(source);                      // 定位 UIService 驱动中的克隆页
            Assert.IsNotNull(driven, "UIService 实例化的克隆页未找到（解析缝未取用挂件）");
            Assert.AreEqual(1, driven.ShowCount, "挂件页未被驱动（OnInit/OnShow 未走）");
            Assert.AreEqual("初始标题", driven.SeenOnInit, "OnInit 经路径 B 索引读到登记控件");
            Assert.AreEqual("初始标题", driven.SeenOnShow, "OnShow 同链");
            var drivenLabel = driven.GetComponentInChildren<TextMeshProUGUI>(true);
            Assert.AreEqual("路径 B 生效", drivenLabel.text, "OnShow 的受控 API 写回落到登记控件");

            var close = ui.CloseAsync(FormId, UIService.CloseReason.User);
            Pump(ui, close);
            Assert.IsFalse(ui.IsOpen(FormId), "已关闭");
        }

        // ---- 泵动（EditMode 无 PlayerLoop：等待一律显式泵）----

        private static void Pump(UIService ui, UniTask task)
        {
            for (int i = 0; i < 200 && task.Status == UniTaskStatus.Pending; i++) ui.Tick(0.05f);
            Assert.AreNotEqual(UniTaskStatus.Pending, task.Status, "任务未完成——用例时序有误");
        }

        /// <summary>定位 UIService 驱动中的克隆页（原 prefab 件与本件同名同型——按引用排除）。</summary>
        private static CarriedPage FindDriven(CarriedPage source)
        {
            foreach (var p in Object.FindObjectsByType<CarriedPage>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!ReferenceEquals(p, source))
                    return p;
            return null;
        }
    }
}
