# 代码评审（主代理）：Settings 页面端口 + friend/零使用边守卫

> 运行 client-module-recut · 创建于 2026-11-09T11:50:08+08:00
>
> 代码由施工子代理撰写；本文件由主代理基于实际 diff 与验证证据评审落盘。

## 评审输入

- 代码基线：`3a2d3b32`（v6.1 开工前）→ `8028ac07`（T9 docs）+ 工作区 `.workflow` 验证证据；`git diff --stat 3a2d3b32..HEAD` = 48 files / +2328 / −338（另有未提交的 .workflow/evidence）。
- 评审角色：主代理（代码作者为施工子代理 T1-T9）
- 评审范围：Settings 页面端口（ISettingsPagePort/ProductSettingsAdapter/IPageContext/PageContext/PageLogicBase/Page_SettingsLogic/ProductSettings/AppComposition）、UI asmdef/units/domain/allowlist、friend/零使用边守卫、测试、文档；不含 Battle/Account/Lobby 另立设计。

## 变更范围

- 对照任务与方案：T1 基线/meta 债、T2 friend、T3 零使用边、T4 端口/上下文、T5 页面迁移、T6 asmdef/模型收口、T7 PlayMode、T8 棘轮、T9 文档；AC0-AC5 全覆盖。
- 变更文件/模块：
  - 端口/装配：`ISettingsPagePort.cs`（28 成员）、`ProductSettingsAdapter.cs`、`IPageContext.cs`、`PageLogicBase.cs`、`PageContext.cs`、`AppComposition.cs`。
  - 页面/模型：`Page_SettingsLogic.cs`（10 处替换/FrameRateIndex/空端口）、`ProductSettings.cs`（删 Bind/Current）。
  - 模型/门禁：`arch/units.json`（friends 55、UI refs/domainEdges）、`LiteGame.Modules.UI.asmdef`、`CrossModuleContractLocationTests.cs`（15→14）、`Docs/说明/18`（生成物）、`zero-use-edges.py`、`arch/zero-use-edges.json`（53）、`toolchain.json`、`.workflow/config.json`。
  - 测试：`ModuleFriendAllowlistTests.cs`、`SettingsPortMigrationTests.cs`、`SettingsPagePlayModeTests.cs`、`scripts/tests/test_zero_use_edges.py`。
  - 文档：说明 03/08/10/14/15/17/20/README、设置/UI总设/UI API/DI/热更/Lua退出/README、技术债+归档、进度 §58。

## 正确性与边界

- 主路径：设置页 OnShow 取 `Ctx.Settings`（28 原语），10 处读写全走端口；adapter 在 `BindProductSettings` 的 `Put` 之后创建，`PageContext` 用 `Func` 惰性取，未在 `BindPageServices` 当场 Require（时序正确）；`FrameRateIndex(ISettingsPagePort)` 唯一调用点在判空之后。
- 边界与错误路径：空端口 OnShow 早退 + 12 控件 `SetInteractable(false)` + 面板/页签可见可切 + Btn_Close 可用；所有写/动作 `?.`；CloseWithSave/OnPageHide 的 Save 不抛。PM_SET_5 实测 5/5。
- 回归风险：UI 不再引 Settings；units 27 边；allowlist 14；friend 55；zero 53；L1 1982；Unity 0 错；BuildHash shared 5937ac51 未变；meta stale 为 HEAD 已知债。

## 测试充分性

- 覆盖：friend 5/5（含未登记合成负例、作用域不递归）、SettingsPortMigration 5/5（源码/asmdef/端口 28/adapter）、Settings PlayMode 5/5（真 prefab/fake localization/画质/音量/Close+Back/空端口）、zero unittest 5/5（脚本/基线/新边红/消失边放行）、L1 1982、verify 9/9。
- 是否迎合实现：未见删/弱化断言；红测在实现前独立落盘（evidence/red.json，2/2 预期失败）；PlayMode 按“控件禁用、面板可见”实现断言，未用 activeSelf 迎合。
- 缺口：空端口只驱动 OnShow/控件/页签/关闭；其余 `?.` 分支由源码断言覆盖，属可接受边界。

## 通用性与扩展性

- 根因与复用边界：端口按页面实际原语列全，不暴露 `ProductSettings`；adapter 住 App，UI 只认端口；friend/zero-use 机制可复用，不写“如果是 Settings 就……”特判。
- 扩展点：新增设置成员 = 端口 + adapter + 页面；新增 friend = units + 测试基线（55 棘轮）；新增零使用边 = 红并需消解或 --write。
- 硬编码/特判：`BaselineFriendCount=55` 与 9 模块作用域是有意棘轮，非业务特判；zero-use 复用 domain-matrix 口径，无第二套定义。
- 命名/结构：文件单一职责；dir-flat 0；注释预算 comment-lint 0；无重复逻辑/过度抽象。

## 安全与性能

- 安全：无注入/权限/敏感信息面；源码扫描只读仓库；未新增 public static 可变状态（反而删除 `ProductSettings.Current`）。
- 性能与资源：zero-use 3.3s、L1 185s、PlayMode 109s；无热路径/每帧开销；未改共享源集；BuildHash shared 绿。

## 发现的问题与处理

| ID | 严重度 | 位置 | 问题 | 处理 | 状态 |
|---|---|---|---|---|---|
| C-1 | minor | `Page_SettingsLogic.ApplyPortlessState` | 空端口每次 OnShow 都注册 Btn_Close/页签回调；若同页反复打开可能重复注册（现有 OnButtonClick 语义若为追加） | 空端口为未装配异常态，PlayMode 只开一次；不改，记录为边界观察 | 已记录 |
| C-2 | minor | `ModuleFriendAllowlistTests.BaselineFriendCount` | 55 硬编码；新增 friend 需同步改测试 | 棘轮本意；已在测试注释说明，T8 记录 | 已记录 |
| C-3 | minor | `zero-use-edges.py` | 计数用 token Counter，与逐名 `\b` 抽样等价但非逐字证明 | 复用 domain-matrix 口径；UT-ZERO-2/3 + 抽样对照；可接受 | 已记录 |

无 blocker/major；C-1..C-3 均为已记录的边界/维护性观察，不影响本批验收。

## 复测记录

- 评审轮次: 第 1 轮
- 修复后执行的命令与结果：无修复；verify 9/9 PASS（L1 1982、arch 33/6/12/27、domain 12、Doc lane、shared 5937ac51、hot 23/66、zero 53、Settings PlayMode 5/5、toolchain docs）。
- 复评执行与复评范围（主代理）：首轮即 PASS；已核对 `Page_SettingsLogic`/端口/adapter/上下文/模型/门禁 diff 与测试断言。
- 验证证据：evidence/verify.json

## 评审结论

- 评审方式: 主代理
- 结论: PASS
- 阻塞问题: 0
- 通用性: PASS
- 扩展性: PASS
- 评审人: 主代理
- 评审时间: 2026-11-09T18:00:00+08:00
