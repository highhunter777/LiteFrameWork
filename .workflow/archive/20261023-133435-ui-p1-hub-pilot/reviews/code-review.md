# 代码评审：UI 系统 P1-5/P1-6

## 变更范围

| 文件 | 变更 |
|---|---|
| `Assets/LiteGame/Editor/UiTools/Contract/UiContractBatch.cs`（新增） | 批量生成/对账编排：表只读一次、按 prefab 去重、根名分类、多行提示、确定性写盘 |
| `Assets/LiteGame/Editor/UiTools/Contract/UiContractGenerator.cs` | `IsContractPageName` 单源；`GenerateRegistered` 按行重载（多行取首行 + 告警） |
| `Assets/LiteGame/Editor/Toolchain/EditorToolHandlers.cs` | 四个动作注册 + 薄映射（该文件同时含并行线的既有改动，非本批） |
| `Assets/LiteGame/Editor/UiTools/Page/UiPageScaffold.cs` | 起页后清单对账（`ReconcilePageManifest`，失败只记日志） |
| `Assets/ShareResource/UI/Screens/Page_Login.prefab` | 三个静态节点加 `LTextKeyRef`（+42/−0，无节点/引用变化） |
| `Assets/LiteGame/Lua/UI/Page_Login.lua` | 删三处静态 `SetTextKey`，注释改为双通道口径 |
| `scripts/gate/toolchain.json` / `Docs/工具链/工具索引.md` | 四条工具登记 + 生成索引 |
| `Assets/LiteGame/Editor/UiTools/Contracts/*.json`、`UiWidgetManifest.json`、`UiPageManifest.json` | 生成物基线入库 |
| `Assets/Tests/EditMode/UiToolchainWiringEditModeTests.cs`（新增） | 8 例：工具面/批量/对账/试点/Lua/起页 |

## 正确性与边界

- **根名分类**：`UIMain` 跳过 + 告警，不计失败；其余失败聚合到 `Errors`，部分成功不伪装全成功。
- **多行登记**：取首行 + 告警（`Tmp_Dialog` 201/206 实测）；单页调用与批量调用同一重载，口径不分叉。
- **无基线**：`ui.contract-diff` 只告警、不判失败；破坏性变更（删除/改名/类型/key 变化）判失败。
- **起页对账失败**：只写结果日志、不回滚页面（页面产物与注册表分离），可重跑 `ui.page-manifest`。
- **试点页**：静态/动态写者分离，`Txt_Version`/`Txt_Status` 保留 Lua 写；运行期 zh→en→zh 实测一致。
- 边界：空行集/空表读取走显式错误；输出目录限 `Assets/`；文件名取 prefab 名，避免路径歧义。

## 测试充分性

- 先红 5/5 为断言失败（非编译/环境），覆盖工具未登记、契约无 text、Lua 双写、基线缺失、`contractPresent=false`。
- 转绿 8/8：新增批量生成（含 UIMain 跳过、多行告警、重复零 diff）、对账（无基线/破坏性）、起页注入路径。
- 回归：catalog 12/12、contract 10/10、diff 5/5、page-manifest 15/15、widget 11/11、static-text 6/6、validation 14/14、validation-rules 8/8。
- 运行期证据：登录页三节点中英切换一致；`Txt_Status` 未被静态绑定覆盖。

## 通用性与扩展性

- **PASS**：批处理只编排，遍历/过滤/哈希复用既有单源（`UiBindSurfaceWalker`、生成器、`UiContractDiff`），没有第二份接口面定义。
- 新页面自动纳入（`tbuiform` + prefab），无按页硬编码；工具注册仍只经 `toolchain.json`，Hub/CLI/文档索引/孤儿检查自动生效。
- 失败语义统一（显式错误/告警/跳过），新增产物类型可复用 `ToolResult` 与 `ToolchainOutputKinds`。

## 安全与性能

- 输出路径强制 `Assets/` 下；无网络/凭据操作；生成物无时间戳、确定性。
- 批量生成 11 页实测 ~3s（表读一次）；重复执行内容零 diff。

## 发现的问题与处理

| ID | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|
| R-1 | 低 | `EditorToolHandlers.cs` 同时承载并行线（P2 工具合并）既有改动，整文件暂存会夹带 | 本批不做提交；提交时按 hunk 显式隔离（已在 build-log 文件清单标注） | 已记录 |
| R-2 | 低 | `UiContractStore.Save` 无条件写盘（内容相同也重写并触发导入） | 评估为非阻断：内容零 diff、不产生 git 噪声、11 文件导入开销可忽略；如需幂等写盘，统一在生成物 store 批处理 | 已记录 |
| R-3 | 低 | 契约 `owner`/`designRef` 未投影 | 明确挂账：页面清单已承载，接线批决定是否投影 | 已记录 |

## 复测记录

- `wf verify`：**PASS 20/20**（L1 1819 · doc 0 violation · restore · l0 · toolchain×3 · doc-lane · l2-meta 14216 · 11 个 EditMode 夹具）。
- Play 运行期：`zh: 联机对战测试 / 游客一键进入 / 设置` → `en: Online Playtest / Enter as Guest / Settings` → 回 zh 一致；版本行随语言、状态行动态保持。
- Unity 编译 0 错（仅既有警告）。

## 评审结论

- 结论: PASS
- 阻塞问题: 0
- 通用性: PASS
- 扩展性: PASS
- 评审人: Codely CLI
- 评审时间: 2026-10-23
