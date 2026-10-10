-- UI/Page_Result.lua：上云测试·结算页（制作批 2026-10-10；接线批挂账）
-- 接线待办（批C）：比分/击杀/死亡（SetText 数据面）；VirtualList_Records 最近战绩（GET /matches）；
-- Button_BackLobby = 返回大厅；结束原因文案值域随枚举（SetTextKey）。
-- 文案 SetTextKey 待 BindLocale 注入后启用（现必抛）。
local class = require("Core.class")

local M = class("Page_Result")

function M:ctor()
    self._viewName = "Page_Result"
end

function M:OnShow(data)
end

function M:OnHide()
end

return M
