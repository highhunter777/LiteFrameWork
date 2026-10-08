using System;
using LiteGame;
using LiteTesting;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 焦点协调者（《UI框架总设计》§6.2"焦点保存/恢复、键盘/手柄 Submit/Cancel 和平台返回统一处理"；
    /// U2-⑦）。焦点事实单源 = EventSystem 选择；用例自建裸 EventSystem 并经解析缝注入
    /// （<c>EventSystem.current</c> 在 EditMode 不更新——实测；实例选择簿记可用）。
    /// 键盘/手柄导航与 Submit/Cancel 由 InputSystemUIInputModule 默认动作集在运行期驱动（实机验证）。
    /// </summary>
    public sealed class UIFocusEditModeTests : UnityTestBase
    {
        private EventSystem _es;

        [SetUp]
        public void SetUp()
        {
            _es = Scope.CreateGameObject("es", typeof(EventSystem)).GetComponent<EventSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_es != null) _es.SetSelectedGameObject(null);   // EventSystem 选择清空防跨用例串焦点
        }

        private UIFocusCoordinator Coordinator() => new UIFocusCoordinator(() => _es);

        private UIForm Form(int id, bool withButton = true, bool buttonInteractable = true)
        {
            var root = Scope.CreateGameObject("form" + id, typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            if (withButton)
            {
                var btnGo = Scope.CreateGameObject("btn" + id, typeof(RectTransform), typeof(Image), typeof(Button));
                btnGo.transform.SetParent(root.transform, false);
                btnGo.GetComponent<Button>().interactable = buttonInteractable;
            }
            return new UIForm(new UIFormInfo { Id = id, Layer = 1, Location = "x" + id, LuaPath = "x" }, root);
        }

        private static GameObject ButtonOf(UIForm form)
            => form.Root.GetComponentInChildren<Button>().gameObject;

        // ---- 打开接管 ----

        [Test]
        [Category(TestCategory.Contract)]
        public void 打开接管_聚焦首个可交互件()
        {
            var form = Form(1);
            var coordinator = Coordinator();

            coordinator.OnFormActivated(form);

            Assert.AreEqual(ButtonOf(form), _es.currentSelectedGameObject,
                "新顶页无条件聚焦首个可交互 Selectable（深度优先序——确定性）");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 打开接管_无可交互件_不动选择()
        {
            var elsewhere = Form(2);
            var coordinator = Coordinator();
            coordinator.OnFormActivated(elsewhere);           // 选中 elsewhere 的按钮

            var bare = Form(3, withButton: false);            // 无任何 Selectable
            coordinator.OnFormActivated(bare);

            Assert.AreEqual(ButtonOf(elsewhere), _es.currentSelectedGameObject,
                "无可聚焦件时保持既有选择（不清不抢）");
        }

        // ---- 保存/恢复（遮盖与暂停同路径）----

        [Test]
        [Category(TestCategory.Contract)]
        public void 遮盖让出焦点_清选择_揭示不抢占地恢复()
        {
            var form = Form(1);
            var coordinator = Coordinator();
            coordinator.OnFormActivated(form);
            var expected = ButtonOf(form);

            coordinator.OnFormSuspended(form);               // 被遮：保存 + 清选择
            Assert.IsNull(_es.currentSelectedGameObject, "被遮页让出焦点（键盘不驱动不可交互页）");

            coordinator.OnFormRevealed(form);                 // 揭示：恢复
            Assert.AreEqual(expected, _es.currentSelectedGameObject, "揭示时还原保存的焦点");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 暂停续跑_同遮盖路径_保存并恢复()
        {
            var form = Form(1);
            var coordinator = Coordinator();
            coordinator.OnFormActivated(form);
            var expected = ButtonOf(form);

            coordinator.OnFormSuspended(form);               // Pause 同挂点
            Assert.IsNull(_es.currentSelectedGameObject);
            coordinator.OnFormRevealed(form);                 // Resume 同挂点
            Assert.AreEqual(expected, _es.currentSelectedGameObject);
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 揭示恢复不抢占_他页持有焦点时不动()
        {
            var a = Form(1);
            var b = Form(2);
            var coordinator = Coordinator();
            coordinator.OnFormActivated(a);
            coordinator.OnFormSuspended(a);                   // a 保存焦点并让出

            coordinator.OnFormActivated(b);                   // b 接管（顶层新页）
            var heldByB = ButtonOf(b);

            coordinator.OnFormRevealed(a);                    // a 揭示，但 b 持有焦点 → 不抢
            Assert.AreEqual(heldByB, _es.currentSelectedGameObject,
                "不抢占：更高层持有焦点时，揭示页不得偷取");
        }

        // ---- 关闭 ----

        [Test]
        [Category(TestCategory.Contract)]
        public void 关闭_清本页选择_保存焦点摘除()
        {
            var a = Form(1);
            var coordinator = Coordinator();
            coordinator.OnFormActivated(a);
            coordinator.OnFormSuspended(a);                   // 保存
            coordinator.OnFormClosed(a);                      // 关闭：摘保存（揭示不再还原）

            var b = Form(2);
            coordinator.OnFormActivated(b);
            Assert.IsNotNull(_es.currentSelectedGameObject);
            coordinator.OnFormClosed(b);                      // b 关闭：b 内选择清空
            Assert.IsNull(_es.currentSelectedGameObject, "关闭页的选择被清空");
        }

        [Test]
        [Category(TestCategory.Contract)]
        public void 关闭_选择在他页_不误清()
        {
            var a = Form(1);
            var b = Form(2);
            var coordinator = Coordinator();
            coordinator.OnFormActivated(b);                   // 焦点在 b
            var heldByB = ButtonOf(b);

            coordinator.OnFormClosed(a);                      // 关的是 a——b 的焦点不动
            Assert.AreEqual(heldByB, _es.currentSelectedGameObject);
        }

        // ---- 无底座安全 ----

        [Test]
        [Category(TestCategory.Contract)]
        public void 无EventSystem_全部簿记安全跳过()
        {
            var form = Form(1);
            var coordinator = new UIFocusCoordinator(() => null);   // 无底座形态

            Assert.DoesNotThrow(() =>
            {
                coordinator.OnFormActivated(form);
                coordinator.OnFormSuspended(form);
                coordinator.OnFormRevealed(form);
                coordinator.OnFormClosed(form);
                coordinator.Tick();
            }, "EventSystem 缺位（未保障/EditMode 无实例）——簿记跳过不抛");
        }

        // ---- 平台返回（输入侧统一；语义侧归导航器）----

        [Test]
        [Category(TestCategory.Contract)]
        public void 平台返回_未接线false_接线后转发()
        {
            var coordinator = Coordinator();
            Assert.IsFalse(coordinator.RequestBack(), "未接线（框架先行无导航者）时无返回");

            int called = 0;
            coordinator.PlatformBack = () =>
            {
                called++;
                return Cysharp.Threading.Tasks.UniTask.FromResult(true);
            };
            Assert.IsTrue(coordinator.RequestBack());
            Assert.AreEqual(1, called, "转发宿主一次（Back 目标推导/拦截链归导航器）");
        }

        [Test]
        public void Tick_无键盘环境_安全跳过()
        {
            var coordinator = Coordinator();
            Assert.DoesNotThrow(() => coordinator.Tick(),
                "无 Keyboard.current（编辑器用例环境）时轮询安全跳过——运行期 ESC 见实机验证");
        }
    }
}
