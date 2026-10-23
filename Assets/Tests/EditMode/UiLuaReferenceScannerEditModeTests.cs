using System.Linq;
using LiteGame.Editor.UiTools.Reference;
using NUnit.Framework;

namespace LiteGame.Tests.EditMode
{
    /// <summary>
    /// Lua 引用扫描器（契约族引用对账与 events[] 的共同数据面）：受控 API 调用点提取、事件接线与
    /// handler 尽力解析、名字字面量全文匹配（覆盖变量中转）。提取口径＝字面量；动态构造名不产出（如实登记）。
    /// </summary>
    [Category(LiteTesting.TestCategory.Contract)]
    public sealed class UiLuaReferenceScannerEditModeTests
    {
        private static readonly string Sample = string.Join("\n",
            "local M = class(\"Page_Demo\")",
            "function M:OnShow()",
            "    self.ui:SetTextKey(\"Txt_Title\", \"UI.Test.Hello\")",
            "    self.ui:OnButton(\"Button_Go\", function() self:_Enter(\"\") end)",
            "    self.ui:OnToggleChanged('Tog_A', function(on) self._flag = on end)",
            "    self.ui:SetInteractable(btn, false)",
            "    self:_Begin(\"Button_QuickEnter\", \"UI.Lobby.Allocating\")",
            "end");

        [Test]
        public void 调用点_字面量名提取_变量名不产出()
        {
            var calls = UiLuaReferenceScanner.ScanApiCalls(Sample);

            var setKey = calls.FirstOrDefault(c => c.Api == "SetTextKey");
            Assert.IsNotNull(setKey, "SetTextKey 调用点须提取");
            Assert.AreEqual("Txt_Title", setKey.Name);
            Assert.AreEqual("UI.Test.Hello", setKey.Args.ElementAtOrDefault(1), "key 实参在 Args[1]");
            Assert.AreEqual(3, setKey.Line, "行号 1 基");

            Assert.IsTrue(calls.Any(c => c.Api == "OnButton" && c.Name == "Button_Go"), "OnButton 调用点须提取");
            Assert.IsTrue(calls.Any(c => c.Api == "OnToggleChanged" && c.Name == "Tog_A"),
                "单引号字面量同样提取");
            Assert.IsFalse(calls.Any(c => c.Api == "SetInteractable"),
                "变量名（btn）非字面量——不产出（不臆造）");
        }

        [Test]
        public void 调用点_名单外API与拼接名不产出()
        {
            string source = string.Join("\n",
                "self.ui:NotInApiList(\"X\")",
                "self.ui:SetTextKey(\"Txt_\" .. i, \"K\")",
                "self.ui:SetTextKey(\"Txt_Late\", \"K\")");
            var calls = UiLuaReferenceScanner.ScanApiCalls(source);

            Assert.AreEqual(1, calls.Count, "名单外 API 与拼接名不产出（可见证据口径）：\n"
                + string.Join("\n", calls.Select(c => c.Api + ":" + c.Name)));
            Assert.AreEqual("Txt_Late", calls[0].Name, "后半行拼接后独立出现的字面量仍提取");
        }

        [Test]
        public void 事件接线_事件名映射与handler尽力解析()
        {
            var wiring = UiLuaReferenceScanner.ScanEventWiring(Sample);

            var go = wiring.FirstOrDefault(w => w.Name == "Button_Go");
            Assert.IsNotNull(go, "OnButton 接线须提取");
            Assert.AreEqual("onClick", go.Event, "事件名与受控 API 同文（映射单源）");
            Assert.AreEqual("_Enter", go.Handler, "匿名闭包内首个 self:方法 尽力解析");

            var toggle = wiring.FirstOrDefault(w => w.Name == "Tog_A");
            Assert.IsNotNull(toggle);
            Assert.AreEqual("onToggleChanged", toggle.Event);
            Assert.AreEqual("anonymous", toggle.Handler, "解析不到记 anonymous（不臆造）");
        }

        [Test]
        public void 字面量全文匹配_覆盖变量中转()
        {
            Assert.IsTrue(UiLuaReferenceScanner.ContainsLiteral(Sample, "Button_QuickEnter"),
                "_Begin(\"Button_QuickEnter\") 字面量中转后经 SetInteractable(btn) 消费——全文匹配须命中");
            Assert.IsFalse(UiLuaReferenceScanner.ContainsLiteral(Sample, "Button_Ghost"));
            Assert.IsFalse(UiLuaReferenceScanner.ContainsLiteral(Sample, "Button_"),
                "部分前缀不得误命中（须完整字面量）");
        }

        [Test]
        public void luaPath解析_页面目录形态映射_异形拒绝()
        {
            Assert.AreEqual("Assets/LiteGame/Lua/UI/Page_Test.lua",
                UiLuaReferenceScanner.TryResolveLuaFile("UI.Page_Test"), "UI.<名> 映射起页 Lua 目录");
            Assert.IsNull(UiLuaReferenceScanner.TryResolveLuaFile("Page_Test"), "无 UI. 前缀异形拒绝");
            Assert.IsNull(UiLuaReferenceScanner.TryResolveLuaFile(null));
        }

        [Test]
        public void 源读取_缺件显式null()
        {
            Assert.IsNull(UiLuaReferenceScanner.ReadSource("Assets/NoSuchUiLuaFile.lua"),
                "缺件返回 null——调用方显式处理，不伪造空源");
        }
    }
}
