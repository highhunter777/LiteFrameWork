# 方案：UI 系统 P2-3 B3：设备档与预览矩阵

> 运行 ui-p3-device-preview · 创建于 2026-10-25T19:47:59+08:00

## 背景与目标

- 现状（P2-3 B2 之后）：
  - 布局档预览已有雏形（P0 批 `UiPagePreview`/`UiPreviewDriver`）：编辑器专用场景 `UiPreview.unity` 进 Play、实例化目标 prefab、隐藏 DevHUD、OnGUI 工具条（重载/退出）；保存热重载。**无设备档、无语言切换、无长文本/字体缩放、无截图矩阵**。
  - 工作台上下文条"设备档"是占位（`UiWorkbenchWindow.cs:179` 写死"设备档：P2-3"）；预览按钮已有。
  - `SafeAreaReceiver` 有 `SetStrategy(ISafeAreaStrategy)` 注入缝（策略可换、实例传递——预览模拟安全区的现成机制）。
  - 页面清单 `UiPageManifest` 已有维护字段 `DeviceProfiles`（P1-3 建，`SetEditorial` 可编辑）——B3 之前无消费面。
  - Device Simulator 在 2022.3 是**编辑器内建能力**（2021.2+ 并入核心，非独立包，manifest 不会出现）——但其无公开的按脚本切设备 API（官方脚本面仅 DeviceSimulatorPlugin 面板扩展）、设备库是编辑器内置资产而非项目 JSON 单源——设备模拟仍走自建设备档案 + GameView 分辨率切换。
  - 总设计 §7.3 钦定设备档至少覆盖：16:9、19.5:9、20:9、4:3、16:10、超宽 21:9（横屏为主，竖屏预留）。
- 要解决的问题（总设计 §7.1/工具链设计 §7 的 B3 缺口）：
  1. 设备档没有**数据单源**（分辨率/宽高比/安全区 inset/方向）——"不在多个场景复制配置"无从谈起；
  2. 布局档预览**不能切设备/语言/长文本**——多端布局验收（§6.3"字体缩放/多语言长文本必须进入预览矩阵"）无工具面；
  3. **截图矩阵**（中/英/长文本/安全区/设备档）不存在——页面交接证据靠手工截图；
  4. 工作台设备档占位未接真数据（P2-1 冻结"B3 填"）。
- 目标（可观察）：
  - 设备档案上任（JSON 单源＋校验，预置总设计六档）；预览驱动按档切换（分辨率/安全区模拟）；
  - 布局档预览支持语言（中/英/伪本地化长文本）与字体缩放档；
  - `ui.preview-matrix` 批量产出截图矩阵（页面×设备档×语言[×长文本]）＋清单索引（交接证据，非门禁）；
  - 工作台上下文条设备档真下拉（读设备档案）。

## 需求澄清与假设

- 需要确认的问题（验收确认阶段一并裁决）：
  - D1 截图矩阵产出方式：自动批量（editor action 开预览场景逐组合截图，推荐——"交接证据"要全组合）vs 手动逐档（预览窗按键截当前档）；
  - D2 GameView 分辨率切换实现：反射 Unity 内部 `GameView.SetSize`（业界通用做法、2022.3 可用，但属内部 API 需容错降级；**注意**：GameView 处于内建 Simulator 模式时经典 SetSize 面可能不适用——矩阵/预览先确保 GameView 在经典分辨率模式，降级路径不变）vs 不切分辨率（只模拟安全区/语言，分辨率提示手动切）；
  - D3 设备档案形态：JSON＋schema 校验＋确定性排序（同 `UiStyleTokens.json` 先例，推荐）vs ScriptableObject 手工维护。
- 已确认答复：无（按推荐假设推进，验收确认时逐条提审）。
- 采用假设：
  - B3 只做**布局档**增强；数据档（`UiDebugState` 注入）依赖 P3 运行时调试线、真链路（Boot/PlayMode）既有——均不在本批；
  - 竖屏档只在档案里**预留标注**（`orientation` 字段 + 默认矩阵排除），不做竖屏预览验证（P4）；
  - 截图矩阵是交接证据不进门禁判据（设计 §7.1 原文）；产物目录 `Artifacts/`（项目根，Assets 外无 meta）**当前不在 `.gitignore`——本批显式增 `[Aa]rtifacts/` 条目**（"产物不入库"要机制化，红线 5 精神），索引 JSON 随跑随生成；
  - 语言面走 tbltext **文件直载**（已核实：`TableLayout.Files` 含 `UI/tbltext`、`cfg.Tables.Tbltext` 在生成面、`LTextCatalog.Build(cfg.Tables)` 纯函数、`LocalizationService` 构造不依赖 DI——`AnimationProfileTableSource` 全表字节模式天然覆盖）；伪本地化＝变换器（zh 串加长＋音标符），不改表。
  - 字体缩放档＝**1.0/1.15/1.3**（总设计 §7.5 钦定口径；不取超集避免两处漂移，AC5 回写时对齐 §7.5）＝预览期对页面内 TMP `fontSize` 乘数（记录原值、退出/切档恢复），不做产品级可访问性系统（P4 复议）。
- 假设影响与风险：GameView 内部 API 在 Unity 升版可能失效→容错降级（安全区/语言矩阵照跑，分辨率档提示手动切，工具报告降级原因）；预览 Play 流程批量截图需要编辑器协程状态机（无对话框、可中断）——实现风险中等，先用单页小矩阵验证。

## 范围与非目标

- 范围内：
  1. 设备档案 `UiDeviceProfiles`：JSON（`Assets/LiteGame/Editor/UiTools/UiDeviceProfiles.json`）＋模型＋校验（ID 文法/宽高比与分辨率一致性/安全区 inset 合法/横竖屏标记）＋确定性排序；预置六横屏档＋竖屏预留标注；
  2. 预览驱动升级（`UiPreviewDriver`）：设备档切换（GameView 分辨率〔D2〕＋`SafeAreaReceiver.SetStrategy` 注入模拟策略〔inset 按分辨率归一〕）；语言切换（中/英/伪本地化——`LTextCatalog` 文件直载建 `LocalizationService` 驱动 `LTextKeyRef` 静态文案绑定）；字体缩放档（TMP fontSize 乘数＋恢复）；
  3. `ui.preview-matrix` editor action：目标页（单页参数或页面清单选中页）× 设备档 × 语言〔×长文本〕组合，进预览场景 Play 逐组合应用→等待稳定帧→`ScreenCapture` 截图→`Artifacts/PreviewMatrix/<页>/<组合>.png`＋`matrix.json` 索引（组合清单/分辨率应用结果/时间/降级记录）；失败组合记录不中断；`.gitignore` 增 `[Aa]rtifacts/`（产物不入库机制化）；
  4. 工作台接线：上下文条"设备档"占位→真下拉（设备档案单源，含"默认跟随"项）；预览按钮带当前选择打开布局档；
  5. 文档回写（总设计 §7.3/工具链设计 §7 现行口径、UI系统线新章、工具索引）。
- 非目标：
  - 数据档 `UiDebugState`（P3 运行时调试线）；真链路预览（Boot/PlayMode 既有）；
  - 竖屏预览与真机验收（P4）；性能预算/视觉基线（P4）；
  - **减少动效维度**（设计 §7.1 钦定矩阵维度之一）——属质量档/系统设置的运行时面，B3 布局档无此基建，显式延后（AC5 回写时在 §7.1 注记）；
  - `UiPageManifest.DeviceProfiles` 的消费自动化（本批只在工作台页面透视透出展示，不按档过滤矩阵——矩阵参数显式给）；
  - 图集/图元在设备档下的渲染差异检查（无基建）。

## 现状与约束

- 相关代码/模块：
  - 预览：`Assets/LiteGame/Editor/UiTools/Preview/{UiPagePreview,UiPreview.unity}`、`Assets/LiteGame/DevTool/UiPreviewDriver.cs`（编辑器态）；
  - 安全区：`Assets/LiteClient/UI/Widgets/SafeAreaReceiver.cs`（`SetStrategy` 缝）、`ISafeAreaStrategy`；
  - 文案：`LTextKeyRef`/`UiStaticTextBinder`（B0 批）、`LTextCatalog`/`LocalizationService`（运行时）、tbltext 表字节在 `Assets/GameData/Config/UI/`；
  - 工作台：`UiWorkbenchWindow.cs:179` 占位、`IWorkbenchSettings`（EditorPrefs 注入缝）；
  - 清单：`UiPageManifest.DeviceProfiles`（展示面）。
- 约束：
  - 红线 4：预览场景 `.unity` 若需改动走 unity-pipeline（本批尽量不动场景——驱动自建覆盖件）；`UiDeviceProfiles.json` 在 Assets 内会生成 .meta（meta 随文件入库）；截图/索引落项目根 `Artifacts/`（Assets 外、无 meta）。
  - 红线 6：预览状态（当前档/语言/缩放）禁 public static 可变——走 `SessionState`（编辑器会话键值 API 调用，与既有 `EditorPrefs`/`SessionState` 先例〔UiPreviewDriver.TargetPrefabKey/UiPagePreview.ReturnSceneKey〕同类，**不构成新增静态可变字段**——键常量收私有/内部，不包 public static 可变属性）或注入实例；驱动/状态机本体走实例字段；`SafeAreaReceiver` 模拟走 `SetStrategy` 实例缝。
  - 红线 5：设备档清单/分辨率/inset 单源＝JSON 档案；不复制到代码常量（预览驱动/工作台/矩阵工具都读同一档案）。
  - 预览场景不进收集组/不进包（既有口径）；矩阵产物进 `Artifacts/`（ignore 区）。
  - 语言切换不得引入运行时静态：`LocalizationService` 实例由驱动持有、注入 binder（B0 静态文案 binder 的 Bind 语义）。
  - 既有 `UiPreviewEditModeTests`（6 例，`ui.workbench` 在册断言）回归必须全绿。

## 候选方案与取舍

| 方案 | 核心思路 | 优点 | 代价/风险 | 结论 |
|---|---|---|---|---|
| A 最小面 | 只做设备档案＋安全区模拟＋工作台下拉；分辨率/语言/矩阵不做（提示手动） | 改动最小 | "预览矩阵"核心承诺（语言/长文本/截图）落空，B3 名不副实 | 不推荐 |
| B 完整布局档矩阵（推荐） | 设备档案＋驱动升级（分辨率〔反射容错〕/安全区/语言/伪本地化/字体缩放）＋批量截图矩阵＋工作台下拉 | 完整落 §7.1"布局档＋截图矩阵"；交接证据自动化；数据档/真链路边界清晰 | GameView 反射需容错降级；批量截图需编辑器协程状态机（实现量中等） | **推荐** |
| C 复用编辑器内建 Device Simulator | GameView 切 Simulator 模式用内置设备库 | 官方模拟（notch/DPI/物理参数齐） | 2022.3 内建但**无公开脚本切换面**（矩阵自动化仍需反射内部窗口）；设备库是编辑器资产非项目单源（页面清单 `DeviceProfiles`/工作台无法引用其 ID）；与"档案被三方共用"的架构目标冲突 | 不推荐（自动化与单源双输） |

推荐方案与理由：**B**。①设计钦定的设备档六档＋预览矩阵都可以用自建 JSON 档案单源表达，且能被页面清单 `DeviceProfiles`/工作台/矩阵工具三方共用；②C 的官方模拟器脚本面弱，矩阵自动化仍需自建，还引入包依赖；③A 不兑现"矩阵"。

## 设计要点

- 接口/数据：
  - `UiDeviceProfiles.json`：`{ schemaVersion, profiles: [{ id, name, width, height, orientation(landscape|portrait), aspect, dpi(可选，信息性——布局档不消费，留运行期预算面), safeInsets {left,top,right,bottom}(px，相对该分辨率), notes, inMatrix(bool) }] }`——`aspect` 由宽高派生（校验器核对）；确定性＝按 id Ordinal 排序、LF、无时间戳；预置：`pc.16-9`(1920×1080)、`phone.19-5-9`(2436×1125，notch 横屏左右 inset)、`phone.20-9`(2408×1080)、`tablet.4-3`(2048×1536)、`tablet.16-10`(1920×1200)、`uw.21-9`(2560×1080)；竖屏预留 `phone.9-19-5.portrait` 等（`inMatrix=false`）。
  - 模型/校验（编辑器）：`UiDeviceProfileCatalog`（Load/Validate/确定性序列化——沿 `UiTokenCatalog` 禁静态缓存口径）＋ `UiDeviceProfileSchema`（JSON schema 文件，登记 UI系统 README）。
  - 安全区模拟：`PreviewSafeAreaStrategy : ISafeAreaStrategy`（编辑器 DevTool 侧）——Resolve 返回**当前屏幕像素空间**的 Rect（inset px × `Screen.当前宽高 / 设备档分辨率` 缩放——`SafeAreaReceiver.Apply` 按实际 Screen 归一，故降级〔分辨率未应用〕时归一锚点仍正确）。
  - 语言：`UiPreviewDriver` 构建 `LocalizationService`（tbltext 直载已核实——`AnimationProfileTableSource` 全表字节模式天然覆盖；万一运行期路径不可用则降级：语言切换仅对 `LTextKeyRef` 的 key 显 `[key]`（缺表语义）并在矩阵报告标注）＋伪本地化 `PseudoLocale` 变换器（实现 `ILocalizationService` 装饰器：加长 30%＋音标符）。
  - 字体缩放：`PreviewFontScale`（驱动内 helper：遍历页面 TMP 记录原值→乘档位→切档/退出恢复）。
  - GameView 分辨率（D2）：`UiPreviewGameView`（反射 `GameView.SetSize`，try/catch＋一次性 Warning 降级）。
  - 矩阵工具：`UiPreviewMatrix`（editor action）：输入（页 prefab 路径或页面清单 id、设备档 ids、语言集、长文本开关、字体缩放档）→ 协程状态机：开场景 Play→逐组合（设 GameView 尺寸/安全区策略/语言/缩放→等 2 帧稳定→`ScreenCapture.CaptureScreenshot`）→ 退出 Play 回原场景→写 `matrix.json`（组合/产物路径/降级记录）。工具登记 `ui.preview-matrix`（writes: none（Artifacts ignore 区）/outputs: Artifacts）。
  - 工作台：`UiWorkbenchWindow` 上下文条设备档下拉（读 catalog；选择存 `IWorkbenchSettings` 新键）；预览按钮把当前设备档/语言传给 `UiPagePreview.Open`（EditorPrefs 键扩展）。
- 关键流程：
  - 交互预览：工作台/菜单 → `UiPagePreview.Open(prefab, deviceProfileId, locale)` → Play → 驱动应用（分辨率/安全区/语言）→ OnGUI 工具条可切档（免重启）。
  - 矩阵：`ui.preview-matrix`（Hub/CLI）→ 状态机批量 → Artifacts 落图＋索引 → 报告（组合数/成功/降级）。
- 失败与边界处理：
  - GameView 反射失败→分辨率维度跳过（安全区/语言照跑），报告降级；矩阵索引记 `resolutionApplied=false`；
  - 表不可直载→语言降级 `[key]` 显示＋报告标注；伪本地化不依赖表（装饰 zh 串或 key 重复拼接）仍可用；
  - 中断（用户停 Play/编译）→ 状态机安全退出、已截组合保留、索引标 `interrupted`；
  - 安全区档 inset 全零（无 notch 档）→ 不注入策略（回归 Screen.safeArea）。

## 通用性与扩展性

- 根因：设备形态参数无单源 → 预览/清单/工作台各自拼凑；预览缺语言/长文本面 → 多端验收靠手工。
- 复用点：档案被三方消费（驱动/工作台下拉/矩阵工具）；`PreviewSafeAreaStrategy` 即未来真机安全区测试的同一策略接口实现；`PseudoLocale` 装饰器可被 P3 运行时调试直接复用。
- 扩展点：新增设备档＝改 JSON（零代码）；矩阵维度增（如减少动效）＝参数集加项；竖屏启用＝`inMatrix=true` 即入矩阵。
- 避免硬编码：六档预置值是**数据**（JSON 内），代码只读；分辨率/inset 不出现在 C# 字面量。

## 测试策略

- EditMode（新 `UiDeviceProfilesEditModeTests`）：档案可解析/校验零问题（预置档）；宽高比与分辨率一致性校验负例；inset 合法性负例（负值/超分辨率）；确定性排序/无时间戳；竖屏预留 `inMatrix=false`。
- EditMode（新 `UiPreviewMatrixEditModeTests`）：组合展开（页×档×语言×长文本）枚举正确；中断安全（状态机字段终态）；索引 JSON 结构；GameView 反射失败注入→降级路径（不抛）；伪本地化变换器（加长/音标/幂等）；字体缩放记录/恢复（TMP 原值还原）。
- EditMode（新 `UiPreviewLocaleEditModeTests`）：tbltext 直载可用性（或降级口径）；中/英取值；伪本地化装饰器。
- PlayMode（新 `UiPreviewDriverPlayModeTests`，仿 ui-theme-apply-playmode 通道）：驱动 apply/restore 一条链——注入安全区策略后 SafeAreaReceiver anchors 变化、撤销后回归 Screen.safeArea；字体缩放应用后 TMP fontSize 变、切档/销毁后原值恢复；语言切换后 LTextKeyRef 文案刷新、退出无残留（LogAssert 无错误）。
- 交互面：真 Play 预览的人工冒烟（`eval` 打开一次、切档日志）——自动化只证窗口/驱动对象与日志，视觉留人工（红线上"真机验证模拟人类操作"对编辑器预览工具的适用面＝可点性由夹具冒烟覆盖）。
- 先红：三套夹具在实现前落盘跑 `wf verify --red`（骨架空转断言失败）。

## 验收标准

- [ ] AC1（设备档案单源）：`UiDeviceProfiles.json` 上任（六横屏档＋竖屏预留，值含分辨率/inset/方向/入矩阵标记）；校验器负例齐（宽高比不符/inset 越界/ID 文法）；确定性（重读逐字节一致、按 id 排序、无时间戳）；驱动/工作台/矩阵工具三方读同一档案（无第二份分辨率字面量）。
- [ ] AC2（预览驱动升级）：布局档预览支持设备档切换（GameView 分辨率〔D2 容错〕＋安全区策略注入/撤销）、语言（中/英/伪本地化长文本）、字体缩放档（TMP 原值恢复）；切换免重启 Play；退出预览回进入前场景且无残留（策略/缩放/语言状态清零）——apply/restore 链有 PlayMode 夹具证（见测试策略）。
- [ ] AC3（截图矩阵）：`ui.preview-matrix` 对给定页产出组合截图（页×档×语言×长文本默认集）到 `Artifacts/PreviewMatrix/`＋`matrix.json` 索引（组合/分辨率应用结果/时间/降级记录）；中断安全；失败组合不中断整体；**产物不入库有机制**（`.gitignore` 增 `[Aa]rtifacts/`，`git status` 零污染验证）。
- [ ] AC4（工作台接线）：上下文条"设备档"真下拉（档案单源＋"跟随预览"缺省项，选择经 `IWorkbenchSettings` 持久）；预览按钮携当前选择打开；占位文案"P2-3"移除。
- [ ] AC5（边界与回写）：数据档/真链路/竖屏验收/减少动效/P4 面在设计文档与进度线明确登记为非目标；总设计 §7.3、§7.5（字体档 1.0/1.15/1.3 口径对齐）与工具链设计 §7 现行口径回写（§7.1 注记减少动效延后）；工具索引/toolchain.json 同步；既有 `UiPreviewEditModeTests` 等回归全绿。
- [ ] AC6（门禁）：新 EditMode 三套＋PlayMode 一套全绿；`.workflow/config.json` 增本批门禁；`toolchain.py check` 0 error；`wf verify` 全绿。

## 风险与回滚

- 风险：GameView 内部 API 反射在升版失效→容错降级已设计（矩阵仍可用、分辨率手动）；tbltext 直载若不可行→语言面降级口径已备；编辑器协程状态机复杂度→先单页小矩阵打样再放量。
- 监控/发现：矩阵索引记录每次降级/失败原因；`UiPreview` 相关夹具在门禁持续回归。
- 回滚：驱动/工作台改动均为增量开关（不选档＝旧行为）；档案/工具/夹具新文件独立可摘；预览场景不改动（零场景风险）。
