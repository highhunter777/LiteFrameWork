-- UI/Page_Room.lua：上云测试·房间页（制作批 2026-10-10；接线批 C-④ 2026-10-13；数据批 C-⑥ 2026-10-13）
-- 接线（《上云测试页面层级设计》§3）：房号 = 导航数据（UI.Room.Info {0}）；
-- **等待人数 = ListRooms 回查该房号投影**（players/expectedPlayers——批C-⑥；进房后投影可能延迟/未含，
-- 则保持隐藏，不显假数）；Button_Leave = 替换回大厅 + Bridge.meta.LeaveMatch（流程回 Main，既有管道收尾）；
-- 开局（StartGame）关闭本页归 C# 流程侧（PageFlow.CloseRoomIfOpen——页面不知对局相位）。
-- **挂账**：SimpleList_Players 席位实时面（`JoinAck.members` 有席位 id 无名/无在线态；命名面全链缺失）。
--
-- 开房与房主开局批（2026-10-12，挂账 Unity，承接）：
--   ① prefab 需新增 Button_StartMatch 控件（本机无 Unity——归后续控件批）。
--   ② 显隐口径：JoinAck.is_owner == true 才亮（开房者=首入座者；协议已带该字段）。
--   ③ 点击接线：BattleContext 会话 → BattleClient.SendStartMatchRequest()（未满员开局，C# 面已就绪）。
--   ④ 反馈：OnStartMatchResponse(ok=false, reason) → FeedbackService 提示 reason；成功即 OnStartGame（现有流程接管）。
--   ⑤ 开房（创建房间）入口在 Lobby 页：输入 roomId 进房即开房（服务器对未知 roomId 自动 OpenRoom——
--      "standard/four/eight" 三模板已配）；进房语义 = JoinRequest(roomId)，无需新消息。
local class = require("Core.class")

local M = class("Page_Room")

local PAGE_LOBBY = "UI.Page_Lobby"

function M:ctor()
    self._viewName = "Page_Room"
end

function M:OnShow(data)
    local roomId = (type(data) == "table") and data.roomId or nil
    if roomId ~= nil and roomId ~= "" then
        self.ui:SetVisible("Txt_RoomId", true)
        self.ui:SetTextKeyArgs("Txt_RoomId", "UI.Room.Info", roomId)
    else
        self.ui:SetVisible("Txt_RoomId", false)          -- 房号缺失（换票结果为空）——不显空标签
    end
    self.ui:SetVisible("Txt_State", false)               -- 先隐藏；回查到投影再显（不显假数）

    self.ui:SetTextKey("Txt_Leave", "Common.Leave")
    self.ui:OnButton("Button_Leave", function() self:_Leave() end)

    self:_RefreshWaiting(roomId)
end

-- 等待人数：房间投影回查（players/expectedPlayers——批C-⑥；无新协议）
function M:_RefreshWaiting(roomId)
    if roomId == nil or roomId == "" then return end
    if not Bridge.meta.IsConfigured() then return end
    Bridge.meta.ListRooms(function(ok, rows)
        if not ok or type(rows) ~= "table" then return end
        for i = 1, #rows do
            if rows[i].roomId == roomId then
                self.ui:SetVisible("Txt_State", true)
                self.ui:SetTextKeyArgs("Txt_State", "UI.Room.Waiting", rows[i].players, rows[i].expectedPlayers)
                return
            end
        end
        -- 投影未含该房（刚开房/心跳延迟）：保持隐藏——不显假数
    end)
end

function M:_Leave()
    Bridge.ui.Replace(PAGE_LOBBY)                        -- 断开 + 返回大厅（§3）：先回大厅，再请流程收尾
    Bridge.meta.LeaveMatch()
end

return M
