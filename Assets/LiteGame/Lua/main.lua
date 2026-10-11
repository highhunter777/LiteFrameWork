-- main.lua：启动入口（只做 require/定义，按约定路径暴露逻辑表——设计方案 §4.4）
-- 注意：M3 阶段目录名与设计方案 §4.4 的大小写差异（Core/Cfg/UI 首字母大写）为既有磁盘状态，
-- Windows 下 require 不区分；跨平台打包前（M6）统一为小写。

require("Core.class")

-- 单界面逻辑表（M4 UI 壳消费；M3 验收 = 可寻址、可 require）
UI = UI or {}
UI.UIMain = require("UI.UIMain")

-- 冒烟锚点：证明 main.lua 真实执行到尾
log.info("main.lua loaded: UI.UIMain = " .. tostring(UI.UIMain ~= nil))
