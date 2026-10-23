# 代码评审：UI 系统 P1 边界收口：五族校验接入+契约补全+遍历统一

> 运行 ui-p1-boundary-closure · 创建于 2026-10-23T14:18:08+08:00

## 变更范围

- 对照任务：T1～T16（原 12 项 + 收口补遗 4 项：行尾锁定、页面清单夹具稳定、console_status 兜底、门禁全异步）。
- 变更文件/模块：校验核心与 13 条新规则（`Assets/LiteGame/Editor/UiTools/Validation/**`）、Lua 引用扫描器（`UiTools/Reference/**`）、契约生成与批量（`UiTools/Contract/**`）、遍历核心（`LiteCodeGen/Editor/EditorTools/BindSurfaceWalk.cs` + `BindSearch.cs`）、文本表视图（`UiLTextTable.cs`）、11 份契约基线、6 个新 EditMode 夹具 + 1 个 PlayMode 夹具、`scripts/gate/unity-tests.py`、`.gitattributes`、`.workflow/config.json`、制作规范/工具链设计/审计/施工进度文档。
- 未纳入：P2 工作台、起页/登记拆分、令牌 SO 管线、设备档/布局基线、图集基建（设计与进度均已登记）。

## 正确性与边界

- 主路径：文本/样式/布局/性能规则均从既有单源投影（`UiLTextTable`、`UiStyleTool`、`UiTextStyleTool`、`UiRuleThresholds`）；契约漂移以 `UiContractBatch.Regenerate` 为唯一判据；PlayMode 三例回归 `PrepareForShow`/`OnLocaleChanged`/`UIModalStack.RecomputeBlocking` 既有契约。
- 边界与错误路径：tbltext 读取失败显式 Error；缺 Lua 源显式告警；缺/损坏清单路径分别处理（损坏清单 fail-open 已作为遗留登记）；11 契约重生成逐字节一致、无时间戳；`cancelled/interrupted/error` 与 0 用例均判红。
- 回归风险：遍历统一后 `UiBindingToolsEditModeTests` hash 12/12 不变；生成输入等价用例覆盖常规层级；真实资产面 `UiValidator.Run()` Error=0、Warning=303 为已登记样式债校准钉。

## 测试充分性

- 测试是否真正覆盖验收与失败路径：7 条新 EditMode 红命令先红后绿；规则/扫描器/契约对账各有正负例；真实面校准探针 Error=0；PlayMode 3/3；新登记补遗均有复跑证据（页面清单连续两次 15/15、控件清单异步 11/11、全新检出 CRLF=0）。
- 是否存在迎合实现的测试：未发现。负例断言的是问题级别/报文/资产定位；契约漂移用例先篡改盘上契约再断言 Error；遍历用例同时钉住旧 hash 与生成输入，不是只复述实现。
- 已知薄弱面：真实面 Warning 总数与颜色/文字分项采用精确数字钉（161/141/303），属有意的债务度量；整改或样式工具批量收敛后需随批更新。模板根绑定 hash、损坏清单 fail-open、多行主行口径已有新登记，未在本批伪装为通过。

## 通用性与扩展性

- 根因与复用边界：样式/文本直接复用工具对账单源，不建第二份阈值与遍历；契约规则与契约生成共用 Lua 扫描器；遍历核心一处实现服务生成器/hash/契约/校验四域；规则阈值与词表集中在 `UiRuleThresholds`。
- 扩展点与接入方式：新增规则只需实现 `IUiRule` 并注册进 `UiValidator.Rules`；族状态由 `Families` 描述；测试可注入表种子、Lua 解析器与清单路径，不依赖真实资产。
- 硬编码、特判、魔法值：CanvasScaler 1920×1080/Match 0.5 为制作规范冻结值，规则内命名常量；严重级别与设计 §6.2 已对齐实况（页面根缩放/Animator 为 Error，其余 Warning）。
- 重复逻辑与过度抽象：未发现新的重复遍历或第二套契约序列化；`run_async` 统一两种测试模式，删除同步通道与 `find_summary` 死代码。
- 相关场景复用与回归影响：生成器域跳过嵌套模板边界与 hash/契约域同语义；真实面零 BindRoot（探针），既有生成文本不变。
- 命名、结构与复杂度：新增规则单文件单责；扫描器纯函数、可注入；`UiValidator` 注册表保持线性可读。

## 安全与性能

- 安全（注入、权限、敏感信息、输入边界）：全部为编辑器只读校验/生成；Lua 扫描只做字面量提取，不执行 Lua；输出路径限定 `Assets/`；未引入网络、凭据或用户输入面。
- 性能与资源：契约漂移逐页重生成在 EditMode 可接受；样式族按资产目录扫描后按上下文资产过滤；PlayMode/EditMode 测试走异步轮询，避免 30s 传输超时截断。环境坑：Unity 主窗口最小化会挂起主循环，已通过恢复窗口解决（未改代码）。

## 发现的问题与处理

| ID | 严重度 | 位置 | 问题 | 处理 | 状态 |
|---|---|---|---|---|---|
| C-1 | 高 | `.gitattributes:4-7`、`ContractDriftRule.cs` | `core.autocrlf=true` 下契约/清单全新检出为 CRLF，与 LF 生成物逐字节比较会全量误报 | 固定 `Contracts/**` 与 `UiTools/*.json` 为 `text eol=lf`；`git checkout-index` 模拟检出 CRLF=0 | 已修复 |
| C-2 | 高 | `UiPageManifestEditModeTests.cs:19-43,239`、`unity-tests.py:99-113` | 夹具复用临时目录偶发 `.meta` 写锁红；`console_status` 空结果仍会 `AttributeError` | 改 `OneTimeSetUp/TearDown`＋陈旧裸目录清理＋负例独立输出名；`groundTruth` 缺失重试 3 次后失败关闭 | 已修复 |
| C-3 | 高 | `unity-tests.py:116-175` | 同步 `run_tests` 受 30s 传输超时；长夹具被取消且可能只带回部分 summary——假绿风险 | 统一为 `--async_tests` 发射＋`test_status` 轮询；`cancelled/interrupted/error` 与 0 用例判红；控件清单 11/11、全量 29/29 实测 | 已修复 |
| C-4 | 中 | `BindSearch.cs:43` | 生成器域跳过所有嵌套 prefab 实例根（含非 `Tmp_`），与旧行为不同；未来 C# BindRoot 页面若嵌套非模板 prefab 会静默少成员 | 设计口径＝模板边界不透明；真实面零 BindRoot 且生成文本回归绿，保持现语义；在评审记录留下行为说明 | 记录（非阻断） |
| C-5 | 中 | `UiBindGenerationHash.cs:75` | 模板根 `BindNode` 不进 hash，根绑定模板契约恒为同一 hash，无法感知根绑定变更 | 本批约束「既有 hash 逐位不变」，不在本批修；已登记审计新债，另批与生成器/令牌改造统一 | 记录（非阻断） |
| C-6 | 中 | `UiContractBatch.cs:186-195` | 损坏页面清单被当作缺失，`ui.contract` 可能重写契约并丢 owner/events（fail-open） | `ui.page-manifest` 规则会显式 Error；已登记审计新债，另批改 fail-fast | 记录（非阻断） |
| C-7 | 中 | `UiContractGenerator.cs:152`、`UiContractBatch.cs:135` | 多行页主行口径：批量取表行首行、漂移重生成取清单 `forms[0]`，顺序不一致时会假漂移 | 当前 `Tmp_Dialog` 未触发；已登记审计新债，另批统一主行判据 | 记录（非阻断） |
| C-8 | 低 | `UI编辑器工具链专项设计.md` §6.2 | 布局/性能族的实现严重级别比设计表更严（页面根缩放/Animator 为 Error） | 按「代码为准、顺手修文档」将设计表补为 `Warning/Error` 并注明依据（UI-LAYOUT-01/UI-PREFAB-03）；doc-lint 0/0 | 已修复 |

## 复测记录

- `wf verify`：**PASS 29/29**（2026-10-23T17:23，L1 1819、doc、restore、l0、toolchain、doc-lane、l2-meta、全部 UI EditMode 夹具、PlayMode 3/3）。
- `UiPageManifestEditModeTests`：连续两次 **15/15**；临时目录收尾干净。
- `UiWidgetManifestEditModeTests`：异步通道 **11/11**（替代此前 30s 传输超时路径）。
- 行尾：`git check-attr` 显示 `text/eol=lf`；`git checkout-index` 模拟全新检出 CRLF=0。
- 文档：`python scripts/gate/doc-lint.py` **0 violation / 0 warning**（在最终文档编辑后单独复跑）。
- 先红：`wf verify --red` PASS（18 条，新增 7 条断言失败转红）。
- 验证证据：`evidence/verify.json`、`evidence/red.json`。

## 评审结论

- 结论: PASS
- 阻塞问题: 0
- 通用性: PASS
- 扩展性: PASS
- 评审人: Qoder 代理复核（用户交接）
- 评审时间: 2026-10-23
