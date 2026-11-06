-- UI/Page_ButtonDemo.lua：起页练习页——按钮点击计数（教学样本 2026-11-06）
-- 这条链路演示照《UI 逻辑 API 参考》§3：按钮在 OnShow 绑定（OnHide 统一解绑），
-- 计数状态在 OnShow 复位（池化复用：OnInit 只跑一次，复用页会再次进 OnShow）。
local class = require("Core.class")

local M = class("Page_ButtonDemo")

function M:ctor()
    self._viewName = "Page_ButtonDemo"
    self._count = 0
end

function M:OnShow(data)
    -- 复用页复位：上次关闭时的计数会留在池化实例上，不复位就会从上次的数字接着加
    self._count = 0
    self:_Render()

    -- 按钮回调放 OnShow：OnHide 会 UnbindAll，重开时必须重新绑定
    self.ui:OnButton("Button_Ping", function() self:_Ping() end)
end

function M:_Ping()
    self._count = self._count + 1
    self:_Render()
    self.ui:Pulse("Button_Ping")
end

function M:_Render()
    self.ui:SetText("Txt_Count", "点击 " .. self._count .. " 次")
end

function M:OnHide()
    self._count = 0
end

return M
