using System;
using Cysharp.Threading.Tasks;
using LiteGame;
using LiteFramework.UI;
using LiteTesting.Unity;
using NUnit.Framework;
using UnityEngine;
using XLua;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// HUD 批C 退场链：<c>self.ui:ShowConfirm</c> 真 Lua 调用面（《UI控件Lua API 参考》§6—— 新增受控方法必须提供真 Lua 调用测试）：真 LuaEnv + 真适配器（OnI。）。
    /// </summary>
    public sealed class UIConfirmApiEditModeTests : UnityTestBase
    {
        private GameObject _root;
        private UIForm _form;
        private LuaEnv _env;
        private LuaBehaviourAdapter _adapter;

        [TearDown]
        public void TearDown()
        {
            _adapter?.Release();
            _adapter = null;
            _env?.Dispose();
            _env = null;
            if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
            _root = null;
        }

        /// <summary>合成页（无控件也能走通门面链——ShowConfirm 不触控件索引）。</summary>
        private void BuildPage(Func<string, string, string, string, UniTask<bool>> confirm)
        {
            _root = Scope.CreateGameObject("ConfirmTestPage", typeof(RectTransform), typeof(Canvas));
            _env = new LuaEnv();
            var module = (LuaTable)_env.DoString(@"
local m = {}
function m.new()
    return { OnShow = function(self) _test_ui = self.ui end }
end
return m")[0];
            _adapter = new LuaBehaviourAdapter(_env, module, confirm);
            _form = new UIForm(new PageDefinition { Id = 902, LuaPath = "", Location = "", Layer = 0, FullScreen = true }, _root);
            _adapter.OnInit(_form, null);
            _adapter.OnShow(null);
            _env.DoString("assert(_test_ui ~= nil, 'ui 门面未挂')");
        }

        private bool LuaResult()
            => (bool)_env.DoString("return result")[0];

        [Test]
        public void ShowConfirm_确认缝OK_载荷齐_回调true()
        {
            string seenTitle = null, seenMessage = null, seenOk = null, seenCancel = null;
            BuildPage((title, message, okText, cancelText) =>
            {
                seenTitle = title;
                seenMessage = message;
                seenOk = okText;
                seenCancel = cancelText;
                return UniTask.FromResult(true);
            });

            _env.DoString(@"
result = 'unset'
_test_ui:ShowConfirm('标题', '内容', '确认', '取消', function(ok) result = ok end)");

            Assert.IsTrue(LuaResult(), "OK 结果回 Lua 回调");
            Assert.AreEqual("标题", seenTitle, "标题载荷");
            Assert.AreEqual("内容", seenMessage, "正文载荷");
            Assert.AreEqual("确认", seenOk, "确认键文案载荷");
            Assert.AreEqual("取消", seenCancel, "取消键文案载荷");
        }

        [Test]
        public void ShowConfirm_确认缝返回false_回调false()
        {
            BuildPage((t, m, ok, cancel) => UniTask.FromResult(false));
            _env.DoString(@"
result = 'unset'
_test_ui:ShowConfirm('t', 'm', 'ok', 'cancel', function(ok) result = ok end)");
            Assert.IsFalse(LuaResult(), "取消/外部关闭＝false（DialogResult.Ok 之外一律 false）");
        }

        [Test]
        public void ShowConfirm_无缝降级_回调false不悬挂()
        {
            BuildPage(null);
            _env.DoString(@"
result = 'unset'
_test_ui:ShowConfirm('t', 'm', 'ok', 'cancel', function(ok) result = ok end)");
            Assert.IsFalse(LuaResult(), "无缝（测试/工具装配）降级回调 false——页面链不悬挂");
        }

        [Test]
        public void ShowConfirm_确认缝异常_回调false不外溢()
        {
            BuildPage((t, m, ok, cancel) => throw new InvalidOperationException("boom"));
            _env.DoString(@"
result = 'unset'
_test_ui:ShowConfirm('t', 'm', 'ok', 'cancel', function(ok) result = ok end)");
            Assert.IsFalse(LuaResult(), "异常路径回调 false（服务失败不吞掉页面回调链）");
        }
    }
}
