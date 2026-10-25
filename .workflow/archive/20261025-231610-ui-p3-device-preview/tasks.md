# 实施任务清单：UI 系统 P2-3 B3：设备档与预览矩阵

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 6/6（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | T1 设备档案 UiDeviceProfiles（JSON 单源+校验+确定性） | - | 完成 | UiDeviceProfiles.json（六横屏档+竖屏预留 inMatrix=false，含分辨率/方向/安全区 inset/可选 dpi）+UiDeviceProfileCatalog（Load/Validate，禁静态缓存）+schema；校验负例（宽高比/inset 越界/ID 文法）；确定性（按 id 排序、重读逐字节一致、无时间戳） | unity-tests --filter UiDeviceProfilesEditModeTests | UiDeviceProfilesEditModeTests 9/9；UiDeviceProfiles.json（8 档=6 横屏+2 竖屏预留）+ schema；确定性排序/严格解析 |
| T2 | T2 预览驱动升级（设备档/安全区/语言/字体缩放） | T1 | 完成 | UiPreviewDriver 支持免重启切档：GameView 分辨率（反射 SetSize 容错降级+确保经典分辨率模式）、PreviewSafeAreaStrategy 注入/撤销（当前屏幕像素空间 Rect）、语言中/英/伪本地化（tbltext 直载 LocalizationService+PseudoLocale 装饰器驱动 LTextKeyRef）、字体缩放 1.0/1.15/1.3（TMP 原值恢复）；退出回场景无残留；SessionState 键收私有 | UiPreviewDriverPlayModeTests+UiPreviewLocaleEditModeTests+eval 冒烟 | UiPreviewLocaleEditModeTests 5/5；UiPreviewDriverPlayModeTests 3/3；PreviewFontScale 用例通过；新增 ConfigTableFileSource 表字节直载单源 |
| T3 | T3 ui.preview-matrix 截图矩阵+.gitignore 机制 | T1, T2 | 完成 | editor action：页×设备档×语言[×长文本]组合状态机（开场景 Play→逐组合应用→等稳定帧→ScreenCapture→matrix.json 索引含分辨率应用结果/降级记录）；中断安全；失败组合不中断；.gitignore 增 [Aa]rtifacts/；真跑一页小矩阵实产+git status 零污染 | unity-tests --filter UiPreviewMatrixEditModeTests；真跑矩阵；git status | UiPreviewMatrixEditModeTests 7/7；真跑 Page_Login 2 组合 1920×1080 截图+matrix.json（resolutionApplied=true）；中断实跑 status=Interr… |
| T4 | T4 工作台接线（设备档下拉+预览携带） | T1 | 完成 | 上下文条设备档真下拉（档案单源+『跟随预览』缺省项，IWorkbenchSettings 新键持久）；预览按钮携当前设备档/语言打开；『设备档：P2-3』占位移除 | unity-tests --filter UiPreviewEditModeTests,UiWorkbenchEditModeTests 回归+窗口冒烟 | UiWorkbenchDeviceEditModeTests 3/3；UiPreviewEditModeTests 6/6；UiWorkbenchEditModeTests 21/21；开窗冒烟设备档=phone.19-5-9 真下拉（截… |
| T5 | T5 文档回写+边界登记 | T3, T4 | 完成 | 总设计 §7.3（设备档现行口径）/§7.5（字体档 1.0/1.15/1.3 对齐）、工具链设计 §7.1（预览矩阵现行口径+减少动效延后注记）回写；非目标登记（数据档 P3/真链路/竖屏/减少动效/P4）；UI系统线新章；工具索引/toolchain.json 同步 | doc-lint 0/0；docs --check | 总设计 §7.3/§7.5 + 工具链 §7.1 回写；schema/README 登记；UI系统线第12章；toolchain docs 重生成；doc-lint 0/0、docs --check current、toolchain c… |
| T6 | T6 门禁收口 | T5 | 完成 | config.json 增本批门禁（EditMode 三套+PlayMode 一套）；toolchain.py check 0 error；wf verify 全绿 | toolchain.py check；wf verify | config.json 新增 5 条门禁（Device/Locale/Matrix/WorkbenchDevice/Driver PlayMode）；toolchain.py check OK；wf verify PASS（L1/doc/… |

## 任务详情

### T1 · T1 设备档案 UiDeviceProfiles（JSON 单源+校验+确定性）

- 文件: Assets/LiteGame/Editor/UiTools/UiDeviceProfiles.json, Assets/LiteGame/Editor/UiTools/Preview/UiDeviceProfileCatalog.cs

### T2 · T2 预览驱动升级（设备档/安全区/语言/字体缩放）

- 文件: Assets/LiteGame/DevTool/UiPreviewDriver.cs, Assets/LiteGame/DevTool/PreviewSafeAreaStrategy.cs, Assets/LiteGame/DevTool/PseudoLocale.cs, Assets/LiteGame/Editor/UiTools/Preview/UiPagePreview.cs

### T3 · T3 ui.preview-matrix 截图矩阵+.gitignore 机制

- 文件: Assets/LiteGame/Editor/UiTools/Preview/UiPreviewMatrix.cs, .gitignore, Assets/LiteGame/Editor/Toolchain/EditorToolHandlers.cs

### T4 · T4 工作台接线（设备档下拉+预览携带）

- 文件: Assets/LiteGame/Editor/UiTools/Workbench/UiWorkbenchWindow.cs, Assets/LiteGame/Editor/UiTools/Workbench/UiWorkbenchSettings.cs

### T5 · T5 文档回写+边界登记

- 文件: Docs/设计文档/客户端/UI系统/UI系统总设计.md, Docs/设计文档/客户端/UI系统/UI编辑器工具链专项设计.md, Docs/施工进度/UI系统线.md

### T6 · T6 门禁收口

- 文件: .workflow/config.json

