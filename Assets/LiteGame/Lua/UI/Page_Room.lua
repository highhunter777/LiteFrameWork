-- UI/Page_Room.lua：上云测试·房间页（制作批 2026-10-10；接线批挂账）
-- 接线待办（批C）：SimpleList_Players 玩家列表（行数据经条目回调上报）；Txt_State 等待人数；
-- Button_Leave = 断开 + 返回大厅；被拒/超时 → 系统弹窗（FeedbackService）。
-- 文案 SetTextKey 待 BindLocale 注入后启用（现必抛）。
local class = require("Core.class")

local M = class("Page_Room")

function M:ctor()
    self._viewName = "Page_Room"
end

function M:OnShow(data)
end

function M:OnHide()
end

return M
