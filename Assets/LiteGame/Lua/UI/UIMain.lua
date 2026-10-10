-- UI/UIMain.lua：单界面逻辑表（M4 UI 壳消费；M3 验收 = 可寻址、可 require）
local class = require("Core.class")

local M = class("UIMain")

function M:ctor()
    self._viewName = "UIMain"
end

-- 生命周期桥（M4 由 LuaBehaviourAdapter 转发；M3 仅保证表结构）
-- 受控面 self.ui 由适配器在 OnInit 时挂好；动效口 = Pulse/Flash/Slide（G20）。
-- 按钮绑定放 OnShow 而非 OnInit：OnHide 会 UnbindAll，而池化复用不重跑 OnInit（会静默失绑）。
function M:OnShow(data)
    log.info("UIMain:OnShow")
    self.ui:OnButton("BtnClose", function()
        self.ui:Flash("BtnClose")            -- 带 Button 的节点：解析走 targetGraphic 回退；时长走缺省（单源＝C# 侧 UiFxDefaults）
    end)
    self.ui:Pulse("Label")                   -- UGUI Text 本身即 Graphic；缺省参由 shim 注入（同上单源）
end

function M:OnHide()
    log.info("UIMain:OnHide")
end

function M:OnClick(btnId)
    log.info("UIMain:OnClick " .. tostring(btnId))
end

return M
