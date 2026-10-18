-- UI/Page_Records.lua：上云测试·最近战绩页（战绩面从结算页拆出——大厅「最近战绩」钮改指本页）
-- 接线（《上云测试页面层级设计》§5·战绩页）：进入即 Bridge.meta.ListRecords → VirtualList_Records 行填充
-- （行字段按条目子节点名 Txt_MatchTime/Txt_Kill/Txt_EndReason；key = matchId）；空表 → 空态（UI.Records.Empty）；
-- Button_Back → 替换回大厅。战绩链本体（ListRecords → GET /matches）以本页为**唯一生产消费面**。
local class = require("Core.class")

local M = class("Page_Records")

local PAGE_LOBBY = "UI.Page_Lobby"

function M:ctor()
    self._viewName = "Page_Records"
end

function M:OnShow(data)
    self.ui:SetTextKey("Txt_Title", "UI.Lobby.Records")
    self.ui:SetTextKey("Txt_Back", "Common.BackLobby")
    self.ui:OnButton("Button_Back", function() Bridge.ui.Replace(PAGE_LOBBY) end)
    self.ui:SetVisible("Txt_Empty", false)                 -- 查询返回前不显空态（防加载闪空）
    self:_Load()
end

-- 战绩查询：未配置显性拒绝（空态）；空表 → 空态；非空 → 行投影（空态与列表一并收口）
function M:_Load()
    if not Bridge.meta.IsConfigured() then
        self:_Empty()
        return
    end
    Bridge.meta.ListRecords(function(ok, rows)
        local n = (ok and type(rows) == "table") and #rows or 0
        if n == 0 then
            self:_Empty()
            return
        end
        self.ui:SetVisible("Txt_Empty", false)
        self:_Push(rows, n)
    end)
end

-- 行投影：rows[i] = { key=matchId, Txt_MatchTime=数据, Txt_Kill=文案键带参, Txt_EndReason=拼键 }
function M:_Push(rows, n)
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

function M:_Empty()
    self.ui:SetVisible("Txt_Empty", true)
    self.ui:SetTextKey("Txt_Empty", "UI.Records.Empty")
    self.ui:SetList("VirtualList_Records", {})
end

return M
