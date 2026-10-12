-- UI/Page_Room.lua：上云测试·房间页（制作批 2026-10-10；接线批挂账）
-- 接线待办（批C）：SimpleList_Players 玩家列表（行数据经条目回调上报）；Txt_State 等待人数；
-- Button_Leave = 断开 + 返回大厅；被拒/超时 → 系统弹窗（FeedbackService）。
-- 文案 SetTextKey 待 BindLocale 注入后启用（现必抛）。
-- 开房与房主开局批（2026-10-12，挂账 Unity）：
--   ① prefab 需新增 Button_StartMatch 控件（本机无 Unity——prefab 编辑归批C 前置）。
--   ② 显隐口径：JoinAck.is_owner == true 才亮（开房者=首入座者；协议已带该字段）。
--   ③ 点击接线：BattleContext 会话 → BattleClient.SendStartMatchRequest()（未满员开局，C# 面已就绪）。
--   ④ 反馈：OnStartMatchResponse(ok=false, reason) → FeedbackService 提示 reason；成功即 OnStartGame（现有流程接管）。
--   ⑤ 开房（创建房间）入口在 Lobby 页：输入 roomId 进房即开房（服务器对未知 roomId 自动 OpenRoom——
--      "standard/four/eight" 三模板已配）；进房语义 = JoinRequest(roomId)，无需新消息。
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
