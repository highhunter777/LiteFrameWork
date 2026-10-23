# 实施任务清单：UI 系统 P2-1：单工作台与角色 Lane（宿主 + 透视迁移）

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 11/11（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | 先红：工作台守卫夹具与最小编译骨架 | - | 完成 | 夹具不少于 7 例首跑全红且失败信息指向未实现策略/未登记工具；Unity 编译 0 错 | python scripts/gate/unity-tests.py --mode editor --filter UiWorkbenchEditModeTests | UiWorkbenchEditModeTests 10/10 断言失败（编译 0 错）；wf verify --red PASS，ui-workbench-red exit=1（evidence/red.json） |
| T2 | Lane 策略内核（动作×角色矩阵单源） | T1 | 完成 | 矩阵夹具全绿；新增动作只改表；窗口内无 if(lane==...) 分散判断 | python scripts/gate/unity-tests.py --mode editor --filter UiWorkbenchEditModeTests | UiLanePolicy 11x3 矩阵+穷举夹具绿；UiWorkbenchEditModeTests 13/13 |
| T3 | 工作台状态与偏好（上下文不丢） | T1 | 完成 | 切换透视/Lane 后目标与选中页不丢；未保存标记与关闭提示可测；测试不读写真实 EditorPrefs | python scripts/gate/unity-tests.py --mode editor --filter UiWorkbenchEditModeTests | UiWorkbenchState 切换上下文夹具绿；IWorkbenchSettings 双实现（EditorPrefs/内存） |
| T4 | 页面透视模型（清单投影与编辑保存门面） | T2, T3 | 完成 | 真实清单 11 页/12 行只读断言；缺清单显式错误；重复保存逐字节一致且 sourceHash 重算 | python scripts/gate/unity-tests.py --mode editor --filter UiWorkbenchEditModeTests | 页面清单 11 页/12 行只读、缺清单显式错误、临时路径双写逐字节一致（夹具绿） |
| T5 | 契约透视模型（生成与 diff 报告投影） | T2, T3 | 完成 | 生成与 diff 报告映射正确；失败显式不静默；不复制契约生成逻辑 | python scripts/gate/unity-tests.py --mode editor --filter UiWorkbenchEditModeTests | 契约报告投影 + Generate(临时目录) 11 份/无 Error/重复一致（夹具绿） |
| T6 | 宿主窗口装配（零 Sirenix IMGUI） | T2, T3, T4, T5 | 完成 | Hub 打开无 Console error；五透视可切且共享上下文；动作受 Lane 门控并显示禁用原因；手动冒烟通过 | python scripts/gate/unity-tests.py --mode editor --filter UiWorkbenchEditModeTests | 窗口打开：title=UI 工作台、Perspective=Page、五透视 5、Console 0 error（eval_file 冒烟） |
| T7 | 工具面接线与 ui.compose 退役 | T6 | 完成 | Catalog 含 ui.workbench、不含 ui.compose；python scripts/toolchain.py check 0 error；全仓无 ui.compose 活引用 | python scripts/toolchain.py check | Catalog：ui.workbench 在册(Kind=window/无 Error)、ui.compose 与 ui.page-new 为 null（夹具绿） |
| T8 | 文档回写（设计/手册/索引/进度） | T7 | 完成 | 08 手册去掉旧工具过渡说明；入口命令全部为 ui.workbench；链接/锚有效；doc-lint 0/0 | python scripts/gate/doc-lint.py | 08/技术总结04/修改指南/平台设计/工具索引/进度第8章 已改；doc-lint 0/0 |
| T9 | 验证与收尾（全量夹具/回归/门禁/施工记录） | T7, T8 | 完成 | 新增与回归夹具全绿；L1/doc/toolchain 全绿；build-log 完整、未决事项登记 | python scripts/gate/test.py --lane L1 --profile PullRequest | 代码冻结后全量 wf verify 复跑（31 命令；证据 evidence/verify.json）；新增 21 例 + 回归 + L1 + doc/toolchain |
| T10 | 退役波及面补遗：page-new/向导/引用/文档入口 | T7 | 完成 | catalog 无 ui.page-new/ui.compose；编译 0 错；全仓无活引用（归档/历史除外）；08 与平台设计入口改 ui.workbench | python scripts/gate/unity-tests.py --mode editor --filter UiPreviewEditModeTests | 两窗口类删除、UiPreviewEditModeTests 6/6、平台设计条目改；全仓无 ui.compose/ui.page-new 活引用（夹具+grep） |
| T11 | 验证补遗：UI EditMode + doc + toolchain 显式证据 | T8, T10 | 完成 | 五条命令全绿且证据入 build-log | python scripts/gate/unity-tests.py --mode editor --filter UiWorkbenchEditModeTests | UiWorkbench 21/21、UiPreview 6/6、doc-lint 0/0、toolchain check OK；最终 wf verify 证据见 evidence/verify.json |

## 任务详情

### T1 · 先红：工作台守卫夹具与最小编译骨架

- 描述: 新增 UiWorkbenchEditModeTests（Lane 矩阵/透视清单/状态切换/页面清单投影/契约报告/工具面/退役断言）与 Workbench 目录最小类型壳；红必须是断言失败而非编译失败。

- 文件: Assets/Tests/EditMode/UiWorkbenchEditModeTests.cs, Assets/LiteGame/Editor/UiTools/Workbench/

### T2 · Lane 策略内核（动作×角色矩阵单源）

- 描述: UiWorkbenchLane/UiWorkbenchAction/UiLanePolicy：动作×Lane×禁用原因表驱动；UI 门控与夹具共用同一份表。

- 文件: Assets/LiteGame/Editor/UiTools/Workbench/UiWorkbenchLane.cs, Assets/LiteGame/Editor/UiTools/Workbench/UiLanePolicy.cs

### T3 · 工作台状态与偏好（上下文不丢）

- 描述: UiWorkbenchState（透视/Lane/目标/页面/未保存标记）+ IWorkbenchSettings 与 EditorPrefs 实现（键名常量单源）。

- 文件: Assets/LiteGame/Editor/UiTools/Workbench/UiWorkbenchState.cs, Assets/LiteGame/Editor/UiTools/Workbench/UiWorkbenchSettings.cs

### T4 · 页面透视模型（清单投影与编辑保存门面）

- 描述: UiWorkbenchPageList/Row 从 UiPageManifestStore 投影；Owner/设计稿/备注/状态编辑经 UiPageManifestEditor+Store 显式保存，失败不写盘。

- 文件: Assets/LiteGame/Editor/UiTools/Workbench/UiWorkbenchPageList.cs, Assets/LiteGame/Editor/UiTools/Workbench/UiWorkbenchPageEditor.cs

### T5 · 契约透视模型（生成与 diff 报告投影）

- 描述: UiContractReportView 把 UiContractBatch.Generate/Diff 结果投影为计数/破坏性/跳过/输出目录，供透视与状态行消费。

- 文件: Assets/LiteGame/Editor/UiTools/Workbench/UiContractReportView.cs

### T6 · 宿主窗口装配（零 Sirenix IMGUI）

- 描述: UiWorkbenchWindow：顶部上下文条（页面/目标/Lane/设备档占位）+ 左树 + 五透视页签 + 底部状态行；复用四个既有分区与新透视模型；输出 [ui/workbench] 计数。

- 文件: Assets/LiteGame/Editor/UiTools/Workbench/UiWorkbenchWindow.cs, Assets/LiteGame/Editor/UiTools/Workbench/UiWorkbenchPerspective.cs

### T7 · 工具面接线与 ui.compose 退役

- 描述: EditorToolHandlers 注册 ui.workbench 并删除 ui.compose；toolchain.json 条目（kind/plane/writes/outputs/docs）；工具索引重生成；旧引用（测试断言/文档/脚本）清理。

- 文件: Assets/LiteGame/Editor/Toolchain/EditorToolHandlers.cs, scripts/gate/toolchain.json, Docs/工具链/工具索引.md, Assets/Tests/EditMode/UiPreviewEditModeTests.cs

### T8 · 文档回写（设计/手册/索引/进度）

- 描述: 工具链设计 §2 实现口径、08-UI 制作与调试手册、技术总结 04、修改指南工具表、施工进度 UI 系统线新章。

- 文件: Docs/设计文档/客户端/UI系统/UI编辑器工具链专项设计.md, Docs/项目说明/08-UI工作台.md, Docs/技术总结/04-UI系统与Lua脚本层.md, Docs/修改指南.md, Docs/施工进度/UI系统线.md

### T9 · 验证与收尾（全量夹具/回归/门禁/施工记录）

- 描述: 跑新增夹具 + 既有 UI 夹具回归 + L1 + doc + toolchain；补齐 build-log 交付摘要/未决事项/文件清单。

- 文件: .workflow/active/ui-p2-workbench/build-log.md

### T10 · 退役波及面补遗：page-new/向导/引用/文档入口

- 描述: 删 UiPageWizardWindow/UiComposeWindow 两文件与 .meta；清理双向调用（向导→RefreshOpenPageList、宿主→向导）；UiPreviewEditModeTests 改为 workbench 在册且两者为 null；平台设计文档 UiComposeWindow 条目、08 手册入口同步；全仓 grep 零活引用。

- 文件: Assets/LiteGame/Editor/UiTools/Page/UiPageWizardWindow.cs, Assets/LiteGame/Editor/UiTools/Compose/UiComposeWindow.cs, Assets/Tests/EditMode/UiPreviewEditModeTests.cs, Docs/设计文档/架构/开发工具链平台专项设计.md

### T11 · 验证补遗：UI EditMode + doc + toolchain 显式证据

- 描述: 除 L1 外显式跑 UiWorkbenchEditModeTests、UiPreviewEditModeTests 与既有 UI 回归夹具、doc-lint、toolchain check，并在 build-log 记录命令与结果。

- 文件: .workflow/active/ui-p2-workbench/build-log.md

