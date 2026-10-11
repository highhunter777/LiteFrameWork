# UI 工具链

《UI编辑器工具专项设计》落地的施工记录。设计文档只描述最新设计，本文件只保留最新进度。

## 执行顺序（依专项设计 §7 排期 + §5 优先级）

| 步 | 工具 | 状态 | 设计依据 |
|---|---|---|---|
| 1 | **校验核心** | **已交付（2026-10-11）** | §4 |
| 2 | 绑定标记工具（BindNode 树窗口 + 前缀预设 + 批量重命名 + 改名引用点提示） | 待做 | §2 |
| 3 | 样式工具补齐（`UiTextStyleRef` 文字档 + 对账扩 `Screens/`） | 待做 | §3 |
| 4 | 校验进 CI（生成物 hash 对账 + 门禁接入） | 待做 | §4 + 修改指南 §2.3 |
| 5 | 编排面板（控件库/绑定树/受控属性/校验生成） | 待做 | §5 |
| 6 | UIPatch 调试补丁 | 待做（随 U4 消费者） | §6 |

## 步 1：校验核心（已交付）

**落点**：`Assets/LiteGame/Editor/UiTools/Validation/`

| 件 | 职责 |
|---|---|
| `UiRuleReport.cs` | 报告模型：`UiIssue{ruleId,severity,asset,node,message,fixHint}` + `UiSeverity`（Info/Warning/Error）+ `UiRuleFamily`（含"是否已接入"）+ `UiReport`（收集/汇总/文本渲染，含"待接入族"栏） |
| `IUiRule.cs` | 规则端口：`RuleId`/`Family`/`Check(context)`；`UiValidationContext` **双侧**输入面（`SourceDirs` 源码面 + `AssetPaths` 资产面 + `ProjectRoot`） |
| `UiValidator.cs` | 校验核心：规则注册表 + `Run(context)→UiReport`；六族表 `Families`（未接入族显式登记）；菜单 `LiteGame/UI/校验/校验 UI 资产`；CLI `RunCli`（Error 非零退出）；`BuildDefaultContext`（源码扫描面 + Screens/Widgets 资产） |
| `Rules/RuntimeVisualRule.cs` | **首条规则**（渲染/适配族，`ui.runtime-visual`）：包既有 `VisualConstructionScanner`（源码面），产出统一报告项 |

**关键约束（§4 落地）**：
- 同一核心三入口共用（菜单/CLI/EditMode 测试）——不各写一套；
- 未接入的五个规则族（绑定/资源/文本/行为/性能）在报告里**显式列为"待接入"，不显示为自动通过**；
- 首条规则**只包装** `VisualConstructionScanner`，不改其判定（该扫描器仍由 `VisualSingleSourceEditModeTests` 直接消费）。

**为何首条选它**：视觉单一来源是既有唯一规则（零风险包装即可验证端口形状）；校验核心是其余工具（绑定标记/样式/编排面板/CI）的公共底座——故第一步做核心 + 首条规则。

## 验证证据

| 项 | 结果 |
|---|---|
| Unity recompile | 0 错误 |
| `UiValidationEditModeTests` | **6/6**（正例×2 / 负例×1 / 报告契约×2 / 端口×1） |
| 三入口共用 | 菜单 `LiteGame/UI/校验/校验 UI 资产` 跑通；`RunCli` 静态入口就位（Error→退出码 1）；测试直调核心 |
| L1 | **1594/1594** |
| 报告形状 | 窄上下文实跑：`PASSED=True errors=0`；"待接入规则族"列出 5 族（绑定/资源/文本/行为/性能），"渲染/适配"已接入 |

## 遗留（如实）

- **全量扫描慢**（~60s，扫 4 个源码目录——与 `VisualSingleSource` 同因）；经 unity-pipeline `run_script` 调用会撞其 30s 管道上限，但**菜单/CLI/EditMode 三入口无此限**，属工具调用方式而非校验器问题。后续若需 CI 高频跑，评估增量扫描或缓存；
- 边界：本步**只落地 1 条规则**，其余五族为骨架占位（`Families` 登记）；绑定/资源等资产面规则留后续步。
