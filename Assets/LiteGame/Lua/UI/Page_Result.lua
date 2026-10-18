-- UI/Page_Result.lua：上云测试·结算页（制作批 2026-10-10；接线批 C-④ 2026-10-13；列表批 C-⑥ 2026-10-13；
-- 玩家排行批：原 Cot_Records「最近战绩」列表改造为**玩家排行**，战绩查询面同时弃用）
-- 接线（《上云测试页面层级设计》§5）：本局结果经导航数据（kills/deaths/rank/reason ＋ **ranks 玩家排行**——
-- 流程侧注入，见 PageFlow.ResultData）；**VirtualList_Rankboard 行填充**（原 Cot_Records 位：名次/玩家/击杀/阵亡，
-- 行字段按条目子节点名 Txt_Rank/Txt_Name/Txt_Kills/Txt_Deaths）；Txt_Score = 本机名次（rank 0 = 隐藏）；
-- 无本局数据（大厅入口）→ 空态 ＋ 空排行（不显上一局残留）；Button_BackLobby → 替换回大厅。
--
-- 排行口径**单源在 C#**（`ResultRanking`：击杀降序 → EntityId 升序、并列同名次、名字 = 玩家{k}）——
-- 本页只做行投影，不重算名次/序号（防与局内排行榜、本机摘要三处漂移）。
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
    if d ~= nil and d.reason ~= nil then
        self:_Scoreboard(d.reason, d.kills, d.deaths, d.rank)      -- 本局冻结结果（流程侧注入）
        self:_PushRankboard(d.ranks)
        return
    end
    self:_Empty()                                                  -- 无本局数据（大厅入口）
    self:_PushRankboard(nil)
end

-- 玩家排行行投影：名次/玩家名是数据直写；击杀/阵亡走文案键带参（同本机摘要口径）
function M:_PushRankboard(rows)
    local data = {}
    local n = (type(rows) == "table") and #rows or 0
    for i = 1, n do
        local r = rows[i]
        data[i] = {
            key = tostring(i),
            Txt_Rank = tostring(r.rank),
            Txt_Name = r.name,
            Txt_Kills = { key = "UI.Result.Kills", arg0 = r.kills },
            Txt_Deaths = { key = "UI.Result.Deaths", arg0 = r.deaths },
        }
    end
    self.ui:SetList("VirtualList_Rankboard", data)
end

-- 本机摘要：结束原因取枚举名拼键（值域随枚举——§8）；缺键开发期显 [key]（不静默兜底）；rank ≤ 0 = 无名次面则隐藏
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
    self.ui:SetTextKey("Txt_ResultTitle", "UI.Result.NoMatchData")
    self.ui:SetVisible("Txt_KillCount", false)
    self.ui:SetVisible("Txt_DeathCount", false)
    self.ui:SetVisible("Txt_Score", false)
end

return M
