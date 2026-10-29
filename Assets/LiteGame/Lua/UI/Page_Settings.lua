-- UI/Page_Settings.lua：设置页（分组批 2026-10-30，按《设置系统专项设计》§7）
-- 结构：左分类导航（TabGroup_Settings，5 页签）+ 右面板（Pnl_*，互斥显隐）。
-- 页签：音频 / 图形 / 显示 / 控制 / 常规；初始页签取 Bridge.settings.general.LastTab()，切换即记忆。
-- 数据：全部经 Bridge.settings（音频平铺 + graphics/display/control/general 子表）；语言走 Bridge.text。
-- 即改即生效（门面 Set 后立即应用）；落盘仍走 SettingService 脏标记三处纪律——关闭/OnHide 调 Save。
-- 对局中（导航数据 battleExit=true）：显示类三行（模式/分辨率/垂直同步）置灰——窗口重建会卡战斗帧。
-- 退出对局钮沿既有退场链：确认弹窗 → 先回大厅再 LeaveMatch（不依赖 OnMatchEnded 的页面切换）。
local class = require("Core.class")

local M = class("Page_Settings")

local PAGE_SETTINGS = "UI.Page_Settings"
local PAGE_LOBBY = "UI.Page_Lobby"

local LOCALES = { "zh-CN", "en" }
local LOCALE_KEYS = { "UI.Settings.Language.Zh", "UI.Settings.Language.En" }
local QUALITY_KEYS = { "UI.Settings.Quality.Low", "UI.Settings.Quality.Medium", "UI.Settings.Quality.High" }

local TAB_COUNT = 5
local PANELS = { "Cot_AudioPanel", "Cot_GraphicsPanel", "Cot_DisplayPanel", "Cot_ControlPanel", "Cot_GeneralPanel" }
local TAB_TEXT_KEYS = {
    "UI.Settings.Tab.Audio", "UI.Settings.Tab.Graphics", "UI.Settings.Tab.Display",
    "UI.Settings.Tab.Control", "UI.Settings.Tab.General",
}
local TAB_TEXT_NODES = { "Txt_Tab_Audio", "Txt_Tab_Graphics", "Txt_Tab_Display", "Txt_Tab_Control", "Txt_Tab_General" }

-- 下拉索引 ↔ 值（与 C# 侧允许值集对齐：Set* 会做值域校验，这里只负责"索引到值"的翻译）
local DISPLAY_MODE_VALUES = { 0, 1, 2 }                       -- 独占全屏 / 无边框 / 窗口
local DISPLAY_MODE_KEYS = {
    "UI.Settings.DisplayMode.Fullscreen", "UI.Settings.DisplayMode.Borderless", "UI.Settings.DisplayMode.Windowed",
}
local VSYNC_VALUES = { -1, 1, 0 }                             -- 跟随画质预设 / 开 / 关
local VSYNC_KEYS = { "UI.Settings.FollowPreset", "Common.On", "Common.Off" }
local WINDOWED_MODE = 2

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
    self._tab = 1
end

function M:OnShow(data)
    -- 静态文案（标签）
    self.ui:SetTextKey("Txt_Title", "Common.Settings")
    self.ui:SetTextKey("Txt_Close", "Common.Close")
    for i = 1, TAB_COUNT do self.ui:SetTextKey(TAB_TEXT_NODES[i], TAB_TEXT_KEYS[i]) end
    self.ui:SetTextKey("Txt_Volume", "UI.Settings.Volume")
    self.ui:SetTextKey("Txt_Sound", "UI.Settings.Sound")
    self.ui:SetTextKey("Txt_Quality", "UI.Settings.Quality")
    self.ui:SetTextKey("Txt_DisplayMode", "UI.Settings.DisplayMode")
    self.ui:SetTextKey("Txt_Resolution", "UI.Settings.Resolution")
    self.ui:SetTextKey("Txt_FrameRate", "UI.Settings.FrameRate")
    self.ui:SetTextKey("Txt_VSync", "UI.Settings.VSync")
    self.ui:SetTextKey("Txt_LookSensitivity", "UI.Settings.LookSensitivity")
    self.ui:SetTextKey("Txt_AdsSensitivity", "UI.Settings.AdsSensitivity")
    self.ui:SetTextKey("Txt_InvertLookY", "UI.Settings.InvertLookY")
    self.ui:SetTextKey("Txt_Language", "UI.Settings.Language")
    self.ui:SetTextKey("Txt_ResetDefaults", "UI.Settings.ResetDefaults")
    self.ui:SetTextKeyArgs("Txt_Version", "Common.Version", Bridge.meta.Version())

    -- 退出对局（对局中打开才有 battleExit 数据）
    self._exitable = (type(data) == "table") and (data.battleExit == true)
    self._leaving = false
    self.ui:SetVisible("Button_Exit", self._exitable)
    if self._exitable then
        self.ui:SetTextKey("Txt_Exit", (type(data) == "table" and data.exitKey) or "UI.Settings.Exit")
        self.ui:OnButton("Button_Exit", function() self:_ConfirmExit() end)
    end

    -- 只读事实
    local account = Bridge.meta.AccountId()
    if account ~= nil and account ~= "" then
        self.ui:SetVisible("Txt_Account", true)
        self.ui:SetTextKeyArgs("Txt_Account", "UI.Settings.Account", account)
    else
        self.ui:SetVisible("Txt_Account", false)
    end

    local server = Bridge.meta.ServerUrl()
    if server ~= nil and server ~= "" then
        self.ui:SetVisible("Txt_ServerAddress", true)
        self.ui:SetTextKeyArgs("Txt_ServerAddress", "UI.Settings.Server", server)
    else
        self.ui:SetVisible("Txt_ServerAddress", false)
    end

    -- 页签：先按记忆切面板（静默），再设选中态（静默——程序 Set 不触发回调）
    self._tab = Bridge.settings.general.LastTab() + 1
    if self._tab < 1 or self._tab > TAB_COUNT then self._tab = 1 end
    self:_ApplyTab(self._tab)
    self.ui:SetTab("TabGroup_Settings", self._tab - 1)

    -- 选项文本（随语言重建——下拉项是引擎组件，不随 LText 自动刷新）
    self:_PushAllOptions()

    -- 初值（程序 Set 不触发变化回调）
    self:_RefreshControls()

    -- 变化通知（只回写设置与生效面；落盘由 Save 承担）
    self.ui:OnTabChanged("TabGroup_Settings", function(index) self:_OnTab(index + 1) end)
    self.ui:OnSliderChanged("Slider_Volume", function(value) Bridge.settings.SetVolume(value) end)
    self.ui:OnToggleChanged("Toggle_Sound", function(on) Bridge.settings.SetSoundEnabled(on) end)
    self.ui:OnDropdownChanged("Dropdown_Quality", function(index) Bridge.settings.SetQuality(index) end)
    self.ui:OnDropdownChanged("Dropdown_DisplayMode", function(index)
        Bridge.settings.display.SetMode(DISPLAY_MODE_VALUES[index + 1])
        self:_RefreshResolutionRow()
    end)
    self.ui:OnDropdownChanged("Dropdown_Resolution", function(index)
        Bridge.settings.display.SetResolutionIndex(index)
    end)
    self.ui:OnDropdownChanged("Dropdown_FrameRate", function(index)
        Bridge.settings.display.SetFrameRate(Bridge.settings.display.FrameRateOption(index))
    end)
    self.ui:OnDropdownChanged("Dropdown_VSync", function(index)
        Bridge.settings.display.SetVSync(VSYNC_VALUES[index + 1])
    end)
    self.ui:OnSliderChanged("Slider_LookSensitivity", function(value)
        Bridge.settings.control.SetLookSensitivity(value)
        self:_PushSensitivityText()
    end)
    self.ui:OnSliderChanged("Slider_AdsSensitivity", function(value)
        Bridge.settings.control.SetAdsSensitivity(value)
        self:_PushSensitivityText()
    end)
    self.ui:OnToggleChanged("Toggle_InvertLookY", function(on)
        Bridge.settings.control.SetInvertLookY(on)
    end)
    self.ui:OnDropdownChanged("Dropdown_Language", function(index)
        Bridge.text.SetLocale(LOCALES[index + 1])
        self:_PushAllOptions()                                -- 新语言下的全部下拉文本
    end)
    self.ui:OnButton("Button_ResetDefaults", function() self:_ConfirmReset() end)
    self.ui:OnButton("Button_Close", function() self:_Close() end)

    -- 对局中禁用显示类（窗口重建卡战斗帧）
    local locked = self._exitable
    self.ui:SetInteractable("Dropdown_DisplayMode", not locked)
    self.ui:SetInteractable("Dropdown_VSync", not locked)
    if locked then self.ui:SetInteractable("Dropdown_Resolution", false) end
end

-- 页签切换（用户点击；持久化记忆）
function M:_OnTab(index)
    self._tab = index
    self:_ApplyTab(index)
    Bridge.settings.general.SetLastTab(index - 1)
end

function M:_ApplyTab(index)
    for i = 1, TAB_COUNT do
        self.ui:SetVisible(PANELS[i], i == index)
    end
end

-- 选项文本：语言切换后必须整批重建（下拉项不随 LText 自动刷新）
function M:_PushAllOptions()
    local quality = {}
    for i = 1, #QUALITY_KEYS do quality[i] = Bridge.text.Get(QUALITY_KEYS[i]) end
    self.ui:SetDropdownOptions("Dropdown_Quality", quality)

    local modes = {}
    for i = 1, #DISPLAY_MODE_KEYS do modes[i] = Bridge.text.Get(DISPLAY_MODE_KEYS[i]) end
    self.ui:SetDropdownOptions("Dropdown_DisplayMode", modes)

    local vsync = {}
    for i = 1, #VSYNC_KEYS do vsync[i] = Bridge.text.Get(VSYNC_KEYS[i]) end
    self.ui:SetDropdownOptions("Dropdown_VSync", vsync)

    self:_PushFrameRateOptions()
    self:_RefreshResolutionRow()

    local locales = {}
    for i = 1, #LOCALE_KEYS do locales[i] = Bridge.text.Get(LOCALE_KEYS[i]) end
    self.ui:SetDropdownOptions("Dropdown_Language", locales)
    self.ui:SetDropdown("Dropdown_Language", IndexOf(LOCALES, Bridge.text.Locale(), 1) - 1)
end

-- 帧率档：数量与取值由 C# 给（桌面/移动不同档），Lua 只做文本
function M:_PushFrameRateOptions()
    local count = Bridge.settings.display.FrameRateOptionCount()
    local options = {}
    for i = 0, count - 1 do
        local cap = Bridge.settings.display.FrameRateOption(i)
        options[i + 1] = (cap == 0) and Bridge.text.Get("UI.Settings.FrameRate.Unlimited") or tostring(cap)
    end
    self.ui:SetDropdownOptions("Dropdown_FrameRate", options)
end

-- 分辨率行：只在窗口模式可改；全屏/无边框显示"跟随显示器"并置灰
function M:_RefreshResolutionRow()
    local mode = Bridge.settings.display.Mode()
    if mode ~= WINDOWED_MODE then
        self.ui:SetDropdownOptions("Dropdown_Resolution", { Bridge.text.Get("UI.Settings.Resolution.FollowDisplay") })
        self.ui:SetDropdown("Dropdown_Resolution", 0)
        self.ui:SetInteractable("Dropdown_Resolution", false)
        return
    end

    local count = Bridge.settings.display.ResolutionCount()
    local options = {}
    for i = 0, count - 1 do options[i + 1] = Bridge.settings.display.ResolutionLabel(i) end
    self.ui:SetDropdownOptions("Dropdown_Resolution", options)
    local index = Bridge.settings.display.ResolutionIndex()
    self.ui:SetDropdown("Dropdown_Resolution", index >= 0 and index or 0)
    self.ui:SetInteractable("Dropdown_Resolution", not self._exitable)
end

function M:_PushSensitivityText()
    self.ui:SetText("Txt_LookSensitivityValue", string.format("%d%%",
        math.floor(Bridge.settings.control.LookSensitivity() * 100 + 0.5)))
    self.ui:SetText("Txt_AdsSensitivityValue", string.format("%d%%",
        math.floor(Bridge.settings.control.AdsSensitivity() * 100 + 0.5)))
end

function M:_RefreshControls()
    self.ui:SetSlider("Slider_Volume", Bridge.settings.Volume())
    self.ui:SetToggle("Toggle_Sound", Bridge.settings.SoundEnabled())
    self.ui:SetDropdown("Dropdown_Quality", Bridge.settings.Quality())

    self.ui:SetDropdown("Dropdown_DisplayMode", IndexOf(DISPLAY_MODE_VALUES, Bridge.settings.display.Mode(), 2) - 1)
    self:_RefreshResolutionRow()
    self.ui:SetDropdown("Dropdown_FrameRate", self:_FrameRateIndex())
    self.ui:SetDropdown("Dropdown_VSync", IndexOf(VSYNC_VALUES, Bridge.settings.display.VSync(), 1) - 1)

    self.ui:SetSlider("Slider_LookSensitivity", Bridge.settings.control.LookSensitivity())
    self.ui:SetSlider("Slider_AdsSensitivity", Bridge.settings.control.AdsSensitivity())
    self.ui:SetToggle("Toggle_InvertLookY", Bridge.settings.control.InvertLookY())
    self:_PushSensitivityText()
end

-- 当前帧率档 → 下拉索引（找不到就用平台默认档）
function M:_FrameRateIndex()
    local current = Bridge.settings.display.FrameRate()
    for i = 0, Bridge.settings.display.FrameRateOptionCount() - 1 do
        if Bridge.settings.display.FrameRateOption(i) == current then return i end
    end
    return 0
end

-- 恢复默认：确认后全量回默认 + 刷新控件（语言不在此列——`lang` 键当前无消费者，切换只走 Bridge.text，
-- 不在本页伪造"默认语言"常量；语言持久化缺口登记见审计表）
function M:_ConfirmReset()
    self.ui:ShowConfirm(
        Bridge.text.Get("UI.Settings.ResetConfirmTitle"),
        Bridge.text.Get("UI.Settings.ResetConfirmMessage"),
        Bridge.text.Get("Common.Confirm"),
        Bridge.text.Get("Common.Cancel"),
        function(ok)
            if not ok then return end
            Bridge.settings.ResetDefaults()
            self:_PushAllOptions()
            self:_RefreshControls()
        end)
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

-- 先回大厅（导航），再请流程收尾（LeaveMatch＝Request(Main)，在途 Battle 阶段被取消、finally 释放）
function M:_ExitBattle()
    Bridge.ui.Replace(PAGE_LOBBY)
    Bridge.meta.LeaveMatch()
end

function M:OnHide()
    Bridge.settings.Save()                                    -- Back/ESC 关闭同径落盘
end

return M
