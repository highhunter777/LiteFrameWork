using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteGame;
using LiteGame.UI;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace LiteGame.Tests.UI.PlayMode
{
    /// <summary>
    /// 列表真资源段 PlayMode 用例（§8.2 窗口复用在真实 PlayerLoop 下的滚动往返）。
    ///
    /// 与 EditMode 虚拟列表用例的差异：本组走**真 prefab**（ListScreen = 页面根 + VirtualList 模板，
    /// 构建器确定性生成）+ **真实布局与滚动管线**（ScrollRect.onValueChanged → Update 窗口重算），
    /// 且经 UIService 打开（壳的页面生命周期与列表控件在同一链路——U0 退出条件"列表可往返"的 PlayMode 段）。
    ///
    /// 「可扩展」接入示例的载体：ListScreen 页面新增只动构建器与测试目录条目——框架零改动。
    /// </summary>
    [Category(TestCategory.Contract)]
    public sealed class ListPlayModeTests
    {
        private const int PageList = 30;   // ListScreen：构建器生成的列表页（空 LuaPath 展示面）

        private sealed class StaticCatalog : IUIFormCatalog
        {
            private readonly UIFormInfo[] _rows;
            public StaticCatalog(UIFormInfo[] rows) => _rows = rows;
            public UIFormInfo Get(int id)
            {
                foreach (var row in _rows) if (row.Id == id) return row;
                throw new System.Collections.Generic.KeyNotFoundException($"StaticCatalog 无行:{id}");
            }
        }

        private sealed class PlainLease : IUIPrefabLease
        {
            private readonly GameObject _prefab;
            public PlainLease(GameObject prefab) => _prefab = prefab;
            public GameObject Prefab => _prefab;
            public void Release() { }
        }

        /// <summary>测试数据源：行内容 = "条目{index}"；Bind 计数供"重绑不重复"断言（§8.2），
        /// <see cref="ZeroIndexText"/> 记录**索引 0** 的绑定文本（首行断言看它——绑定循环按
        /// 索引升序执行，"最后一次绑定"落在窗口最高索引上，不能用作首行证据）。</summary>
        private sealed class TestSource : IVirtualListSource
        {
            public int Count = 500;
            public long BindCalls;
            public string ZeroIndexText;

            int IVirtualListSource.Count => Count;

            void IVirtualListSource.Bind(int index, Component item)
            {
                BindCalls++;
                var label = item.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
                if (label != null)
                {
                    var text = $"条目{index}";
                    label.text = text;
                    if (index == 0) ZeroIndexText = text;   // 首行证据：索引 0 每次入窗绑定时刷新
                }
            }
        }

        private PlayModeTestScope _scope;
        private UIService _ui;
        private TestSource _source;
        private bool _assetsReady;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _scope = new PlayModeTestScope(nameof(ListPlayModeTests));

            if (!_assetsReady)
            {
                var init = AssetService.InitAsync();
                yield return WaitUniTask(init, 60f);
                Assert.IsFalse(init.Status == UniTaskStatus.Pending, "资源包初始化超时（60s）");
                _assetsReady = true;
            }

            _ui = new UIService(
                new StaticCatalog(new[]
                {
                    new UIFormInfo { Id = PageList, Layer = 0, FullScreen = true,
                        LuaPath = "", Location = "Assets/UI/Screens/ListScreen.prefab" },
                }),
                loadPrefab: LoadRealPrefabAsync,
                transitionStrategy: new FadeSlideTransition());   // 生产同款（默认已改零动效，见 §5.1）

            var host = _scope.CreateGameObject("PlayModeHost");
            PlayModeTicker.Attach(host, _ui.Tick);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_ui != null)
            {
                var shutdown = _ui.ShutdownAsync();
                yield return WaitUniTask(shutdown, 20f);
                _ui = null;
            }
            yield return _scope.DisposeAsync();
        }

        // ---- 用例 ----

        [UnityTest]
        public IEnumerator 列表_真资源窗口复用_节点有界且首尾往返()
        {
            var open = _ui.ShowAsync(PageList);
            yield return WaitUniTask(open, 20f);
            var form = open.GetAwaiter().GetResult();

            var list = form.Root.GetComponentInChildren<VirtualList>(true);
            Assert.IsNotNull(list, "ListScreen 页面应含 VirtualList（构建器接线）");
            var scroll = form.Root.GetComponentInChildren<ScrollRect>(true);
            Assert.IsNotNull(scroll, "VirtualList 模板自带 ScrollRect");

            _source = new TestSource();
            list.SetSource(_source);                       // SetSource 内部 Refresh
            yield return null;                             // 布局/窗口在真 PlayerLoop 下收敛一帧
            Canvas.ForceUpdateCanvases();                  // Content 总尺寸立即生效——ScrollRect 的
                                                           // normalizedPosition 换算依赖 content 高度，
                                                           // 布局未收敛时设置被 clamp 到顶部（Offset=0 假滚动）

            Assert.AreEqual(500, list.DataCount, "数据量 500");
            Assert.Less(list.RealizedCount, 500, "虚拟化核心读数：节点由视口容量决定，不随数据量线性创建（§8.2）");
            Assert.AreEqual(0, list.FirstIndex, "顶部窗口首索引为 0");
            Assert.AreEqual("条目0", _source.ZeroIndexText, "首行绑定内容正确（索引 0 在窗口内且绑定为条目0）");

            // 滚到底：normalizedPosition 1(顶)→0(底)——真实滚动管线驱动窗口平移
            scroll.verticalNormalizedPosition = 0f;
            yield return null; yield return null;          // onValueChanged → _dirty → Update 重算
            Assert.GreaterOrEqual(list.FirstIndex, 400,
                $"滚到底后窗口首索引应达高段（500-容量±裕量）——实际:{list.FirstIndex}");
            Assert.Less(list.RealizedCount, 500, "底部窗口节点仍恒定于容量");

            // 滚回顶：窗口回退、索引 0 重新入窗
            scroll.verticalNormalizedPosition = 1f;
            yield return null; yield return null;
            Assert.AreEqual(0, list.FirstIndex, "回顶后窗口首索引归零（可往返——UI-07 回归）");
            Assert.AreEqual("条目0", _source.ZeroIndexText, "回顶后索引 0 重新入窗绑定（复用节点写回正确数据）");
        }

        [UnityTest]
        public IEnumerator 列表_重复刷新_不重绑已绑项且节点不增()
        {
            var open = _ui.ShowAsync(PageList);
            yield return WaitUniTask(open, 20f);
            var list = open.GetAwaiter().GetResult().Root.GetComponentInChildren<VirtualList>(true);

            _source = new TestSource();
            list.SetSource(_source);
            yield return null;

            long bindsAfterFirst = _source.BindCalls;
            int realizedAfterFirst = list.RealizedCount;

            list.Refresh();                                // 重复刷新（数据未变）
            list.Refresh();
            list.Refresh();
            yield return null;

            Assert.AreEqual(bindsAfterFirst, _source.BindCalls,
                "已绑索引原地不动——重复 Refresh 不重绑（§8.2 重绑前解除旧回调的对称面）");
            Assert.AreEqual(realizedAfterFirst, list.RealizedCount, "重复刷新不增节点（池上限恒于容量）");
            Assert.AreEqual(500, list.DataCount, "数据量不变");
        }

        [UnityTest]
        public IEnumerator 列表_缩容_500到1到0跟随且空表安全()
        {
            var open = _ui.ShowAsync(PageList);
            yield return WaitUniTask(open, 20f);
            var list = open.GetAwaiter().GetResult().Root.GetComponentInChildren<VirtualList>(true);

            _source = new TestSource();
            list.SetSource(_source);
            yield return null;
            Assert.AreEqual(500, list.DataCount);

            _source.Count = 1;                            // 缩容到 1（数据源变更 → Refresh）
            list.Refresh();
            yield return null;
            Assert.AreEqual(1, list.DataCount, "缩容跟随");
            Assert.AreEqual(1, list.RealizedCount, "单条仅一个窗口节点");
            Assert.AreEqual(0, list.FirstIndex);

            _source.Count = 0;                           // 清空（§8.2"减少到 0"边界）
            list.Refresh();
            yield return null;
            Assert.AreEqual(0, list.DataCount, "空表 DataCount=0");
            Assert.AreEqual(0, list.RealizedCount, "空表无激活节点");
        }

        // ---- 辅助 ----

        private static async UniTask<IUIPrefabLease> LoadRealPrefabAsync(string location, CancellationToken ct)
        {
            await UniTask.Yield(ct);
            GameObject prefab = await AssetService.LoadAssetAsync<GameObject>(location, ct);
            if (prefab == null) throw new InvalidOperationException($"真资源加载失败:{location}");
            return new PlainLease(prefab);
        }

        private static IEnumerator WaitUniTask(UniTask task, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (task.Status == UniTaskStatus.Pending && Time.realtimeSinceStartup < deadline)
                yield return null;
        }
    }
}
