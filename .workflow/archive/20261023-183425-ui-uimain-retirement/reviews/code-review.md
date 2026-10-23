# 代码评审：UI 系统 UIMain 退役：注册表锚迁移 · 产品注册摘除 · 测试载体归位

> 运行 ui-uimain-retirement · 创建于 2026-10-23T17:29:30+08:00

## 变更范围

- 对照任务：T1～T7（先红守卫/锚迁移/载体归位/表与生成物/真实面校准/文档回写/门禁加固）。
- 本批职责文件：
  - 表与生成：`Luban/Data/#uiform.xlsx`、`scripts/codegen/_uiform_row.py`、`Assets/LiteGame/Lua/Cfg/tbuiform.lua`、`Assets/LiteGame/LuaBridge/Generated/LuaKeys.g.cs`、`Assets/GameData/Config/UI/tbuiform.bytes`、`Assets/LiteNet/Protocol/BuildHash.g.cs`、`Assets/LiteGame/Editor/UiTools/UiPageManifest.json`；
  - 启动/注册：`Assets/LiteGame/LuaBridge/RegistryFiller.cs`、`Assets/LiteGame/Lua/main.lua`、`Assets/LiteGame/Editor/UiTools/Page/UiPageManifestEditor.cs`；
  - 载体：`Assets/ShareResource/UI/Screens/Tmp_UiTestPage.prefab`（原 UIMain，保 GUID）、`Assets/LiteGame/Lua/Tests/UiTestPage.lua`（原 UIMain Lua）；
  - 测试：`UiUimainRetirementEditModeTests`、`UiPageManifestEditModeTests`、`UiToolchainWiringEditModeTests`、`UiValidationEditModeTests`、`SafeAreaEditModeTests`、`UiBindingToolsEditModeTests`、`UiU0EditModeTests`、`ReleaseLayoutEditModeTests`、`ContentRuntimeAdaptersEditModeTests`、`Assets/Tests/UI/PlayMode/{LuaPage,EnvRebuild,Navigation}PlayModeTests`；
  - 门禁/文档：`scripts/gate/unity-tests.py`、`.workflow/config.json`、`Docs/开发导航/代码地图.md`、`Docs/技术总结/{04,11}*.md`、`Docs/项目说明/03-技术细节-客户端.md`、`Docs/设计文档/客户端/UI系统/{UI系统总设计,UI编辑器工具链专项设计}.md`、`UiPageManifest.schema.json`、`Docs/审计/代码质量审计-客户端.md`、`Docs/施工进度/UI系统线.md`。
- 工作区另有并行线 WIP（DebugKernel/GM/云端文档/Boot 场景等），不属本批；本评审只覆盖上述职责文件。

## 正确性与边界

- **表与清单**：`_uiform_row.py --delete-id` 有 dry-run/备份/回滚；删 id=1 后 Luban 重生成 `tbuiform.lua`/表 bytes/`LuaKeys.g.cs`（UI 常量 6 个）；页面清单对账的孤儿策略不会自动删非 Draft 条目，故新增 `UiPageManifestEditor.Remove` 并显式移除 UIMain，再 `SyncToDisk` 得 11 页/12 行、0 告警——没有削弱对账策略。
- **启动锚**：`RegistryFiller.KnownUiKey = LuaKeys.UI.Page_Login` 单源，`ValidateKnownKeys` 只用它；UIMain 不再参与启动校验。`main.lua` 去掉 UIMain require/全局，冒烟日志改中性 `main.lua loaded`。
- **载体**：`rename_asset` 保 GUID；`run_script` 通过 `PrefabUtility.LoadPrefabContents` 改根名、移除 CanvasScaler、把 2 个 BindName 对齐 `Button_Close`/`Txt_Label` 后保存；`move_asset` 移动 Lua 保 meta。Tmp_ 系统面口径（Canvas/CanvasGroup、禁 CanvasScaler）由真实面校验 14/14 验证。
- **测试**：`UiU0EditModeTests` 的原真配置两用例改 `FakeCatalog`（不再依赖 tbuiform 行 1），另加真实 `ConfigService → UIFormCatalog` 投影用例（Page_Login id=11 可解析、id=1 抛 KeyNotFound）；`NavigationPlayModeTests` 也被纳入载体迁移（全仓 grep 发现）。
- **门禁**：异步 `run_tests` 的工具 `--timeout` 放在 `--filter` 后会被 CLI 吞掉 filter（实测跑 841 全集）；已移除并保留 `args.timeout` 只作轮询上限；发射前等 domain reload/编译/Play 结束；陈旧 completed 按 mtime 忽略；testName 结果必须命中 filter。定向 red 19 命令与全量验证均走该通道。

## 测试充分性

- 测试是否真正覆盖验收与失败路径：是。先红阶段退役守卫 6 例中 5 例断言失败（页面清单/LuaKeys/锚/main.lua/载体路径），既有夹具按目标态转红 3 条；实施后守卫 6/6，PageManifest 16/16、Wiring 8/8、Validation 14/14（Warning 302/命名 0）、SafeArea 6/6、BindingTools 12/12、U0 7/7、LuaPage 4/4、EnvRebuild 3/3、Navigation 4/4。
- 是否存在迎合实现的测试：未发现。清单断言真实生成物；载体断言旧路径消失/新路径存在；启动锚用反射常量；U0 的 `Remove` 用例验证指纹重算与重复移除失败。
- 薄弱面：`RegistryFiller.FillAll` 的真实启动填充仍无 EditMode 端到端（缺 ConfigService 装配），用常量/源码守卫 + 代码评审兜；测试载体 `Tmp_UiTestPage` 仍在产品收集目录（显式范围）；全量 verify 首轮被并行线 DebugKernel 的残留 request 卡住，单跑 DebugKernel 17/17 通过后复跑全量（见复测记录）。

## 通用性与扩展性

- `KnownUiKey` 是启动锚单源，未来换首屏只改一处；`Remove` 是通用页面退役 API；`--delete-id` 是通用表行删除（带备份/回滚），不写死 UIMain。
- 载体命名遵循 `Tmp_`/`Tests.` 约定；若后续要包内零测试资产，可整体迁测试收集组，不影响现有测试断言。
- 未引入 new public static 可变状态；未新增第二份 UIMain 判断；`UiContractBatch` 的非常驻壳跳过逻辑保留，`IsContractPageName` 守卫仍拒 UIMain。

## 安全与性能

- 安全：xlsx 删除有备份/回滚；资产改名/移动走 unity-pipeline 保 GUID；无密钥/隐私内容；门禁改动只作用于测试通道。
- 性能：测试载体是小型 prefab/Lua；Luban/BuildHash 只在开发期跑；运行时无新增路径。

## 发现的问题与处理

| ID | 严重度 | 位置 | 问题 | 处理 | 状态 |
|---|---|---|---|---|---|
| P-1 | 低 | `Assets/ShareResource/UI/Screens/Tmp_UiTestPage.prefab` | 测试载体仍在 `UIScreens` 收集目录，产品包保留一个小测试 prefab/Lua | 显式范围（PlayMode 真资源链路需要）；测试收集组另批（方案候选 C） | 记录 |
| P-2 | 低 | `UiPageManifestEditor.Remove` | API 可移除任何已登记页（含 contractPresent）；属显式退役动作，无引用守卫 | 调用方显式确认；若后续误用再加“有契约/有 form 行需二次确认”守卫 | 记录 |
| P-3 | 低 | `_uiform_row.py --delete-id` | 可删除任意 id（含保留系统面），仅 confirm+备份保护 | 与新增行同级的显式写操作；备份可回滚 | 记录 |
| P-4 | 环境 | 全量 verify 首轮 | 并行线 DebugKernel 夹具残留 request/Play 状态导致 600s 卡住；单跑 17/17 通过 | 取消/清理/恢复窗口后复跑；不归因于本批代码 | 已处理 |
| P-5 | 低 | 文档/合成夹具 | `signed-candidate*.json` 仍以 `lua/ui/UIMain.lua` 为固定签名样例；历史归档文档保留 UIMain | 签名绑定不改；live 文档只保留“已退役”说明 | 记录 |

无阻塞问题。

## 复测记录

- 全量 `wf verify`：**PASS 30/30**（2026-10-23T18:33:46；L1 **1819** + 全部 UI EditMode + PlayMode 3/3 + `ui-uimain-retirement-editmode` 6/6 + doc/l0/toolchain）；证据 `evidence/verify.json`。
- 先红：`wf verify --red` PASS（19 命令；退役守卫 5/6 失败，既有 14 条通过）。
- 定向：PageManifest 16/16 · Wiring 8/8 · Validation 14/14 · ValidationRules 10/10 · SafeArea 6/6 · BindingTools 12/12 · U0 7/7 · LuaPage 4/4 · EnvRebuild 3/3 · Navigation 4/4。
- DebugKernel 单独复跑：17/17（排除全量首轮的外部残留）。

## 评审结论

- 结论: PASS
- 阻塞问题: 0
- 通用性: PASS
- 扩展性: PASS
- 评审人: Qoder 代理复核（用户交接）
- 评审时间: 2026-10-23
