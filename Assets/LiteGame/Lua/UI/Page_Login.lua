-- UI/Page_Login.lua：上云测试·登录页（制作批 2026-10-10；接线批挂账）
-- 接线待办（批C/语言装配批）：Button_GuestLogin → MetaClient /auth/guest；失败页内重试（Txt_Status）；
-- Button_Settings → 打开 Page_Settings；文案 SetTextKey 待 BindLocale 注入后启用（现必抛）。
local class = require("Core.class")

local M = class("Page_Login")

function M:ctor()
    self._viewName = "Page_Login"
end

function M:OnShow(data)
end

function M:OnHide()
end

return M
