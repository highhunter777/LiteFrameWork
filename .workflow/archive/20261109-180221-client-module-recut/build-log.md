# 施工记录：客户端按功能/行为重切模块（方案）

> 运行 client-module-recut · 创建于 2026-11-09T11:50:08+08:00

## 施工记录

<!-- 任务开始、完成、阻塞、取消等事件由 workflow.py 自动追加。 -->
- 2026-11-09T15:37:46+08:00 · 新增任务 T1「Phase 0 基线快照与 meta 债登记」
- 2026-11-09T15:37:46+08:00 · 新增任务 T2「friend 允许集单一来源 + L1 守卫」
- 2026-11-09T15:37:47+08:00 · 新增任务 T3「零使用边脚本/基线 + toolchain 注册」
- 2026-11-09T15:38:00+08:00 · 新增任务 T4「ISettingsPagePort + ProductSettingsAdapter + 页面上下文」
- 2026-11-09T15:38:00+08:00 · 新增任务 T5「Page_SettingsLogic 10 处替换 + FrameRateIndex + 空端口守卫」
- 2026-11-09T15:38:01+08:00 · 新增任务 T6「UI asmdef 去 Settings + units/domain/allowlist 收口」
- 2026-11-09T15:38:12+08:00 · 新增任务 T7「Settings 页 PlayMode 用例」
- 2026-11-09T15:38:13+08:00 · 新增任务 T8「friend/零使用边棘轮」
- 2026-11-09T15:38:13+08:00 · 新增任务 T9「文档回写与审计登记路径核销」
- 2026-11-09T15:40:04+08:00 · 更新任务 T9（files）
- 2026-11-09T15:57:46+08:00 · 开始任务 T1「Phase 0 基线快照与 meta 债登记」
- 2026-11-09T16:08:04+08:00 · 更新任务 T1（verify）
- 2026-11-09T16:08:05+08:00 · 完成任务 T1 · 证据: HEAD 3a2d3b32/0283a9e3；生产树干净；arch 33/6/12/28、domain 12、doc 0、shared 5937ac51、hot 23/66 绿；L1 1972 基线（3a2d3b32 前），当前 7 红=新增红测；meta stale 已登记 技术债 + 客户端重构线 §58.1（g…
- 2026-11-09T16:08:27+08:00 · 更新任务 T2（verify）
- 2026-11-09T16:08:28+08:00 · 开始任务 T2「friend 允许集单一来源 + L1 守卫」
- 2026-11-09T16:09:12+08:00 · 开始任务 T4「ISettingsPagePort + ProductSettingsAdapter + 页面上下文」
- 2026-11-09T16:14:15+08:00 · 完成任务 T2 · 证据: arch/units.json +73 行，9 单元 friends 合计 55（Battle4/Composition7/Config1/Dev8/Map7/Meta7/Procedure7/Session7/UI7）；ModuleFriendAllowlistTests 5/5 通过；arch-check OK …
- 2026-11-09T16:14:43+08:00 · 开始任务 T3「零使用边脚本/基线 + toolchain 注册」
- 2026-11-09T16:26:16+08:00 · 完成任务 T4 · 证据: 6 授权文件 + 2 .meta；ISettingsPagePort 28 成员（15 读+13 写/动作，无 ProductSettings/Apply）；ProductSettingsAdapter 在 App；IPageContext.Settings/PageLogicBase.Settings；PageCo…
- 2026-11-09T16:26:41+08:00 · 开始任务 T5「Page_SettingsLogic 10 处替换 + FrameRateIndex + 空端口守卫」
- 2026-11-09T16:38:11+08:00 · 完成任务 T5 · 证据: Page_SettingsLogic 10 处替换（+55/-26，305→334）、FrameRateIndex(ISettingsPagePort)、空端口 ApplyPortlessState+12 控件禁用、OnShow 早退、10 处 ?.；删 ProductSettings.Bind/Current（-1…
- 2026-11-09T16:40:34+08:00 · 完成任务 T3 · 证据: zero-use-edges.py（复用 domain-matrix 口径，--write/--check，子集语义）；arch/zero-use-edges.json 54 条基线；toolchain.json quality.zero-use-edges；config commands +1；unittest 5…
- 2026-11-09T16:41:03+08:00 · 开始任务 T6「UI asmdef 去 Settings + units/domain/allowlist 收口」
- 2026-11-09T16:52:53+08:00 · 完成任务 T6 · 证据: UI asmdef 去 Settings；units UI refs/domainEdges 去 Ui→Settings（28→27）；CrossModule allowlist 15→14；Docs/18 生成物 Ui→Settings 0、Ui 类型 28；SettingsPortMigration 5/5 + …
- 2026-11-09T16:52:53+08:00 · 开始任务 T7「Settings 页 PlayMode 用例」
- 2026-11-09T16:52:54+08:00 · 开始任务 T8「friend/零使用边棘轮」
- 2026-11-09T17:00:12+08:00 · 完成任务 T8 · 证据: zero-use-edges --write 收紧 54→53（删除 UI→Settings）；--check 53/53；unittest 5/5；friend 55；CrossModule allowlist 14（ProductSettings 已删）；arch 27 edges；L1 1982 PASS（19…
- 2026-11-09T17:15:19+08:00 · 完成任务 T7 · 证据: SettingsPagePlayModeTests 5/5（PM_SET_1..5）；真 Page_Settings.prefab + 真 UIService/PageContext；fake ILocalizationService + fake ISettingsPagePort；画质/音量写入、Close+Ba…
- 2026-11-09T17:15:21+08:00 · 开始任务 T9「文档回写与审计登记路径核销」
- 2026-11-09T17:50:21+08:00 · 完成任务 T9 · 证据: 19 份 Docs 回写：说明 03/08/10/14/15/17/20/README、设计 设置/UI总设/UI API/DI/热更/Lua退出/README、审计 技术债+归档、进度 §58；doc-lint 0；toolchain docs current；Doc lane PASS；arch 27 edges…

## 关键决定

- **v6.1 最小切片**：只做 Settings 页面端口 + friend/零使用边守卫/棘轮；Battle 内部收口、Account/Lobby 重切另立设计（§7），避免边设计边施工。
- **端口形状**：ISettingsPagePort 28 个原语成员（15 读 + 13 写/动作，无 Apply），不暴露 ProductSettings；ProductSettingsAdapter 住 App，PageContext 用 Func 惰性取，创建点在 BindProductSettings 的 Put 之后，避免 BindPageServices 当场 Require 的时序抛。
- **空端口策略**：OnShow 早退 + 12 个设置控件禁用 + 面板/页签仍可见可切 + 关闭可用；不隐藏页面，读写全 ?.。
- **friend 单源**：arch/units.json 每 unit friends（程序集名），作用域 9 个 Modules/*/AssemblyInfo.cs、55 基线；最近 asmdef 归属、不递归子单元。
- **零使用边口径**：复用 domain-matrix 的去注释/字符串 + depth=1 顶层类型；基线子集只减不增；挂 .workflow/config.json，不挂 L1（扫描面宽）。
- **BuildHash**：meta stale 记已知债，不重烘 Server/MetaServer/Host/BuildHash.g.cs；本批只守 --check-shared。

## 偏差与处理

- **Phase 0/1 顺序**：原方案曾写“可并行”，实际都改 arch/units.json/Tests，改为 Phase 0 → Phase 1 顺序；T3 基线在 T4/T5 中间态生成 54 条，T6 删 Ui→Settings 后 T8 --write 收紧为 53，属预期。
- **空端口表现**：方案写“禁用/收起”，实现选择“控件禁用、面板可见、页签可切、关闭可用”；T7 按此断言（interactable=false，不断言 activeSelf=false）。
- **L1 计数**：基线 1972 → 1982（+10：friend 5 + SettingsPortMigration 5）；非回归，已在 §58 归因。
- **T9 范围**：只核销本批触及的审计路径，未全量清理 69 条历史 stale 路径；剩余记入 §58.4 已知债。
- **工具数**：T9 附带修正说明 README 71→72（toolchain.json 实况）。

## 问题与修复

- **红测编译**：LiteFramework.JsonValueKind 遮蔽 System.Text.Json.JsonValueKind；测试用 `using StjKind = System.Text.Json.JsonValueKind` 修复，语义不变。
- **PageContext 兼容**：第 7 参用可选 `Func<ISettingsPagePort> settings = null`，现有 6 参调用点不受影响。
- **删除静态门面**：T5 删 ProductSettings.Bind/Current 后，AppComposition 同步删 Bind 调用；全仓 grep 无代码消费者。
- **T6 生成物行尾**：arch-check --write 把 ScanTargets.Generated.cs 落成 LF 产生仅行尾 diff，已 git checkout 还原；无内容 diff。
- **T3 中间态红**：domain-matrix 在 T4/T5 后报 Ui→Settings 16→0，T6 --write 收口；非 T3 引入。
- **T7 时限**：首次 PlayMode 调用被 120s 工具时限掐断，轮询后重跑 5/5。

## 验证结果

- 2026-11-09T15:56:41+08:00 · 先行测试 PASS · 2/2 · 证据 evidence/red.json
- 2026-11-09T17:56:46+08:00 · 验证 PASS · 9/9 · 证据 evidence/verify.json

## 交付摘要

- 方案 v6.1 落地：Settings 页面端口（28 成员/adapter/页面 10 处/空端口）+ friend 55 + zero-use 53 + allowlist 14 + 27 域边。
- 测试：friend 5/5、SettingsPortMigration 5/5、Settings PlayMode 5/5、zero unittest 5/5、L1 1982。
- 提交：3a2d3b32（基线/WIP）→ 0283a9e3（红测）→ 66b9f632（T1-T5）→ d6551f0e（T6-T8）→ 8028ac07（T9 docs）。

## 未决事项

- BuildHash meta stale（已知债，另批重烘）。
- Battle 内部收口、Account/Lobby 重切（§7 另立设计）。
- 全量 69 条审计 stale 路径未清（只核销本批触及项）。
- verify/代码评审/归档由流程收尾。

## 文件清单

- 端口/装配：ISettingsPagePort.cs、ProductSettingsAdapter.cs、IPageContext.cs、PageLogicBase.cs、PageContext.cs、AppComposition.cs。
- 页面/模型：Page_SettingsLogic.cs、ProductSettings.cs。
- 模型/门禁：arch/units.json、LiteGame.Modules.UI.asmdef、CrossModuleContractLocationTests.cs、Docs/说明/18-域间引用矩阵.md、zero-use-edges.py、arch/zero-use-edges.json、toolchain.json、.workflow/config.json。
- 测试：ModuleFriendAllowlistTests.cs、SettingsPortMigrationTests.cs、SettingsPagePlayModeTests.cs、scripts/tests/test_zero_use_edges.py。
- 文档：说明 03/08/10/14/15/17/20/README、设置/UI总设/UI API/DI/热更/Lua退出/README、技术债+归档、进度 §58。

## 后续建议

- 归档本运行；下一批另立 Account/Lobby 或 Battle 设计（先裁决方案 §7 七项）。
- 发布 Meta 产物前重烘 Server/MetaServer/Host/BuildHash.g.cs 并跑 build-hash --check。
- 保持 friend/zero-use 棘轮：新增 friend 或零使用边必须同步登记/收紧基线。

## 阶段流转

<!-- 阶段推进与回退记录由 workflow.py 自动追加。 -->
- 2026-11-09T11:50:08+08:00 · 初始化运行，当前阶段「提出方案」
- 2026-11-09T11:52:21+08:00 · 完成「提出方案」→ 「子代理评审方案」
- 2026-11-09T14:44:46+08:00 · 完成「子代理评审方案」→ 「用户确认验收标准」
- 2026-11-09T15:32:27+08:00 · 完成「用户确认验收标准」→ 「实施任务清单」
- 2026-11-09T15:38:59+08:00 · 完成「实施任务清单」→ 「任务评审（本仓默认跳过：2026-11-09 用户裁决）」
- 2026-11-09T15:38:59+08:00 · 完成「任务评审（本仓默认跳过：2026-11-09 用户裁决）」→ 「子代理先写测试」
- 2026-11-09T15:56:53+08:00 · 完成「子代理先写测试」→ 「子代理正式施工」
- 2026-11-09T17:58:43+08:00 · 完成「子代理正式施工」→ 「测试代码」
- 2026-11-09T17:58:52+08:00 · 完成「测试代码」→ 「主代理评审代码」
- 2026-11-09T18:00:48+08:00 · 完成「主代理评审代码」→ 「回写与收尾」
- 2026-11-09T18:02:20+08:00 · 完成「回写与收尾」→ 完成
