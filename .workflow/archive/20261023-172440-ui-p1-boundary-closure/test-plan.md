# 测试计划（先写测试）

## 测试策略

- **红测先行**：新规则/新能力按目标语义先写用例；规则类与扫描器以「端口真身 + 空实现」骨架落盘保证编译，负例用例因**断言失败**转红（缺报＝断言不满足），不靠编译错误。骨架不注册进 `UiValidator.Rules`（注册与 `Families` 翻绿归 T11，红阶段既有门禁零扰动）。
- **既有语义回归**：遍历统一的嵌套 BindRoot/根节点/重名/常规层级用例、结构族既有用例、hash 回归套件（`UiBindingToolsEditModeTests` 12/12）作为「生成输入等价」的稳定性钉——统一后必须保持绿。
- **注入面**：文本族用 `LTextSeed` 种子（合成 tbltext 条目）+ 规则构造器注入 Lua 扫描根；样式族注入临时目录合成 prefab；漂移用例走真实 Page_Login（生成器路径守卫辖内）+ 合成清单指向篡改/真实契约；引用/双写用合成清单+合成契约+合成 Lua（规则构造器注入 luaPath 解析）。
- **行为族（PlayMode）**：三用例（复用复位/语言切换/模态射线穿透）测的是**已存在**的框架契约（`UIForm.PrepareForShow`/`ApplyLocale`/`UIModalStack.RecomputeBlocking`），属回归覆盖，按协议以**替代验证**处理：不进 red_commands（无法在不破坏框架的前提下先红），以 commands 门禁（`ui-behavior-playmode`）作绿钉。
- **真实面校准**：红阶段 `UiValidator.Rules` 未注册新规则，既有真实面门禁（零 Error、Warning=1）不扰动；T11 注册后按实测基线更新真实面门禁断言并记录 Warning 基线数。

## 用例清单

| 夹具 | 用例 | 红期预期 |
| --- | --- | --- |
| `UiTextRuleEditModeTests`（7） | LTextKeyRef key 在/缺；Lua SetTextKey key 缺/合法；静态键挂带参条目；SetTextKey 带参+SetTextKeyArgs 参数数不符；语言列 Zh 空/En 空；富文本白名单/越界 | 缺报用例红（≈5）；「零误报」正例骨架期空过 |
| `UiStyleRuleEditModeTests`（4） | 非 token 色 Warning；未标记文本 Warning；漂移 Warning+失效 Error；工具应用后零误报 | 报问题用例红（3）；基线相符空过 |
| `UiLayoutPerformanceRuleEditModeTests`（6） | scaler 模式/分辨率/match 偏离 Error；子树第二套 Error；非 Page 根不辖；raycastTarget 超预算 Warning；布局链超限 Warning；根/子树 Animator Error/Warning | 全部期望报的用例红（5）；「不辖」「预算内」空过 |
| `UiLuaReferenceScannerEditModeTests`（6） | 字面量名提取/变量名不产出；名单外与拼接名不产出；事件名映射+handler 尽力解析；字面量全文匹配（变量中转）；luaPath 映射/异形拒绝；缺件 null | 5 红；「缺件 null」空过 |
| `UiContractReconciliationEditModeTests`（7） | 手改契约漂移 Error；一致零误报；无契约页不辖；未消费 Warning/越界 Error；双写 Error；清单字段投影；events 应用+排序+hash 重算；真实 Page_Login 端到端 | 7 红（骨架空实现） |
| `BindSearchUnificationEditModeTests`（7） | Clone_ 子树不入；模板内部件不入；嵌套 BindRoot 不深入；根自身不入；`partial` 拒识；重名收集；常规层级 marks 等价 | 新语义 3 红（当前 BindSearch 会收）；既有语义 4 绿（回归钉） |
| `UiValidationRulesEditModeTests`（+2） | HUD 根缺件 Error/无射线合法；Tmp_ 系统面 CanvasGroup 必备/禁缩放器 | +2 红（当前规则不辖）；既有 8 绿 |
| `UiValidationEditModeTests`（改 1） | 门禁：十族全接入+已接入族 Note 非空 | 红（五族仍待接入）；真实面门禁不变绿 |
| `UiBehaviorPlayModeTests`（3，施工期落地） | 复用复位（PrepareForShow 四复位面）；语言切换（静态文案刷新+OnShow 不重跑）；模态射线穿透（blocksRaycasts=false+射线打不到） | 回归绿（测既有框架契约）；替代验证依据如上 |

## 预期失败证据

`wf verify --red` 产出 `evidence/red.json`（17 条 red 命令）。预期形态：

- 新增 7 条 red 命令：**全部非零退出**（断言失败——每夹具至少一半用例转红，空过用例为「零误报」正例，属预期空过）；
- 既有 10 条 red 命令：通过（上一批实现已绿——先例口径：混合 pass/fail 即有效红证据）；
- 失败必须是断言失败（`Expected: … But was: …`），不得是编译错误/环境故障（编译门禁已先行转绿：refresh 0 错）。

## 运行方式

- 红证据：`python <skill>/scripts/workflow.py --run ui-p1-boundary-closure verify --red`（Unity 侧 `Process.Start` 后台执行 + 日志轮询——EditMode 测试驱动编辑器域重载，长命令不同步等待）。
- 单夹具复验：`python scripts/gate/unity-tests.py --mode editor --filter <夹具名>`。
- 编译门禁：`unity_editor.refresh`（返回 0 错即过；本批红阶段实测 0 错）。
- 绿验证（阶段 6）：`wf verify`（29 条 commands：既有 20 + 本批新增 9，含 `ui-behavior-playmode`）。
