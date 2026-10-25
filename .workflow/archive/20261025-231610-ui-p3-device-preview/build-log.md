# 施工记录：UI 系统 P2-3 B3：设备档与预览矩阵

> 运行 ui-p3-device-preview · 创建于 2026-10-25T19:47:59+08:00

## 施工记录

<!-- 任务开始、完成、阻塞、取消等事件由 workflow.py 自动追加。 -->
- 2026-10-25T20:28:30+08:00 · 新增任务 T1「T1 设备档案 UiDeviceProfiles（JSON 单源+校验+确定性）」
- 2026-10-25T20:28:31+08:00 · 新增任务 T2「T2 预览驱动升级（设备档/安全区/语言/字体缩放）」
- 2026-10-25T20:28:31+08:00 · 新增任务 T3「T3 ui.preview-matrix 截图矩阵+.gitignore 机制」
- 2026-10-25T20:28:31+08:00 · 新增任务 T4「T4 工作台接线（设备档下拉+预览携带）」
- 2026-10-25T20:28:31+08:00 · 新增任务 T5「T5 文档回写+边界登记」
- 2026-10-25T20:28:31+08:00 · 新增任务 T6「T6 门禁收口」
- 2026-10-25T21:29:43+08:00 · 开始任务 T1「T1 设备档案 UiDeviceProfiles（JSON 单源+校验+确定性）」
- 2026-10-25T21:31:57+08:00 · 完成任务 T1 · 证据: UiDeviceProfilesEditModeTests 9/9；UiDeviceProfiles.json（8 档=6 横屏+2 竖屏预留）+ schema；确定性排序/严格解析
- 2026-10-25T21:31:57+08:00 · 开始任务 T2「T2 预览驱动升级（设备档/安全区/语言/字体缩放）」
- 2026-10-25T21:34:56+08:00 · 完成任务 T2 · 证据: UiPreviewLocaleEditModeTests 5/5；UiPreviewDriverPlayModeTests 3/3；PreviewFontScale 用例通过；新增 ConfigTableFileSource 表字节直载单源
- 2026-10-25T21:34:56+08:00 · 开始任务 T3「T3 ui.preview-matrix 截图矩阵+.gitignore 机制」
- 2026-10-25T21:42:10+08:00 · 完成任务 T3 · 证据: UiPreviewMatrixEditModeTests 7/7；真跑 Page_Login 2 组合 1920×1080 截图+matrix.json（resolutionApplied=true）；中断实跑 status=Interrupted 索引落盘；.gitignore 已排除 Artifacts，git …
- 2026-10-25T21:42:10+08:00 · 开始任务 T4「T4 工作台接线（设备档下拉+预览携带）」
- 2026-10-25T21:49:47+08:00 · 完成任务 T4 · 证据: UiWorkbenchDeviceEditModeTests 3/3；UiPreviewEditModeTests 6/6；UiWorkbenchEditModeTests 21/21；开窗冒烟设备档=phone.19-5-9 真下拉（截图 evidence/workbench.png）；预览携带档冒烟 Screen…
- 2026-10-25T21:49:47+08:00 · 开始任务 T5「T5 文档回写+边界登记」
- 2026-10-25T21:50:44+08:00 · 完成任务 T5 · 证据: 总设计 §7.3/§7.5 + 工具链 §7.1 回写；schema/README 登记；UI系统线第12章；toolchain docs 重生成；doc-lint 0/0、docs --check current、toolchain check OK（1 既存警告）
- 2026-10-25T21:50:44+08:00 · 开始任务 T6「T6 门禁收口」
- 2026-10-25T21:16:00+08:00 · 接手：修复主线程死锁的 Unity 编辑器（重启 PID 39788）；修复测试编译错并重跑真红
- 2026-10-25T21:31:00+08:00 · 先红证据重跑（断言级）：9/9、4/5、7/7、1/3、3/3 失败；推进正式施工
- 2026-10-25T21:40:00+08:00 · T3 真跑 Page_Login 小矩阵（2 图 + matrix.json）；中断实跑（Interrupted）核对
- 2026-10-25T21:47:00+08:00 · T4 工作台/预览冒烟（2436×1125、en、回 Boot）；T5 文档回写与工具索引重生成
- 2026-10-25T21:51:00+08:00 · T6 启动全量 wf verify
- 2026-10-25T22:32:40+08:00 · 完成任务 T6 · 证据: config.json 新增 5 条门禁（Device/Locale/Matrix/WorkbenchDevice/Driver PlayMode）；toolchain.py check OK；wf verify PASS（L1/doc/l0/toolchain/L2 + 全部 UI 套件）

## 关键决定

- **先红证据必须重跑**：接手时 `.workflow` 的 red.json 虽标 5/5 PASS，但五条命令均在 `ensure_compiled` 阶段因编辑器主线程死锁退出（“unity still compiling after 180s”/“console_status unavailable”），测试未执行。重启编辑器、修测试编译错后重跑，失败均为断言级。
- **测试先补强再施工**：骨架期“空集恒真”的用例（预置档案校验/确定性排序/矩阵排除非入矩阵档）补非空前置；语言直载与伪本地化去掉 `Assume` 改硬断言；新增矩阵运行态两例（失败隔离、中断终态）。
- **PlayMode 测试程序集保持 Player 可链接**：UI PlayMode 测试集刻意不引 `AssetDatabase`（见 FeedbackSurface 注释）；驱动测试用文件级 `#if UNITY_EDITOR` 包裹，仅给 asmdef 加 `LiteGame.DevTool` 引用，不改 `includePlatforms`。
- **表字节直载单源**：新增 `ConfigTableFileSource`（`ConfigService.TableDataFiles` + `TableLayout.TableName` + `cfg.Tables`）；`AnimationProfileTableSource` 与本批预览语言复用它，避免第三份装载循环。
- **矩阵执行器跨域重载恢复**：Job 状态（阶段/游标/修订/组合/降级）序列化进 `SessionState`；编辑器 `[InitializeOnLoad]` 订阅 update，进入/退出 Play 的域重载后从 SessionState 恢复；驱动以 `OptionsRevision/Applied` 握手确认应用完成。
- **GameView 分辨率内聚单点**：反射 `UnityEditor.GameView.SetCustomResolution(Vector2,string)` 并用 `GetPlayModeViewSize` 回读校验；失败返回 false，由矩阵记 `resolutionApplied=false`＋降级记录。
- **矩阵入口形态**：Hub/Registry 的 editor action 无参数字段，实作为 `ui.preview-matrix` 窗口（参数面）＋ `UiPreviewMatrixRunner` API（eval/自动化可启动）；产物与验收不变（见偏差）。
- **交互预览切档**：预览工具条新增设备档（含分辨率）/语言/字体轮换按钮，写 SessionState＋修订号，驱动轮询免重启应用；`UiPreviewGameView` 下沉 DevTool（编辑器态）供驱动与矩阵共用。
- **语言不可用不假绿**：驱动上报 `LocaleStatus`（off/pending/ok/unavailable）；矩阵在 applied 后检查，unavailable → 该组合记失败＋降级记录，不产出“成功”截图。
- **GameView 还原**：手动预览在入口记录原分辨率、退出 Play 还原；矩阵 Job 记录原尺寸、Finalize 还原。
- **矩阵执行器拆分**：`UiPreviewMatrixRunner`（状态机，366 行）＋ `UiPreviewMatrixPlanner`（输入/路径校验，201 行）＋ `UiPreviewMatrixJob`（DTO/SessionState/路径单源，110 行），满足 >400 行多职责先拆分。
- **作者文本快照**：`PreviewAuthorText` 在首次绑定前记录 LTextKeyRef 目标原串；切回“作者文本”/退出/重载时还原（不改运行时 `UiStaticTextBinder.Bind(null)` 语义）。
- **同指纹不丢状态**：驱动在同指纹短路分支也重写 `LocaleStatus` 终态，避免 `pending` 被矩阵当作成功。
- **产物隔离机制化**：`.gitignore` 增 `[Aa]rtifacts/`；矩阵截图/索引全部落项目根 `Artifacts/`，`git status` 零污染。

## 偏差与处理

- **入口 action → window**：方案写 editor action；实作窗口＋API 运行器。原因＝Hub action 无参数面（页/档/语言集合需要选择）。AC3 判定（截图/索引/中断/失败隔离/git 零污染）不变。
- **分辨率 API 名称**：方案写反射 `SetSize`；2022.3 实际可用内部面是 `SetCustomResolution(Vector2,string)`（`SetSize` 无此公开面）。语义相同：切 GameView 经典分辨率并回读校验。
- **设备档案竖屏预留数量**：方案写“`phone.9-19-5.portrait` 等”；实作两条竖屏（手机 9:19.5、平板 3:4），均 `inMatrix=false`，为 P4 留位。
- **宽高比容差**：市场标称比是近似值（2560×1080＝64:27 称 21:9）；测试容差由 0.01 调整为 0.05，避免把真实分辨率判错。
- **测试先红期新增用例**：矩阵运行态两例（失败隔离/中断）在 tests-first 阶段补入，作为 AC3 的可判定面；重跑 red 后文档同步。
- **`IsClassicResolutionMode` 语义**：2022.3 的 Device Simulator 是独立窗口，不是 GameView 模式；本判定＝GameView 窗口存在。矩阵显式取 GameView 并聚焦。
- **布局档不切 Lua 动态文案**：只驱动 `LTextKeyRef`（tbltext）；登录页 `Txt_Status`/`Txt_Version` 等 Lua 动态文本保持作者文本，符合“不跑 Boot/Lua/真实数据”。
- **矩阵索引含时间戳**：`matrix.json` 是交接证据（非确定性生成物），保留 `capturedAt`；不纳入契约/清单的确定性对账。

## 问题与修复

- **Unity 编辑器主线程死锁**：接手时 `editor_status` 30s 超时、pipeline `list` 可通但主线程命令全挂；无调试器/模态框/CPU 自旋。强制结束 PID 11248 并以相同 `-projectpath` 重启（PID 39788），pipeline 恢复；未保存场景风险已核对（Boot/预览场景无未提交改动）。
- **首版 red 证据作废**：五条命令均在编译/状态获取阶段退出，测试未执行；重启并修编译后重跑，red.json 覆盖。
- **CS0230**：`UiDeviceProfilesEditModeTests.cs` 的 `foreach (float钦定 in …)` 缺空格；改为 `float aspect`。
- **CS0246（UiPreviewDriver）**：UI PlayMode 测试集缺 `LiteGame.DevTool` 引用；补 asmdef 引用并用 `#if UNITY_EDITOR` 包住驱动测试（保持 Player 可链接）。
- **EditMode 测试缺 DevTool 引用**：`UiPreviewLocale/PreviewSafeAreaStrategy/PreviewFontScale` 不可见；给 `LiteGame.EditModeTests.asmdef` 补 `LiteGame.DevTool`。
- **无效枚举**：`PlayModeStateChange.ExitedEditMode` 不存在；改用 `EnteredEditMode` 作为退出 Play 的收尾信号。
- **`BuildIndexCore` 访问级**：`UiPreviewMatrixRun` 跨类调用私有方法；改为 `internal`。
- **陈旧 applied 修订**：新矩阵任务可能读到上一任务的 `OptionsAppliedKey`；Start 时重置 `OptionsRevision/Applied=0`，再写首组合修订。
- **`ConfigTableFileSource.Load` 初版重复读盘**：loader 委托内调用 `LoadBytes` 会逐表重读；改为先缓存一次再建 `cfg.Tables`（已修）。
- **矩阵工具条入镜**：驱动 OnGUI 会出现在截图；新增 `HideOverlayKey`，Start 置 true、Finalize 置 false。
- **中断早于首档应用**：此时 combos 的 `resolutionApplied=false`（尚未走到切换点），索引如实记录；degraded 不记（未执行切换）。已作为边界登记。
- **独立评审首轮 FAIL（两个 major）**：① 语言表直载失败时驱动仍回写 applied，矩阵会假绿——修：`LocaleStatus` 上报＋Runner 失败/降级记录；② 交互预览工具条无切档入口——修：设备/语言/字体轮换按钮（写 SessionState＋revision），设备轮换含 GameView 分辨率；另修测试恒真断言与 driver 未销毁/EditorPrefs 污染、删除 `IsClassicResolutionMode` 死码、planner 用 `UiValidator.ScreensDir`/`UiPreviewLocale.SupportedLocales` 单源、产物目录 `..` 逃逸拒绝、GameView 退出还原。
- **独立复评 FAIL（两个 major）**：① 切回“作者文本”不还原译文——修：`PreviewAuthorText` 快照＋还原，PlayMode 用例覆盖；② 同指纹短路不复报语言状态，unavailable 可能假绿——修：短路分支重写终态，新增 `IsLocaleUnavailable`/`StatusFor` 纯函数与真实无表矩阵实跑证据；另修 `RecordFailure` 保留已应用分辨率、矩阵窗口状态刷新、矩阵不弹保存对话框（dirty 直接拒绝）、`UiPreviewEditModeTests` 纳入门禁。
- **拆分后编译错**：`UiPreviewMatrixRun` 的 `_failed` 列表与新增失败位重名——失败位改 `_runFailed`。
- **矩阵入口窗口参数面**：窗口与 Planner 共用 `UiPreviewMatrixOptions`；Start 先出计划再开场景，校验失败不产生作业。

## 验证结果

- 先红（重跑）：`wf verify --red` PASS——Device **9/9**、Locale **4/5**、Matrix **7/7**、WorkbenchDevice **1/3**、Driver PlayMode **3/3** 失败；均为断言级。
- 新夹具绿（两轮评审修复后）：Device **9/9** · Locale **7/7** · Matrix **12/12** · WorkbenchDevice **3/3** · Driver PlayMode **3/3**（语言用例含作者文本还原）。
- 回归：UiPreview **6/6** · UiWorkbench **21/21** · UiValidation **14/14**（全量门禁复核）。
- 真跑矩阵：`Page_Login` × `pc.16-9` × {zh-CN,en,pseudo} × 1.0 → 多张 **1920×1080** PNG + `matrix.json`（`status=Completed`、`resolutionApplied=true`、`degraded=[]`）；人工核对中文/英文/伪本地化静态文案；`Artifacts/` 被 `.gitignore` 排除，`git status` 零污染。
- 中断实跑：4 组合运行中待首档截图落盘后 `Cancel` → `matrix.json` `status=Interrupted`、`interrupted=true`；首档 `captured=true`/`resolutionApplied=true` 保留，其余 `captured=false`（`Page_LoginInterrupt2`）。
- 语言不可用实跑（`LocaleDataDirKey` 注入无效目录）：`matrix.json` `status=CompletedWithFailures`、`degraded` 含“语言表直载失败”、该组合 `captured=false` 且 `resolutionApplied=true`——不再假绿，分辨率结果不被失败覆盖。
- 工作台/预览冒烟：设备档下拉 `phone.19-5-9`（占位已移除）；预览工具条轮换 pseudo/1.3 字体生效（标题 60→78）、`UiPreviewGameView` 分辨率切换生效；退出还原 GameView 1920×1080；切回“作者文本”还原原串（PlayMode 用例证）。
- 文档/工具：`doc-lint` **0/0**；`toolchain.py docs --check` current；`toolchain.py check` OK（1 条既存未注册脚本警告）。
- 全量 `wf verify`：**PASS 52/52**（2026-10-25T23:12；L1 1843＋doc/l0/restore/toolchain/L2＋全部 UI EditMode/PlayMode，含 `ui-preview-editmode`；证据 `evidence/verify.json`）。
- 2026-10-25T20:55:17+08:00 · 先行测试 PASS · 5/5 · 证据 evidence/red.json（首版，后因编辑器死锁作废）
- 2026-10-25T21:09:09+08:00 · 先行测试 PASS · 5/5 · 证据 evidence/red.json（首版，后作废）
- 2026-10-25T21:26:07+08:00 · 先行测试 PASS · 5/5 · 证据 evidence/red.json（重跑，断言级）
- 2026-10-25T21:27:53+08:00 · 先行测试 PASS · 5/5 · 证据 evidence/red.json（补强后重跑）
- 2026-10-25T21:29:20+08:00 · 先行测试 PASS · 5/5 · 证据 evidence/red.json（矩阵运行态补例后重跑）
- 2026-10-25T22:09:41+08:00 · 验证 PASS · 51/51 · 证据 evidence/verify.json
- 2026-10-25T22:32:26+08:00 · 验证 PASS · 51/51 · 证据 evidence/verify.json
- 2026-10-25T22:55:07+08:00 · 验证 FAIL · 51/52 · 证据 evidence/verify.json
- 2026-10-25T23:12:46+08:00 · 验证 PASS · 52/52 · 证据 evidence/verify.json

## 交付摘要

设备档案（JSON 单源＋校验＋确定性）与 schema；预览驱动免重启切换设备档/安全区/语言/伪本地化/字体缩放（含工具条轮换与 GameView 还原）；tbltext 直载与表字节单源；语言不可用状态上报不假绿；截图矩阵窗口＋Play 状态机（组合展开/截图/索引/中断/失败隔离）与真实产物；工作台设备档下拉与预览携带；`.gitignore` 产物隔离；总设计/工具链设计/进度线/schema/工具索引回写；五套新夹具（34 例）并接入 workflow 门禁。

## 未决事项

- 独立子代理代码评审：首轮 FAIL、复评 FAIL（各两个 major）均已修复；第三轮 PASS（无阻塞，minor 已收口/登记）。
- 提交：工作区含并行线改动（ClientHost 空白、审计/代码地图复核），本批需按文件/hunk 拆分提交；待用户确认。
- B4：存量 161 处颜色收敛、uGUI Selectable 33 处/17 prefab 状态色；P3–P5 后续。
- 边界：GameView 内部 API 升版风险；布局档不切 Lua 动态文案；伪本地化为近似；减少动效维度延后；竖屏仅预留。
- 中断早于首档应用时 `resolutionApplied=false` 不记 degraded（如实索引，未见失真）。

## 文件清单

- 数据/契约：`Assets/LiteGame/Editor/UiTools/UiDeviceProfiles.json`（＋meta）、`Docs/设计文档/客户端/UI系统/UiDeviceProfiles.schema.json`
- 运行时/预览：`Assets/LiteGame/DevTool/UiPreviewDriver.cs`、`Assets/LiteGame/DevTool/Preview/{PreviewSafeAreaStrategy,PreviewFontScale,PreviewAuthorText,PseudoLocale,UiPreviewLocale,UiPreviewGameView}.cs`
- 编辑器：`Assets/LiteGame/Editor/UiTools/Preview/{UiDeviceProfileCatalog,UiPreviewMatrix,UiPreviewMatrixJob,UiPreviewMatrixPlanner,UiPreviewMatrixRunner,UiPreviewMatrixWindow}.cs`、`UiPagePreview.cs`、`UiDeterministicJson.cs`、`Workbench/{UiWorkbenchWindow,UiWorkbenchSettings}.cs`、`Toolchain/EditorToolHandlers.cs`
- 表直载：`Assets/LiteClient/Adapters/Serialization.Luban/ConfigTableFileSource.cs`（＋meta）、`AnimationProfileTableSource.cs`（复用）
- 夹具：`Assets/Tests/EditMode/{UiDeviceProfiles,UiPreviewLocale,UiPreviewMatrix,UiWorkbenchDevice}EditModeTests.cs`、`Assets/Tests/UI/PlayMode/UiPreviewDriverPlayModeTests.cs`、两份测试 asmdef
- 配置/文档：`.gitignore`、`.workflow/config.json`、`scripts/gate/toolchain.json`、`Docs/工具链/工具索引.md`、`Docs/施工进度/UI系统线.md`、`Docs/设计文档/客户端/UI系统/{README,UI系统总设计,UI编辑器工具链专项设计}.md`
- 运行证据：`.workflow/active/ui-p3-device-preview/`（red/verify/reviews）、`Artifacts/PreviewMatrix/`（不入库）

## 后续建议

1. 完成独立代码评审（重点：矩阵状态机中断/域重载、SessionState 修订握手、`ConfigTableFileSource` 分层、窗口/运行器职责边界）。
2. 评审通过后按批拆分提交（排除并行线 ClientHost/审计/代码地图改动），并补第 12 章提交号。
3. B4 处理存量颜色与 uGUI Selectable；P3 数据档/运行时调试复用 `PseudoLocale` 与设备档。
4. 若后续需要矩阵作为 CLI 自动任务，可补 `ui.preview-matrix-status`/`cancel` action（当前窗口＋eval 已可用）。

## 阶段流转

<!-- 阶段推进与回退记录由 workflow.py 自动追加。 -->
- 2026-10-25T19:47:59+08:00 · 初始化运行，当前阶段「提出方案」
- 2026-10-25T19:49:24+08:00 · 完成「提出方案」→ 「子代理评审方案」
- 2026-10-25T20:18:31+08:00 · 完成「子代理评审方案」→ 「用户确认验收标准」
- 2026-10-25T20:28:17+08:00 · 完成「用户确认验收标准」→ 「实施任务清单」
- 2026-10-25T20:28:31+08:00 · 完成「实施任务清单」→ 「评审任务」
- 2026-10-25T20:28:41+08:00 · 完成「评审任务」→ 「先写测试」
- 2026-10-25T21:29:42+08:00 · 完成「先写测试」→ 「正式施工」
- 2026-10-25T22:32:56+08:00 · 完成「正式施工」→ 「测试代码」
- 2026-10-25T23:15:52+08:00 · 完成「测试代码」→ 「评审代码」
- 2026-10-25T23:16:06+08:00 · 完成「评审代码」→ 「回写与收尾」
- 2026-10-25T23:16:10+08:00 · 完成「回写与收尾」→ 完成
