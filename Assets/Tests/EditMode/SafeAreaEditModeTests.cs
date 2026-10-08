using LiteGame.UI;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// SafeArea 接收器四形态（《UI框架总设计》§7"SafeArea 作用于可交互内容容器，背景可铺满；
    /// 处理分辨率、方向和安全区变化。测试至少覆盖常规横屏、超宽屏、刘海、窗口缩放"）。
    ///
    /// 形态经 <see cref="ISafeAreaStrategy"/> 注入缝注入（设备无关的确定性验证——
    /// 桌面编辑器无真实刘海，策略缝是"处理安全区变化"的可测面）；模板契约跑真 prefab。
    /// </summary>
    public sealed class SafeAreaEditModeTests : UnityTestBase
    {
        /// <summary>定值策略（测试注入避让矩形——像素口径，与 Screen.safeArea 一致）。</summary>
        private sealed class FixedStrategy : ISafeAreaStrategy
        {
            private readonly Rect _rect;
            public FixedStrategy(Rect rect) => _rect = rect;
            public Rect Resolve() => _rect;
        }

        private (SafeAreaReceiver receiver, RectTransform rt) Build()
        {
            var go = Scope.CreateGameObject("safe", typeof(RectTransform));
            var receiver = go.AddComponent<SafeAreaReceiver>();     // OnEnable → 以默认策略立即应用
            return (receiver, (RectTransform)go.transform);
        }

        private static void ApplyStrategy(SafeAreaReceiver receiver, Rect rect)
            => receiver.SetStrategy(new FixedStrategy(rect));

        // ---- 形态①：常规横屏（安全区=全屏 → 容器铺满）----

        [Test]
        [Category(TestCategory.Contract)]
        public void 常规横屏_安全区全屏_内容容器铺满()
        {
            var (receiver, rt) = Build();
            int w = Screen.width, h = Screen.height;

            ApplyStrategy(receiver, new Rect(0, 0, w, h));

            Assert.AreEqual(0f, rt.anchorMin.x, 1e-4f);
            Assert.AreEqual(0f, rt.anchorMin.y, 1e-4f);
            Assert.AreEqual(1f, rt.anchorMax.x, 1e-4f);
            Assert.AreEqual(1f, rt.anchorMax.y, 1e-4f);
            Assert.AreEqual(Vector2.zero, rt.offsetMin, "锚点式几何：offset 归零（由锚点决定大小）");
            Assert.AreEqual(Vector2.zero, rt.offsetMax, "锚点式几何：offset 归零（由锚点决定大小）");
        }

        // ---- 形态②：刘海（左刘海挖孔 / 顶部刘海两种朝向）----

        [Test]
        [Category(TestCategory.Contract)]
        public void 左刘海_避让矩形_内容左沿内收()
        {
            var (receiver, rt) = Build();
            int w = Screen.width, h = Screen.height;
            const int notch = 132;

            ApplyStrategy(receiver, new Rect(notch, 0, w - notch, h));

            Assert.AreEqual(notch / (float)w, rt.anchorMin.x, 1e-4f, "左刘海：anchorMin.x 收进避让宽度");
            Assert.AreEqual(0f, rt.anchorMin.y, 1e-4f);
            Assert.AreEqual(1f, rt.anchorMax.x, 1e-4f);
            Assert.AreEqual(1f, rt.anchorMax.y, 1e-4f);
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 顶部刘海_避让矩形_内容上沿下收()
        {
            var (receiver, rt) = Build();
            int w = Screen.width, h = Screen.height;
            const int notch = 88;

            ApplyStrategy(receiver, new Rect(0, 0, w, h - notch));    // 屏幕坐标原点在左下：顶部内收 = 高度减

            Assert.AreEqual(0f, rt.anchorMin.x, 1e-4f);
            Assert.AreEqual(0f, rt.anchorMin.y, 1e-4f);
            Assert.AreEqual(1f, rt.anchorMax.x, 1e-4f);
            Assert.AreEqual((h - notch) / (float)h, rt.anchorMax.y, 1e-4f, "顶部刘海：anchorMax.y 下收");
        }

        // ---- 形态③：超宽屏（21:9 圆角/摄像头组合避让——四向归一正确性）----

        [Test]
        [Category(TestCategory.Contract)]
        public void 超宽屏_四向组合避让_归一锚点正确()
        {
            var (receiver, rt) = Build();
            int w = Screen.width, h = Screen.height;
            var safe = new Rect(64, 40, w - 64 - 64, h - 40 - 36);  // 左右圆角 + 上下摄像头条

            ApplyStrategy(receiver, safe);

            Assert.AreEqual(64 / (float)w, rt.anchorMin.x, 1e-4f);
            Assert.AreEqual(40 / (float)h, rt.anchorMin.y, 1e-4f);
            Assert.AreEqual((w - 64) / (float)w, rt.anchorMax.x, 1e-4f);
            Assert.AreEqual((h - 36) / (float)h, rt.anchorMax.y, 1e-4f);
        }

        // ---- 形态④：窗口缩放/安全区变化（策略变化即重算——低频轮询外的确定性路径）----

        [Test]
        [Category(TestCategory.Contract)]
        public void 安全区变化_换策略即重算_不残留旧锚点()
        {
            var (receiver, rt) = Build();
            int w = Screen.width, h = Screen.height;

            ApplyStrategy(receiver, new Rect(100, 0, w - 100, h));   // 先左刘海
            Assert.AreEqual(100 / (float)w, rt.anchorMin.x, 1e-4f);

            ApplyStrategy(receiver, new Rect(0, 0, w, h - 60));       // 窗口缩放后变顶部避让
            Assert.AreEqual(0f, rt.anchorMin.x, 1e-4f, "变化后左沿不残留旧避让");
            Assert.AreEqual((h - 60) / (float)h, rt.anchorMax.y, 1e-4f, "变化后上沿取新避让");
        }

        // ---- 模板契约：可交互内容容器挂接收器 ----

        [Test]
        [Category(TestCategory.Contract)]
        public void 模板契约_UIMain与ListScreen内容容器挂SafeAreaReceiver()
        {
            var main = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UI/Screens/UIMain.prefab");
            Assert.IsNotNull(main, "UIMain 模板缺失");
            var safeNode = main.transform.Find("SafeArea");
            Assert.IsNotNull(safeNode, "UIMain 需有 SafeArea 内容容器节点");
            Assert.IsNotNull(safeNode.GetComponent<SafeAreaReceiver>(), "UIMain.SafeArea 挂 SafeAreaReceiver");

            var list = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UI/Screens/ListScreen.prefab");
            Assert.IsNotNull(list, "ListScreen 模板缺失");
            var listNode = list.transform.Find("VirtualList");
            Assert.IsNotNull(listNode, "ListScreen 需有 VirtualList 交互容器节点");
            Assert.IsNotNull(listNode.GetComponent<SafeAreaReceiver>(),
                "ListScreen.VirtualList 挂 SafeAreaReceiver（列表贴边——安全区必须避让）");
        }
    }
}
