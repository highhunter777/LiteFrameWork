using System;
using System.Collections.Generic;
using System.Linq;
using LiteFramework;
using LiteGame;
using LiteFramework.UI;
using LiteTesting.Unity;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using XLua;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// 批C-⑥ 控件族（G15～G17）与列表（G6）**真 Lua 调用面**（《UI控件Lua API 参考》§6：
    /// "新增受控方法必须同步 shim/Dispatch/UIBindIndex/白名单并**提供真 Lua 调用测试**"）——
    /// 真 LuaEnv + 真轻适配器 + 合成控件树（BindNode 标记 → BindIndexBuilder 索引 → shim → Dispatch → UIBindIndex 全链），
    /// 断言落到**控件状态/回调/条目文本**（不是"C# 方法存在"）。
    /// </summary>
    public sealed class UiControlListProtocolEditModeTests : UnityTestBase
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

        /// <summary>合成控件树：滑块/开关/下拉/列表（列表条目 = 一文本 + 一行点击按钮）。</summary>
        private void BuildPage()
        {
            _root = new GameObject("ProtocolTestPage", typeof(RectTransform), typeof(Canvas));

            var sliderNode = NewNode("Slider_Volume", 200, 40);
            sliderNode.AddComponent<Slider>();

            var toggleNode = NewNode("Toggle_Sound", 40, 40);
            toggleNode.AddComponent<Toggle>();

            var dropdownNode = NewNode("Dropdown_Quality", 200, 40);
            dropdownNode.AddComponent<Dropdown>();

            var listNode = NewNode("VirtualList_Rows", 300, 120);
            var virtualList = listNode.AddComponent<VirtualList>();
            virtualList.Spacing = 8f;

            var template = NewNode("Clone_Item", 300, 48, listNode.transform);
            var itemText = NewNode("Txt_Label", 300, 48, template.transform);
            itemText.AddComponent<TextMeshProUGUI>();
            var itemButton = NewNode("Button_RowClick", 300, 48, template.transform);
            itemButton.AddComponent<Image>().raycastTarget = true;
            itemButton.AddComponent<Button>().targetGraphic = itemButton.GetComponent<Image>();
            template.SetActive(false);
            virtualList.Template = (RectTransform)template.transform;

            _env = new LuaEnv();
            var module = (LuaTable)_env.DoString(@"
local m = {}
function m.new()
    return { OnShow = function(self) _test_ui = self.ui end }
end
return m")[0];
            _adapter = new LuaBehaviourAdapter(_env, module);
            _form = new UIForm(new PageDefinition { Id = 901, LuaPath = "", Location = "", Layer = 0, FullScreen = true }, _root);
            _adapter.OnInit(_form, null);
            _adapter.OnShow(null);
            _env.DoString("assert(_test_ui ~= nil, 'ui 门面未挂')");
        }

        private GameObject NewNode(string name, float w, float h, Transform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent ?? _root.transform, false);
            rt.sizeDelta = new Vector2(w, h);
            var bindType = FindType("LiteCodeGen.BindNode");
            if (bindType != null)
            {
                var bind = go.AddComponent(bindType);
                bindType.GetField("BindName").SetValue(bind, name);
            }
            return go;
        }

        private static Type FindType(string fullName)
            => AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(fullName, throwOnError: false))
                .FirstOrDefault(t => t != null);

        private Slider Slider => _root.transform.Find("Slider_Volume").GetComponent<Slider>();
        private Toggle Toggle => _root.transform.Find("Toggle_Sound").GetComponent<Toggle>();
        private Dropdown Dropdown => _root.transform.Find("Dropdown_Quality").GetComponent<Dropdown>();
        private VirtualList List => _root.transform.Find("VirtualList_Rows").GetComponent<VirtualList>();

        // ---- G15～G17 控件族 ----

        [Test]
        public void 控件族_程序设值不触发回调_读取回当前值()
        {
            BuildPage();
            _env.DoString(@"
_got = 'none'
_test_ui:OnSliderChanged('Slider_Volume', function(v) _got = 'slider:' .. tostring(v) end)
_test_ui:OnToggleChanged('Toggle_Sound', function(on) _got = 'toggle:' .. tostring(on) end)
_test_ui:OnDropdownChanged('Dropdown_Quality', function(i) _got = 'dropdown:' .. tostring(i) end)");

            _env.DoString(@"_test_ui:SetSlider('Slider_Volume', 0.3)
_test_ui:SetToggle('Toggle_Sound', true)
_test_ui:SetDropdownOptions('Dropdown_Quality', { '低', '中', '高' })
_test_ui:SetDropdown('Dropdown_Quality', 2)");

            Assert.AreEqual(0.3f, Slider.value, 1e-4, "程序 Set 落到控件");
            Assert.IsTrue(Toggle.isOn);
            Assert.AreEqual(2, Dropdown.value);
            Assert.AreEqual(3, Dropdown.options.Count);
            Assert.AreEqual("高", Dropdown.options[2].text);
            Assert.AreEqual("none", (string)_env.DoString("return _got")[0], "**程序 Set 不触发用户变化回调**（§5 语义）");

            Assert.AreEqual(0.3f, Convert.ToSingle(_env.DoString("return _test_ui:GetSlider('Slider_Volume')")[0]), 1e-4, "get* 系回值");
            Assert.IsTrue((bool)_env.DoString("return _test_ui:GetToggle('Toggle_Sound')")[0]);
            Assert.AreEqual(2, Convert.ToInt32(_env.DoString("return _test_ui:GetDropdown('Dropdown_Quality')")[0]));
        }

        [Test]
        public void 控件族_用户操作触发回调_带值_重开退订()
        {
            BuildPage();
            _env.DoString(@"
_slider_v, _toggle_v, _dropdown_i = -1, nil, -1
_test_ui:OnSliderChanged('Slider_Volume', function(v) _slider_v = v end)
_test_ui:OnToggleChanged('Toggle_Sound', function(on) _toggle_v = on end)
_test_ui:OnDropdownChanged('Dropdown_Quality', function(i) _dropdown_i = i end)
_test_ui:SetDropdownOptions('Dropdown_Quality', { '低', '中', '高' })");

            Slider.value = 0.7f;                                  // 用户路径（与 WithoutNotify 相对）
            Toggle.isOn = true;
            Dropdown.value = 1;

            Assert.AreEqual(0.7f, Convert.ToSingle(_env.DoString("return _slider_v")[0]), 1e-4, "回调带值");
            Assert.IsTrue((bool)_env.DoString("return _toggle_v")[0]);
            Assert.AreEqual(1, Convert.ToInt32(_env.DoString("return _dropdown_i")[0]));

            // 重复登记替换旧回调（不是两份都在）
            _env.DoString(@"_dup = 0
_test_ui:OnSliderChanged('Slider_Volume', function(v) _dup = _dup + 1 end)");
            Slider.value = 0.2f;
            Assert.AreEqual(1, Convert.ToInt32(_env.DoString("return _dup")[0]), "同名重复登记 = 替换");

            // 关闭（OnHide → UnbindAll）：回调全部退订（池化复用不留旧回调）
            _adapter.OnHide();
            _env.DoString("_dup = 0");
            Slider.value = 0.9f;
            Toggle.isOn = false;
            Assert.AreEqual(0, Convert.ToInt32(_env.DoString("return _dup")[0]), "UnbindAll 后控件回调退订");
        }

        // ---- G6 列表 ----

        [Test]
        public void 列表_行投影按子节点名_稳定key_缩容清空()
        {
            BuildPage();
            _env.DoString(@"
_test_ui:SetList('VirtualList_Rows', {
    { key = 'Room-A', Txt_Label = 'Room-A' },
    { key = 'Room-B', Txt_Label = 'Room-B' },
})");

            Assert.AreEqual(2, List.DataCount, "数据量");
            Assert.AreEqual(2, List.RealizedCount, "整表可见（无 ScrollRect）时全部实体化");
            var texts = TextsUnderList();
            texts.Sort();                                      // 内核空闲槽位 LIFO：节点顺序 ≠ 索引顺序（非缺陷）
            CollectionAssert.AreEqual(new[] { "Room-A", "Room-B" }, texts, "行字段按**子节点名**投影到条目");

            _env.DoString("_test_ui:SetList('VirtualList_Rows', { { key = 'Room-C', Txt_Label = 'Room-C' } })");
            Assert.AreEqual(1, List.DataCount, "整表替换（缩容）");
            Assert.AreEqual(1, List.RealizedCount, "缩容后窗口重算");
            Assert.AreEqual("Room-C", TextsUnderList().Single(), "复用条目重绑到新行（整表替换强迫全部重绑）");
        }

        [Test]
        public void 列表_行点击回传key与按钮名_缺节点字段留空()
        {
            BuildPage();
            _env.DoString(@"
_click_key, _click_action = nil, nil
_test_ui:OnListClick('VirtualList_Rows', function(key, action) _click_key = key; _click_action = action end)
_test_ui:SetList('VirtualList_Rows', {
    { key = 'Room-A', Txt_Label = 'Room-A', Txt_Missing = '不存在的节点' },
})");

            Assert.AreEqual("Room-A", TextsUnderList().Single(), "缺节点字段留空不炸（行协议允许）");

            Button button = SettledItemButton();
            Assert.IsNotNull(button, "条目池化实例应就位");
            button.onClick.Invoke();
            Assert.AreEqual("Room-A", (string)_env.DoString("return _click_key")[0], "行点击回传稳定 key");
            Assert.AreEqual("Button_RowClick", (string)_env.DoString("return _click_action")[0], "条目内按钮回自己的节点名（根按钮才是空 action）");
        }

        [Test]
        public void 列表_行含文案键而语言服务未注入_装配期显性拒绝()
        {
            BuildPage();
            var ex = Assert.Throws<LuaException>(() => _env.DoString(@"
_test_ui:SetList('VirtualList_Rows', { { key = 'k', Txt_Label = { key = 'UI.Lobby.RoomState', arg0 = 1, arg1 = 2 } } })"));
            StringAssert.Contains("BindLocale", ex.Message, "缺语言服务 = 装配错误（fail-fast，不在滚动路径上抛）");
        }

        private List<string> TextsUnderList()
        {
            var result = new List<string>();
            foreach (Transform child in List.transform)
            {
                if (child == List.Template) continue;
                if (!child.gameObject.activeSelf) continue;      // 窗口外已回收节点不计（池化复用保留对象）
                var tmp = child.GetComponentInChildren<TextMeshProUGUI>(true);
                if (tmp != null) result.Add(tmp.text);
            }
            return result;
        }

        private Button SettledItemButton()
        {
            foreach (Transform child in List.transform)
            {
                if (child == List.Template) continue;
                var btn = child.GetComponentInChildren<Button>(true);
                if (btn != null) return btn;
            }
            return null;
        }
    }
}
