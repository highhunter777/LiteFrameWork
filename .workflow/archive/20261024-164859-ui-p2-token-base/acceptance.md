# 验收标准确认：UI 系统 P2-3a 令牌源与主题资产（JSON→UiThemeAsset，退役硬编码）

> 运行 ui-p2-token-base · 创建于 2026-10-24T12:10:40+08:00
>
> 本阶段把方案验收标准逐条提审用户；未经用户确认不得进入任务拆解与施工。

## 验收标准

| 编号 | 验收标准 | 判定方式/验证命令 | 对应任务 |
|---|---|---|---|
| AC1 | 令牌源唯一：`Assets/LiteGame/Editor/UiTools/UiStyleTokens.json` 存在、可被 `ui.style.tokens` 校验通过（12 个 `color.*` + 5 个 `font.*`，五组空集合法）；连续两次生成，第二次 `changed=false` 且 `UiTheme.asset` 逐字节不变 | 连跑 `ui.style.tokens` 两次，比对 SO 哈希；报告留证据 | T1/T2/T3/T6 |
| AC2 | 生成物可读回：从 `UiTheme.asset` 读回主题名/版本/`sourceHash` 与七组计数（12/5/0/0/0/0/0），颜色读回与 JSON 的 8bit 值逐位一致 | `UiThemeAssetGeneratorEditModeTests` + 资产读回断言 | T3/T5 |
| AC3 | 漂移可检出：只改 JSON 不重生成时，全量校验报 `ui.style.token-drift` **Error**（文案指出 SO 过期）；跑 `ui.style.tokens` 后 Error 消失 | `UiStyleRetirementEditModeTests` 红绿两例（注入临时副本）+ 命令链写入施工记录 | T4/T5 |
| AC4 | 基线不漂：改完后 `UiValidationEditModeTests` 仍绿——**Error 0 / Warning 302（颜色 161 + 未标记文本 141）**；`UiStyleRuleEditModeTests`、`UiTextStyleEditModeTests` 全绿；量化若导致翻转 → 停工提审，不改钉 | `unity-tests --filter UiValidationEditModeTests`（`:220/221/222` 钉）+ 两套样式夹具 | T5/T7 |
| AC5 | 零资产改动：`git status --porcelain Assets/` 只出现白名单（令牌 JSON、`UiTheme.asset(.meta)`、`Editor/Style/`、`Editor/UiTools/`、`Editor/Toolchain/`、`Tests/EditMode/`、`LiteClient/UI/Style/`）；`Assets/TempUiThemeTests/` 不存在；`Assets/ShareResource/` 零变更 | `git status --short` + `Test-Path` 复核，证据进 build-log | T7 |
| AC6 | 工具与门禁在册：`toolchain.py list` 含 `ui.style.tokens`，工具索引重生成且 `docs --check` 通过；`wf verify` 全绿、`doc-lint` 0/0 | `python scripts/toolchain.py list` / `docs --check` / `wf verify --run ui-p2-token-base` | T6/T7 |

## 提审记录

- **提审时间**：2026-10-24 13:1x（+08:00）
- **提审方式**：对话中逐条列出 AC 表 + 三个关键取舍（五组空集 / 8bit 量化 / 不改 prefab），结构化提问确认
- **用户原话或选择**：选择「**确认，按 AC1–AC6 施工（推荐）**」
- **附注**：提审时同时给出 AC4 的止损口径（量化翻转 → 停工提审、不在本批改钉），用户未提出修改。

## 用户确认

- 确认结论: 通过
- 确认人: 用户（对话中选择「确认，按 AC1–AC6 施工」）
- 确认时间: 2026-10-24T13:18+08:00
- 变更要求：无

## 变更记录

- 无（方案经 4 轮独立子代理评审：第 2/3 轮 FAIL → 修复 → 第 4 轮 PASS，阻塞问题 0；详见 `reviews/plan-review.md`）
