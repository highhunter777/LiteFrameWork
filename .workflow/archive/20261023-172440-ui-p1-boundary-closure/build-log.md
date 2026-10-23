# 施工记录（build-log）

## 施工记录

### 红测阶段（先写测试）

- 7 个新 EditMode 夹具 + 2 个既有夹具改造 + 1 个 PlayMode 夹具（施工期落地）：目标语义先落盘；
  规则/扫描器以「端口真身＋空实现」骨架保证编译，负例用例因断言失败转红。
- `wf verify --red`：**PASS（已观察到预期失败）**——新增 7 条 red 命令全部 exit=1（断言失败，
  形如 `Expected: 1 But was: 0`）；既有 10 条 red 命令通过（混合形态＝有效红证据，P1-4 先例口径）；
  编译门禁先行转绿（refresh 0 错）。
- 红期实测：`ui-validation-core-red` 的失败信息同时暴露了「文本规则默认构造器＋种子注入」会把
  真实 Lua 目录键误判缺 key——测试接线改为注入空扫描根（资产面用例不混真实 Lua 面）。

### 实现阶段（按任务序）

- **T1 地基**：`UiRuleIds` 追加 13 个规则 ID；`UiRuleThresholds` 单源（raycastTarget 预算 60、
  布局深度上限 4、富文本白名单 b/i/color/size、Lua 受控 API 名单 23 个、事件 API→事件名映射）。
- **T5 Lua 引用扫描器**：`UiLuaReferenceScanner`——单行 `self.ui:Api(…)` 调用点提取
  （括号配对跳字符串、闭包深度配对；名字型第一参须字面量）、事件接线提取＋handler 尽力解析
  （匿名闭包内首个 `self:方法(`，解析不到记 anonymous）、字面量全文匹配（覆盖 `_Begin("Button_X")`
  变量中转）、`TryResolveLuaFile`（`UI.<名>` → 起页 Lua 目录）。**6/6 绿**。
- **T2 文本族**：`TextStaticKeyRule`（LTextKeyRef＋Lua SetTextKey* 两路 key 对账，表读失败显式 Error）、
  `TextPlaceholderRule`（静态键挂带参条目 Error／SetTextKey 带参 Error／SetTextKeyArgs 参数数不符 Warning）、
  `TextLocaleColumnRule`（Zh 空 Error／En 空 Warning）、`TextRichTextRule`（标签白名单 Warning）。
  表数据面＝`UiValidationContext.LTextSeed` 种子注入＋`UiLTextTable.TryGetEntries` 视图。
  **7/7 绿**（含接线修正后复跑）。
- **T3 样式族**：`StyleColorDriftRule`（复用 `UiStyleTool.ReportDrift`——非 token 色 Warning）、
  `StyleTextDriftRule`（复用 `ReportTextDrift`＋**`ReportBrokenRefs` 两路合并**——Unmarked/Mismatch Warning、
  BrokenRef Error）。发现并修复：BrokenRef「无文本组件」在 `ReportTextDrift` 之外的独立补查 API 里——
  规则合并两路后 **4/4 绿**。
- **T4 布局/性能**：`LayoutScalerRule`（Page_* 根参数偏离一条汇总 Error＋子树第二套 Error；
  组件缺失归结构族不重复报）、`PerformanceRaycastTargetRule`（单条预算 Warning）、
  `PerformanceLayoutDepthRule`（同轴嵌套链深度超限单条 Warning）、`PerformanceAnimatorRule`
  （页面根 Error／其余 Warning）。**6/6 绿**。
- **T7 契约生成器补全**：`ApplyManifestEditorial`（owner/designRef 投影，元数据不入 hash）、
  `ApplyEvents`（events[] 稳定排序＋sourceHash 重算，幂等）；`UiContractBatch.GenerateRegistered(…, manifest)`
  全链（表行身份＋清单维护字段＋Lua 接线）＋`Regenerate(entry)`（漂移对账判据单源）；
  `Generate/Diff` 批量入口全部走清单感知路径。**11 契约重生成入库**：owner=art.ui 全投影、
  Page_Login events=2（`Button_GuestLogin.onClick→_Login`、`Button_Settings.onClick→anonymous`）、
  Page_Lobby=5／Page_Room=2／Page_Settings=5；UIMain 跳过＋Tmp_Dialog 多行告警（既有口径不变）。
- **T6 契约族规则**：`ContractDriftRule`（清单 `contractPresent` 页逐页 `Regenerate` 重生成逐字节比较——
  含手改检出；缺契约归 `ui.page-manifest` 不重复报）、`ContractReferenceRule`（required 未消费 Warning
  ／调用点名不在契约面 Error——面＝controls∪text 节点名）、`ContractTextDoubleWriteRule`（静态文案节点被
  `SetText*` 再写 Error）。
- **T8 遍历统一**：`BindSurfaceWalk`（LiteCodeGen.Editor 核心——clone 作用域/嵌套 BindRoot/模板边界标记，
  路径口径与既有 `RelativePath` 逐字节一致）；`UiBindSurfaceWalker` 变委托包装（公共 API 不变，
  hash 域零扰动）；`BindSearch.Search` 变「核心遍历＋生成器域适配」（根不入/Clone_/模板边界不入；
  标识符校验/重名收集/OrderIndex 保留）。顺手修审计 L268：关键字表补 `partial`。
- **T9 结构族扩覆盖**：`PageStructureRule` 三类口径（Page 四件套＋SafeArea／Hud 三件（射线按需）／
  Screens Tmp_ 系统面（Canvas+CanvasGroup、**禁** CanvasScaler、射线按需）；Widgets Tmp_ 不辖）。
  制作规范 §3.1/§3.3 同批改文对齐（用户裁决 A）。
- **T10 行为族**：`UiBehaviorPlayModeTests` 三用例（复用复位/语言切换/模态射线穿透——
  回归钉既有框架契约 `PrepareForShow`/`OnLocaleChanged`/`UIModalStack.RecomputeBlocking`）。
- **T11 收尾接线**：13 规则注册进 `UiValidator.Rules`；`Families` 十族全部翻绿（各族 Note 列明
  未落地子项与原因——令牌 SO/缺字/设备档/图集/Player 侧/焦点）；真实面门禁更新为校准钉
  （Error=0；Warning 基线 303＝颜色 161＋未标记 141＋UIMain 根名债 1；文本/契约对账/布局/性能零问题）。
- 2026-10-23T15:40:57+08:00 · 完成任务 T5 · 证据: UiLuaReferenceScannerEditModeTests 6/6 通过（fx 日志）；扫描器供契约生成与校验共用
- 2026-10-23T15:41:18+08:00 · 开始任务 T2「文本族规则接入（4 规则）」
- 2026-10-23T15:41:18+08:00 · 完成任务 T2 · 证据: UiTextRuleEditModeTests 7/7 通过（接线修正后复跑 fx-text.log）
- 2026-10-23T15:41:18+08:00 · 开始任务 T3「样式族规则接入（复用两工具对账）」
- 2026-10-23T15:41:19+08:00 · 完成任务 T3 · 证据: UiStyleRuleEditModeTests 4/4 通过（合并 ReportBrokenRefs 补查路后 fx2-style.log）
- 2026-10-23T15:41:19+08:00 · 开始任务 T4「布局/性能静态规则接入（4 规则）」
- 2026-10-23T15:41:19+08:00 · 完成任务 T4 · 证据: UiLayoutPerformanceRuleEditModeTests 6/6 通过（fx-layout.log）
- 2026-10-23T15:41:19+08:00 · 开始任务 T7「契约生成器补全与 11 契约重生成」
- 2026-10-23T15:41:19+08:00 · 完成任务 T7 · 证据: UiContractReconciliationEditModeTests 9/9（含真实 Page_Login 端到端 owner/events 断言）+ UiToolchainWiringEditModeTests 8/8 回归；11 契约重生成入库（owner 全投影、events 接线、逐字节确定性）
- 2026-10-23T15:41:19+08:00 · 开始任务 T6「契约族补全规则（漂移/引用对账/双写）」
- 2026-10-23T15:41:19+08:00 · 完成任务 T6 · 证据: UiContractReconciliationEditModeTests 9/9（漂移/引用对账/双写负例全绿）；真实面引用对账零问题（校准探针 Error=0）
- 2026-10-23T15:41:20+08:00 · 开始任务 T8「绑定遍历统一（核心下沉+BindSearch 适配）」
- 2026-10-23T15:41:20+08:00 · 完成任务 T8 · 证据: BindSearchUnificationEditModeTests 7/7 + UiBindingToolsEditModeTests 12/12 hash 回归；partial 关键字已入表（审计 L268）
- 2026-10-23T15:41:20+08:00 · 开始任务 T9「结构族扩覆盖与制作规范对齐」
- 2026-10-23T15:41:20+08:00 · 完成任务 T9 · 证据: UiValidationRulesEditModeTests 10/10（HUD/Tmp_ 新口径用例 + 既有例）；制作规范 §3.1/§3.3 改文同批；真实面零 Error（校准探针）
- 2026-10-23T15:41:20+08:00 · 开始任务 T10「行为族 PlayMode 夹具与门禁接线」
- 2026-10-23T15:41:20+08:00 · 完成任务 T10 · 证据: UiBehaviorPlayModeTests 3/3（test_status 终态+TestResults.xml）；gate 脚本 PlayMode 异步通道端到端 3/3 passed；ui-behavior-playmode 命令已入 config
- 2026-10-23T15:41:20+08:00 · 开始任务 T11「收尾接线：Families 翻绿+门禁命令+真实面校准」
- 2026-10-23T15:41:21+08:00 · 完成任务 T11 · 证据: UiValidationEditModeTests 14/14（十族全接入门禁+真实面校准钉 Error=0/Warning=303）；Families 十族翻绿；config 增 10 命令
- 2026-10-23T15:43:35+08:00 · 开始任务 T12「文档回写：审计登记与施工进度新章」
- 2026-10-23T15:43:35+08:00 · 完成任务 T12 · 证据: 审计修复登记 5 行回写（L247/L268/L252＋两项新登记，不删原条目）；UI系统线.md 第6章（状态/交付/验证证据/边界如实含 UIMain 冻结决策与 Warning 基线 303）+ 索引行
- 2026-10-23T16:37:55+08:00 · 新增任务 T13「门禁稳定性：页面清单夹具临时目录唯一化」
- 2026-10-23T16:37:55+08:00 · 新增任务 T14「生成物行尾锁定：UI 工具 JSON 固定 LF」
- 2026-10-23T16:38:00+08:00 · 开始任务 T13「门禁稳定性：页面清单夹具临时目录唯一化」
- 2026-10-23T16:38:01+08:00 · 开始任务 T14「生成物行尾锁定：UI 工具 JSON 固定 LF」
- 2026-10-23T16:38:21+08:00 · 完成任务 T14 · 证据: git check-attr: Contracts/** 与 UiTools/*.json 均为 text/eol=lf；checkout-index 模拟全新检出 CRLF=0（core.autocrlf=true）
- 2026-10-23T16:38:31+08:00 · 新增任务 T15「gate 脚本：console_status 空结果重试并失败关闭」
- 2026-10-23T16:38:32+08:00 · 开始任务 T15「gate 脚本：console_status 空结果重试并失败关闭」
- 2026-10-23T16:47:02+08:00 · 完成任务 T13 · 证据: UiPageManifestEditModeTests 连续两次 15/15（OneTimeSetUp/TearDown + 陈旧裸目录清理）；临时目录收尾干净
- 2026-10-23T16:47:02+08:00 · 完成任务 T15 · 证据: ensure_compiled 对 console_status 空结果重试 3 次、仍缺失则红（不再 AttributeError/假绿）；随后两次夹具运行均经该路径正常完成
- 2026-10-23T17:15:59+08:00 · 新增任务 T16「gate 通道统一：EditMode/PlayMode 全异步」
- 2026-10-23T17:15:59+08:00 · 开始任务 T16「gate 通道统一：EditMode/PlayMode 全异步」
- 2026-10-23T17:16:00+08:00 · 完成任务 T16 · 证据: UiWidgetManifestEditModeTests 异步通道 11/11 passed（此前同步通道被 30s 传输超时取消）；test_status 终态含 cancelled/interrupted/error 判红

## 关键决定

1. **样式族数据源**：复用 `UiStyleTool`/`UiTextStyleTool` 对账函数（含 `ReportBrokenRefs` 补查路），
   规则只做「对账结果 → UiIssue」投影——零第二份阈值/遍历（单源纪律）。
2. **遍历核心落点**：`LiteCodeGen.Editor`（`LiteGame.Editor` 已引用之，零新依赖边；反向不可行）。
3. **未消费判定**＝bindName 字面量全文匹配（实证 `_Begin("Button_X")` 变量中转形态；动态构造名不识别如实登记）；
   **越界面**＝controls bindName ∪ text 节点名（`self.ui` 按名寻址依赖 BindNode——名字不在契约面即运行时取不到）。
4. **漂移判据单源**：`UiContractBatch.Regenerate(entry)`——身份取清单 forms[0]（清单/表同步由
   `ui.page-manifest` 规则另辖），维护字段/events 同批量口径；规则与批量共用一份「什么算与盘上一致」。
5. **真实面校准**：探针两轮（重生成前 Error=11 全为契约漂移→重生成后 Error=0）；Warning 基线如实钉进门禁
   （样式债可见不阻断——SHOULD 级设计口径）。

## 偏差与处理

- 连跑命令首版把重定向挂在 `&&` 链尾（cmd 只重定向最后一条）＋链因夹具失败短路——前三夹具输出丢失；
  改为逐命令独立日志＋`&` 不短路重跑取证（无代码影响，纯取证补拍）。
- `UiLTextTable.TryGetEntries` 首版插进类中部（构造点后）——功能正确但阅读序欠佳，保留（方法在 Reload 后
  的相邻读写方法区，可接受）。

## 问题与修复

| 问题 | 修复 |
| --- | --- |
| 文本夹具 1 例红：默认构造器扫真实 Lua 目录＋种子表误判真实 key 缺失 | 测试接线注入空扫描根（资产面用例与真实 Lua 面隔离） |
| 样式夹具 1 例红：BrokenRef（无文本组件）未报 | 规则合并 `ReportBrokenRefs` 补查路（该分类在独立 API） |
| 编译错 3 轮：TMP using/const 初始化/UiRuleFamily.Note 字段名/委托元组迭代类型 | 逐处修正后 refresh 0 错 |
| 脚本 WMI 查询命令行不可用（System.Management 不在脚本引用面） | 改用 tasklist + 日志/证据文件轮询（不依赖进程命令行） |

## 阶段流转
- 2026-10-23T15:43:35+08:00 · 完成「正式施工」→ 「测试代码」
- 2026-10-23T17:23:24+08:00 · 完成「测试代码」→ 「评审代码」
- 2026-10-23T17:23:56+08:00 · 完成「评审代码」→ 「回写与收尾」
- 2026-10-23T17:24:37+08:00 · 完成「回写与收尾」→ 完成

## 验证结果
- 2026-10-23T16:00:42+08:00 · 验证 FAIL · 28/29 · 证据 evidence/verify.json
- 2026-10-23T16:23:38+08:00 · 验证 FAIL · 28/29 · 证据 evidence/verify.json
- 2026-10-23T16:34:10+08:00 · 验证 FAIL · 28/29 · 证据 evidence/verify.json
- 2026-10-23T17:23:03+08:00 · 验证 PASS · 29/29 · 证据 evidence/verify.json

### 失败根因与收口（三次 FAIL 均为环境/门禁基础设施，不是规则实现）
- 16:00 FAIL：`ui-behavior-playmode` 600s 仍 running——上一轮测试被中止后 `Temp/pipeline_test_request.json` 残留，`test_status` 永远报 running；`cancel_tests` 复位后单跑 3/3 通过。
- 16:23 FAIL：同一条命令仍被中止残留状态拖住；同时发现 Unity 主窗口被最小化后编辑器主循环挂起（CPU 近零/心跳仍更新/无对话框），恢复窗口后恢复响应。
- 16:34 FAIL：`UiPageManifestEditModeTests` 一例 `.meta` 写锁（临时目录复用竞态）；随后 `unity-tests.py` 的 `console_status` 空结果触发 `AttributeError`。修复见 T13/T15。
- 17:23 PASS：门禁统一异步通道后 29/29 全绿，PlayMode 3/3。

## 交付摘要

P1 边界收口完成：五族校验（文本/样式/布局/性能/契约）13 条规则接入、Lua 引用扫描器、契约 owner/designRef 投影与 events[] 填充、生成器/遍历统一、结构族扩覆盖；P1 六个子项与五项待接边界全部落地。

验证阶段补齐三项收口基础设施：
1. **行尾锁定**：`.gitattributes` 固定 UI 工具 JSON 为 LF，根治 `core.autocrlf=true` 下契约漂移全量误报。
2. **夹具稳定**：页面清单夹具改 OneTime 生命周期＋陈旧裸目录清理＋负例独立输出名，连续两次 15/15。
3. **门禁通道**：EditMode/PlayMode 统一 `run_tests --async_tests` + `test_status` 轮询；`cancelled/interrupted/error` 与 0 用例判红，长夹具不再被 30s 传输超时截断/假绿；`console_status` 空结果重试后失败关闭。

## 未决事项

- **C-4 生成器域嵌套 prefab 实例根**：`BindSearch` 现跳过所有嵌套 prefab 实例根（含非 `Tmp_`）；设计口径为模板边界不透明，真实面零 BindRoot、生成文本回归绿，保持现语义并在评审记录。
- **C-5 模板 bindingHash 不含根 BindNode**：根绑定模板契约 hash 恒为同一值；受「既有 hash 逐位不变」约束未修，已登记审计新债。
- **C-6 损坏清单 fail-open**：`UiContractBatch.TryLoadManifest` 把损坏当缺失；`ui.page-manifest` 会显式 Error，已登记另批改 fail-fast。
- **C-7 多行主行口径**：批量取表行首行、漂移重生成取清单 `forms[0]`；当前 Tmp_Dialog 未触发，已登记另批统一。
- **Warning 基线 303**（颜色 161 + 未标记 141 + UIMain 1）仍是样式债度量，整改后随批更新门禁钉。
- **本批工作区未提交**；是否提交由用户决定。

## 文件清单

- 校验：`Assets/LiteGame/Editor/UiTools/Validation/UiRuleIds.cs`、`UiRuleThresholds.cs`、`UiValidator.cs`、`IUiRule.cs`、`Rules/{Text*,Style*,Layout*,Performance*,Contract*,PageStructure}*.cs`、`Assets/LiteGame/Editor/UiTools/Reference/UiLuaReferenceScanner.cs`、`Text/UiLTextTable.cs`
- 契约/遍历：`UiContractGenerator.cs`、`UiContractBatch.cs`、`11× Contracts/*.contract.json`、`LiteCodeGen/Editor/EditorTools/BindSurfaceWalk.cs`、`BindSearch.cs`、`UiBindSurfaceWalker.cs`
- 测试：`Assets/Tests/EditMode/{UiTextRule,UiStyleRule,UiLayoutPerformanceRule,UiLuaReferenceScanner,UiContractReconciliation,BindSearchUnification}EditModeTests.cs`、`UiBehaviorPlayModeTests.cs`、`UiPageManifestEditModeTests.cs`、`UiValidationEditModeTests.cs`、`UiValidationRulesEditModeTests.cs`、`UiBindingToolsEditModeTests.cs`
- 工具/配置：`scripts/gate/unity-tests.py`、`.gitattributes`、`.workflow/config.json`
- 文档：`Docs/设计文档/客户端/UI系统/{UI编辑器工具链专项设计,UI制作规范}.md`、`Docs/审计/代码质量审计-客户端.md`、`Docs/施工进度/UI系统线.md`

## 后续建议

- 先把本批与 `ui-p1-boundary-closure` 归档一并提交，避免契约生成物与规则代码分叉。
- 下一批优先处理 C-5/C-6/C-7 三项单源问题（都与契约/hash 判定可信度相关），再进入 P2 工作台与起页/登记拆分。
- 样式债 303 条单独开整改批；整改时同步更新 `UiValidationEditModeTests` 的校准钉。

