# 实施任务清单：UI 系统 P1 边界收口：五族校验接入+契约补全+遍历统一

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 16/16（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | 地基：规则 ID 常量与阈值单源 | - | 完成 | UiRuleIds 追加 13 个新规则 ID 常量；新增 UiRuleThresholds 单源（raycastTarget 预算、布局深度上限、富文本标签白名单、Lua 受控 API 名单），编译零错 | unity_editor.refresh 0 错 | UiRuleIds 追加 13 个规则 ID 常量 + UiRuleThresholds 单源（预算/深度/富文本白名单/Lua 受控 API 名单/事件映射）已落盘；unity_editor.refresh 编译 0 错（12 条警告均… |
| T2 | 文本族规则接入（4 规则） | T1 | 完成 | ui.text.static-key/placeholder/locale-column/rich-text 四规则注册进 UiValidator，读 tbltext 真表，表读失败显式 Error；红测夹具 UiTextRuleEditModeTests 全绿且先红证据在案 | python scripts/gate/unity-tests.py --mode editor --filter UiTextRuleEditModeTests | UiTextRuleEditModeTests 7/7 通过（接线修正后复跑 fx-text.log） |
| T3 | 样式族规则接入（复用两工具对账） | T1 | 完成 | ui.style.color-drift/ui.style.text-drift 两规则注册，复用 UiStyleTool.ReportDrift 与 UiTextStyleTool.ReportTextDrift 单源，级别映射 Warning/Warning/Error；夹具 UiStyleRuleEditModeTests 全绿且先红证据在案 | python scripts/gate/unity-tests.py --mode editor --filter UiStyleRuleEditModeTests | UiStyleRuleEditModeTests 4/4 通过（合并 ReportBrokenRefs 补查路后 fx2-style.log） |
| T4 | 布局/性能静态规则接入（4 规则） | T1 | 完成 | ui.layout.scaler/ui.performance.raycast-target/layout-depth/animator 四规则注册，阈值取 UiRuleThresholds 单源；夹具 UiLayoutPerformanceRuleEditModeTests 全绿且先红证据在案 | python scripts/gate/unity-tests.py --mode editor --filter UiLayoutPerformanceRuleEditModeTests | UiLayoutPerformanceRuleEditModeTests 6/6 通过（fx-layout.log） |
| T5 | Lua 引用扫描器（引用对账数据面） | - | 完成 | 新增 UiLuaReferenceScanner：受控 API 调用点提取（名字+API+handler 尽力解析）与 bindName 字面量全文匹配，纯函数可注入合成源；真实 6 个 Lua 页提取清单记录进施工记录；夹具用例全绿且先红证据在案 | python scripts/gate/unity-tests.py --mode editor --filter UiLuaReferenceScannerEditModeTests | UiLuaReferenceScannerEditModeTests 6/6 通过（fx 日志）；扫描器供契约生成与校验共用 |
| T6 | 契约族补全规则（漂移/引用对账/双写） | T5, T7 | 完成 | ui.contract.drift/reference/text-double-write 三规则注册：重生成对账含手改检出、required 未消费 Warning、越界 Error、LTextKeyRef 节点被 Lua SetText* 再写 Error；真实面零 Error 且基线清单记录；夹具 UiContractReconciliationEditModeTests 全绿且先红证据在案 | python scripts/gate/unity-tests.py --mode editor --filter UiContractReconciliationEditModeTests | UiContractReconciliationEditModeTests 9/9（漂移/引用对账/双写负例全绿）；真实面引用对账零问题（校准探针 Error=0） |
| T7 | 契约生成器补全与 11 契约重生成 | T5 | 完成 | GenerateRegistered/UiContractBatch 从页面清单投影 owner/designRef（缺条目 null+告警）；CollectEvents 从 Lua 扫描器填充 events[]（bindName/event/handler 稳定排序）；11 契约重生成入库、text/controls 不变、owner 全投影、Page_Login events 含两钮接线、重复生成逐字节一致；夹具 UiContractReconciliationEditModeTests 与 UiContractGeneratorEditModeTests/UiToolchainWiringEditModeTests 回归全绿 | python scripts/gate/unity-tests.py --mode editor --filter UiContractGeneratorEditModeTests 与 UiToolchainWiringEditModeTests（契约基线断言随重生成同步更新后须复绿） | UiContractReconciliationEditModeTests 9/9（含真实 Page_Login 端到端 owner/events 断言）+ UiToolchainWiringEditModeTests 8/8 回归；11… |
| T8 | 绑定遍历统一（核心下沉+BindSearch 适配） | - | 完成 | 遍历核心下沉 LiteCodeGen.Editor（BindSurfaceWalk），UiBindSurfaceWalker 委托核心（公共 API 不变、既有 hash 12/12 回归绿），BindSearch.Search 变核心遍历+生成器域适配（标识符校验含 partial 关键字、重名收集、OrderIndex 保留）；合成层级新语义用例先红后绿；生成文本逐字节不变回归；审计 L247/L268 修复登记回写 | python scripts/gate/unity-tests.py --mode editor --filter BindSearchUnificationEditModeTests | BindSearchUnificationEditModeTests 7/7 + UiBindingToolsEditModeTests 12/12 hash 回归；partial 关键字已入表（审计 L268） |
| T9 | 结构族扩覆盖与制作规范对齐 | - | 完成 | 制作规范 §3.1 根契约限定 Page_*、§3.1/§3.3 记 Tmp_ 系统面与 HUD 根新口径（Tmp_ 禁缩放器/按需射线；HUD 根去 GraphicRaycaster、记子 Canvas 模式）；PageStructureRule 照新口径扩 Hud_*/Tmp_*；真实面零 Error（Hud_Battle/Tmp_* 现状合规） | python scripts/gate/unity-tests.py --mode editor --filter UiValidationRulesEditModeTests | UiValidationRulesEditModeTests 10/10（HUD/Tmp_ 新口径用例 + 既有例）；制作规范 §3.1/§3.3 改文同批；真实面零 Error（校准探针） |
| T10 | 行为族 PlayMode 夹具与门禁接线 | - | 完成 | 新增 UiBehaviorPlayModeTests 三用例（复用复位/语言切换/模态射线穿透）全绿；既有行为覆盖盘点（列表重绑/取消/转场输入锁）记入施工记录；焦点例不含并登记原因；config.json 增 ui-behavior-playmode 命令（commands+red 双列） | python scripts/gate/unity-tests.py --mode playmode --filter UiBehaviorPlayModeTests --timeout 600 | UiBehaviorPlayModeTests 3/3（test_status 终态+TestResults.xml）；gate 脚本 PlayMode 异步通道端到端 3/3 passed；ui-behavior-playmode 命令… |
| T11 | 收尾接线：Families 翻绿+门禁命令+真实面校准 | T2, T3, T4, T6, T7, T8, T9, T10 | 完成 | UiValidator.Families 十族全部 implemented=true、各族待接入子项描述含原因；新 EditMode 夹具命令入 config.json 双列；全量 UiValidator.Run 真实面零 Error、Warning 基线数（颜色/未标记/未消费实测）记录进施工记录；UiValidationEditModeTests 真实面门禁用例全绿 | python scripts/gate/unity-tests.py --mode editor --filter UiValidationEditModeTests | UiValidationEditModeTests 14/14（十族全接入门禁+真实面校准钉 Error=0/Warning=303）；Families 十族翻绿；config 增 10 命令 |
| T12 | 文档回写：审计登记与施工进度新章 | T11 | 完成 | 审计表 L247/L268 记修复、L248/L250/L252 记口径收敛（修复登记表追加，不删原条目）；Docs/施工进度/UI系统线.md 新章（P1 边界收口批：状态+交付+验证证据+边界如实）；UIMain 无壳契约冻结决策与五族不含清单（令牌 SO/设备档/图集/焦点/状态色）记入边界条目 | python scripts/gate/doc-lint.py | 审计修复登记 5 行回写（L247/L268/L252＋两项新登记，不删原条目）；UI系统线.md 第6章（状态/交付/验证证据/边界如实含 UIMain 冻结决策与 Warning 基线 303）+ 索引行 |
| T13 | 门禁稳定性：页面清单夹具临时目录唯一化 | - | 完成 | UiPageManifestEditModeTests 连续两次 15/15；不再出现 TempPageManifestTests.meta 写入竞态 | python scripts/gate/unity-tests.py --mode editor --filter UiPageManifestEditModeTests | UiPageManifestEditModeTests 连续两次 15/15（OneTimeSetUp/TearDown + 陈旧裸目录清理）；临时目录收尾干净 |
| T14 | 生成物行尾锁定：UI 工具 JSON 固定 LF | - | 完成 | core.autocrlf=true 下全新检出契约/清单 JSON 仍为 LF；drift 字节对账不因平台行尾误报 | git check-attr text eol -- Assets/LiteGame/Editor/UiTools/Contracts/Page_Login.contract.json | git check-attr: Contracts/** 与 UiTools/*.json 均为 text/eol=lf；checkout-index 模拟全新检出 CRLF=0（core.autocrlf=true） |
| T15 | gate 脚本：console_status 空结果重试并失败关闭 | - | 完成 | console_status 返回空/非对象时重试 3 次；仍不可用则红并打印原因，不再 AttributeError/假绿 | python scripts/gate/unity-tests.py --mode editor --filter UiPageManifestEditModeTests | ensure_compiled 对 console_status 空结果重试 3 次、仍缺失则红（不再 AttributeError/假绿）；随后两次夹具运行均经该路径正常完成 |
| T16 | gate 通道统一：EditMode/PlayMode 全异步 | - | 完成 | 所有夹具走 run_tests --async_tests + test_status 轮询；cancelled/interrupted/error 判红；长夹具不再被 30s 传输超时截断 | python scripts/gate/unity-tests.py --mode editor --filter UiWidgetManifestEditModeTests --timeout 600 | UiWidgetManifestEditModeTests 异步通道 11/11 passed（此前同步通道被 30s 传输超时取消）；test_status 终态含 cancelled/interrupted/error 判红 |

## 任务详情

### T1 · 地基：规则 ID 常量与阈值单源

- 文件: Assets/LiteGame/Editor/UiTools/Validation/UiRuleIds.cs, Assets/LiteGame/Editor/UiTools/Validation/UiRuleThresholds.cs

### T2 · 文本族规则接入（4 规则）

- 文件: Assets/LiteGame/Editor/UiTools/Validation/Rules/TextRule.cs, Assets/Tests/EditMode/UiTextRuleEditModeTests.cs

### T3 · 样式族规则接入（复用两工具对账）

- 文件: Assets/LiteGame/Editor/UiTools/Validation/Rules/StyleRule.cs, Assets/Tests/EditMode/UiStyleRuleEditModeTests.cs

### T4 · 布局/性能静态规则接入（4 规则）

- 文件: Assets/LiteGame/Editor/UiTools/Validation/Rules/LayoutScalerRule.cs, Assets/LiteGame/Editor/UiTools/Validation/Rules/PerformanceRule.cs, Assets/Tests/EditMode/UiLayoutPerformanceRuleEditModeTests.cs

### T5 · Lua 引用扫描器（引用对账数据面）

- 文件: Assets/LiteGame/Editor/UiTools/Reference/UiLuaReferenceScanner.cs, Assets/Tests/EditMode/UiLuaReferenceScannerEditModeTests.cs

### T6 · 契约族补全规则（漂移/引用对账/双写）

- 文件: Assets/LiteGame/Editor/UiTools/Validation/Rules/ContractRule.cs, Assets/Tests/EditMode/UiContractReconciliationEditModeTests.cs

### T7 · 契约生成器补全与 11 契约重生成

- 文件: Assets/LiteGame/Editor/UiTools/Contract/UiContractGenerator.cs, Assets/LiteGame/Editor/UiTools/Contract/UiContractBatch.cs, Assets/LiteGame/Editor/UiTools/Contracts

### T8 · 绑定遍历统一（核心下沉+BindSearch 适配）

- 文件: Assets/LiteCodeGen/Editor/EditorTools/BindSurfaceWalk.cs, Assets/LiteCodeGen/Editor/EditorTools/BindSearch.cs, Assets/LiteGame/Editor/UiTools/Binding/UiBindSurfaceWalker.cs, Assets/Tests/EditMode/BindSearchUnificationEditModeTests.cs

### T9 · 结构族扩覆盖与制作规范对齐

- 文件: Docs/设计文档/客户端/UI系统/UI制作规范.md, Assets/LiteGame/Editor/UiTools/Validation/Rules/PageStructureRule.cs, Assets/Tests/EditMode/UiValidationEditModeTests.cs

### T10 · 行为族 PlayMode 夹具与门禁接线

- 文件: Assets/Tests/UI/PlayMode/UiBehaviorPlayModeTests.cs, .workflow/config.json

### T11 · 收尾接线：Families 翻绿+门禁命令+真实面校准

- 文件: Assets/LiteGame/Editor/UiTools/Validation/UiValidator.cs, .workflow/config.json

### T12 · 文档回写：审计登记与施工进度新章

- 文件: Docs/审计/代码质量审计-客户端.md, Docs/施工进度/UI系统线.md

### T13 · 门禁稳定性：页面清单夹具临时目录唯一化

- 文件: Assets/Tests/EditMode/UiPageManifestEditModeTests.cs

### T14 · 生成物行尾锁定：UI 工具 JSON 固定 LF

- 文件: .gitattributes

### T15 · gate 脚本：console_status 空结果重试并失败关闭

- 文件: scripts/gate/unity-tests.py

### T16 · gate 通道统一：EditMode/PlayMode 全异步

- 文件: scripts/gate/unity-tests.py

