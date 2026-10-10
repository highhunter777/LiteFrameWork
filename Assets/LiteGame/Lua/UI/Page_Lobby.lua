-- UI/Page_Lobby.lua：上云测试·大厅页（制作批 2026-10-10；接线批挂账）
-- 接线待办（批C）：VirtualList_Rooms 数据（投影查询房间列表，G6 SetList 协议）；
-- Button_QuickEnter（容量分配→Join Ticket）/Button_Records（进结算页看列表）/Button_Settings；
-- 文案 SetTextKey 待 BindLocale 注入后启用（现必抛）。
local class = require("Core.class")

local M = class("Page_Lobby")

function M:ctor()
    self._viewName = "Page_Lobby"
end

function M:OnShow(data)
end

function M:OnHide()
end

return M
