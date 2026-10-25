# 实施任务清单：UI 系统 P2-3a：令牌源与主题资产（JSON→UiThemeAsset，退役硬编码）

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 7/7（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | 令牌源 JSON 定稿（schema 2.0：12 色 + 5 文字角色 + 五组空集） | - | 完成 | UiStyleTokens.json 过校验器：12 个 color.*、5 个 font.*，五组空集合法，ID 前缀/唯一性/颜色格式/字号范围全部合规（AC1 数据面） | unity-tests --filter UiThemeTokensEditModeTests | UiStyleTokens.json：12 color.* + 5 font.* + 五组空集；UiThemeTokensEditModeTests 13/13 |
| T2 | 令牌模型、校验器与规范序列化（sourceHash） | T1 | 完成 | UiThemeTokens/校验器/UiThemeTokenJson 落地；规范文本两次一致、sourceHash 稳定；非法输入逐条报位置（AC1/AC2 前置） | unity-tests --filter UiThemeTokensEditModeTests | UiThemeTokens/校验器/UiThemeTokenJson 实现；UiThemeTokensEditModeTests 13/13（校验负例/未知字段/排序/指纹） |
| T3 | UiThemeAsset SO 与确定性生成器（含首份生成物） | T2 | 完成 | UiThemeAsset + UiThemeAssetGenerator：二次生成 changed=false 且字节不变、负例零写盘、路径守卫；生成 Assets/GameData/Config/UI/UiTheme.asset 并可读回 12/5/0×5（AC1/AC2） | unity-tests --filter UiThemeAssetGeneratorEditModeTests | UiThemeAsset + 生成器；UiThemeAssetGeneratorEditModeTests 7/7；eval 二次生成 变更=否；资产 12/5/0×5 |
| T4 | 漂移规则 ui.style.token-drift 与上下文注入缝 | T3 | 完成 | 规则在 JSON/SO 不一致时 Error（SO 过期/手改）、一致时无问题；UiValidationContext 增 ThemeJsonPath/ThemeAssetPath 且测试可注入（AC3） | unity-tests --filter UiStyleRetirementEditModeTests | StyleTokenDriftRule + UiValidationContext 注入缝；retirement 规则两例绿（一致 0 条 / 改 JSON 1 条 Error / 重生成 0 条） |
| T5 | 读面与值表退役（UiTokenCatalog / UiStyle / UiTextStyle / 工具 With 重载） | T3 | 完成 | UiStyle/UiTextStyle 不再持有颜色或字号字面量（只读属性由 catalog 物化）；既有调用点源兼容；工具内部只装载一次并下传（AC2/AC4） | unity-tests --filter UiStyleRetirementEditModeTests; UiStyleRuleEditModeTests; UiTextStyleEditModeTests | UiStyle/UiTextStyle 退役为 catalog 读口（无字面量）；retirement 7/7；style-rule 4/4；text-style 8/8；validation 14/14（302 不变） |
| T6 | ui.style.tokens 工具登记与工具索引重生成 | T3 | 完成 | Hub/CLI 在册；toolchain.py check 0 error；docs 重生成后 --check 通过；list 可见 ui.style.tokens（AC6） | python scripts/toolchain.py check; python scripts/toolchain.py docs --check; python scripts/toolchain.py list | ui.style.tokens 登记；toolchain check OK（1 既存告警）；docs 重生成 + --check 通过；list 可见；Catalog 12/12 + Wiring 8/8 |
| T7 | 文档回写与收尾门禁（含 AC5 白名单证据） | T4, T5, T6 | 完成 | 工具链 §8.1/§8.3、Families 样式族 Note、施工进度第 10 章回写；wf verify 全绿；AC5 白名单与 ShareResource 零变更证据入 build-log（AC4/AC5/AC6） | python scripts/gate/test.py --lane Doc; wf verify --run ui-p2-token-base; git status --porcelain Assets/ | 工具链 §8.1/§8.3、Families Note、施工进度第 10 章 + 卷索引、build-log 回写；AC5：git status Assets/ 仅白名单、ShareResource 0 变更、临时目录已清；wf veri… |

## 任务详情

### T1 · 令牌源 JSON 定稿（schema 2.0：12 色 + 5 文字角色 + 五组空集）

- 文件: Assets/LiteGame/Editor/UiTools/UiStyleTokens.json

### T2 · 令牌模型、校验器与规范序列化（sourceHash）

- 文件: Assets/LiteGame/Editor/Style/UiThemeTokens.cs, Assets/LiteGame/Editor/Style/UiThemeTokenValidator.cs, Assets/LiteGame/Editor/Style/UiThemeTokenJson.cs

### T3 · UiThemeAsset SO 与确定性生成器（含首份生成物）

- 文件: Assets/LiteClient/UI/Style/UiThemeAsset.cs, Assets/LiteGame/Editor/Style/UiThemeAssetGenerator.cs, Assets/LiteGame/Editor/Style/UiThemePaths.cs, Assets/GameData/Config/UI/UiTheme.asset

### T4 · 漂移规则 ui.style.token-drift 与上下文注入缝

- 文件: Assets/LiteGame/Editor/UiTools/Validation/Rules/StyleTokenDriftRule.cs, Assets/LiteGame/Editor/UiTools/Validation/IUiRule.cs, Assets/LiteGame/Editor/UiTools/Validation/UiRuleIds.cs, Assets/LiteGame/Editor/UiTools/Validation/UiValidator.cs

### T5 · 读面与值表退役（UiTokenCatalog / UiStyle / UiTextStyle / 工具 With 重载）

- 文件: Assets/LiteGame/Editor/Style/UiTokenCatalog.cs, Assets/LiteGame/Editor/Style/UiStyle.cs, Assets/LiteGame/Editor/Style/UiStyleTool.cs, Assets/LiteGame/Editor/Style/UiTextStyle.cs, Assets/LiteGame/Editor/Style/UiTextStyleTool.cs, Assets/Tests/EditMode/UiStyleRuleEditModeTests.cs, Assets/Tests/EditMode/UiTextStyleEditModeTests.cs

### T6 · ui.style.tokens 工具登记与工具索引重生成

- 文件: Assets/LiteGame/Editor/Toolchain/EditorToolHandlers.cs, scripts/gate/toolchain.json, Docs/工具链/工具索引.md

### T7 · 文档回写与收尾门禁（含 AC5 白名单证据）

- 文件: Docs/设计文档/客户端/UI系统/UI编辑器工具链专项设计.md, Docs/施工进度/UI系统线.md

