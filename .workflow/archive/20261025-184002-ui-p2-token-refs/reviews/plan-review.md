# 方案评审（独立子代理）：UI 系统 P2-3 B2：令牌引用组件与应用

> 运行 ui-p2-token-refs · 创建于 2026-10-25T14:18:32+08:00
>
> 本文件由主代理根据独立子代理的返回整理落盘；不是主代理的自评。

## 评审输入

- 评审对象：plan.md（方案与验收标准；两轮——初版 → 按首轮意见修订 → 复评）
- 代码/文档基线：git HEAD `981668f5`（UI 面以 HEAD + 工作区 UI 文件为准）；抽查面＝`UiThemeAsset.cs`、`UiTextStyleRef.cs`、`UIForm.cs`、`UiTokenCatalog.cs`、`UiStyleTool.cs`、`UiValidator.cs` 与 `Validation/Rules/`、总设计 §6.1/§6.2、工具链设计 §8.1、`toolchain.json`、`Docs/工具链/工具索引.md`、`.workflow/config.json`
- 评审范围与不在范围：UI 系统 P2-3 B2（令牌引用组件与应用）；工作区并行线（TPS/weaknet/build-preflight/文档重构）不在范围
- 子代理标识：general-purpose `agent_c0f4402a-4fc7-40a4-a494-8f2cc8a5be82`（ZCode Agent 工具）
- 子代理约束：只读评审，不修改生产代码、测试与门禁

## 方案检查

- 目标可观察、范围与非目标清晰：通过（无范围膨胀；B4 存量收敛/B3 预览矩阵/主题切换 UI/Lua 面均显式列为非目标）。
- 现状与根因有代码/配置/文档依据：**逐项抽查属实**——B1 纯数据零运行期消费者、UiTokenCatalog 禁静态缓存、ApplyColors 最近 token 匹配（`UiStyleTool.cs:53-74`）、状态色自持（`StateButton.cs:16-19`）、Lua 零 SetColor、161/141 统计与施工进度一致、Config 收集组含 `Assets/GameData/Config`。
- 候选方案取舍充分，推荐理由回应约束：通过（B 的消费链闭环回应红线 10；降级路径回应运行期风险；跨程序集可行性由 `LiteGame.Editor`→`LiteClient.UI` 既有引用保证〔`UiTokenCatalog.cs:5`〕，resolver 无编辑器 API 泄漏/引用环/序列化风险）。
- 设计覆盖接口、数据、关键流程、失败与边界：通过（装载失败降级、未知 token 不猜默认色、refs 与裸值工具的写者关系、Tmp_* 克隆体生效路径均覆盖）。
- 通用性与扩展点明确，非特判式补丁：通过（Apply 结果枚举三方共用；ThemeProvider 缝即未来主题切换驱动点；后续 spacing/radius 引用组件同模式扩展；合法 ID 集＝生成物令牌集，无前缀特判）。
- 测试策略能先失败后通过，覆盖主路径/边界/错误路径：通过（PlayMode 先例真实——`UiBehaviorPlayModeTests` 用 `AssetService.InitAsync`+真 prefab+ctor 注入；`ui-behavior-playmode` 门禁通道已有）。
- 风险、监控、回滚可执行：通过（回滚＝注入缝置 null 回到纯烘焙行为）。

## 验收标准可判定性

| 验收标准 | 判定方式 | 是否可观察/可判定 | 说明 |
|---|---|---|---|
| AC1 | UiTokenCatalog 公开面 8 成员签名不变＋既有 UI 夹具回归全绿＋阈值单源断言 | 是 | 可映射门禁命令 |
| AC2 | 组件约束静态断言＋试点 prefab 可查（经工具挂载，非手改 YAML） | 是 | |
| AC3 | 幂等重跑零变更＋报告计数＋裸值段跳过断言（注入式测试口径） | 是（修订后） | 初版「Undo 可回退」不可判定，已按首轮意见改为可判定回退口径 |
| AC4 | PlayMode 真 prefab：颜色＝令牌现值；注入缝置 null＝保持烘焙值不抛 | 是 | ctor 注入先例＝`UiBehaviorPlayModeTests` |
| AC5 | 规则注册＋`UiValidator.RunCli` 真实面 0 新增 Error | 是 | |
| AC6 | 设计文档 + 审计登记为文档事实（Doc lane 可查） | 是 | 依赖 D3 裁决（已显式标注） |
| AC7 | `toolchain.py check` 0 error＋`wf verify` 全绿＋文档回写清单 | 是 | 依赖 D2（已显式标注） |

## 发现的问题与处理

| ID | 严重度 | 问题 | 处理 | 状态 |
|---|---|---|---|---|
| P-1 | major | AC3「Undo 可回退」无机制定义：既有批量工具走 `LoadPrefabContents→Save→Unload`，Undo 记录随 Unload 失效、不跨资产落盘，两既有工具也未实现 Undo——AC3 照抄则验收不可回答 | 回退口径钉死＝幂等重跑零变更＋变更仅经工具落盘（版本管理可回退）；沿用既有批量模式；总设计 §6.2「可 Undo」措辞列入 AC3/AC7 同批澄清 | 已修，复评确认解决 |
| P-2 | minor | 先例措辞不准确：BindSurfaceWalk 是编辑器域内单源化，非"下沉运行时程序集" | 改为"编辑器域内先例＋引用方向论证可行性" | 已修，复评确认准确 |
| P-3 | minor | 命名漂移：总设计 §6.1 明文 `UiStyleResolver`，初版写 `UiThemeResolver`；§6.1/§6.2/§8.1 回写不在任何 AC | 统一 `UiStyleResolver`；回写并入 AC7 | 已修 |
| P-4 | minor | 注入缝描述不精确："两个 OnInit 调用点"非真实注入点（实况＝`UiServiceModule.BuildLocaleProvider`→`UIService` ctor 形参→`RunOpenAsync` 单点赋值） | 设计要点/AC4 点名该缝（也是 PlayMode 置 null 的唯一注入口） | 已修 |
| P-5 | minor | 阈值单源落点未写（HitEpsilon 现为 `UiStyleTool` 私有常量） | HitEpsilon 移驻 resolver 单源，工具/规则/`UiRuleThresholds` 三处引用；AC1/AC5 同步 | 已修 |
| P-6 | minor | 试点挂载未写操作路径（红线 4：序列化资产禁手改） | 范围项 7 写明经 applier 工具或 unity-pipeline CLI | 已修 |
| P-7 | minor | D2/D3 未确认即绑定 AC3/AC6 | AC3/AC6 显式标注依赖，验收确认阶段一并提审 | 已修 |

## 复测记录

- 复评方式：同子代理会话续用，复评七项修复；通读修订版 plan.md 全文，逐项对照代码（`UiStyleTool.cs:53-74`、`UiServiceModule.cs:34/68`、`UIService.cs:98/107/226`、`UiTokenCatalog.cs:5`）。
- 复评结果：七项全部解决；阻塞问题 0；残留 4 处措辞（旧词「Undo」×3、旧名 `UiThemeResolver` ×1，均不拦施工）——主代理随后已清（统一为「可回退〔口径见设计要点〕」与 `UiStyleResolver.Apply`）。
- 第二轮结论：PASS。

## D3 增量评审（用户裁决"本批令牌化"后的范围扩展，两轮）

- 第三轮（D3 增量首评）：**FAIL**——①[blocker] 「新增令牌 ≤5 枚」按实测资产面值数学不可满足（去重需 7 枚；近金/近灰合并属可见色变超出 D3 授权）；②[major] 「uGUI Selectable 9 处」计数错误（实测 33 处/17 prefab，Screens+Hud 16＋Widgets 17，主代理早期只数了 Widgets 文件数）；③[major] `UiStyleRef` 与 `IThemeBound` widget 同节点双写者无裁决无判据。子代理实测抽查：三 widget prefab 序列化现值逐一读取、与 12 既有令牌逐一比对（零命中除白）、`m_Colors:` 全目录复测、无页面级 override 覆盖 widget 颜色字段。
- 第三轮修复后复评：**仍 FAIL（单点）**——映射表漏第 8 个面值（`Tmp_BottomNav.prefab:61` TabGroup NormalColor #BFBFC7，非白、不与任何令牌同值）；其余三项确认解决（计数 33/17 三处齐改、双写者裁决＋第 5 判据＋AC5 同步、非阻塞三条落地）；评审明示"纯数据单点修订、无需整轮复评"并给出可直接采用的 file:line。
- 第四轮（数据单点确认）：主代理补第 8 行（`color.state.nav.normal＝#BFBFC7`）＋"7 枚"→"8 枚"两处＋表格来源列补齐 file:line＋三条措辞（差值改"R/G/B 差 12/25/26 每通道"、判据计数补齐五判据、去重复行）；子代理抽点核验通过。
- **最终：结论 PASS；验收标准 PASS（AC1–AC7 逐条可判定）；阻塞问题 0。**

## 评审结论

- 评审方式: 独立子代理
- 结论: PASS
- 验收标准: PASS
- 阻塞问题: 0
- 评审人: general-purpose agent_c0f4402a（ZCode Agent 工具）
- 评审时间: 2026-10-25（首轮＋修复复评＋D3 增量两轮＋数据单点确认，共 5 次交互）
