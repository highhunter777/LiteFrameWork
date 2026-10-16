-- UI/Page_Settings.lua：上云测试·设置页（制作批 2026-10-10；接线批 C-④ 2026-10-13；控件批 C-⑥ 2026-10-13）
-- 接线（《上云测试页面层级设计》§6）：标签/只读数据/关闭（批C-④）+ **控件族**（批C-⑥）——
-- Slider_Volume → Bridge.settings.SetVolume（拖动即生效）；Toggle_Sound → SetSoundEnabled（Effect 组门）；
-- Dropdown_Quality → SetQuality（三档低/中/高，选项文本随语言）；Dropdown_Language → Bridge.text.SetLocale
-- （语言自名不翻译；切换后重设画质选项文本——下拉项文本是引擎组件，不随 LText 自动刷新）。
-- 落盘：SettingService 脏标记协议——关闭/OnHide 时 Bridge.settings.Save()（"设置界面关闭"纪律点）。
--
-- 退出对局（HUD 批C 退场链）：**对局中入口**（HUD 菜单钮 / 空栈 ESC——PageFlow.OpenBattleSettings）
-- 的导航数据带 battleExit=true，本页据此显示 Button_Exit；登录/大厅/结算的设置入口不带数据＝不显。
-- 点击 → self.ui:ShowConfirm 确认弹窗（DialogService 承载）→ 确认后**先回大厅再 LeaveMatch**
-- （Page_Room._Leave 同款先例——先导航再请流程收尾，主动离场路径不依赖 OnMatchEnded 页面切换）。
local class = require("Core.class")

local M = class("Page_Settings")

local PAGE_SETTINGS = "UI.Page_Settings"
local PAGE_LOBBY = "UI.Page_Lobby"

local LOCALES = { "zh-CN", "en" }
local LOCALE_KEYS = { "UI.Settings.Language.Zh", "UI.Settings.Language.En" }
local QUALITY_KEYS = { "UI.Settings.Quality.Low", "UI.Settings.Quality.Medium", "UI.Settings.Quality.High" }

local function IndexOf(list, value, fallback)
    for i = 1, #list do
        if list[i] == value then return i end
    end
    return fallback
end

function M:ctor()
    self._viewName = "Page_Settings"
    self._exitable = false
    self._leaving = false
end

function M:OnShow(data)
    self.ui:SetTextKey("Txt_Title", "Common.Settings")
    self.ui:SetTextKey("Txt_Close", "Common.Close")
    self.ui:SetTextKey("Txt_Volume", "UI.Settings.Volume")
    self.ui:SetTextKey("Txt_Sound", "UI.Settings.Sound")
    self.ui:SetTextKey("Txt_Quality", "UI.Settings.Quality")
    self.ui:SetTextKey("Txt_Language", "UI.Settings.Language")
    self.ui:SetTextKeyArgs("Txt_Version", "Common.Version", Bridge.meta.Version())

    -- 退出对局（对局中打开才有 battleExit 数据——门控见文件头）
    self._exitable = (type(data) == "table") and (data.battleExit == true)
    self._leaving = false
    self.ui:SetVisible("Button_Exit", self._exitable)
    if self._exitable then
        -- 文案按落点：dev 落点（直入工作台）带 exitKey 覆盖；缺省＝对局语义
        self.ui:SetTextKey("Txt_Exit", (type(data) == "table" and data.exitKey) or "UI.Settings.Exit")
        self.ui:OnButton("Button_Exit", function() self:_ConfirmExit() end)
    end

    local account = Bridge.meta.AccountId()
    if account ~= nil and account ~= "" then
        self.ui:SetVisible("Txt_Account", true)
        self.ui:SetTextKeyArgs("Txt_Account", "UI.Settings.Account", account)
    else
        self.ui:SetVisible("Txt_Account", false)              -- 未登录（离线/登录前）——不显空标签
    end

    local server = Bridge.meta.ServerUrl()
    if server ~= nil and server ~= "" then
        self.ui:SetVisible("Txt_ServerAddress", true)
        self.ui:SetTextKeyArgs("Txt_ServerAddress", "UI.Settings.Server", server)
    else
        self.ui:SetVisible("Txt_ServerAddress", false)
    end

    -- 初值（程序 Set 不触发变化回调——§5 输入控件语义）
    self.ui:SetSlider("Slider_Volume", Bridge.settings.Volume())
    self.ui:SetToggle("Toggle_Sound", Bridge.settings.SoundEnabled())
    self:_PushQualityOptions()
    self.ui:SetDropdown("Dropdown_Quality", Bridge.settings.Quality())
    self:_PushLanguageOptions()

    -- 变化通知（只回写设置与生效面；节流落盘由 Save 承担）
    self.ui:OnSliderChanged("Slider_Volume", function(value) Bridge.settings.SetVolume(value) end)
    self.ui:OnToggleChanged("Toggle_Sound", function(on) Bridge.settings.SetSoundEnabled(on) end)
    self.ui:OnDropdownChanged("Dropdown_Quality", function(index) Bridge.settings.SetQuality(index) end)
    self.ui:OnDropdownChanged("Dropdown_Language", function(index)
        Bridge.text.SetLocale(LOCALES[index + 1])
        self:_PushQualityOptions()                            -- 新语言下的画质选项文本
    end)

    self.ui:OnButton("Button_Close", function() self:_Close() end)
end

-- 画质选项：文本已本地化（语言自名同款问题——下拉项不随 LText 自动重刷，切换后主动重设）
function M:_PushQualityOptions()
    local options = {}
    for i = 1, #QUALITY_KEYS do options[i] = Bridge.text.Get(QUALITY_KEYS[i]) end
    self.ui:SetDropdownOptions("Dropdown_Quality", options)
end

-- 语言选项（语言自名：各语言下都显示本语言名）
function M:_PushLanguageOptions()
    local options = {}
    for i = 1, #LOCALE_KEYS do options[i] = Bridge.text.Get(LOCALE_KEYS[i]) end
    self.ui:SetDropdownOptions("Dropdown_Language", options)
    self.ui:SetDropdown("Dropdown_Language", IndexOf(LOCALES, Bridge.text.Locale(), 1) - 1)
end

function M:_Close()
    Bridge.settings.Save()
    Bridge.ui.Close(PAGE_SETTINGS)
end

-- 确认退出对局（防重入：弹窗期间再点不再发；确认/取消都复位按钮可点）
function M:_ConfirmExit()
    if self._leaving then return end
    self._leaving = true
    self.ui:SetInteractable("Button_Exit", false)
    self.ui:ShowConfirm(
        Bridge.text.Get("UI.Settings.ExitConfirmTitle"),
        Bridge.text.Get("UI.Settings.ExitConfirmMessage"),
        Bridge.text.Get("Common.Confirm"),
        Bridge.text.Get("Common.Cancel"),
        function(ok)
            self._leaving = false
            self.ui:SetInteractable("Button_Exit", true)
            if ok then self:_ExitBattle() end
        end)
end

-- 先回大厅（导航），再请流程收尾（LeaveMatch＝Request(Main)，在途 Battle 阶段被取消、finally 释放——
-- 既有"离房/离局同一窄接口"；先导航使主动离场不依赖 OnMatchEnded 的页面切换）
function M:_ExitBattle()
    Bridge.ui.Replace(PAGE_LOBBY)
    Bridge.meta.LeaveMatch()
end

function M:OnHide()
    Bridge.settings.Save()                                    -- Back/ESC 关闭同径落盘
end

return M
