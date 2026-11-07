-- UI/Page_Lobby.lua：大厅页（新 prefab 回灌版；行为沿用旧大厅：列表轮询/状态键/在途锁/快进与建房）
local class = require("Core.class")

local M = class("Page_Lobby")

local PAGE_ROOM = "UI.Page_Room"
local PAGE_RECORDS = "UI.Page_Records"
local PAGE_SETTINGS = "UI.Page_Settings"

local PLAYER_COUNTS = { 2, 4, 6, 8 }
local REFRESH_INTERVAL_SECONDS = 1
local BUBBLE_SECONDS = 3

function M:ctor()
    self._viewName = "Page_Lobby"
    self._entering = false
    self._closed = true
    self._refreshPending = false
    self._refreshAccum = 0
    self._rowsSignature = nil
    self._statusKey = nil
    self._roomsOpen = false
    self._createOpen = false
    self._bubble = 0
    self._line = 0
    self._lines = {}
end

function M:OnShow(data)
    self.ui:SetTextKey("Txt_QuickEnter", "UI.Lobby.QuickEnter")
    self.ui:SetTextKey("Txt_Rooms", "UI.Lobby.RoomList")
    self.ui:SetTextKey("Txt_CreateRoom", "UI.Lobby.CreateRoom")
    self.ui:SetTextKey("Txt_Records", "UI.Lobby.Records")
    self.ui:SetTextKey("Txt_Settings", "Common.Settings")
    self.ui:SetTextKey("Txt_RoomsTitle", "UI.Lobby.RoomList")
    self.ui:SetTextKey("Txt_RoomsClose", "Common.Close")
    self.ui:SetTextKey("Txt_CreateTitle", "UI.Lobby.CreateRoom")
    self.ui:SetTextKey("Txt_MapLabel", "UI.Lobby.Map")
    self.ui:SetTextKey("Txt_PlayersLabel", "UI.Lobby.Players")
    self.ui:SetTextKey("Txt_ConfirmCreate", "UI.Lobby.Create")
    self.ui:SetTextKey("Txt_CancelCreate", "Common.Cancel")
    self.ui:SetText("Txt_PlayerName", Bridge.meta.AccountId())

    self._lines = {
        Bridge.text.Get("UI.Lobby.Bubble1"),
        Bridge.text.Get("UI.Lobby.Bubble2"),
        Bridge.text.Get("UI.Lobby.Bubble3"),
    }
    self._line = 0
    self._bubble = 0
    self.ui:SetVisible("Cot_Bubble", false)
    self.ui:SetVisible("Cot_Rooms", false)
    self.ui:SetVisible("Cot_CreateParams", false)

    self.ui:OnButton("Btn_QuickEnter", function() self:_Enter("") end)
    self.ui:OnButton("Btn_Rooms", function() self:_ShowRooms(true) end)
    self.ui:OnButton("Btn_RoomsClose", function() self:_ShowRooms(false) end)
    self.ui:OnButton("Btn_CreateRoom", function() self:_ShowCreate(true) end)
    self.ui:OnButton("Btn_CancelCreate", function() self:_ShowCreate(false) end)
    self.ui:OnButton("Btn_ConfirmCreate", function() self:_CreateRoom() end)
    self.ui:OnButton("Btn_Records", function() Bridge.ui.Show(PAGE_RECORDS) end)
    self.ui:OnButton("Btn_Settings", function() Bridge.ui.Show(PAGE_SETTINGS) end)
    self.ui:OnButton("Btn_Character", function() self:_Tap() end)
    self.ui:OnListClick("VList_Rooms", function(key) self:_Enter(key) end)

    self:_SetupCreateParams()
    self._closed = false
    self._entering = false
    self._statusKey = nil
    self._rowsSignature = nil
    self._refreshAccum = 0
    self:_RefreshRooms()
end

function M:OnUpdate(dt)
    dt = dt or 0
    if self._bubble > 0 then
        self._bubble = self._bubble - dt
        if self._bubble <= 0 then self.ui:SetVisible("Cot_Bubble", false) end
    end

    if self._closed then return end
    self._refreshAccum = self._refreshAccum + dt
    if self._refreshAccum < REFRESH_INTERVAL_SECONDS then return end
    self._refreshAccum = 0
    self:_RefreshRooms()
end

function M:OnReveal()
    if self._closed then return end
    self._refreshAccum = 0
    self:_RefreshRooms()
end

function M:_SetupCreateParams()
    local maps = Bridge.data.ListMaps()
    self._maps = maps
    local mapOptions = {}
    for i = 1, #maps do mapOptions[i] = maps[i].Name end
    self.ui:SetDropdownOptions("Ddl_Map", mapOptions)
    self.ui:SetDropdown("Ddl_Map", 0)

    local countOptions = {}
    for i = 1, #PLAYER_COUNTS do
        countOptions[i] = Bridge.text.Format("UI.Lobby.PlayersOption", PLAYER_COUNTS[i])
    end
    self.ui:SetDropdownOptions("Ddl_Players", countOptions)
    self.ui:SetDropdown("Ddl_Players", 0)
end

function M:_SelectedMapId()
    local row = self._maps and self._maps[self.ui:GetDropdown("Ddl_Map") + 1]
    return row and row.Id or 0
end

function M:_SelectedPlayers()
    return PLAYER_COUNTS[self.ui:GetDropdown("Ddl_Players") + 1] or 0
end

function M:_SetStatus(key)
    if self._statusKey == key then return end
    self._statusKey = key
    self.ui:SetTextKey("Txt_RoomListStatus", key)
end

function M:_RefreshRooms()
    if self._closed or self._entering or self._refreshPending then return end
    if not Bridge.meta.IsConfigured() then self:_SetStatus("UI.Login.NoServer"); return end
    self._refreshPending = true
    Bridge.meta.ListRooms(function(ok, rows)
        self._refreshPending = false
        if self._closed or self._entering then return end
        if not ok then self:_SetStatus("UI.Lobby.ListFailed"); return end
        local n = (type(rows) == "table") and #rows or 0
        self:_SetStatus(n == 0 and "UI.Lobby.RoomEmpty" or "UI.Lobby.Ready")
        self:_PushRooms(rows)
    end)
end

function M:_PushRooms(rows)
    local data = {}
    local signature = {}
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
            Txt_RoomState = {
                key = "UI.Lobby.RoomState",
                arg0 = room.players,
                arg1 = room.expectedPlayers,
            },
        }
        signature[i] = name .. "/" .. tostring(room.players) .. "/" .. tostring(room.expectedPlayers)
    end

    local fingerprint = table.concat(signature, "|")
    if fingerprint == self._rowsSignature then return end
    self._rowsSignature = fingerprint
    self.ui:SetList("VList_Rooms", data)
end

function M:_Enter(roomId)
    if self._entering then return end
    if not Bridge.meta.IsConfigured() then self:_SetStatus("UI.Login.NoServer"); return end
    self._entering = true
    self.ui:SetInteractable("Btn_QuickEnter", false)
    self:_SetStatus("UI.Lobby.Allocating")

    local callback = function(ok, joinedRoomId)
        self._entering = false
        self.ui:SetInteractable("Btn_QuickEnter", true)
        if ok then
            Bridge.ui.Replace(PAGE_ROOM, { roomId = joinedRoomId })
        else
            self:_SetStatus("UI.Lobby.EnterFailed")
            self.ui:ShowToast(Bridge.text.Get("UI.Lobby.EnterFailed"))
        end
    end

    if roomId ~= nil and roomId ~= "" then
        Bridge.meta.EnterRoom(roomId, callback)
    else
        Bridge.meta.QuickEnter(callback)
    end
end

function M:_CreateRoom()
    if self._entering then return end
    if not Bridge.meta.IsConfigured() then self:_SetStatus("UI.Login.NoServer"); return end
    self._entering = true
    self.ui:SetInteractable("Btn_ConfirmCreate", false)
    self:_SetStatus("UI.Lobby.Creating")

    Bridge.meta.CreateRoom(self:_SelectedMapId(), self:_SelectedPlayers(), function(ok, joinedRoomId)
        self._entering = false
        self.ui:SetInteractable("Btn_ConfirmCreate", true)
        if ok then
            Bridge.ui.Replace(PAGE_ROOM, { roomId = joinedRoomId })
        else
            self:_SetStatus("UI.Lobby.CreateFailed")
            self.ui:ShowToast(Bridge.text.Get("UI.Lobby.CreateFailed"))
        end
    end)
end

function M:_ShowRooms(open)
    self._roomsOpen = open
    self.ui:SetVisible("Cot_Rooms", open)
    if open then
        self._createOpen = false
        self.ui:SetVisible("Cot_CreateParams", false)
        self._refreshAccum = 0
        self:_RefreshRooms()
    end
end

function M:_ShowCreate(open)
    self._createOpen = open
    self.ui:SetVisible("Cot_CreateParams", open)
    if open then
        self._roomsOpen = false
        self.ui:SetVisible("Cot_Rooms", false)
    end
end

function M:_Tap()
    if #self._lines == 0 then return end
    self._line = self._line % #self._lines + 1
    self._bubble = BUBBLE_SECONDS
    self.ui:SetText("Txt_Bubble", self._lines[self._line])
    self.ui:SetVisible("Cot_Bubble", true)
end

function M:OnHide()
    self._entering = false
    self._closed = true
    self._bubble = 0
end

return M
