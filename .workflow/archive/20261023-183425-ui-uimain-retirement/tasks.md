# 实施任务清单：UI 系统 UIMain 退役：注册表锚迁移 · 产品注册摘除 · 测试载体归位

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 7/7（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | 先红：退役守卫与既有夹具目标态 | - | 完成 | 新增 UiUimainRetirementEditModeTests（页面清单无 UIMain/11页12行、载体新路径存在、main.lua 无 UIMain、RegistryFiller 锚用 Page_Login）；既有 PageManifest/Wiring/Validation/SafeArea/BindingTools/U0/LuaPage/EnvRebuild 夹具改到目标态；定向运行断言失败（非编译/环境错误） | python scripts/gate/unity-tests.py --mode editor --filter UiUimainRetirementEditModeTests | wf verify --red PASS（19 命令；4 条新失败 + 既有 14 条通过；退役守卫 5/6 断言失败） |
| T2 | 启动锚迁移：RegistryFiller KnownUiKey + main.lua 去 UIMain | T1 | 完成 | RegistryFiller 单源常量指向 LuaKeys.UI.Page_Login 并用于 ValidateKnownKeys；main.lua 不再 require/暴露 UI.UIMain，冒烟日志改 main.lua loaded；无其它生产引用 LuaKeys.UI.UIMain | python scripts/gate/unity-tests.py --mode editor --filter UiUimainRetirementEditModeTests | RegistryFiller 增 KnownUiKey=LuaKeys.UI.Page_Login 并用于 ValidateKnownKeys；main.lua 去 UI.UIMain require/全局，冒烟改 main.lua lo… |
| T3 | 测试载体归位：Tmp_UiTestPage prefab + Tests.UiTestPage Lua | T1 | 完成 | UIMain.prefab 改名 Tmp_UiTestPage.prefab（根同名、去 CanvasScaler、BindName Button_Close/Txt_Label）；UIMain.lua 移到 Lua/Tests/UiTestPage.lua（class/viewName/绑定名同步）；旧路径不再存在；unity-pipeline 执行 asset 改名/组件/字段 | python scripts/gate/unity-tests.py --mode editor --filter UiUimainRetirementEditModeTests | unity-pipeline rename_asset UIMain→Tmp_UiTestPage（保 GUID）；run_script 根名/去 CanvasScaler/BindName 2 处（ok bindRenamed=2）；c… |
| T4 | 表与生成物：tbuiform 删 id=1 + Luban/LuaKeys/页面清单/BuildHash | T2, T3 | 完成 | _uiform_row.py 增 --delete-id 1（备份/确认/回滚）；Luban/gen.bat 重生成 tbuiform.lua/LuaKeys.g.cs/表数据；gen-build-hash.py 复算；ui.page-manifest 重生成 11页12行；11 契约不变 | python scripts/codegen/gen-build-hash.py --check | _uiform_row.py 增 --delete-id（备份/确认/回滚）删 id=1；Luban/gen.bat 发布 tbuiform.lua/LuaKeys.g.cs（6 UI 路径）；UiPageManifestEditor.R… |
| T5 | 真实面校准与基线收口：11/12、302/0、Wiring 无跳过 | T4 | 完成 | UiValidator.Run 真实面 Error=0/Warning=302/命名 Warning=0；PageManifest 11页12行且无 UIMain；Wiring 写入 11 契约且不跳过 UIMain；6 个载体夹具全绿；合成夹具路径去 UIMain | python scripts/gate/unity-tests.py --mode editor --filter UiValidationEditModeTests | PageManifest 16/16（11页12行）· Wiring 8/8（无跳过）· Validation 14/14（Warning 302/命名0）· ValidationRules 10/10 · SafeArea 6/6 · … |
| T6 | 文档与审计回写：UIMain 退役事实+代码地图/技术总结/审计/进度 | T5 | 完成 | 设计/注释去 UIMain 现行页表述；代码地图/技术总结页数变 6；审计登记 UIMain 根名债清零与退役；施工进度新章；doc-lint 0/0 | python scripts/gate/doc-lint.py | 代码地图/技术总结04·11/项目说明03/UI系统总设计/工具链设计/schema/审计/进度第7章回写；doc-lint 0/0 |
| T7 | gate 异步通道：过滤参数透传与陈旧状态防护 | - | 完成 | 异步发射不再把工具 --timeout 放在 filter 后（实测会丢 filter 跑全集）；轮询忽略发射前遗留的 completed 状态；testName 结果必须命中 filter，否则判红 | python scripts/gate/unity-tests.py --mode editor --filter UiUimainRetirementEditModeTests --timeout 600 | 异步发射去掉 filter 后的工具 --timeout（实测会丢 filter 跑全集）；发射前 wait_editor_ready 等域重载/编译/Play 结束；陈旧 completed 按 mtime 忽略；testName 结果… |

## 任务详情

### T1 · 先红：退役守卫与既有夹具目标态

- 文件: Assets/Tests/EditMode/UiUimainRetirementEditModeTests.cs, Assets/Tests/EditMode/UiPageManifestEditModeTests.cs, Assets/Tests/EditMode/UiToolchainWiringEditModeTests.cs, Assets/Tests/EditMode/UiValidationEditModeTests.cs, Assets/Tests/EditMode/SafeAreaEditModeTests.cs, Assets/Tests/EditMode/UiBindingToolsEditModeTests.cs, Assets/Tests/EditMode/UiU0EditModeTests.cs, Assets/Tests/UI/PlayMode/LuaPagePlayModeTests.cs, Assets/Tests/UI/PlayMode/EnvRebuildPlayModeTests.cs

### T2 · 启动锚迁移：RegistryFiller KnownUiKey + main.lua 去 UIMain

- 文件: Assets/LiteGame/LuaBridge/RegistryFiller.cs, Assets/LiteGame/Lua/main.lua

### T3 · 测试载体归位：Tmp_UiTestPage prefab + Tests.UiTestPage Lua

- 文件: Assets/ShareResource/UI/Screens/UIMain.prefab, Assets/LiteGame/Lua/UI/UIMain.lua, Assets/ShareResource/UI/Screens/Tmp_UiTestPage.prefab, Assets/LiteGame/Lua/Tests/UiTestPage.lua

### T4 · 表与生成物：tbuiform 删 id=1 + Luban/LuaKeys/页面清单/BuildHash

- 文件: Luban/Data/#uiform.xlsx, scripts/codegen/_uiform_row.py, Assets/LiteGame/Lua/Cfg/tbuiform.lua, Assets/LiteGame/LuaBridge/Generated/LuaKeys.g.cs, Assets/LiteGame/Editor/UiTools/UiPageManifest.json

### T7 · gate 异步通道：过滤参数透传与陈旧状态防护

- 文件: scripts/gate/unity-tests.py

