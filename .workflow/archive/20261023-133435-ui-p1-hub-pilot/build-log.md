# 施工记录：UI 系统 P1-5/P1-6

## 施工记录

1. **契约批处理（T1）**：新增 `UiContractBatch`（生成/对账两入口）。`tbuiform` 经 `UiPageScaffold.TryListPages` 只读一次，按 prefab 去重后按路径排序；根名不在 `Page_/Hud_/Tmp_` 的条目（`UIMain`）跳过并告警；同 prefab 多行取首行并告警。`UiContractGenerator` 抽出 `IsContractPageName`（根名判据单源）与「按已读行生成」重载（表不再逐页读）。实测：`Generate` 11 页 / 跳过 1 / 告警 2 / 错误 0；重复生成 11/11 逐字节一致。
2. **工具面（T2）**：`EditorToolHandlers` 注册四个动作（handler 只做调用与 `ToolResult` 映射）；`scripts/gate/toolchain.json` 增 4 条 `action`（plane=editor、writes/outputs/docs/menu 齐），order 120～150；`python scripts/toolchain.py docs` 重生成 `Docs/工具链/工具索引.md`；`check` 0 error（1 条既存「未注册脚本」告警）。
3. **起页接线（T3）**：`UiPageScaffold.Create` 在 `SaveAssets` 后调用 `ReconcilePageManifest()`（internal，可注入输出路径）；失败只写 `result.Log`，不回滚页面。
4. **试点页（T4）**：`Page_Login.prefab` 经 `PrefabUtility.LoadPrefabContents` 给 `Txt_Title`/`Txt_GuestLogin`/`Txt_Settings` 加 `LTextKeyRef` 并设 key；`Page_Login.lua` 删除对应三处 `SetTextKey`，保留 `Txt_Version`（带参）与 `Txt_Status`（状态）。
5. **生成物（T5）**：批量生成 11 份契约入库；`UiWidgetManifest` 30 控件 0 告警；`UiPageManifest` 12 页 / 13 行、`contractPresent` 转真。
6. **测试（T6）**：先红 5/5（断言失败，非编译/环境），实现后新夹具 8/8；补三条实现期用例（批量生成/确定性、批量 diff 无基线与破坏性、起页对账注入路径）。
7. **门禁与文档（T7）**：L1 PASS 1819、Doc 0 violation、L2 meta PASS 14216、restore PASS、toolchain unittest 222 OK；设计 §5.2.1/§8.4/§8.5/§9.1 与进度卷第 5 章回写；审计 L248/L251 登记回写。

## 关键决定

- **批处理与生成器分离**：批量只管编排（读表/去重/分类/写盘），单页语义仍归 `UiContractGenerator`；不复制接口面定义。
- **UIMain 不产契约**：常驻壳根名不在契约面，显式跳过 + 告警，不计失败——否则 `ui.contract` 永远红，P1 出口无法成立。
- **契约按主表单行**：多行差异归页面清单 `forms[]`；批量生成对多行显式告警，避免静默丢失。
- **生成物入库承载基线**：契约/控件清单/页面清单入库后 `ui.contract-diff` 才有跨提交基线（与 `UiBindManifest` 同口径）；手改仍禁。
- **试点页选 `Page_Login`**：唯一同时具备「纯静态文案 + 带参/状态文案」的页面，可验证静态与动态写者分离。
- **起页后做对账而非草稿**：现有 `Create` 已写 `tbuiform` 行，对账得到真实 `forms[]` 与 `Registered`；草稿留给 P2 的「只建 prefab」起页流程。

## 偏差与处理

- **计划中的「先红含批量用例」无法直接编译**（类型尚不存在）：先红只覆盖外部可观察面（清单/产物/契约/文案），批量 API 用例在实现期补写并纳入同一夹具；理由写进 `test-plan.md`。
- **批量 diff 破坏性用例方向修正**：首版从基线删除控件构造的是「新增」（非破坏）；改为向基线追加不存在控件（现页面已移除）→ `Removed/Breaking`，断言转绿。
- **本机 shell 通道不可用**：Unity CLI 命令若在 `exec_editor_script` 内同步执行会阻塞编辑器主线程（`run_tests` 30s 超时）。改为「后台进程 + 日志轮询」执行测试与门禁；这是环境限制，不改变仓库脚本。
- **夹具断言计数**：`EditorToolCatalogEditModeTests` 本机实测 12/12（文档旧记 14/14 为当时用例数），以本次输出为准。

## 问题与修复

| 问题 | 处理 |
|---|---|
| `UiToolchainWiringEditModeTests` 缺 `using UnityEditor`（`AssetDatabase` 找不到） | 补 using，重编译 0 错 |
| 批量 diff「破坏性」用例构造方向错误（删基线＝新增变更） | 改为向基线追加幽灵控件，验证 `Removed + Breaking` |
| 同步运行 `unity` CLI 导致 30s 超时（编辑器主线程被占） | 改后台进程 + 日志轮询，不阻塞编辑器 |
| `UIMain` 在 tbuiform 登记但不合契约根名 | 批处理显式跳过 + 告警；设计 §5.2.1/§8.5 记录口径 |

## 交付摘要

- **四个工具入口**：`ui.contract`（批量生成）、`ui.contract-diff`（对账，破坏性判失败）、`ui.widget-manifest`、`ui.page-manifest`，Hub/CLI 均可打开执行。
- **起页自动对账**：`UiPageScaffold.Create` 成功后页面清单落盘（失败显式记录）。
- **试点页静态文案**：`Page_Login` 三个纯静态节点由 `LTextKeyRef` 声明，Lua 不再双写；运行期中/英切换正确。
- **生成物基线**：11 份页面契约 + 控件清单 + 页面清单入库；`contractPresent` 全真。

## 验证结果

| 命令 | 结果 |
|---|---|
| `unity-tests --filter UiToolchainWiringEditModeTests` | **8/8 通过**（先红阶段 **5/5 失败**，均为断言） |
| `unity-tests --filter EditorToolCatalogEditModeTests` | 12/12 |
| `unity-tests --filter UiContractGeneratorEditModeTests` | 10/10 |
| `unity-tests --filter UiContractDiffEditModeTests` | 5/5 |
| `unity-tests --filter UiPageManifestEditModeTests` | 15/15 |
| `unity-tests --filter UiWidgetManifestEditModeTests` | 11/11 |
| `unity-tests --filter UiStaticTextEditModeTests` | 6/6 |
| `unity-tests --filter UiValidationEditModeTests` | 14/14 |
| `unity-tests --filter UiValidationRulesEditModeTests` | 8/8 |
| `python scripts/toolchain.py check` | 0 error（1 条既存告警） |
| `python scripts/toolchain.py docs --check` | 通过 |
| `python scripts/gate/test.py --lane L1 --profile PullRequest` | PASS 1819 |
| `python scripts/gate/test.py --lane Doc` | PASS（0 violation） |
| `python scripts/gate/l2-unity-gate.py --meta-scan-only` | PASS（14216 meta） |
| `python scripts/gate/restore-check.py` | PASS |
| `python -m unittest discover -s scripts/tests` | 222 tests OK |
| 运行期（Play） | 登录页 zh `联机对战测试 / 游客一键进入 / 设置` → en `Online Playtest / Enter as Guest / Settings` → 回 zh 一致；`Txt_Version` 随语言、`Txt_Status` 保持动态 |
- 2026-10-23T13:33:01+08:00 · 验证 PASS · 20/20 · 证据 evidence/verify.json

## 未决事项

- **契约 `owner`/`designRef` 未投影**：页面清单已承载，契约暂不取值（生成器与批量一致），待接线批决定。
- **五族校验未接入**：样式/文本/布局/性能/行为（P1-4 报告标「待接入」）。
- **多行契约 `formIds[]`**：属次版本追加，本批只告警 + 清单 `forms[]` 承担。
- **P2 工作台在途 WIP**（`UiComposeWindow`/`UiBindingEditor`/`UiPageWizardWindow`/`Preview`/`Templates`）不属本批，未触碰。
- **工作区未提交**：本批未自行 commit；工作区混有并行线改动，提交需按文件显式隔离。

## 文件清单

新增：
- `Assets/LiteGame/Editor/UiTools/Contract/UiContractBatch.cs`（+ meta）
- `Assets/LiteGame/Editor/UiTools/Contracts/*.contract.json`（11 份 + 目录 meta）
- `Assets/LiteGame/Editor/UiTools/UiWidgetManifest.json`（+ meta）
- `Assets/Tests/EditMode/UiToolchainWiringEditModeTests.cs`

修改：
- `Assets/LiteGame/Editor/UiTools/Contract/UiContractGenerator.cs`（根名判据单源 + 按行重载）
- `Assets/LiteGame/Editor/UiTools/Page/UiPageScaffold.cs`（起页后清单对账）
- `Assets/LiteGame/Editor/Toolchain/EditorToolHandlers.cs`（四个动作）
- `Assets/LiteGame/Editor/UiTools/UiPageManifest.json`（`contractPresent` 转真）
- `Assets/ShareResource/UI/Screens/Page_Login.prefab`（三处 `LTextKeyRef`）
- `Assets/LiteGame/Lua/UI/Page_Login.lua`（去三处静态写）
- `scripts/gate/toolchain.json`（四条工具登记）
- `Docs/工具链/工具索引.md`（生成）
- `Docs/设计文档/客户端/UI系统/UI编辑器工具链专项设计.md`（§5.2.1/§8.4/§8.5/§9.1）
- `Docs/施工进度/UI系统线.md`（第 5 章）、`Docs/施工进度/README.md`（索引行）
- `Docs/审计/代码质量审计-客户端.md`（修复登记两条）
- `.workflow/config.json`（新夹具验证/先红命令）

## 后续建议

1. 按 P2 工作台批次把「契约透视 / 页面选择器」接到这批动作上（直接复用 `UiContractBatch`）。
2. 契约投影补 `owner`/`designRef`（页面清单已是单源），再让 `ui.contract-diff` 覆盖元数据变更分级。
3. 五族校验按「令牌表 → 文本表 → 布局基线 → 性能 → PlayMode」顺序接入，规则变更走评审。

## 阶段流转
- 2026-10-23T13:33:33+08:00 · 完成「正式施工」→ 「测试代码」
- 2026-10-23T13:33:39+08:00 · 完成「测试代码」→ 「评审代码」
- 2026-10-23T13:34:11+08:00 · 完成「评审代码」→ 「回写与收尾」
- 2026-10-23T13:34:35+08:00 · 完成「回写与收尾」→ 完成
