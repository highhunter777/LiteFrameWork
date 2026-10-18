-- UI/Page_Lobby.lua：上云测试·大厅页（制作批 2026-10-10；接线批 C-④ 2026-10-13；列表批 C-⑥ 2026-10-13；创房批 2026-10-15；开房参数批：选图/选人数；列表刷新批：新鲜度）
-- 接线（《上云测试页面层级设计》§4）：进入即 Bridge.meta.ListRooms → 状态键 + **VirtualList_Rooms 行填充**（G6）；
-- Button_CreateRoom → CreateRoom（服务端出号建新房）；Button_QuickEnter → QuickEnter（容量分配）；
-- **行点击 = 选择式进房**（EnterRoom，行 key = roomId）；
-- 三路成功都**替换** Page_Room（房号随导航数据交房间页）；Button_Records → Page_Records（战绩页）；Button_Settings → 叠加页。
-- 行字段按**条目子节点名**寻址（Txt_RoomName 数据 / Txt_RoomState 文案键带参）；行 key = roomId（稳定，交互回传）。
--
-- **列表新鲜度（列表刷新批）**：本页是投影的**唯一**消费面（房间页已改读本房实时席位面），而投影带宽 = Meta 心跳
-- 近似（RoomServer `lobby.heartbeat_interval_ms`，现配 1s）＋ 房间实体随首入座者物化 ⇒ "别人刚开的房"对本页天然滞后。
-- 本页又不随覆盖页揭示重跑 OnShow（遮盖页关闭走 `OnReveal`、`Replace` 回已开页按幂等空转）⇒ **只在 OnShow 查一次 =
-- 整场会话定格**。故按固定节拍自轮询（帧驱动累计，不读系统钟）＋ 揭示即重查；投影未变**不下发** SetList（整表替换会
-- 强迫已实体化条目全部重绑）——两处都在 `_RefreshRooms`/`_PushRooms` 收口。
local class = require("Core.class")

local M = class("Page_Lobby")

local PAGE_ROOM = "UI.Page_Room"
local PAGE_RECORDS = "UI.Page_Records"
local PAGE_SETTINGS = "UI.Page_Settings"

-- 开房参数（开房参数批）：房规模候选档位（下拉索引 → 人数；上界归服务端内核席位容量校验）
local PLAYER_COUNTS = { 2, 4, 6, 8 }

-- 列表自轮询节拍（秒）：与 RoomServer 心跳同拍（`lobby.heartbeat_interval_ms = 1000`）——投影不会快于心跳变，
-- 故这是"不白查"的密上限；最坏可见延迟 ≈ 节拍 + 一跳心跳 + 首入座物化 ≈ 3s。（更短的唯一正解是服务端变更即
-- 上报＋推送，见施工记录《大厅列表刷新》"再短"节；本常量与心跳间隔是两个独立旋钮，需一起调才有效。）
local REFRESH_INTERVAL_SECONDS = 1

function M:ctor()
    self._viewName = "Page_Lobby"
    self._entering = false
    self._closed = true                 -- 未展示/已关页：轮询与投影写入的总闸
    self._refreshPending = false        -- 查询在途（不叠加第二次）
    self._refreshAccum = 0              -- 帧驱动累计秒（OnUpdate 的 dt，不读系统钟）
    self._rowsSignature = nil           -- 上次下发的投影指纹（未变则跳过 SetList）
    self._statusKey = nil               -- 上次写入的状态键（同键不重写）
end

function M:OnShow(data)
    self.ui:SetTextKey("Txt_QuickEnter", "UI.Lobby.QuickEnter")
    self.ui:SetTextKey("Txt_CreateRoom", "UI.Lobby.CreateRoom")
    self.ui:SetTextKey("Txt_Records", "UI.Lobby.Records")
    self.ui:SetTextKey("Txt_Settings", "Common.Settings")
    self.ui:SetText("Txt_PlayerName", Bridge.meta.AccountId())      -- 账号短号 = 数据（无键——§8）

    self.ui:OnButton("Button_QuickEnter", function() self:_Enter("") end)
    self.ui:OnButton("Button_CreateRoom", function() self:_CreateRoom() end)
    self.ui:OnButton("Button_Records", function() Bridge.ui.Show(PAGE_RECORDS) end)
    self.ui:OnButton("Button_Settings", function() Bridge.ui.Show(PAGE_SETTINGS) end)
    self.ui:OnListClick("VirtualList_Rooms", function(key) self:_Enter(key) end)

    self:_SetupCreateParams()
    -- 展示会话起点：状态键/投影指纹归零（池化复用后节点仍在 ⇒ 重新绑定文案与行，不靠"上次没变"省步）
    self._closed = false
    self._statusKey = nil
    self._rowsSignature = nil
    self._refreshAccum = 0
    self:_RefreshRooms()
end

-- 列表自轮询（帧驱动节拍；仅 Active 态收到 OnUpdate——遮盖/暂停期间框架不派发，回来自会补查）
function M:OnUpdate(dt)
    if self._closed then return end
    self._refreshAccum = self._refreshAccum + (dt or 0)
    if self._refreshAccum < REFRESH_INTERVAL_SECONDS then return end
    self._refreshAccum = 0
    self:_RefreshRooms()
end

-- 覆盖页（战绩/设置）关闭回大厅：不等节拍，立即重查一次（这条路径不重跑 OnShow）
function M:OnReveal()
    if self._closed then return end
    self._refreshAccum = 0
    self:_RefreshRooms()
end

-- 开房参数控件（选图/选人数）：标签走文案键；**下拉项文本由页面给**（引擎组件不随 LText 自动刷新——同设置页口径，
-- 语言切换时页面重设）。地图候选 = 地图表全行（Bridge.data.ListMaps 表序；名称用表列 name），默认选第一张。
function M:_SetupCreateParams()
    self.ui:SetTextKey("Txt_MapLabel", "UI.Lobby.Map")
    self.ui:SetTextKey("Txt_PlayersLabel", "UI.Lobby.Players")

    local maps = Bridge.data.ListMaps()
    self._maps = maps
    local mapOptions = {}
    for i = 1, #maps do mapOptions[i] = maps[i].Name end
    self.ui:SetDropdownOptions("Dropdown_Map", mapOptions)
    self.ui:SetDropdown("Dropdown_Map", 0)

    local countOptions = {}
    for i = 1, #PLAYER_COUNTS do
        countOptions[i] = Bridge.text.Format("UI.Lobby.PlayersOption", PLAYER_COUNTS[i])
    end
    self.ui:SetDropdownOptions("Dropdown_Players", countOptions)
    self.ui:SetDropdown("Dropdown_Players", 0)
end

-- 当前选择（下拉索引 0 基）：地图行键 = 表序取值（缺件退 0 = 未指定）；人数 = 档位表取值
function M:_SelectedMapId()
    local row = self._maps and self._maps[self.ui:GetDropdown("Dropdown_Map") + 1]
    return row and row.Id or 0
end

function M:_SelectedPlayers()
    return PLAYER_COUNTS[self.ui:GetDropdown("Dropdown_Players") + 1] or 0
end

-- 状态行：同键不重写（轮询每拍都可能落同一个"就绪/无房"——避免无谓文字写与文案绑定重挂）
function M:_SetStatus(key)
    if self._statusKey == key then return end
    self._statusKey = key
    self.ui:SetVisible("Txt_Status", true)
    self.ui:SetTextKey("Txt_Status", key)
end

-- 投影查询：整局在途（`_entering`）与已有查询在途都不叠加——在途进房期间更不许覆盖在途状态文案
function M:_RefreshRooms()
    if self._closed or self._entering or self._refreshPending then return end
    if not Bridge.meta.IsConfigured() then self:_SetStatus("UI.Login.NoServer"); return end
    self._refreshPending = true
    Bridge.meta.ListRooms(function(ok, rows)
        self._refreshPending = false
        if self._closed or self._entering then return end   -- 页已关/已改在途进房：丢弃本拍投影
        if not ok then self:_SetStatus("UI.Lobby.ListFailed"); return end
        local n = (type(rows) == "table") and #rows or 0
        self:_SetStatus(n == 0 and "UI.Lobby.RoomEmpty" or "UI.Lobby.Ready")
        self:_PushRooms(rows)
    end)
end

-- 行投影：rows[i] = { key=roomId, Txt_RoomName=数据, Txt_RoomState=文案键带参 }
-- 多图批：房号后带地图名（`mapName` 由 C# 门面按地图表 name 列解析；空 = 未标注，退化为只显房号）
-- 列表刷新批：投影未变**不下发**——SetList 是"空源过渡 + 换源"，会强迫已实体化条目全部重绑（滚动与条目状态白重建）
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
            Txt_RoomState = { key = "UI.Lobby.RoomState", arg0 = room.players, arg1 = room.expectedPlayers },
        }
        signature[i] = name .. "/" .. tostring(room.players) .. "/" .. tostring(room.expectedPlayers)
    end

    local fingerprint = table.concat(signature, "|")
    if fingerprint == self._rowsSignature then return end
    self._rowsSignature = fingerprint
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
-- 开房参数：地图行键（选图控件）+ 期望人数（选人数控件）随创建意图送服务端盖章；
-- 房间端建房时校验（缺行/越内核容量 → 拒建房），房事实经 JoinAck 回执给客户端定容。
function M:_CreateRoom()
    self:_Begin("Button_CreateRoom", "UI.Lobby.Creating", "UI.Lobby.CreateFailed",
        function(cb) Bridge.meta.CreateRoom(self:_SelectedMapId(), self:_SelectedPlayers(), cb) end)
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
    self._closed = true                 -- 关页即停轮询并丢弃在途投影（关页后写节点会重挂文案绑定）
end

return M
