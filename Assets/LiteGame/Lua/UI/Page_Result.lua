-- UI/Page_Result.lua：上云测试·结算页（制作批 2026-10-10；接线批 C-④ 2026-10-13；列表批 C-⑥ 2026-10-13）
-- 接线（《上云测试页面层级设计》§5）：本局结果经导航数据（kills/deaths/rank/reason——流程侧注入，见 PageFlow）；
-- **VirtualList_Records 行填充**（G6：key=matchId，时间数据 + 击杀文案键 + 结束原因拼键）；
-- 大厅入口（无本局数据）→ ListRecords 首行补记分板；空表 → RecordsEmpty 空态；Button_BackLobby → 替换回大厅。
-- Txt_Score = 名次（线上冻结结果无 score 字段——FFA 口径按 players[] 击杀序推导，rank 0 = 不可用则隐藏）。
local class = require("Core.class")

local M = class("Page_Result")

local PAGE_LOBBY = "UI.Page_Lobby"

function M:ctor()
    self._viewName = "Page_Result"
end

function M:OnShow(data)
    self.ui:SetTextKey("Txt_BackLobby", "Common.BackLobby")
    self.ui:OnButton("Button_BackLobby", function() Bridge.ui.Replace(PAGE_LOBBY) end)

    local d = (type(data) == "table") and data or nil
    local hasLocal = d ~= nil and d.reason ~= nil
    if hasLocal then
        self:_Scoreboard(d.reason, d.kills, d.deaths, d.rank)          -- 本局冻结结果（流程侧注入）
    end
    self:_LoadRecords(not hasLocal)
end

-- 战绩查询：本局记分板已定 = 只推列表；否则首行补记分板 + 推列表
function M:_LoadRecords(asScoreboard)
    if not Bridge.meta.IsConfigured() then
        if asScoreboard then self:_Empty() end
        self.ui:SetList("VirtualList_Records", {})
        return
    end
    Bridge.meta.ListRecords(function(ok, rows)
        local n = (ok and type(rows) == "table") and #rows or 0
        if asScoreboard then
            if n == 0 then self:_Empty()
            else
                local r = rows[1]
                self:_Scoreboard(r.reason, r.kills, r.deaths, 0)        -- 无排名面（历史行不带名次）
            end
        end
        self:_PushRecords(rows, n)
    end)
end

-- 行投影：rows[i] = { key=matchId, Txt_MatchTime=数据, Txt_Kill=文案键带参, Txt_EndReason=拼键 }
function M:_PushRecords(rows, n)
    local data = {}
    for i = 1, n do
        local r = rows[i]
        data[i] = {
            key = r.matchId,
            Txt_MatchTime = r.time,
            Txt_Kill = { key = "UI.Result.Kills", arg0 = r.kills },
            Txt_EndReason = { key = "UI.Result.Reason." .. tostring(r.reason) },
        }
    end
    self.ui:SetList("VirtualList_Records", data)
end

-- 结束原因取枚举名拼键（值域随枚举——§8）；缺键开发期显 [key]（不静默兜底）；rank ≤ 0 = 无名次面则隐藏
function M:_Scoreboard(reasonName, kills, deaths, rank)
    self.ui:SetVisible("Txt_KillCount", true)
    self.ui:SetVisible("Txt_DeathCount", true)
    self.ui:SetTextKey("Txt_ResultTitle", "UI.Result.Reason." .. tostring(reasonName))
    self.ui:SetTextKeyArgs("Txt_KillCount", "UI.Result.Kills", kills or 0)
    self.ui:SetTextKeyArgs("Txt_DeathCount", "UI.Result.Deaths", deaths or 0)
    if rank ~= nil and rank > 0 then
        self.ui:SetVisible("Txt_Score", true)
        self.ui:SetTextKeyArgs("Txt_Score", "UI.Result.Rank", rank)
    else
        self.ui:SetVisible("Txt_Score", false)
    end
end

function M:_Empty()
    self.ui:SetTextKey("Txt_ResultTitle", "UI.Result.RecordsEmpty")
    self.ui:SetVisible("Txt_KillCount", false)
    self.ui:SetVisible("Txt_DeathCount", false)
    self.ui:SetVisible("Txt_Score", false)
end

return M
