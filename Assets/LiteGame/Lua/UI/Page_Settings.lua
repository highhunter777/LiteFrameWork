-- UI/Page_Settings.lua：上云测试·设置页（制作批 2026-10-10；接线批挂账）
-- 接线待办：Slider_Volume/Toggle_Sound/Dropdown_Quality（G15～G17 设值与变化通知）；
-- Dropdown_Language = 语言切换入口（语言自名不翻译）；账号/服务器信息只读（SetText 数据面）；
-- Button_Close = Back 关闭；文案 SetTextKey 待 BindLocale 注入后启用（现必抛）。
local class = require("Core.class")

local M = class("Page_Settings")

function M:ctor()
    self._viewName = "Page_Settings"
end

function M:OnShow(data)
end

function M:OnHide()
end

return M
