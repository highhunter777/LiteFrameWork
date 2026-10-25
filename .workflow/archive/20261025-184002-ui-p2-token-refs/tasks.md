# 实施任务清单：UI 系统 P2-3 B2：令牌引用组件与应用

> 本文件由 workflow.py 生成，请使用 `task` 子命令更新，勿手改。

## 任务清单

阶段：已收尾（待归档） ｜ 完成 9/9（取消 0，阻塞 0）

| ID | 任务 | 依赖 | 状态 | 验收标准 | 验证方式 | 证据 |
|---|---|---|---|---|---|---|
| T1 | T1 解析核心下沉 UiStyleResolver + UiTokenCatalog 委托化 | - | 完成 | LiteClient.UI 新增 UiStyleResolver（FromAsset/TryColor/TryTypography/Apply 结果枚举，命中阈值移驻单源）；UiTokenCatalog 变薄委托（公开面签名不变）；既有编辑器调用点零改动、全部既有 UI 夹具回归全绿 | unity-tests --filter UiStyleResolverEditModeTests；既有 UiStyleRetirement/UiTheme* 回归 | UiStyleResolverEditModeTests 10/10（resolver 单源/Apply 结果枚举/重复空 ID 口径/与 catalog 同源）；UiStyleRetirement 8/8、UiThemeTokens 1… |
| T2 | T2 状态令牌补集（8 枚）+ SO 重生成 | T1 | 完成 | UiStyleTokens.json 按 plan.md 8 枚清单补 color.state.*（值量化 ≤0.5/255、来源 file:line 落设计文档）；ui.style.tokens 重生成 UiTheme.asset 幂等（二次生成零变更）；token-drift/漂移规则回归全绿；schema 校验通过 | unity-tests --filter UiThemeTokensEditModeTests,UiStyleTokenDrift；eval 连跑两次 changed=false | UiThemeStateTokensEditModeTests 3/3；生成幂等 changed=False colors=20；生成器夹具 8/8 |
| T3 | T3 UiStyleRef 颜色引用组件 | T1 | 完成 | LiteClient.UI 新增 UiStyleRef（colorToken 字段、空合法、DisallowMultipleComponent、只声明不驱动）；组件夹具全绿 | unity-tests --filter UiStyleRefEditModeTests | UiStyleRef 组件即终态（声明式数据组件）；UiStyleRefEditModeTests 引用面 6/6 |
| T4 | T4 编辑期 applier：refs 应用段+迁移段并入 colors-refresh | T1, T2, T3 | 完成 | 刷新工具三段式：refs 按声明应用（幂等/报告）→ widget 状态 token 最近匹配迁移（不命中不填）→ 裸值最近匹配（跳过已声明/IThemeBound 节点）；报告分段计数；回退口径=幂等重跑+工具落盘 | unity-tests --filter UiStyleRefEditModeTests,UiStyleTool 夹具；真实目录冒烟后字节不变 | UiStyleRefEditModeTests 6/6；三段式并入 colors-refresh（UiStyleTool 编排 + TryNearestMatch 单源） |
| T5 | T5 运行期消费链：装载+注入+装配应用 | T1, T3 | 完成 | bootstrap 一次装载 UiTheme.asset；UIService ctor 形参+RunOpenAsync 赋值注入 UIForm.ThemeProvider；PrepareForShow 应用 refs（跳过 IThemeBound 节点）并注入 IThemeBound；装载缺失/异常降级=保持烘焙值+Warning 一次 | PlayMode 行为用例（真 prefab）+Editmode 夹具 | PlayMode UiThemeApplyPlayModeTests 2/2（真页面 Txt_Title 装配期=令牌现值；null provider 降级不抛）；UIService ctor 形参+RunOpenAsync 赋值+UIF… |
| T6 | T6 三 widget 令牌化 + ui.style.ref 规则族（五判据） | T2, T5 | 完成 | StateButton/TabGroup/StarRating 伴生 token 字段+IThemeBound 实现+RefreshVisual 取值序（token 可解析→令牌值，否则 fallback）；ui.style.ref 五判据进 UiValidator.Rules（组件未知/漂移/无目标+widget 三态+双写者） | unity-tests --filter UiValidationRules,UiStyleRef；widget 注入/降级夹具 | UiWidgetThemeEditModeTests 7/7；UiStyleRefRuleEditModeTests 7/7（五判据+注册）；顺手修 StateButton.OnSelectedChanged 裸调用 NRE |
| T7 | T7 试点挂载+真实面校准 | T4, T6 | 完成 | ≥1 产品页/控件模板经工具挂 refs（非手改 YAML）；UiValidator 真实面 0 新增 Error、Warning 基线只降不升（记录新基线） | unity-tests --filter UiValidation；真实面 RunCli 输出 | 试点 Txt_Title 挂 ref（Unity API）；迁移 4 prefab/10 字段；幂等 0/0；真实面 14/14（Error=0，基线 298） |
| T8 | T8 文档回写+审计登记 | T7 | 完成 | 总设计 §6.1/§6.2/§6.5（状态色裁决与映射表含 8 枚来源 file:line）/§8.1 刷新口径回写；「可 Undo」措辞澄清；审计登记 uGUI Selectable 33 处/17 prefab 清单与三 widget C# 默认色债；UI系统线新章 | doc-lint 0 违规；docs --check | 总设计 §6.2/§6.5（映射表 8 枚含 file:line）/§8.1 三段式与 B2 消费者回写；审计 4 行登记；UI系统线第 11 章；doc-lint 0/0 |
| T9 | T9 工具/门禁面收口 | T7 | 完成 | toolchain.json/工具索引同步（colors-refresh 语义变化+新增产物）；config.json 增本批门禁命令；toolchain.py check 0 error；wf verify 全绿（L1+UI EditMode+PlayMode+doc） | toolchain.py check；wf verify | toolchain.json colors-refresh 三段式 summary+索引核对；config.json 6 门禁命令；toolchain check OK；wf verify 46/46 PASS |

## 任务详情

### T1 · T1 解析核心下沉 UiStyleResolver + UiTokenCatalog 委托化

- 文件: Assets/LiteClient/UI/Style/UiStyleResolver.cs, Assets/LiteGame/Editor/Style/UiTokenCatalog.cs

### T2 · T2 状态令牌补集（8 枚）+ SO 重生成

- 文件: Assets/LiteGame/Editor/UiTools/UiStyleTokens.json, Assets/GameData/Config/UI/UiTheme.asset

### T3 · T3 UiStyleRef 颜色引用组件

- 文件: Assets/LiteClient/UI/Style/UiStyleRef.cs

### T4 · T4 编辑期 applier：refs 应用段+迁移段并入 colors-refresh

- 文件: Assets/LiteGame/Editor/Style/UiStyleRefApplier.cs, Assets/LiteGame/Editor/Style/UiStyleTool.cs

### T5 · T5 运行期消费链：装载+注入+装配应用

- 文件: Assets/LiteClient/UI/UIForm.cs, Assets/LiteClient/UI/UIService.cs, Assets/LiteGame/App/Bootstrap/相关注入点

### T6 · T6 三 widget 令牌化 + ui.style.ref 规则族（五判据）

- 文件: Assets/LiteClient/UI/Widgets/StateButton.cs, Assets/LiteClient/UI/Widgets/TabGroup.cs, Assets/LiteClient/UI/Widgets/StarRating.cs, Assets/LiteGame/Editor/UiTools/Validation/Rules/StyleRefRule.cs

### T7 · T7 试点挂载+真实面校准

- 文件: Assets/ShareResource/UI/试点prefab

### T8 · T8 文档回写+审计登记

- 文件: Docs/设计文档/客户端/UI系统/*, Docs/审计/代码质量审计-客户端.md, Docs/施工进度/UI系统线.md

### T9 · T9 工具/门禁面收口

- 文件: scripts/gate/toolchain.json, Docs/工具链/工具索引.md, .workflow/config.json

