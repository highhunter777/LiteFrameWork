-- UI/Page_Login.lua：上云测试·登录页（制作批 2026-10-10；接线批 C-④ 2026-10-13）
-- 接线（《上云测试页面层级设计》§2）：Button_GuestLogin → Bridge.meta.GuestLogin → 成功**替换** Page_Lobby；
-- 失败页内重试（Txt_Status 键 UI.Login.Failed + 系统 Toast 兜底）；Button_Settings → 叠加打开 Page_Settings。
-- 文案走 SetTextKey/SetTextKeyArgs（LText 链——语言切换自动重刷）；未配置服务器（-meta.url）= 显性拒绝（fail-closed）。
local class = require("Core.class")

local M = class("Page_Login")

local PAGE_LOBBY = "UI.Page_Lobby"
local PAGE_SETTINGS = "UI.Page_Settings"

function M:ctor()
    self._viewName = "Page_Login"
    self._busy = false
end

function M:OnShow(data)
    self.ui:SetTextKey("Txt_Title", "UI.Login.Title")
    self.ui:SetTextKey("Txt_GuestLogin", "UI.Login.GuestEnter")
    self.ui:SetTextKey("Txt_Settings", "Common.Settings")
    self.ui:SetTextKeyArgs("Txt_Version", "Common.Version", Bridge.meta.Version())
    self:_SetBusy(false)

    self.ui:OnButton("Button_GuestLogin", function() self:_Login() end)
    self.ui:OnButton("Button_Settings", function() Bridge.ui.Show(PAGE_SETTINGS) end)
end

-- 忙碌/配置态：登录在途 → 按钮禁用 + Connecting；未配置 → 常驻 NoServer 且禁用（不悄悄退回离线形态）
function M:_SetBusy(busy)
    self._busy = busy
    if not Bridge.meta.IsConfigured() then
        self.ui:SetVisible("Txt_Status", true)
        self.ui:SetTextKey("Txt_Status", "UI.Login.NoServer")
        self.ui:SetInteractable("Button_GuestLogin", false)
        return
    end
    self.ui:SetInteractable("Button_GuestLogin", not busy)
    self.ui:SetVisible("Txt_Status", busy)
    if busy then self.ui:SetTextKey("Txt_Status", "UI.Login.Connecting") end
end

function M:_Login()
    if self._busy or not Bridge.meta.IsConfigured() then return end
    self:_SetBusy(true)
    self.ui:Pulse("Button_GuestLogin")
    Bridge.meta.GuestLogin(function(ok, v)
        self._busy = false
        if ok then
            Bridge.ui.Replace(PAGE_LOBBY)                        -- 登录成功用替换（返回栈不残留——§1）
        else
            self.ui:SetInteractable("Button_GuestLogin", true)
            self.ui:SetVisible("Txt_Status", true)
            self.ui:SetTextKey("Txt_Status", "UI.Login.Failed")  -- 页内重试口径（§2）
            self.ui:ShowToast(Bridge.text.Get("UI.Login.Failed"))
        end
    end)
end

function M:OnHide()
    self._busy = false
end

return M
