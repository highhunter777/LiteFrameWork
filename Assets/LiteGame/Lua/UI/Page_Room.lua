-- UI/Page_Room.lua：上云测试·房间页（制作批 2026-10-10；接线批 C-④ 2026-10-13；数据批 C-⑥ 2026-10-13）
-- 接线（《上云测试页面层级设计》§3）：房号 = 导航数据（UI.Room.Info {0}）；
-- **等待人数 = ListRooms 回查该房号投影**（players/expectedPlayers——批C-⑥；进房后投影可能延迟/未含，
-- 则保持隐藏，不显假数）；Button_Leave = 替换回大厅 + Bridge.meta.LeaveMatch（流程回 Main，既有管道收尾）；
-- 开局（StartGame）关闭本页归 C# 流程侧（PageFlow.CloseRoomIfOpen——页面不知对局相位）。
--
-- 房主开局（HUD 批C·房主开局批，承接《开房与房主开局》挂账①~④）：**房主事实经 Bridge.room.OnOwner 订阅**
-- （替换语义；订阅/装订即回放——页面先于 JoinAck 打开的竞态由门面回放消解；房主迁移〔OwnerChanged〕
-- 由同一订阅推送——旧主失位即隐藏开钮、新主到位即亮）；Button_StartMatch **缺省隐藏**，isOwner=true 才显；
-- 点击 → Bridge.room.StartMatch（防重入禁钮；被拒恰一次回调 → Toast 开局被拒 reason + 复位；
-- 成功不回调——OnStartGame 流程接管并关本页）。
-- **挂账**：SimpleList_Players 席位实时面（`JoinAck.members` 有席位 id 无名/无在线态；命名面全链缺失）。
local class = require("Core.class")

local M = class("Page_Room")

local PAGE_LOBBY = "UI.Page_Lobby"

function M:ctor()
    self._viewName = "Page_Room"
    self._starting = false
end

function M:OnShow(data)
    self._starting = false
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

    -- 房主开局：订阅房主事实（回放/迁移同源推送）——钮显隐在回调内收口
    self.ui:SetVisible("Button_StartMatch", false)       -- 缺省隐藏（非房主/未进房不显）
    self.ui:SetTextKey("Txt_StartMatch", "UI.Room.StartMatch")
    self.ui:OnButton("Button_StartMatch", function() self:_StartMatch() end)
    Bridge.room.OnOwner(function(isOwner) self:_OnOwner(isOwner) end)

    self:_RefreshWaiting(roomId)
end

-- 房主事实回调：门面回放与 OwnerChanged 迁移同一路径（旧主失位隐藏、新主到位亮钮）
function M:_OnOwner(isOwner)
    self.ui:SetVisible("Button_StartMatch", isOwner == true)
end

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

-- 提前开局（房主钮）：被拒恰一次回调——Toast reason 后复位可再点；成功不回调（OnStartGame 流程接管关页）
function M:_StartMatch()
    if self._starting then return end
    self._starting = true
    self.ui:SetInteractable("Button_StartMatch", false)
    Bridge.room.StartMatch(function(ok, reason)
        self._starting = false
        self.ui:SetInteractable("Button_StartMatch", true)
        if not ok then
            self.ui:ShowToast(Bridge.text.Format("UI.Room.StartDenied", reason))
        end
    end)
end

function M:OnHide()
    self._starting = false
    Bridge.room.OnOwner(nil)                             -- 退订房主事实（门面持引用纪律——页面关闭即放手）
end

return M
