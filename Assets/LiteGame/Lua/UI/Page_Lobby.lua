-- UI/Page_Lobby.lua：上云测试·大厅页（制作批 2026-10-10；接线批 C-④ 2026-10-13；列表批 C-⑥ 2026-10-13）
-- 接线（《上云测试页面层级设计》§4）：进入即 Bridge.meta.ListRooms → 状态键 + **VirtualList_Rooms 行填充**（G6）；
-- Button_QuickEnter → QuickEnter（容量分配）；**行点击 = 选择式进房**（EnterRoom，行 key = roomId）；
-- 两路成功都**替换** Page_Room（房号随导航数据交房间页）；Button_Records → Page_Result；Button_Settings → 叠加页。
-- 行字段按**条目子节点名**寻址（Txt_RoomName 数据 / Txt_RoomState 文案键带参）；行 key = roomId（稳定，交互回传）。
local class = require("Core.class")

local M = class("Page_Lobby")

local PAGE_ROOM = "UI.Page_Room"
local PAGE_RESULT = "UI.Page_Result"
local PAGE_SETTINGS = "UI.Page_Settings"

function M:ctor()
    self._viewName = "Page_Lobby"
    self._entering = false
end

function M:OnShow(data)
    self.ui:SetTextKey("Txt_QuickEnter", "UI.Lobby.QuickEnter")
    self.ui:SetTextKey("Txt_Records", "UI.Lobby.Records")
    self.ui:SetTextKey("Txt_Settings", "Common.Settings")
    self.ui:SetText("Txt_PlayerName", Bridge.meta.AccountId())      -- 账号短号 = 数据（无键——§8）

    self.ui:OnButton("Button_QuickEnter", function() self:_Enter("") end)
    self.ui:OnButton("Button_Records", function() Bridge.ui.Show(PAGE_RESULT) end)
    self.ui:OnButton("Button_Settings", function() Bridge.ui.Show(PAGE_SETTINGS) end)
    self.ui:OnListClick("VirtualList_Rooms", function(key) self:_Enter(key) end)

    self:_RefreshRooms()
end

function M:_SetStatus(key)
    self.ui:SetVisible("Txt_Status", true)
    self.ui:SetTextKey("Txt_Status", key)
end

function M:_RefreshRooms()
    if not Bridge.meta.IsConfigured() then self:_SetStatus("UI.Login.NoServer"); return end
    Bridge.meta.ListRooms(function(ok, rows)
        if not ok then self:_SetStatus("UI.Lobby.ListFailed"); return end
        local n = (type(rows) == "table") and #rows or 0
        self:_SetStatus(n == 0 and "UI.Lobby.RoomEmpty" or "UI.Lobby.Ready")
        self:_PushRooms(rows)
    end)
end

-- 行投影：rows[i] = { key=roomId, Txt_RoomName=数据, Txt_RoomState=文案键带参 }
function M:_PushRooms(rows)
    local data = {}
    local n = (type(rows) == "table") and #rows or 0
    for i = 1, n do
        local room = rows[i]
        data[i] = {
            key = room.roomId,
            Txt_RoomName = room.roomId,
            Txt_RoomState = { key = "UI.Lobby.RoomState", arg0 = room.players, arg1 = room.expectedPlayers },
        }
    end
    self.ui:SetList("VirtualList_Rooms", data)
end

-- 进房（roomId 空 = 免列表快速通道；行点击传 roomId = 选择式）
function M:_Enter(roomId)
    if self._entering then return end
    if not Bridge.meta.IsConfigured() then self:_SetStatus("UI.Login.NoServer"); return end
    self._entering = true
    self.ui:SetInteractable("Button_QuickEnter", false)
    self:_SetStatus("UI.Lobby.Allocating")

    local callback = function(ok, joinedRoomId)
        self._entering = false
        self.ui:SetInteractable("Button_QuickEnter", true)
        if ok then
            Bridge.ui.Replace(PAGE_ROOM, { roomId = joinedRoomId })   -- 进房替换（§1）；换票/Join 走既有管道
        else
            self:_SetStatus("UI.Lobby.EnterFailed")
            self.ui:ShowToast(Bridge.text.Get("UI.Lobby.EnterFailed"))
        end
    end

    if roomId ~= nil and roomId ~= "" then Bridge.meta.EnterRoom(roomId, callback)
    else Bridge.meta.QuickEnter(callback) end
end

function M:OnHide()
    self._entering = false
end

return M
