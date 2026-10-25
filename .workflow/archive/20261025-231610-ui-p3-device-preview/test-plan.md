# 测试计划：UI 系统 P2-3 B3：设备档与预览矩阵

> 运行 ui-p3-device-preview · 创建于 2026-10-25T19:47:59+08:00

## 测试策略

- 测试层级：
  - **EditMode 契约/纯函数**（`UiDeviceProfilesEditModeTests`、`UiPreviewLocaleEditModeTests`、`UiPreviewMatrixEditModeTests`、`UiWorkbenchDeviceEditModeTests`）：档案装载/校验/确定性、tbltext 直载、伪本地化、安全区换算、矩阵组合/索引/运行态、工作台偏好与选项面。纯函数不依赖 Play、可注入路径/参数。
  - **PlayMode apply/restore 链**（`UiPreviewDriverPlayModeTests`）：安全区策略注入→anchors 收缩→撤销回归；字体缩放应用→复位原值；语言绑定→静态文案刷新→复位解绑无残留。真实 `SafeAreaReceiver`/`TMP_Text`/`LTextKeyRef` 组件。
  - **集成实产**（T3 验收）：真跑一页小矩阵，核对 `Artifacts/PreviewMatrix/<页>/` 截图与 `matrix.json`、`git status` 零污染——产物/中断/失败隔离的运行时证据，不放进门禁（设计 §7.1：交接证据，非门禁替代）。
  - **人工视觉**：预览工具条开关/观感留人工一次确认；自动化只证对象/状态/日志。
- 关键风险与覆盖重点：
  - GameView 内部 API 反射失败 → `TrySetSize` 不抛、降级记录 `resolutionApplied=false`（矩阵索引断言）；
  - tbltext 直载不可用 → 直载用例硬断言非空（不做 `Assume` 跳过），迫使实现给出可用路径；若最终不可行必须改验收口径而非弱化测试；
  - 骨架期弱用例：所有“集合为空则 All() 恒真”的断言已补非空前置（设备档案校验/确定性、矩阵排除非入矩阵档）；
  - 失败隔离/中断：`UiPreviewMatrixRun` 运行态用例覆盖“单组合失败→CompletedWithFailures”“中断→Interrupted 且保留已截组合”。

## 用例清单

| 用例 | 层级 | 关联任务/验收 | 输入或步骤 | 预期结果 |
|---|---|---|---|---|
| 装载_六横屏档入矩阵且竖屏预留 | EditMode | T1 / AC1 | 载入真实 `UiDeviceProfiles.json` | ≥6 横屏档且全入矩阵；竖屏档存在且 `inMatrix=false` |
| 装载_钦定宽高比全覆盖 | EditMode | T1 / AC1 | 载入真实档案，取入矩阵档宽高比 | 16:9、19.5:9、20:9、4:3、16:10、21:9 全覆盖 |
| 校验_预置档零问题 | EditMode | T1 / AC1 | 真实档案 → `Validate` | 档案非空且零问题 |
| 校验_宽高比与分辨率不符报错 | EditMode | T1 / AC1 | 1920×1080 声明 Aspect=1.5 | 返回非空问题集 |
| 校验_inset负值或越界报错 | EditMode | T1 / AC1 | Left=-10 / Right=600（宽 1000） | 返回非空问题集 |
| 校验_ID文法非法报错 | EditMode | T1 / AC1 | Id="Bad Id!" | 返回非空问题集 |
| 确定性_重读一致且按id排序无时间戳 | EditMode | T1 / AC1 | 两次 Load+Serialize；检查 id 顺序 | 规范文本非空、含 profiles、逐字节一致、无时间字段、按 id Ordinal |
| TryGet_已知与未知 | EditMode | T1 / AC1 | 查 `pc.16-9` 与不存在 id | 已知命中且宽 1920；未知 false |
| MatrixProfiles_只含入矩阵档 | EditMode | T1 / AC1 | 真实档案 | 非空且全为 `InMatrix` |
| 直载_服务非空且中文可用 | EditMode | T2 / AC2 | `UiPreviewLocale.BuildService()` + 真实 key `UI.Login.Title` | 服务非空；中文非 key、非 `[key]` |
| 直载_英文切换可用 | EditMode | T2 / AC2 | 同上，切 `en` | 服务非空；中英取值不同 |
| 直载_表缺失返回空并降级 | EditMode | T2 / AC2 | `BuildService("Assets/NoSuchDir/")` | 返回 null（调用方降级并报告） |
| 状态词_off_ok_unavailable | EditMode | T2 / AC2 | `StatusFor` 三组输入 | off/unavailable/ok 映射正确 |
| 伪本地化_加长且区分音标 | EditMode | T2 / AC2 | `PseudoLocale` 包装真实服务 | 长度 ≥ ceil(原文×1.3)；内容变化 |
| 伪本地化_事件与缺key透传 | EditMode | T2 / AC2 | 假服务触发语言事件 | `OnLocaleChanged`/`MissingKeyCount` 透传（骨架期即应通过，防回归） |
| 安全区策略_当前屏幕像素空间换算 | EditMode | T2 / AC2 | 2436×1125 档、左右 inset 132 | insets 按 `Screen` 宽高比例缩放；上下无边 |
| 展开_组合数与确定性顺序 | EditMode | T3 / AC3 | 2 入矩阵档 | 2×3×3=18；档位按 id Ordinal |
| 展开_排除非入矩阵档 | EditMode | T3 / AC3 | 1 入矩阵档+1 竖屏 | 组合非空且为 9；不含竖屏档 |
| 计划_默认产物目录与组合 | EditMode | T3 / AC3 | Page_Login + 1 档×1 语言×1 字体档 | 计划通过；产物目录默认 `Artifacts/PreviewMatrix/Page_Login`；1 组合 |
| 计划_产物目录逃逸拒绝 | EditMode | T3 / AC3 | `Artifacts/../Assets/PreviewMatrix` | 拒绝并提示不能包含 `..` |
| 计划_非法语言拒绝 | EditMode | T3 / AC3 | 语言 `fr` | 拒绝并提示不支持的语言 |
| 语言不可用_仅非空语言且状态unavailable | EditMode | T3 / AC3 | 空/en+pending/en+unavailable/en+ok | 仅 en+unavailable 判定为不可用 |
| 运行_语言失败保留分辨率结果 | EditMode | T3 / AC3 | 失败记录携带 resolutionApplied=true | 索引 `resolutionApplied=true` 且含失败原因 |
| 文件名_确定性与平台安全 | EditMode | T3 / AC3 | 同组合/不同语言 | 无空格、`.png` 结尾、确定性、不同组合不同名 |
| 索引_结构含分辨率结果与降级记录 | EditMode | T3 / AC3 | 组合+分辨率表+降级记录 | 含页名、`resolutionApplied`、降级文本 |
| 运行_失败组合不中断且索引留痕 | EditMode | T3 / AC3 | 3 组合：成功/失败/成功 | 终态 CompletedWithFailures；成功 2/失败 1；索引含失败原因与页名 |
| 运行_中断终态保留已完成组合 | EditMode | T3 / AC3 | 成功 1 → MarkInterrupted | 终态 Interrupted；成功列表保留 1；索引含 interrupted |
| 字体缩放_应用与原值恢复 | EditMode | T2 / AC2 | TMP fontSize=20，1.3→1.0→Restore | 26→20→20（记录原值恢复，非逆乘） |
| 偏好_内存实现设备档键往返 | EditMode | T4 / AC4 | 内存 settings | 缺省空串；写入可读回（T4 预接，红期通过） |
| 偏好_EditorPrefs实现往返并回落默认 | EditMode | T4 / AC4 | EditorPrefs settings；测试后恢复 | 键名常量单源；可读回 |
| 选项_档案驱动且首项为跟随预览 | EditMode | T4 / AC4 | 真实档案 → `DeviceProfileOptions` | 首项空串；其余=入矩阵档数；含 `pc.16-9` |
| 安全区_注入后anchors收缩_撤销后回归全屏 | PlayMode | T2 / AC2 | 真实 `SafeAreaReceiver` + driver | 注入后 anchorMin/Max 收缩；撤销回归全屏 |
| 字体缩放_应用后放大_复位回原值 | PlayMode | T2 / AC2 | 真实 `TextMeshProUGUI` + driver | 1.15 档 20→23；复位 20 |
| 语言_绑定后静态文案刷新_复位解绑无残留 | PlayMode | T2 / AC2 | `LTextKeyRef` + FakeLocale + driver | 绑定后取值；复位后语言变化不再影响 |

## 预期失败证据

- 命令：`python .agents/skills/staged-delivery/scripts/workflow.py verify --red --run ui-p3-device-preview`（等价于 `.workflow/config.json` 五条 red_commands）
- 观察到的失败（2026-10-25T21:31，`evidence/red.json`）：
  - `ui-device-profiles-red`：**9/9 失败**（断言失败）
  - `ui-preview-locale-red`：**4/5 失败**（1 例透传守卫在骨架期通过）
  - `ui-preview-matrix-red`：**7/7 失败**（断言失败）
  - `ui-workbench-device-red`：**1/3 失败**（2 例偏好往返已在 T4 预接，骨架期通过）
  - `ui-preview-driver-red`：**3/3 失败**（断言失败）
- 为什么这是正确的失败：失败均来自断言（Expected/But was），非编译错误/环境错误；此前一版 red 证据因编辑器主线程死锁在 `ensure_compiled` 阶段退出而无效，已重跑覆盖。
- **评审补测（实现后补入，非红期用例）**：`直载_表缺失返回空并降级`、`状态词_off_ok_unavailable`、`计划_默认产物目录与组合`、`计划_产物目录逃逸拒绝`、`计划_非法语言拒绝`、`语言不可用_仅非空语言且状态unavailable`、`运行_语言失败保留分辨率结果` 七例在独立代码评审后补入，用于把「语言不可用不假绿」「产物路径不逃逸」「失败不覆盖分辨率结果」变成回归守卫；由全量 `wf verify` 覆盖。PlayMode 语言用例扩展为「绑定→切回作者文本→再绑定→复位」四段。

## 运行方式

- 全量测试：`python .agents/skills/staged-delivery/scripts/workflow.py verify --run ui-p3-device-preview`
- 仅运行新增测试：
  - `python scripts/gate/unity-tests.py --mode editor --filter UiDeviceProfilesEditModeTests`
  - `python scripts/gate/unity-tests.py --mode editor --filter UiPreviewLocaleEditModeTests`
  - `python scripts/gate/unity-tests.py --mode editor --filter UiPreviewMatrixEditModeTests`
  - `python scripts/gate/unity-tests.py --mode editor --filter UiWorkbenchDeviceEditModeTests`
  - `python scripts/gate/unity-tests.py --mode playmode --filter UiPreviewDriverPlayModeTests`
- 集成实产：`ui.preview-matrix` 对 `Page_Login` 真跑一页小矩阵（1 档×2 语言×1 字体档），核对 `Artifacts/PreviewMatrix/` 与 `matrix.json`；`git status` 零污染。

## 豁免与说明

- **GameView 分辨率真切换**不在 EditMode 断言（内部 API + 依赖 GameView 状态）；`TrySetSize` 只保证不抛/可降级，真应用结果由矩阵实跑索引 `resolutionApplied` 与人工预览核对。
- **语言不可用实跑**通过 `LocaleDataDirKey` 注入无效目录：`matrix.json` 期望 `status=CompletedWithFailures`、`degraded` 含语言失败、该组合 `captured=false` 且 `resolutionApplied=true`（已实跑，产物不入库）。
- **截图视觉正确性**不做像素比对（无基线）；矩阵产出用于人工审阅，属交接证据。
- **竖屏预览/减少动效/数据档**不在本批（见验收 AC5 非目标）。
