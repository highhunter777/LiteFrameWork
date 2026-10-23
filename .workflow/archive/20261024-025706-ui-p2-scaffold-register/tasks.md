# 实施任务清单：UI 系统 P2-2：起页/登记拆分（ui.page-scaffold / ui.page-register）

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 11/11（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | 先红：夹具骨架与可编译桩 | - | 完成 | 新增 EditMode 夹具与 Python 单测可编译；正例与“负例断言具体失败语义（FailedAt/错误文本/磁盘字节）”运行后均断言失败（非编译/环境错误）；wf verify --red 记录失败清单 | python scripts/gate/unity-tests.py --mode editor --filter UiPageScaffoldRegisterEditModeTests --timeout 600 | UiPageScaffoldRegisterEditModeTests 25 例编译通过、24 失败 1 通过（断言失败，非编译/环境错误）；Python test_uiform_row 6 例 4 失败 1 错误（--table 未实现… |
| T2 | 表门面与路径注入（UiPageTableHelper/ToolProcess） | T1 | 完成 | UiPageTableRow/Helper/ToolProcess 单源落地，PageRow 全调用点迁移后编译通过；--table 覆盖 list/check/suggest/confirm/rollback/delete，备份目录随表隔离；layer 0–3 与 Page_ PascalCase 双侧一致；Python 单测全绿 | python -m unittest scripts.tests.test_uiform_row -v | UiPageTableRow/Helper/ToolProcess 落地；PageRow 全调用点迁移（Sync/ContractGenerator/Batch/3 夹具）；_uiform_row.py --table/备份目录随表/la… |
| T3 | 清单原子写与缓存策略维护字段 | T2 | 完成 | UiPageManifestStore.Save 临时文件+原子替换且失败清理；SetCacheStrategy 按 UICacheStrategy 校验；Sync 保留 cacheStrategy 并刷新草稿 contractPresent；相关夹具绿 | python scripts/gate/unity-tests.py --mode editor --filter UiPageManifestEditModeTests --timeout 600 | UiPageManifestStore.Save 临时件+File.Replace（保留 folderKnown/Refresh）；SetCacheStrategy + UiPageContract.IsValidCacheStrateg… |
| T4 | 草稿契约链（formId=0 · 批量 · 漂移 · diff 口径） | T3 | 完成 | schema formId 允许 0；UiPageMetadata.IsDraft 与生成器草稿口径；UiContractBatch.Generate/Diff 纳入草稿并支持 manifest 注入；Regenerate(allowDraft)；ContractDriftRule 处理草稿；既有 11 份契约字节不变；diff 页面段口径（0→真实/缓存/层级非破坏）夹具绿 | python scripts/gate/unity-tests.py --mode editor --filter UiContractDiffEditModeTests --timeout 600 | 新夹具 UiPageScaffoldRegisterEditModeTests 28/28 通过（草稿契约/起页/登记/工作台/端口注入失败）；UI 回归 20 套件全绿；toolchain check 0 error（1 既存 warn… |
| T5 | 起页内核（基线·根契约·安全区归属·原子清理） | T4 | 完成 | Baselines 表与 IsValidPageName；EnsureRootContract 补四件套且安全区优先挂现有 Cot_* 拉伸容器/无容器则搬入新建 Cot_SafeArea；Scaffold 失败删除新 prefab 并恢复清单；Create 删除、调用点改指；起页夹具绿 | python scripts/gate/unity-tests.py --mode editor --filter UiPageScaffoldRegisterEditModeTests --timeout 600 | 新夹具 UiPageScaffoldRegisterEditModeTests 28/28 通过（草稿契约/起页/登记/工作台/端口注入失败）；UI 回归 20 套件全绿；toolchain check 0 error（1 既存 warn… |
| T6 | 登记内核（契约门槛·表核验·代码生成链·状态/缓存/契约刷新） | T5 | 完成 | 预检含草稿契约（formId=0）/Draft/layer 0–3/缓存策略；注入守卫；Lua 骨架；表写失败 TryFindRow→回滚→复核；Luban+BuildHash 失败回滚并重跑；清单 Registered+缓存；契约重生成真实 formId；无 BindRoot 不记 hash；登记夹具绿 | python scripts/gate/unity-tests.py --mode editor --filter UiPageScaffoldRegisterEditModeTests --timeout 600 | 新夹具 UiPageScaffoldRegisterEditModeTests 28/28 通过（草稿契约/起页/登记/工作台/端口注入失败）；UI 回归 20 套件全绿；toolchain check 0 error（1 既存 warn… |
| T7 | 窗口与工作台接线（事件·门控·冲突保护·焦点重载） | T6 | 完成 | 两个 IMGUI 窗口 Open/Open(prefill) 可用；工作台起页/登记事件与窗口打开；availability 全实现、能力标签补 page.scaffold/register；输入即标脏、保存前磁盘指纹冲突拒绝、ReloadIfChanged 保留选中与目标；工作台夹具绿 | python scripts/gate/unity-tests.py --mode editor --filter UiWorkbenchEditModeTests --timeout 600 | 新夹具 UiPageScaffoldRegisterEditModeTests 28/28 通过（草稿契约/起页/登记/工作台/端口注入失败）；UI 回归 20 套件全绿；toolchain check 0 error（1 既存 warn… |
| T8 | 工具面与索引（handlers + toolchain.json + 工具索引） | T7 | 完成 | ui.page-scaffold/ui.page-register 登记为 window 且 handler 在册；Catalog 无 Error；toolchain.py check 0 error；工具索引重生成且 docs --check 通过 | python scripts/toolchain.py check | 新夹具 UiPageScaffoldRegisterEditModeTests 28/28 通过（草稿契约/起页/登记/工作台/端口注入失败）；UI 回归 20 套件全绿；toolchain check 0 error（1 既存 warn… |
| T9 | 文档回写（08/修改指南/技术总结/设计/schema/进度） | T8 | 完成 | 08 手册入口与起页/草稿契约/登记/排障；修改指南工具表；技术总结 04；设计 §3.1/§3.5/§3.6/§5.2/§9.1/§9.2；schema；施工进度第 9 章；工具索引；doc-lint 0/0 | python scripts/gate/doc-lint.py | 文档回写：08/修改指南/技术总结/设计 §3.1/§3.5/§3.6/§5.2/§9/schema/施工进度第9章/README/工具索引；doc-lint 0/0、Doc lane PASS。全量验证：wf verify PASS（L… |
| T10 | 全量验证与冒烟（UI 回归·L1·Doc·toolchain·wf verify·eval_file） | T9 | 完成 | UI EditMode 全量回归与新增夹具绿；L1/Doc/toolchain 绿；wf verify 全绿；eval_file 打开两窗口 Console 0 error；真实表/清单/hash/契约零残留；BuildHash --check-shared 通过 | python scripts/gate/test.py --lane L1 --profile PullRequest | 文档回写：08/修改指南/技术总结/设计 §3.1/§3.5/§3.6/§5.2/§9/schema/施工进度第9章/README/工具索引；doc-lint 0/0、Doc lane PASS。全量验证：wf verify PASS（L… |
| T11 | 独立代码评审与修复复评 | T10 | 完成 | 独立子代理评审无阻塞（结论/通用性/扩展性 PASS）；阻塞修复后重新验证并复评；wf verify 重跑 | python .agents/skills/staged-delivery/scripts/workflow.py verify --run ui-p2-scaffold-register | 独立评审 4 轮：FAIL(4 阻塞 B1-B4) → FAIL(I2 修复引入 R1) → FAIL(仅文档计数) → PASS；新夹具 31/31、Python 10/10、wf verify PASS；证据 reviews/code… |

## 任务详情

### T1 · 先红：夹具骨架与可编译桩

- 文件: Assets/Tests/EditMode/UiPageScaffoldRegisterEditModeTests.cs, scripts/tests/test_uiform_row.py, Assets/LiteGame/Editor/UiTools/Page/UiPageScaffold.cs, Assets/LiteGame/Editor/UiTools/Page/UiPageRegistration.cs, Assets/LiteGame/Editor/UiTools/Page/UiPageTableHelper.cs, .workflow/config.json

### T2 · 表门面与路径注入（UiPageTableHelper/ToolProcess）

- 文件: Assets/LiteGame/Editor/UiTools/Page/UiPageTableHelper.cs, Assets/LiteGame/Editor/UiTools/Page/UiPageTableRow.cs, Assets/LiteGame/Editor/UiTools/Page/UiPageToolProcess.cs, scripts/codegen/_uiform_row.py, scripts/tests/test_uiform_row.py, Assets/LiteGame/Editor/UiTools/Page/UiPageManifestSync.cs, Assets/LiteGame/Editor/UiTools/Contract/UiContractGenerator.cs, Assets/LiteGame/Editor/UiTools/Contract/UiContractBatch.cs, Assets/Tests/EditMode/UiPageManifestEditModeTests.cs, Assets/Tests/EditMode/UiContractReconciliationEditModeTests.cs

### T3 · 清单原子写与缓存策略维护字段

- 文件: Assets/LiteGame/Editor/UiTools/Page/UiPageManifestJson.cs, Assets/LiteGame/Editor/UiTools/Page/UiPageManifestEditor.cs, Assets/LiteGame/Editor/UiTools/Page/UiPageManifestSync.cs

### T4 · 草稿契约链（formId=0 · 批量 · 漂移 · diff 口径）

- 文件: Docs/设计文档/客户端/UI系统/UiPageContract.schema.json, Assets/LiteGame/Editor/UiTools/Contract/UiPageContract.cs, Assets/LiteGame/Editor/UiTools/Contract/UiContractGenerator.cs, Assets/LiteGame/Editor/UiTools/Contract/UiContractBatch.cs, Assets/LiteGame/Editor/UiTools/Contract/UiContractDiff.cs, Assets/LiteGame/Editor/UiTools/Validation/Rules/ContractDriftRule.cs, Assets/Tests/EditMode/UiContractDiffEditModeTests.cs

### T5 · 起页内核（基线·根契约·安全区归属·原子清理）

- 文件: Assets/LiteGame/Editor/UiTools/Page/UiPageScaffold.cs

### T6 · 登记内核（契约门槛·表核验·代码生成链·状态/缓存/契约刷新）

- 文件: Assets/LiteGame/Editor/UiTools/Page/UiPageRegistration.cs, Assets/LiteGame/Editor/UiTools/Page/UiPageCodegen.cs

### T7 · 窗口与工作台接线（事件·门控·冲突保护·焦点重载）

- 文件: Assets/LiteGame/Editor/UiTools/Page/UiPageScaffoldWindow.cs, Assets/LiteGame/Editor/UiTools/Page/UiPageRegisterWindow.cs, Assets/LiteGame/Editor/UiTools/Workbench/UiWorkbenchPageSection.cs, Assets/LiteGame/Editor/UiTools/Workbench/UiWorkbenchWindow.cs, Assets/LiteGame/Editor/UiTools/Workbench/UiWorkbenchAvailability.cs, Assets/LiteGame/Editor/UiTools/Workbench/UiWorkbenchPerspective.cs

### T8 · 工具面与索引（handlers + toolchain.json + 工具索引）

- 文件: Assets/LiteGame/Editor/Toolchain/EditorToolHandlers.cs, scripts/gate/toolchain.json, Docs/工具链/工具索引.md

### T9 · 文档回写（08/修改指南/技术总结/设计/schema/进度）

- 文件: Docs/项目说明/08-UI工作台.md, Docs/修改指南.md, Docs/技术总结/04-UI系统与Lua脚本层.md, Docs/设计文档/客户端/UI系统/UI编辑器工具链专项设计.md, Docs/设计文档/客户端/UI系统/UiPageContract.schema.json, Docs/施工进度/UI系统线.md

### T10 · 全量验证与冒烟（UI 回归·L1·Doc·toolchain·wf verify·eval_file）

- 文件: .workflow/active/ui-p2-scaffold-register/build-log.md

### T11 · 独立代码评审与修复复评

- 文件: .workflow/active/ui-p2-scaffold-register/reviews/code-review.md

