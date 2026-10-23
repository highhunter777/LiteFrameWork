-- UI/Tests/UiTestPage.lua：UI 栈测试载体（UIMain 退役后的真 Lua 夹具；非产品页、tbuiform 不登记）
-- 仅声明最小生命周期与受控 API 绑定，供 EditMode/PlayMode 验证 Adapter/注册表/转场链路。
local class = require("Core.class")

local M = class("UiTestPage")

function M:ctor()
    self._viewName = "UiTestPage"
end

-- 生命周期桥（M4 由 LuaBehaviourAdapter 转发）
-- 按钮绑定放 OnShow 而非 OnInit：OnHide 会 UnbindAll，而池化复用不重跑 OnInit（会静默失绑）。
function M:OnShow(data)
    log.info("UiTestPage:OnShow")
    self.ui:OnButton("Button_Close", function()
        self.ui:Flash("Button_Close")            -- 带 Button 的节点：解析走 targetGraphic 回退；时长走缺省（单源＝C# 侧 UiFxDefaults）
    end)
    self.ui:Pulse("Txt_Label")                   -- UGUI Text 本身即 Graphic；缺省参由 shim 注入（同上单源）
end

function M:OnHide()
    log.info("UiTestPage:OnHide")
end

return M
