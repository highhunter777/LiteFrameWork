# 施工记录：UI 系统 P2-3a：令牌源与主题资产（JSON→UiThemeAsset，退役硬编码）

> 运行 ui-p2-token-base · 创建于 2026-10-24T12:10:40+08:00

## 施工记录

<!-- 任务开始、完成、阻塞、取消等事件由 workflow.py 自动追加。 -->
- 2026-10-24T13:07:21+08:00 · 新增任务 T1「令牌源 JSON 定稿（schema 2.0：12 色 + 5 文字角色 + 五组空集）」
- 2026-10-24T13:07:21+08:00 · 新增任务 T2「令牌模型、校验器与规范序列化（sourceHash）」
- 2026-10-24T13:07:21+08:00 · 新增任务 T3「UiThemeAsset SO 与确定性生成器（含首份生成物）」
- 2026-10-24T13:07:22+08:00 · 新增任务 T4「漂移规则 ui.style.token-drift 与上下文注入缝」
- 2026-10-24T13:07:30+08:00 · 新增任务 T5「读面与值表退役（UiTokenCatalog / UiStyle / UiTextStyle / 工具 With 重载）」
- 2026-10-24T13:07:30+08:00 · 新增任务 T6「ui.style.tokens 工具登记与工具索引重生成」
- 2026-10-24T13:07:30+08:00 · 新增任务 T7「文档回写与收尾门禁（含 AC5 白名单证据）」
- 2026-10-24T13:44:27+08:00 · 开始任务 T1「令牌源 JSON 定稿（schema 2.0：12 色 + 5 文字角色 + 五组空集）」
- 2026-10-24T13:44:27+08:00 · 完成任务 T1 · 证据: UiStyleTokens.json：12 color.* + 5 font.* + 五组空集；UiThemeTokensEditModeTests 13/13
- 2026-10-24T13:44:27+08:00 · 开始任务 T2「令牌模型、校验器与规范序列化（sourceHash）」
- 2026-10-24T13:44:27+08:00 · 完成任务 T2 · 证据: UiThemeTokens/校验器/UiThemeTokenJson 实现；UiThemeTokensEditModeTests 13/13（校验负例/未知字段/排序/指纹）
- 2026-10-24T13:44:27+08:00 · 开始任务 T3「UiThemeAsset SO 与确定性生成器（含首份生成物）」
- 2026-10-24T13:44:27+08:00 · 完成任务 T3 · 证据: UiThemeAsset + 生成器；UiThemeAssetGeneratorEditModeTests 7/7；eval 二次生成 变更=否；资产 12/5/0×5
- 2026-10-24T13:44:27+08:00 · 开始任务 T4「漂移规则 ui.style.token-drift 与上下文注入缝」
- 2026-10-24T13:44:28+08:00 · 完成任务 T4 · 证据: StyleTokenDriftRule + UiValidationContext 注入缝；retirement 规则两例绿（一致 0 条 / 改 JSON 1 条 Error / 重生成 0 条）
- 2026-10-24T13:44:33+08:00 · 开始任务 T5「读面与值表退役（UiTokenCatalog / UiStyle / UiTextStyle / 工具 With 重载）」
- 2026-10-24T13:44:33+08:00 · 完成任务 T5 · 证据: UiStyle/UiTextStyle 退役为 catalog 读口（无字面量）；retirement 7/7；style-rule 4/4；text-style 8/8；validation 14/14（302 不变）
- 2026-10-24T13:44:34+08:00 · 开始任务 T6「ui.style.tokens 工具登记与工具索引重生成」
- 2026-10-24T13:44:34+08:00 · 完成任务 T6 · 证据: ui.style.tokens 登记；toolchain check OK（1 既存告警）；docs 重生成 + --check 通过；list 可见；Catalog 12/12 + Wiring 8/8
- 2026-10-24T14:06:51+08:00 · 开始任务 T7「文档回写与收尾门禁（含 AC5 白名单证据）」
- 2026-10-24T14:06:51+08:00 · 完成任务 T7 · 证据: 工具链 §8.1/§8.3、Families Note、施工进度第 10 章 + 卷索引、build-log 回写；AC5：git status Assets/ 仅白名单、ShareResource 0 变更、临时目录已清；wf verify PASS 33/33

## 关键决定

- **令牌源放编辑器目录、SO 放收集目录**：`UiStyleTokens.json` 是设计态源，放 `Assets/LiteGame/Editor/UiTools/`（不进包；该目录已有 `UiTools/*.json text eol=lf` 规则）；`UiTheme.asset` 按设计 §8.3 放 `Assets/GameData/Config/UI/`（收集组内），避免把设计源打进 Player。
- **组按消费者落地**：只写有消费者的 `color`（12）与 `typography`（5），另五组空集；实现按"新增组＝加一行校验前缀表"组织，不为空组造工具。
- **8bit 量化**：schema 的 `colorValue` 只有 `#RRGGBBAA`；用 `UiThemeColorHex.Quantize`（`Mathf.RoundToInt`）作为唯一取整口径，旧浮点值与量化值差 ≤0.5/255，全部落在 `UiStyleTool` 阈值 1e-4 内（实测 302/161/141 未漂）。
- **退役但不破调用点**：`UiStyle`/`UiTextStyle` 的旧名改为只读属性（每次从 catalog 物化、无静态缓存），`All`/`TryGet` 签名保留；工具新增 `…With(catalog, …)` 重载，`StyleColorDriftRule`/`StyleTextDriftRule`/`EditorToolHandlers` 既有调用点零改动（仅重新编译）。
- **生成幂等＝"不写"**：生成器先算期望快照并与盘上 SO 逐字段比较，无差异直接返回 `changed=false`（不触碰文件，字节自然不变）；有差异才写——比较面与 `ui.style.token-drift` 共用 `UiThemeSnapshot`，不写第二套比对。
- **规范文本口径**：`JsonProperty(Order)` 定字段序 + 组内按 ID `Ordinal` 排序 + LF + 末尾换行 + null 省略 + 未知字段拒绝；`sourceHash` 取规范文本 SHA-256。

## 偏差与处理

- **测试写法偏差（不影响验证强度）**：编辑测试程序集 `overrideReferences=true` 且未引 Newtonsoft，首版「未知字段被拒绝」用例用 `Assert.Throws<JsonSerializationException>` 导致编译失败；改为 `Assert.That(..., Throws.Exception)`，断言目标仍是"反序列化必须抛异常"。
- **生成器附带目录创建**：为让夹具能在临时目录建资产，`UiThemeAssetGenerator` 内置 `EnsureFolderRecursive`（仅 `AssetDatabase.CreateFolder`，无额外行为面）；方案未逐字写明，属实现必要件。
- **批外修复 1**：`scripts/gate/unity-tests.py` 两处 `Path(__file__).resolve().parents[2]` 触发 `test_single_source_helpers` 基线红（P2-2 引入），改为 `_common.repo_root()`；与 `scripts/gate/test.py` 同型，不属本批设计面但阻塞全量门禁（已在施工进度第 10 章登记）。
- **批外修复 2**：同一脚本的异步通道只对 `completed` 终态做陈旧判定，且 `failed/error/cancelled` 终态直接采信——并行跑测时出现「上一次运行的终态被当成本次结果」（filter 不匹配）。改为：终态 payload 的 filter 不匹配且 `total>0` 时继续轮询到本轮结果（0 选中仍快速判红），并把陈旧判据从"仅 completed"扩到全部终态。属门禁稳健性修复，不放宽任何判定。
- 其余与方案一致：资产路径/校验口径/规则 ID/工具 ID/夹具命名均未偏离。

## 问题与修复

1. **首轮编译失败**：测试引用 `Newtonsoft.Json`（测试程序集不引用）→ 改 `Throws.Exception` 断言（见偏差 1）。
2. **二轮编译失败**：`StyleTokenDriftRule` 使用 `UiThemeAsset` 但缺 `using LiteClient.UI`（运行时装集类型）→ 补 using 后编译通过。
3. **缺组校验被"补空组"掩盖**：`UiThemeTokenJson.Sort` 初版缺组补空组，会让"七组必须存在"永远通过 → 改为缺组保持 `null`，由校验器报错（红测 `校验_缺组报错` 因此转绿）。
4. **`UiStyleTool.TryMatch` 改签名后残留旧变量**：`value = tokenValue` 未随 `foreach` 改 `pair.Value` → 编译报错后修正。
5. **写盘偶发 EIO**：`write` 工具替换 `UiThemeTokenJson.cs` 时一次 `ReplaceFileW EIO`（文件被 Unity 占用）→ 原样重试成功，无内容丢失。
6. **全量门禁首跑 FAIL（1/33）**：`toolchain-tests` 的 `test_no_duplicate_root_derivation_in_product_scripts` 指向 `scripts/gate/unity-tests.py`（P2-2 的显式 `--project-path` 引入重复根推导）→ 按共享 helper 修根因，`toolchain-tests` 232/232、全量 33/33 转绿。

## 验证结果

- 2026-10-24T14:16 · 全量 `wf verify` PASS 33/33（本批新增三条 `commands` 之前）
- 2026-10-24T14:33/14:50/15:08/15:33/16:01 · 多次全量重跑均因**并行批次共享同一 Editor/工作区**失败：`weaknet-injection`/`build-preflight` 同时在跑 Unity 测试与 dotnet 构建，表现为「filter not reflected（陈旧结果）」「editor not ready（play mode 未退出）」与 L1 的 `CS2012 文件被另一进程占用`。
- 本批自有证据（均在本机单独复跑通过）：`UiThemeTokens` 17/17 · `UiThemeAssetGenerator` 8/8 · `UiStyleRetirement` 8/8 · `UiStyleRule` 4/4 · `UiTextStyle` 8/8 · `UiValidation` 14/14（302/161/141 未漂）· `EditorToolCatalog` 12/12 · `UiToolchainWiring` 8/8；`ui.style.tokens` eval 复跑 `变更=否`。
- 结论：最终在并行批次让出 Editor 的窗口内补跑全量门禁 **PASS 40/40**（2026-10-24T16:52，含本批三套 + 并行线四条新命令）；前述失败均为共享 Editor/工作区竞争（陈旧结果、play mode 未退出、DLL 占用、临时 `.meta` 写锁），不视为本批回归。

- 2026-10-24T13:34:56+08:00 · 先行测试 PASS · 3/3 · 证据 evidence/red.json
- 2026-10-24T13:54:41+08:00 · 验证 FAIL · 32/33 · 证据 evidence/verify.json
- 2026-10-24T14:05:58+08:00 · 验证 PASS · 33/33 · 证据 evidence/verify.json
- 2026-10-24T14:16:00+08:00 · 验证 PASS · 33/33 · 证据 evidence/verify.json
- 2026-10-24T14:33:06+08:00 · 验证 FAIL · 32/33 · 证据 evidence/verify.json
- 2026-10-24T14:54:24+08:00 · 验证 FAIL · 29/36 · 证据 evidence/verify.json
- 2026-10-24T15:07:54+08:00 · 验证 FAIL · 29/40 · 证据 evidence/verify.json
- 2026-10-24T15:18:55+08:00 · 验证 FAIL · 14/16 · 证据 evidence/verify.json
- 2026-10-24T15:36:55+08:00 · 验证 FAIL · 29/40 · 证据 evidence/verify.json
- 2026-10-24T15:50:26+08:00 · 验证 FAIL · 15/16 · 证据 evidence/verify.json
- 2026-10-24T16:03:02+08:00 · 验证 FAIL · 14/16 · 证据 evidence/verify.json
- 2026-10-24T16:16:29+08:00 · 验证 PASS · 16/16 · 证据 evidence/verify.json
- 2026-10-24T16:31:19+08:00 · 验证 FAIL · 39/40 · 证据 evidence/verify.json
- 2026-10-24T16:48:15+08:00 · 验证 PASS · 40/40 · 证据 evidence/verify.json

## 交付摘要

- **交付**：UI 令牌值从 C# 常量搬到 `UiStyleTokens.json`（12 色 + 5 文字角色），由 `ui.style.tokens` 确定性生成 `Assets/GameData/Config/UI/UiTheme.asset`（`sourceHash=78f288ad…`，二次生成零变更/字节不变）；`UiStyle`/`UiTextStyle` 退役为 catalog 派生只读读口（C# 零颜色/字号字面量），工具走 `…With(catalog, …)` 注入；新增 `ui.style.token-drift`（JSON↔SO 不一致/缺文件/非法均 Error，提示重生成）。
- **验收**：AC1–AC6 全部达成——令牌源唯一且幂等、生成物可读回 12/5/0×5、漂移可检出、真实面 Warning 302（161+141）未漂、`Assets/ShareResource/` 零变更、工具在册且 `wf verify` **PASS 40/40**。
- **评审**：方案 4 轮独立评审（末轮 PASS）、代码 2 轮独立评审（首轮 FAIL → 修 1 blocker + 2 major + 2 测试/通用性 → 复评 PASS）；全部证据在 `.workflow/active/ui-p2-token-base/`。

## 未决事项

- B2 才消费 `UiThemeAsset`（引用组件/解析器）；B1 期间 SO 随收集组进包但无运行期读者。
- 五组空集（spacing/radius/elevation/motion/icon）待 B2 首个应用器补值；本批不预造。
- 存量颜色 161 处与未标记文本 141 处未动（B4 统一收敛）；本批只保证基线不漂。
- `toolchain.py run` 对 editor action 只给 "use open"，生成动作暂只能从 Hub 或 eval 触发（与既有 ui.* action 同型，非本批引入）。

## 文件清单

**新增**

- `Assets/LiteGame/Editor/UiTools/UiStyleTokens.json`（令牌源）
- `Assets/GameData/Config/UI/UiTheme.asset`（生成物，含 .meta）
- `Assets/LiteClient/UI/Style/UiThemeAsset.cs`（运行期纯数据 SO）
- `Assets/LiteGame/Editor/Style/UiThemeTokens.cs` / `UiThemeTokenValidator.cs` / `UiThemeTokenJson.cs` / `UiThemeColorHex.cs` / `UiThemeSnapshot.cs` / `UiThemeAssetGenerator.cs` / `UiThemePaths.cs` / `UiTokenCatalog.cs`
- `Assets/LiteGame/Editor/UiTools/Validation/Rules/StyleTokenDriftRule.cs`
- `Assets/Tests/EditMode/UiThemeTokensEditModeTests.cs` / `UiThemeAssetGeneratorEditModeTests.cs` / `UiStyleRetirementEditModeTests.cs`

**修改**

- `Assets/LiteGame/Editor/Style/UiStyle.cs`（值表→只读属性）
- `Assets/LiteGame/Editor/Style/UiStyleTool.cs`（catalog 注入 + With 重载）
- `Assets/LiteGame/Editor/Style/UiTextStyle.cs`（值表→catalog 物化）
- `Assets/LiteGame/Editor/Style/UiTextStyleTool.cs`（catalog 注入 + With 重载）
- `Assets/LiteGame/Editor/Toolchain/EditorToolHandlers.cs`（+ `ui.style.tokens`）
- `Assets/LiteGame/Editor/UiTools/Validation/IUiRule.cs`（上下文 +2 字段）
- `Assets/LiteGame/Editor/UiTools/Validation/UiRuleIds.cs`（+ `ui.style.token-drift`）
- `Assets/LiteGame/Editor/UiTools/Validation/UiValidator.cs`（规则注册 + 样式族 Note）
- `scripts/gate/toolchain.json`（+ 工具条目）
- `scripts/gate/unity-tests.py`（批外修复：复用 `_common.repo_root()`）
- `Docs/工具链/工具索引.md`（重生成）
- `Docs/设计文档/客户端/UI系统/UI编辑器工具链专项设计.md`（§8.1 现行口径 + §8.3 生成物说明）
- `Docs/施工进度/UI系统线.md`（第 10 章 + 索引）· `Docs/施工进度/README.md`（卷索引）
- `.workflow/config.json`（补 review-plan/confirm-acceptance 两阶段 + 本批 red_commands）

**未改**：任何 prefab/场景/字体/图集（`Assets/ShareResource/` 零变更）；BuildHash 源集/表/协议未触碰。

## 后续建议

- **B2（引用与应用）**：先落 `UiStyleRef` 与 `UiStyleResolver`（消费 `UiTokenCatalog`），再做样式透视与应用器（幂等/Undo/dry-run），最后补 spacing/radius 组的首个消费者。
- **B3（预览矩阵/设备档）**：设备档表 + 语言/长文本/安全区切换 + 截图矩阵，工作台第 6 透视。
- **B4（存量收敛）**：跑 `ui.style.colors-refresh` 一次性收敛 161 处颜色（会有 ≤0.5/255 的不可见位移，需留 diff/截图），补文本标记降 141 条 Warning，然后把 `UiValidationEditModeTests` 的 302 钉下调。
- 迁移批顺手把 `Assets/LiteGame/Editor/Style/` 归并到 `UiTools/Style/`（本批未搬，避免与并行线冲突）。

## 阶段流转

<!-- 阶段推进与回退记录由 workflow.py 自动追加。 -->
- 2026-10-24T12:10:40+08:00 · 初始化运行，当前阶段「提出方案」
- 2026-10-24T12:13:24+08:00 · 完成「提出方案」→ 「子代理评审方案」
- 2026-10-24T13:04:38+08:00 · 完成「子代理评审方案」→ 「用户确认验收标准」
- 2026-10-24T13:06:17+08:00 · 完成「用户确认验收标准」→ 「实施任务清单」
- 2026-10-24T13:07:52+08:00 · 完成「实施任务清单」→ 「评审任务」
- 2026-10-24T13:07:57+08:00 · 完成「评审任务」→ 「先写测试」
- 2026-10-24T13:35:32+08:00 · 完成「先写测试」→ 「正式施工」
- 2026-10-24T14:16:08+08:00 · 完成「正式施工」→ 「测试代码」
- 2026-10-24T14:16:08+08:00 · 完成「测试代码」→ 「评审代码」
- 2026-10-24T16:48:16+08:00 · 完成「评审代码」→ 「回写与收尾」
- 2026-10-24T16:48:53+08:00 · 完成「回写与收尾」→ 完成
