# 代码评审（独立子代理）：UI 系统 P2-3 B3：设备档与预览矩阵

> 运行 ui-p3-device-preview · 评审时间 2026-10-25
>
> 本文件由主代理根据独立子代理返回整理落盘；不是主代理自评。

## 评审输入

- 代码基线：git HEAD `a5183d8b`；B3 全部改动在工作区未提交（并行线 ClientHost 空白/审计/代码地图改动不属本批）。
- 范围：`UiDeviceProfiles.json`/catalog/schema；`UiPreviewDriver`＋DevTool/Preview 六件；`UiPagePreview`；矩阵 `UiPreviewMatrix`/`Job`/`Planner`/`Runner`/`Window`；工作台设备档接线；`ConfigTableFileSource` 表字节单源；五套夹具＋两份 asmdef；`.gitignore`/`toolchain.json`/workflow 门禁；总设计/工具链设计/进度线/工具索引。
- 子代理约束：只读评审，不修改生产代码、测试与门禁；三个独立会话（首轮/复评/第三轮）分别执行，修复后重新开新会话复评。
- 评审依据：`plan.md`、`acceptance.md`、`tasks.md`、`test-plan.md`、`evidence/red.json`、`evidence/verify.json`、`build-log.md`。

## 变更范围

- 设备档案 JSON/schema/catalog＋严格解析；预览驱动 apply/reset、SessionState 修订握手、交互工具条、语言状态与作者文本还原；安全区/字体/伪本地化/语言；GameView 反射切换与还原。
- 矩阵 Planner/Job/Runner/Window：组合展开、Play 状态机、截图、索引、中断/失败隔离、产物路径防护；工作台设备档下拉与预览携带；表字节直载单源；`.gitignore`/toolchain/门禁/文档回写；五套夹具 34 例。
- 并行线改动（ClientHost 空白、审计/代码地图复核）不属本批；本批无 prefab/场景改动。

## 首轮评审（FAIL）

首轮阻塞问题：
1. major：tbltext 直载失败时驱动仍回写 `OptionsApplied`，矩阵只记分辨率降级，en/pseudo 会假绿（matrix.json 成功、degraded 空）。
2. major：交互预览工具条仅重载/退出，设备/语言/字体免重启切档无入口（AC2）。

 minors：PlayMode 恒真断言、driver 未销毁/EditorPrefs 污染、`IsClassicResolutionMode` 死码、planner 硬编码 Screens/语言白名单、产物路径未拒绝 `..`、退出不还原 GameView。

处理：`LocaleStatus` 上报＋矩阵失败/降级；工具条设备（含分辨率）/语言/字体轮换；`UiPreviewGameView` 下沉 DevTool；测试隔离与弱断言修复；planner 单源/逃逸拒绝；GameView 还原；矩阵执行器拆分（Runner 366/Planner 201/Job 110 行）。

## 复评（FAIL）

复评阻塞问题：
1. major：切回“作者文本”不还原——`UiStaticTextBinder.Bind(null)` 只解绑，页面仍留译文，工具条却显示“作者文本”。
2. major：`WriteOptions` 写 pending 后，驱动同指纹短路分支直接置 applied、不重报状态；自定义档位分辨率/inset 相同（校验不拒重复）时 unavailable 可能假绿；unavailable 分支无实跑证据。

处理：新增 `PreviewAuthorText` 作者原串快照/还原（PlayMode 用例覆盖绑定→作者文本→再绑定→复位）；同指纹短路也写 `LocaleStatus` 终态；`IsLocaleUnavailable`/`StatusFor` 纯函数＋测试；`RecordFailure` 保留已应用分辨率；矩阵不弹保存对话框（dirty 直接拒绝）；矩阵窗口显示实时状态；`UiPreviewEditModeTests` 纳入门禁；真实无表矩阵实跑（`CompletedWithFailures` + degraded + captured=false + resolutionApplied=true）；中断实跑首档保留。

## 第三轮（PASS）

结论 PASS，无阻塞。两个 blocker 均核实真修；工具条/还原/矩阵失败路径/测试隔离有效。剩余 minor（已收口或登记）：
- 字体档字面量重复 → `PreviewFontScale.SupportedScales` 单源。
- `LocaleDataDirKey` 注入持久化 → 收进 `UiPreviewMatrixOptions.LocaleDataDir`，Start 统一写入。
- 驱动异常周期 Warning → 同一错误只告警一次。
- `ConfigTableFileSource` 默认目录未用 `TableLayout.TableDataPath` → 已用。
- 审计表 Preview“在途桩” → 已标记 B3 修复。
- 留存 minor（登记后续）：SessionState 七键写面仍有编辑器三处重复；语言 ok 仅判 BuildService 非空（缺 key 仍会以 `[key]` 入图，MissingKeyCount 未上报）；`ResetPreviewOptions` 参数未用；job.Error 无索引出口；高分辨率 GameView 下工具条可读性依赖 GameView 缩放。

## 发现的问题与处理

| ID | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|
| R1-1 | major | 语言表直载失败仍回写 applied，矩阵假绿 | `LocaleStatus` 上报＋矩阵失败/降级；无表矩阵实跑 | 已修 |
| R1-2 | major | 交互预览无设备/语言/字体切档入口 | 工具条轮换（SessionState+revision）；设备含 GameView 分辨率 | 已修 |
| R1-minor | minor | 恒真断言、driver 测试污染、死码、硬编码目录/语言、路径逃逸、GameView 不还原 | 逐一修复/补测；planner 单源；Runner 拆分 | 已修 |
| R2-1 | major | 切回作者文本不还原译文 | `PreviewAuthorText` 快照/还原；PlayMode 四段用例 | 已修 |
| R2-2 | major | 同指纹短路不重报状态，unavailable 可能假绿 | 短路写终态；`StatusFor`/`IsLocaleUnavailable` 纯函数＋测试；实跑无表矩阵 | 已修 |
| R2-minor | minor | 失败覆盖分辨率、窗口只显示启动文案、矩阵弹保存框、UiPreview 未入门禁 | `RecordFailure` 保留结果；窗口实时状态；dirty 拒绝；门禁加 `ui-preview-editmode` | 已修 |
| R3-minor | minor | 字体档重复、注入键持久化、周期 Warning、TableDataPath、审计在途桩 | 单源/选项注入/告警去重/路径 helper/审计回写 | 已修 |
| R3-残留 | minor | SessionState 写面重复、缺 key 仅 `[key]`、Reset 参数未用、job.Error 出口、高分辨率工具条可读性 | 登记后续（B4/预览体验批）；不阻塞本批 | 已登记 |

## 正确性与边界

- AC1 PASS：8 档/负例/确定性/三方同源；AC2 PASS：设备档+安全区+语言（含 pseudo）+字体档，Play 内切档、作者文本/GameView 还原、语言不可用不假绿；AC3 PASS：真跑截图+索引、中断保留、失败隔离、无表失败实跑、Artifacts 忽略；AC4 PASS：工作台下拉/跟随预览/持久/预览携带；AC5 PASS：设计/进度/审计/工具索引回写，非目标登记；AC6 PASS：52 条 verify 全绿。
- 边界：布局档不跑 Lua（动态文案不切）；伪本地化为近似；竖屏预留/减少动效/数据档/真链路不在本批；GameView 内部 API 升版风险（降级已设计）。

## 通用性与扩展性

- 通用性: PASS
  - 依据：设备档 JSON 单源被驱动/工作台/矩阵共用；安全区策略/伪本地化/表字节源可复用；路径/校验/持久化分层；无第二份分辨率/inset/语言/字体档字面量（Tool minor 已收口）。
- 扩展性: PASS
  - 依据：新增设备档改 JSON；矩阵维度/入口可扩；状态机 Job 快照可恢复；语言新增仍需代码评估（复数/RTL，已登记边界）。

## 安全与性能

- 安全：产物限 `Artifacts/` 相对路径并拒 `.`/`..` 与绝对路径；无敏感日志；测试注入 `LocaleDataDir` 仅在编辑器会话。
- 性能：驱动 0.2s 轮询；表直载一次；矩阵逐组合截图；窗口状态读取轻量。无热路径周期性 Warning（已去重）。

## 测试充分性

- 先红：5 条 red 命令断言级失败（首版因编辑器死锁的无效 red 已重跑覆盖）。
- 新夹具 34 例：Device 9／Locale 7／Matrix 12／WorkbenchDevice 3／Driver PlayMode 3；另有既有 UiPreview 6、UiWorkbench 21 回归。
- 实跑证据：正常矩阵（zh/en/pseudo，1920×1080，resolutionApplied=true）；中断（首档 captured 保留）；语言不可用（CompletedWithFailures/degraded/captured=false/resolutionApplied=true）；工作台/工具条冒烟（pseudo/1.3/分辨率切换/退出还原）。

## 评审结论

- 评审方式: 独立子代理
- 结论: PASS
- 通用性: PASS
- 扩展性: PASS
- 阻塞问题: 0
- 验证有效性: `wf verify` **PASS 52/52**（2026-10-25T23:12；L1 1843＋doc/l0/restore/toolchain/L2＋全部 UI EditMode/PlayMode，含 `ui-preview-editmode`）；red 断言级；实跑覆盖正常/中断/无表失败三条路径。
- 评审人: general-purpose（Qoder Agent 工具，三个独立会话）
- 评审时间: 2026-10-25（首轮 FAIL → 复评 FAIL → 第三轮 PASS；minor 收口后复测，不再开新轮）

## 复测记录

- 修复面复测：Locale 7/7、Matrix 12/12、WorkbenchDevice 3/3、UiPreview 6/6、Driver PlayMode 3/3、Device 9/9。
- 实跑复测：正常矩阵（pseudo 截图）、中断（首档保留）、无表失败矩阵（失败/降级/分辨率保留）、工具条切档与退出还原。
- 全量复跑：`wf verify` PASS 52/52（evidence/verify.json，2026-10-25T23:12）。
- 审计回写：Preview 在途桩条目标记已修复；剩余 minor 登记后续批次。
