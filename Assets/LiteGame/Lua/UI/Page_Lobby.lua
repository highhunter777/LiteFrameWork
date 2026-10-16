-- UI/Page_Lobby.lua：上云测试·大厅页（制作批 2026-10-10；接线批 C-④ 2026-10-13；列表批 C-⑥ 2026-10-13；创房批 2026-10-15）
-- 接线（《上云测试页面层级设计》§4）：进入即 Bridge.meta.ListRooms → 状态键 + **VirtualList_Rooms 行填充**（G6）；
-- Button_CreateRoom → CreateRoom（服务端出号建新房）；Button_QuickEnter → QuickEnter（容量分配）；
-- **行点击 = 选择式进房**（EnterRoom，行 key = roomId）；
-- 三路成功都**替换** Page_Room（房号随导航数据交房间页）；Button_Records → Page_Result；Button_Settings → 叠加页。
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
    self.ui:SetTextKey("Txt_CreateRoom", "UI.Lobby.CreateRoom")
    self.ui:SetTextKey("Txt_Records", "UI.Lobby.Records")
    self.ui:SetTextKey("Txt_Settings", "Common.Settings")
    self.ui:SetText("Txt_PlayerName", Bridge.meta.AccountId())      -- 账号短号 = 数据（无键——§8）

    self.ui:OnButton("Button_QuickEnter", function() self:_Enter("") end)
    self.ui:OnButton("Button_CreateRoom", function() self:_CreateRoom() end)
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
-- 多图批：房号后带地图名（`mapName` 由 C# 门面按地图表 name 列解析；空 = 未标注，退化为只显房号）
function M:_PushRooms(rows)
    local data = {}
    local n = (type(rows) == "table") and #rows or 0
    for i = 1, n do
        local room = rows[i]
        local name = room.roomId
        if type(room.mapName) == "string" and room.mapName ~= "" then
            name = room.roomId .. " · " .. room.mapName
        end
        data[i] = {
            key = room.roomId,
            Txt_RoomName = name,
            Txt_RoomState = { key = "UI.Lobby.RoomState", arg0 = room.players, arg1 = room.expectedPlayers },
        }
    end
    self.ui:SetList("VirtualList_Rooms", data)
end

-- 进房（roomId 空 = 免列表快速通道；行点击传 roomId = 选择式）
function M:_Enter(roomId)
    if roomId ~= nil and roomId ~= "" then
        self:_Begin("Button_QuickEnter", "UI.Lobby.Allocating", "UI.Lobby.EnterFailed",
            function(cb) Bridge.meta.EnterRoom(roomId, cb) end)
    else
        self:_Begin("Button_QuickEnter", "UI.Lobby.Allocating", "UI.Lobby.EnterFailed",
            function(cb) Bridge.meta.QuickEnter(cb) end)
    end
end

-- 创建房间（服务端出号 → 进新房；成功后流程机接管，与进房同收口）
-- 多图批：地图行键 0 = 未指定（宿主默认图）——玩家侧选图控件归后续批（门面已收 mapId 参数）
function M:_CreateRoom()
    self:_Begin("Button_CreateRoom", "UI.Lobby.Creating", "UI.Lobby.CreateFailed",
        function(cb) Bridge.meta.CreateRoom(0, cb) end)
end

-- 进房/创建房间**统一收口**：防重入锁 + 在途禁钮 + 成功**替换** Page_Room（房号随导航数据）+ 失败 Toast 复位
function M:_Begin(btn, statusKey, failKey, call)
    if self._entering then return end
    if not Bridge.meta.IsConfigured() then self:_SetStatus("UI.Login.NoServer"); return end
    self._entering = true
    self.ui:SetInteractable(btn, false)
    self:_SetStatus(statusKey)

    call(function(ok, joinedRoomId)
        self._entering = false
        self.ui:SetInteractable(btn, true)
        if ok then
            Bridge.ui.Replace(PAGE_ROOM, { roomId = joinedRoomId })   -- 进房替换（§1）；换票/Join 走既有管道
        else
            self:_SetStatus(failKey)
            self.ui:ShowToast(Bridge.text.Get(failKey))
        end
    end)
end

function M:OnHide()
    self._entering = false
end

return M
